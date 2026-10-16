using System.Text.Json;
using Arcanist.Core.Application.Libraries;
using Arcanist.Core.Data;
using Arcanist.Core.Data.Entities;
using Arcanist.Core.Dlna;
using Arcanist.Core.Import.Dlna;
using Microsoft.EntityFrameworkCore;

namespace Arcanist.Core.Application.Import;

// Types in this file are part of the public API contracts (GraphQL schema and OpenAPI document).

/// <summary>A DLNA/UPnP media server on the network.</summary>
/// <param name="Id">Its UUID.</param>
/// <param name="Location">Its description URL, which identifies it in the other operations.</param>
public sealed record DlnaMediaServer(string Id, string Name, string? Manufacturer, string? Model, string Location);

/// <summary>A folder of a media server.</summary>
/// <param name="ChildCount">How many folders and files it holds, when the server says.</param>
public sealed record DlnaFolder(string Id, string Title, int? ChildCount);

/// <summary>A folder of a media server: its subfolders and how much music it holds directly.</summary>
/// <param name="Tracks">Pieces of music directly in the folder (not in its subfolders).</param>
/// <param name="Truncated">The folder holds more than was read; only the first ones are listed.</param>
public sealed record DlnaFolderContents(string Id, IReadOnlyList<DlnaFolder> Folders, int Tracks, bool Truncated);

public sealed record DlnaFolderInput(string Id, string Title);

/// <summary>What to copy from a media server, and where to.</summary>
/// <param name="Folders">Folders of the server; their subfolders come with them. Each piece of music is copied once,
/// however many of them list it.</param>
/// <param name="SourceId">An existing library folder to copy into.</param>
/// <param name="NewLibraryName">Or a new library, with this name…</param>
/// <param name="NewLibraryPath">…in this folder of this server.</param>
public sealed record StartDlnaImportInput(
    string Location,
    IReadOnlyList<DlnaFolderInput> Folders,
    Guid? SourceId,
    string? NewLibraryName,
    string? NewLibraryPath);

/// <summary>A piece of music that could not be copied.</summary>
/// <param name="Reason"><c>download_failed</c>, <c>unreadable</c> or <c>write_failed</c>.</param>
public sealed record DlnaImportFailure(string Title, string? Artist, string? Album, string Reason);

/// <summary>A copy of music from a media server, and how far it got.</summary>
/// <param name="Found">Pieces of music found so far.</param>
/// <param name="Duplicates">Skipped: the library has the same audio already.</param>
/// <param name="Failures">The first 50 that failed.</param>
public sealed record DlnaImportJob(
    Guid Id,
    string ServerName,
    IReadOnlyList<string> Folders,
    Guid LibraryId,
    string LibraryName,
    DlnaImportStatus Status,
    int Found,
    int Copied,
    int Duplicates,
    int Failed,
    long CopiedBytes,
    DateTime CreatedAt,
    DateTime? FinishedAt,
    string? ErrorCode,
    IReadOnlyList<DlnaImportFailure> Failures);

