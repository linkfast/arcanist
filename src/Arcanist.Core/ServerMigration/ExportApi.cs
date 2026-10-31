using System.IO.Compression;
using System.Net;
using System.Text.Json;
using Arcanist.Core.Application;
using Arcanist.Core.Application.ServerMigration;
using Arcanist.Core.Application.Users;
using Arcanist.Core.Auth;
using Arcanist.Core.Backups;
using Arcanist.Core.Data;
using Arcanist.Core.Data.Entities;
using Arcanist.Core.Extensions;
using Arcanist.Core.Hosting;
using Arcanist.Core.Storage;
using Microsoft.EntityFrameworkCore;

namespace Arcanist.Core.ServerMigration;

// The export API: what another Arcanist reads, with a migration code, to bring this server over. Not part of the
// public contract (server to server, like the peer API): see docs/developers/migration.md.

/// <summary>What this server has, for the other one to choose what to bring.</summary>
/// <param name="ServerId">A stable id of this server (its DLNA device id), so plays brought twice count once.</param>
public sealed record ExportManifest(
    int Format,
    Guid ServerId,
    string ServerName,
    string Version,
    List<ExportLibrary> Libraries,
    List<ExportUser> Users,
    List<ExportComponent> Extensions,
    List<ExportComponent> Players,
    string? DefaultPlayer);

/// <param name="Files">Audio files in the catalog (the copy also takes the rest of the folders: covers, lyrics, cue sheets).</param>
public sealed record ExportLibrary(Guid Id, string Name, bool VisibleToAll, string? OrganizeTemplate, int? ScanIntervalMinutes, int Files, long Bytes, int Tracks);

/// <param name="SecondFactor">The user signs in with a second factor (the new server makes them pair it again).</param>
public sealed record ExportUser(
    Guid Id,
    string UserName,
    string? DisplayName,
    UserRole Role,
    List<Permission> Permissions,
    bool IsDisabled,
    List<Guid> LibraryIds,
    List<Guid> ReadOnlyLibraryIds,
    bool HasPassword,
    bool SecondFactor);

/// <param name="Package">Its package can be downloaded from this server (an installed one, not a built-in).</param>
public sealed record ExportComponent(string Id, string Name, string Version, bool Enabled, bool BuiltIn, bool OpenSource, bool Package);

/// <summary>A user's password, as this server keeps it (ASP.NET Core Identity's hash, or empty).</summary>
public sealed record ExportCredential(Guid UserId, string PasswordHash);

/// <param name="ContentHash">The audio hash of a catalogued audio file (tags excluded), null for the rest (covers, lyrics…).</param>
public sealed record ExportFile(string Path, long Size, DateTime ModifiedAt, string? ContentHash = null);

public sealed record ExportWish(
    Guid UserId,
    string Title,
    string Artist,
    string? Album,
    int? DurationMs,
    Guid? MusicBrainzRecordingId,
    Guid? AcoustId,
    string? Note,
    DateTime CreatedAt,
    string? SourceUrl);

/// <summary>Users' data in the backups' format (see docs/backups.md), with their wishes.</summary>
internal sealed record ExportUserData(BackupCatalog Catalog, BackupUserData UserData, List<ExportWish> Wishes);

/// <summary>The export API's endpoints and their authorization: the migration code in a header.</summary>
internal static class ExportApi
{
    public const string Prefix = "/rest/v1/export";
    public const string CodeHeader = "X-Arcanist-Migration";

    /// <summary>The name the reading server says it has, kept with the code.</summary>
    public const string ClientHeader = "X-Arcanist-Server";

    public const int Format = 1;

    /// <summary>Settings another server can take as they are (machine- and identity-specific ones stay).</summary>
    public static readonly string[] PortableSettings =
    [
        SettingKeys.RatingMode, SettingKeys.Privacy, SettingKeys.ShareAnonymousStatistics, SettingKeys.OnlineServices, SettingKeys.AcoustIdKey,
        SettingKeys.DefaultLanguage, SettingKeys.AnalysisEnabled, SettingKeys.UpdatesEnabled, SettingKeys.Dlna, SettingKeys.Mstream,
        SettingKeys.Transcoding, SettingKeys.StreamingDefaults, SettingKeys.RemoteLibraries, SettingKeys.Ai, SettingKeys.Sharing,
    ];

    public static IEndpointRouteBuilder MapExportApi(this IEndpointRouteBuilder app)
    {
        var export = app.MapGroup("/export").ExcludeFromDescription().AllowAnonymous().AddEndpointFilter(AuthorizeAsync);

        export.MapGet("/manifest", ManifestAsync);
        export.MapGet("/credentials", async (ArcanistDbContext db, CancellationToken ct) =>
            Results.Json(await db.Users.AsNoTracking().OrderBy(u => u.UserName)
                .Select(u => new ExportCredential(u.Id, u.PasswordHash)).ToListAsync(ct)));
        export.MapGet("/libraries/{id:guid}/files", FilesAsync);
        export.MapGet("/libraries/{id:guid}/file", FileAsync);
        export.MapGet("/userdata", UserDataAsync);
        export.MapGet("/settings", SettingsAsync);
        export.MapGet("/extensions/{id}/package", (string id, ExtensionHost host, CancellationToken ct) =>
            PackageAsync(host.Extensions.FirstOrDefault(e => e.Manifest.Id == id && !e.BuiltIn)?.Root, id, ct));
        export.MapGet("/players/{id}/package", (string id, FrontendCatalog frontends, CancellationToken ct) =>
            PackageAsync(frontends.Find(id) is { BuiltIn: false } player ? player.Root : null, id, ct));
        return app;
    }

    /// <summary>A valid code (not revoked, not expired) in <see cref="CodeHeader"/>; failures count against the caller.</summary>
    private static async ValueTask<object?> AuthorizeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        var services = http.RequestServices;
        var throttle = services.GetRequiredService<LoginThrottle>();
        var key = "migration:" + (http.Connection.RemoteIpAddress?.ToString() ?? "?");
        if (throttle.GetLockout(key) is not null)
            return Problem(HttpStatusCode.TooManyRequests, ErrorCodes.TooManyRequests);

        var code = http.Request.Headers[CodeHeader].ToString();
        var time = services.GetRequiredService<TimeProvider>();
        var now = time.GetUtcNow().UtcDateTime;
        var db = services.GetRequiredService<ArcanistDbContext>();
        var hash = code.Length is > 0 and < 100 ? MigrationCodeService.Hash(code) : null;
        var grant = hash is null ? null : await db.MigrationGrants.SingleOrDefaultAsync(g => g.CodeHash == hash, http.RequestAborted);
        if (grant is null || grant.RevokedAt is not null || grant.ExpiresAt <= now)
        {
            throttle.RecordFailure(key);
            return Problem(HttpStatusCode.Forbidden, ErrorCodes.MigrationCodeInvalid);
        }

        throttle.RecordSuccess(key);
        if (grant.LastUsedAt is null || now - grant.LastUsedAt > TimeSpan.FromMinutes(1))
        {
            grant.LastUsedAt = now;
            var client = http.Request.Headers[ClientHeader].ToString();
            if (client.Length > 0)
                grant.LastUsedBy = client.Length > 200 ? client[..200] : client;
            await db.SaveChangesAsync(http.RequestAborted);
        }