/// <summary>
/// Copies music from DLNA/UPnP media servers into libraries. Administrators only: it reads other devices of the
/// network and writes into the server's folders; starting an import needs a recent authentication.
/// </summary>
public sealed class DlnaImportService(
    ArcanistDbContext db,
    AccessGuard guard,
    LibraryService libraries,
    TimeProvider time,
    IServiceProvider services)
{
    private const int MaxFolderPages = 25;

    private MediaServerClient Client => services.GetRequiredService<MediaServerClient>();
    private DlnaSettings Dlna => services.GetRequiredService<DlnaSettings>();
    private DlnaImportWorker Worker => services.GetRequiredService<DlnaImportWorker>();

    /// <summary>Searches the local networks for media servers for a few seconds (Arcanist itself is left out).</summary>
    public async Task<IReadOnlyList<DlnaMediaServer>> SearchAsync(CancellationToken cancellationToken)
    {
        guard.Admin();
        return await SearchAsync(Client, (await Dlna.DeviceIdAsync(cancellationToken)).ToString(), cancellationToken);
    }

    /// <summary>The media server at a description URL (for servers the search does not find).</summary>
    public async Task<DlnaMediaServer> DescribeAsync(string location, CancellationToken cancellationToken)
    {
        guard.Admin();
        return await DescribeAsync(Client, location, cancellationToken);
    }

    /// <summary>A folder of a media server; <c>0</c> is the root.</summary>
    public async Task<DlnaFolderContents> BrowseAsync(string location, string? folderId, CancellationToken cancellationToken)
    {
        guard.Admin();
        return await BrowseAsync(Client, location, folderId, cancellationToken);
    }

    /// <summary>The search, also for the setup wizard (no account yet). <paramref name="self"/> is this server's own, left out.</summary>
    internal static async Task<IReadOnlyList<DlnaMediaServer>> SearchAsync(MediaServerClient client, string? self, CancellationToken cancellationToken)
    {
        var servers = await client.SearchAsync(TimeSpan.FromSeconds(3), cancellationToken);
        return servers.Where(s => self is null || !s.Id.Equals(self, StringComparison.OrdinalIgnoreCase)).Select(ToModel).ToList();
    }

    internal static async Task<DlnaMediaServer> DescribeAsync(MediaServerClient client, string location, CancellationToken cancellationToken) =>
        ToModel(await client.DescribeAsync(ParseLocation(location), cancellationToken));

    internal static async Task<DlnaFolderContents> BrowseAsync(MediaServerClient client, string location, string? folderId, CancellationToken cancellationToken)
    {
        var server = await client.DescribeAsync(ParseLocation(location), cancellationToken);
        var id = string.IsNullOrEmpty(folderId) ? "0" : folderId;
        var folders = new List<DlnaFolder>();
        var tracks = 0;
        var truncated = true;
        for (var page = 0; page < MaxFolderPages; page++)
        {
            var result = await client.BrowseAsync(server, id, page * 200, 200, cancellationToken);
            folders.AddRange(result.Containers.Select(c => new DlnaFolder(c.Id, c.Title, c.ChildCount)));
            tracks += result.Tracks.Count;
            if (result.Returned == 0 || (page + 1) * 200 >= result.Total)
            {
                truncated = false;
                break;
            }
        }

        return new DlnaFolderContents(id, folders, tracks, truncated);
    }

    public async Task<DlnaImportJob> StartAsync(StartDlnaImportInput input, CancellationToken cancellationToken)
    {
        var actor = guard.Admin();
        guard.RecentAuthentication();

        var errors = new ValidationErrors();
        if (input.Folders is not { Count: > 0 })
            errors.Add("folders", ErrorCodes.Required);
        if (input.SourceId is null && string.IsNullOrWhiteSpace(input.NewLibraryPath))
            errors.Add("sourceId", ErrorCodes.Required);
        errors.ThrowIfAny();

        var server = await Client.DescribeAsync(ParseLocation(input.Location), cancellationToken);

        Guid sourceId;
        if (input.SourceId is { } existing)
        {
            var source = await db.LibrarySources.AsNoTracking().SingleOrDefaultAsync(s => s.Id == existing, cancellationToken)
                         ?? throw ArcanistException.Field("sourceId", ErrorCodes.NotFound);
            if (source.Kind != LibrarySourceKind.Local)
                throw ArcanistException.Field("sourceId", ErrorCodes.ReadOnly);
            sourceId = source.Id;
        }
        else
        {
            Libraries.Library library;
            try
            {
                library = await libraries.CreateAsync(new CreateLibraryInput(
                    string.IsNullOrWhiteSpace(input.NewLibraryName) ? server.Name : input.NewLibraryName.Trim(), false, 24,
                    [new LibrarySourceInput(input.NewLibraryPath!)]), cancellationToken);
            }
            catch (ArcanistException.Invalid invalid)
            {
                var field = invalid.Errors.ToDictionary().Keys.FirstOrDefault() ?? "";
                throw ArcanistException.Field(field.StartsWith("sources", StringComparison.Ordinal) ? "newLibraryPath" : "newLibraryName",
                    invalid.Errors.ToDictionary().Values.First()[0]);
            }

            sourceId = library.Sources![0].Id;
        }

        var import = new DlnaImport
        {
            Location = server.Location.ToString(),
            ServerName = server.Name,
            Containers = JsonSerializer.Serialize(
                input.Folders!.DistinctBy(f => f.Id).Select(f => new DlnaContainerChoice(f.Id, f.Title)).ToList(), JsonSerializerOptions.Web),
            LibrarySourceId = sourceId,
            Status = DlnaImportStatus.Queued,
            RequestedByUserId = actor.UserId,
            CreatedAt = time.GetUtcNow().UtcDateTime,
        };
        db.DlnaImports.Add(import);
        await db.SaveChangesAsync(cancellationToken);
        Worker.Wake();
        return (await ListAsync([import.Id], cancellationToken))[0];
    }

    /// <summary>The 20 latest imports.</summary>
    public async Task<IReadOnlyList<DlnaImportJob>> ListAsync(CancellationToken cancellationToken)
    {
        guard.Admin();
        var ids = await db.DlnaImports.AsNoTracking().OrderByDescending(i => i.CreatedAt).Take(20).Select(i => i.Id).ToListAsync(cancellationToken);
        return await ListAsync(ids, cancellationToken);
    }

    public async Task<DlnaImportJob> CancelAsync(Guid id, CancellationToken cancellationToken)
    {
        guard.Admin();
        var import = await db.DlnaImports.SingleOrDefaultAsync(i => i.Id == id, cancellationToken) ?? throw new ArcanistException.NotFound();
        if (import.Status is not (DlnaImportStatus.Queued or DlnaImportStatus.Listing or DlnaImportStatus.Copying))
            throw new ArcanistException.Conflict(ErrorCodes.ImportNotActive);

        // The worker marks the one it is running when it stops; one still waiting is marked here.
        if (!Worker.Cancel(id))
        {
            import.Status = DlnaImportStatus.Cancelled;
            import.FinishedAt = time.GetUtcNow().UtcDateTime;
            await db.SaveChangesAsync(cancellationToken);
        }
        else
        {
            for (var i = 0; i < 50 && await db.DlnaImports.AsNoTracking().AnyAsync(x => x.Id == id && x.FinishedAt == null, cancellationToken); i++)
                await Task.Delay(100, cancellationToken);
        }

        return (await ListAsync([id], cancellationToken))[0];
    }

    /// <summary>Walks the server again and copies what is missing: what failed, and what was added since.</summary>
    public async Task<DlnaImportJob> RetryAsync(Guid id, CancellationToken cancellationToken)
    {
        guard.Admin();
        guard.RecentAuthentication();
        var import = await db.DlnaImports.SingleOrDefaultAsync(i => i.Id == id, cancellationToken) ?? throw new ArcanistException.NotFound();
        if (import.Status is DlnaImportStatus.Queued or DlnaImportStatus.Listing or DlnaImportStatus.Copying)
            throw new ArcanistException.Conflict(ErrorCodes.ImportNotActive);

        import.Status = DlnaImportStatus.Queued;
        import.ErrorCode = null;
        import.FinishedAt = null;
        await db.SaveChangesAsync(cancellationToken);
        Worker.Wake();
        return (await ListAsync([id], cancellationToken))[0];
    }

    private async Task<IReadOnlyList<DlnaImportJob>> ListAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
    {
        var imports = await db.DlnaImports.AsNoTracking()
            .Where(i => ids.Contains(i.Id))
            .Select(i => new { Import = i, i.LibrarySource.LibraryId, LibraryName = i.LibrarySource.Library.Name })
            .ToListAsync(cancellationToken);
        var counts = await db.DlnaImportItems.AsNoTracking()
            .Where(i => ids.Contains(i.ImportId))
            .GroupBy(i => new { i.ImportId, i.Status })
            .Select(g => new { g.Key.ImportId, g.Key.Status, Count = g.Count() })
            .ToListAsync(cancellationToken);
        var failures = await db.DlnaImportItems.AsNoTracking()
            .Where(i => ids.Contains(i.ImportId) && i.Status == DlnaImportItemStatus.Failed)
            .OrderBy(i => i.Album).ThenBy(i => i.TrackNumber)
            .Select(i => new { i.ImportId, i.Title, i.Artist, i.Album, i.Reason })
            .ToListAsync(cancellationToken);

        int Count(Guid id, DlnaImportItemStatus? status) =>
            counts.Where(c => c.ImportId == id && (status is null || c.Status == status)).Sum(c => c.Count);

        return imports
            .OrderByDescending(i => i.Import.CreatedAt)
            .Select(i => new DlnaImportJob(
                i.Import.Id,
                i.Import.ServerName,
                (JsonSerializer.Deserialize<List<DlnaContainerChoice>>(i.Import.Containers, JsonSerializerOptions.Web) ?? []).Select(c => c.Title).ToList(),
                i.LibraryId,
                i.LibraryName,
                i.Import.Status,
                Count(i.Import.Id, null),
                Count(i.Import.Id, DlnaImportItemStatus.Copied),
                Count(i.Import.Id, DlnaImportItemStatus.Duplicate),
                Count(i.Import.Id, DlnaImportItemStatus.Failed),
                i.Import.CopiedBytes,
                i.Import.CreatedAt,
                i.Import.FinishedAt,
                i.Import.ErrorCode,
                failures.Where(f => f.ImportId == i.Import.Id).Take(50)
                    .Select(f => new DlnaImportFailure(f.Title, f.Artist, f.Album, f.Reason ?? ErrorCodes.DownloadFailed)).ToList()))
            .ToList();
    }

    private static Uri ParseLocation(string? location) =>
        Uri.TryCreate(location?.Trim(), UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https"
            ? uri
            : throw ArcanistException.Field("location", ErrorCodes.InvalidFormat);

    private static DlnaMediaServer ToModel(MediaServerDevice server) =>
        new(server.Id, server.Name, server.Manufacturer, server.Model, server.Location.ToString());
}