        return await next(context);
    }

    private static IResult Problem(HttpStatusCode status, string code) =>
        Results.Problem(statusCode: (int)status, extensions: new Dictionary<string, object?> { ["code"] = code });

    /// <summary>The libraries that can be brought: those with this server's own folders (local or S3), not personal or kept by an extension.</summary>
    private static IQueryable<Library> Exportable(ArcanistDbContext db) =>
        db.Libraries.Where(l => l.OwnerUserId == null && l.ManagedBy == null && l.Sources.Any()
                                && l.Sources.All(s => s.Kind == LibrarySourceKind.Local || s.Kind == LibrarySourceKind.S3));

    private static async Task<IResult> ManifestAsync(
        ArcanistDbContext db, ExtensionHost host, FrontendCatalog frontends, IServiceProvider services, IConfiguration configuration, CancellationToken ct)
    {
        var libraries = await Exportable(db).AsNoTracking().OrderBy(l => l.Number)
            .Select(l => new { l.Id, l.Name, l.VisibleToAll, l.OrganizeTemplate, l.ScanIntervalMinutes })
            .ToListAsync(ct);
        var ids = libraries.Select(l => l.Id).ToList();
        var files = (await db.MediaFiles.AsNoTracking()
                .Where(f => f.MissingSince == null && ids.Contains(f.LibrarySource.LibraryId))
                .Select(f => new { f.LibrarySource.LibraryId, f.LibrarySourceId, f.RelativePath, f.Size, f.TrackId })
                .ToListAsync(ct))
            .GroupBy(f => f.LibraryId)
            .ToDictionary(g => g.Key, g =>
            {
                // A file split by a cue sheet is one file.
                var physical = g.DistinctBy(f => (f.LibrarySourceId, f.RelativePath)).ToList();
                return (Files: physical.Count, Bytes: physical.Sum(f => f.Size), Tracks: g.Select(f => f.TrackId).Distinct().Count());
            });

        var users = await db.Users.AsNoTracking().OrderBy(u => u.UserName).Include(u => u.LibraryAccess).ToListAsync(ct);
        var dlna = services.GetRequiredService<Dlna.DlnaSettings>();
        var defaultPlayer = (await frontends.DefaultAsync(ct))?.Manifest.Id;
        var name = (await services.GetRequiredService<P2P.P2PSettings>().GetAsync(ct)).DisplayName;

        return Results.Json(new ExportManifest(
            Format,
            await dlna.DeviceIdAsync(ct),
            name,
            ArcanistVersion.Current,
            libraries.Select(l =>
            {
                var counts = files.GetValueOrDefault(l.Id);
                return new ExportLibrary(l.Id, l.Name, l.VisibleToAll, l.OrganizeTemplate, l.ScanIntervalMinutes, counts.Files, counts.Bytes, counts.Tracks);
            }).ToList(),
            users.Select(u => new ExportUser(
                u.Id, u.UserName, u.DisplayName, u.Role, PermissionMapping.ToList(u.Permissions).ToList(), u.IsDisabled,
                u.LibraryAccess.Where(a => ids.Contains(a.LibraryId)).Select(a => a.LibraryId).ToList(),
                u.LibraryAccess.Where(a => a.ReadOnly && ids.Contains(a.LibraryId)).Select(a => a.LibraryId).ToList(),
                u.PasswordHash.Length > 0,
                SecondFactor: false)).ToList(),
            host.Extensions.Select(e => new ExportComponent(e.Manifest.Id, e.Manifest.Name, e.Manifest.Version, host.IsEnabled(e.Manifest.Id), e.BuiltIn,
                e.BuiltIn || OpenSource.IsOpenLicense(e.Manifest.License), !e.BuiltIn && e.Root is not null)).ToList(),
            frontends.List().Select(f => new ExportComponent(f.Manifest.Id, f.Manifest.Name, f.Manifest.Version, true, f.BuiltIn,
                OpenSource.IsOpenLicense(f.Manifest.License), !f.BuiltIn)).ToList(),
            defaultPlayer));
    }

    /// <summary>Every file of the library's folders (audio and the rest: covers, lyrics, cue sheets), as stored.</summary>
    /// <param name="after">Only the paths after this one (ordinal order), for paging with <paramref name="first"/>; without
    /// both, the whole list.</param>
    private static async Task<IResult> FilesAsync(Guid id, string? after, int? first, ArcanistDbContext db, ILibraryStorageResolver storages, CancellationToken ct)
    {
        var library = await Exportable(db).AsNoTracking().Include(l => l.Sources).SingleOrDefaultAsync(l => l.Id == id, ct);
        if (library is null)
            return Problem(HttpStatusCode.NotFound, ErrorCodes.NotFound);
        var files = new List<ExportFile>();
        foreach (var source in library.Sources)
        {
            var storage = storages.For(source.Kind);
            await foreach (var entry in storage.EnumerateAsync(StorageLocation.From(source), ct))
                files.Add(new ExportFile(entry.RelativePath, entry.Size, entry.ModifiedAt));
        }

        IEnumerable<ExportFile> listed = files.DistinctBy(f => f.Path).OrderBy(f => f.Path, StringComparer.Ordinal);
        if (after is not null)
            listed = listed.Where(f => string.CompareOrdinal(f.Path, after) > 0);
        if (first is { } count)
            listed = listed.Take(Math.Clamp(count, 1, 10_000));
        var page = listed.ToList();

        var sourceIds = library.Sources.Select(s => s.Id).ToList();
        var paths = page.Select(f => f.Path).ToList();
        var hashes = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var chunk in paths.Chunk(500))
        {
            foreach (var row in await db.MediaFiles.AsNoTracking()
                         .Where(f => sourceIds.Contains(f.LibrarySourceId) && chunk.Contains(f.RelativePath) && f.CueTrack <= 1 && f.MissingSince == null)
                         .Select(f => new { f.RelativePath, f.ContentHash, f.CueTrack }).ToListAsync(ct))
            {
                // A file split by a cue sheet: the hash of its first track's range is not the file's; leave it out.
                if (row.CueTrack == 0)
                    hashes.TryAdd(row.RelativePath, row.ContentHash);
            }
        }

        return Results.Json(page.Select(f => f with { ContentHash = hashes.GetValueOrDefault(f.Path) }).ToList());
    }

    /// <summary>One file as stored (with ranges, so a copy resumes). Never a play: nothing is recorded.</summary>
    private static async Task<IResult> FileAsync(Guid id, string path, ArcanistDbContext db, ILibraryStorageResolver storages, CancellationToken ct)
    {
        var library = await Exportable(db).AsNoTracking().Include(l => l.Sources).SingleOrDefaultAsync(l => l.Id == id, ct);
        if (library is null)
            return Problem(HttpStatusCode.NotFound, ErrorCodes.NotFound);
        foreach (var source in library.Sources)
        {
            var storage = storages.For(source.Kind);
            var location = StorageLocation.From(source);
            try
            {
                if (!await storage.ExistsAsync(location, path, ct))
                    continue;
                var stream = await storage.OpenReadAsync(location, path, ct);
                return Results.Stream(stream, "application/octet-stream", enableRangeProcessing: true);
            }
            catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException or ArgumentException)
            {
            }
        }

        return Problem(HttpStatusCode.NotFound, ErrorCodes.NotFound);
    }

    /// <summary>The users' ratings, play counts and history, playlists and wishes, with the catalog of the songs they name.</summary>
    private static async Task<IResult> UserDataAsync(string? users, ArcanistDbContext db, CancellationToken ct)
    {
        var wanted = (users ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries).Select(u => Guid.TryParse(u, out var g) ? g : Guid.Empty).ToHashSet();
        var data = await MigrationData.UserDataAsync(db, wanted, ct);
        var tracks = data.Users.SelectMany(u => u.Ratings.Select(r => r.Track)
                .Concat(u.PlayCounts.Select(c => c.Track))
                .Concat(u.Plays.Select(p => p.Track))
                .Concat(u.Playlists.SelectMany(p => p.Entries).Select(e => e.Track)))
            .ToHashSet();
        var catalog = await MigrationData.CatalogAsync(db, tracks, ct);
        var wishes = await db.WishListItems.AsNoTracking().Where(w => wanted.Contains(w.UserId) && w.ObtainedAt == null)
            .Select(w => new ExportWish(w.UserId, w.Title, w.Artist, w.Album, w.DurationMs, w.RecordingMbid, w.AcoustId, w.Note, w.CreatedAt, w.SourceUrl))
            .ToListAsync(ct);
        return Results.Json(new ExportUserData(catalog, data, wishes));
    }

    private static async Task<IResult> SettingsAsync(SettingsStore settings, CancellationToken ct)
    {
        var values = new Dictionary<string, JsonElement>();
        foreach (var key in PortableSettings)
        {
            if (await settings.GetAsync<JsonElement?>(key, null, ct) is { } value)
                values[key] = value;
        }

        return Results.Json(values);
    }

    /// <summary>An installed package's folder, zipped again (packages are unpacked when installed).</summary>
    private static async Task<IResult> PackageAsync(string? root, string id, CancellationToken ct)
    {
        if (root is null || !Directory.Exists(root))
            return Problem(HttpStatusCode.NotFound, ErrorCodes.NotFound);
        var zip = Path.Combine(Path.GetTempPath(), $"arcanist-export-{Guid.NewGuid():N}.zip");
        await Task.Run(() => ZipFile.CreateFromDirectory(root, zip, CompressionLevel.Fastest, includeBaseDirectory: false), ct);
        var stream = new FileStream(zip, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.DeleteOnClose | FileOptions.Asynchronous);
        return Results.Stream(stream, "application/zip", fileDownloadName: id + ".zip");
    }
}
