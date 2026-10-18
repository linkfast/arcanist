using Arcanist.Core.Application.Catalog;
using Arcanist.Core.Application.Libraries;
using Arcanist.Core.Application.Listening;
using Arcanist.Core.Auth;
using Arcanist.Core.Capabilities;
using Arcanist.Core.Cloud;
using Arcanist.Core.Data;
using Arcanist.Core.Data.Entities;
using Track = Arcanist.Core.Application.Catalog.Track;
using Microsoft.EntityFrameworkCore;

namespace Arcanist.Core.Application.Uploads;

// Types in this file are part of the public API contracts (GraphQL schema and OpenAPI document).

public enum MyUploadKind
{
    /// <summary>A file into a library of this server.</summary>
    Library,

    /// <summary>A song of the catalog into the user's own Ultimate Cloud library.</summary>
    UltimateCloud,
}

public enum MyUploadStatus
{
    /// <summary>In the library, waiting for its scan (a few seconds).</summary>
    Processing,

    /// <summary>In the library's catalog: <c>track</c> is the song.</summary>
    Added,

    /// <summary>The library already had this audio.</summary>
    Duplicate,

    /// <summary>The library was scanned but the song is not in its catalog (moved or deleted since, or unreadable).</summary>
    NotAdded,

    /// <summary>Waiting for its turn to go to Ultimate Cloud.</summary>
    Queued,

    /// <summary>Going to Ultimate Cloud.</summary>
    Uploading,

    /// <summary>In the user's Ultimate Cloud library.</summary>
    Uploaded,

    /// <summary>The user's Ultimate Cloud library already had it.</summary>
    AlreadyInCloud,

    /// <summary>It could not be sent: <c>errorCode</c> says why.</summary>
    Failed,
}

/// <param name="Name">The file's name (into a library) or the song's title (into Ultimate Cloud).</param>
/// <param name="Track">The song, when the user can see it: once catalogued, or the one sent to Ultimate Cloud.</param>
/// <param name="ErrorCode">Why it failed: <c>download_not_allowed</c>, <c>cloud_quota_exceeded</c>, <c>unreadable</c>…</param>
/// <param name="Source">The extension that brought the file (such as <c>youtube</c>); null when the user sent it.</param>
public sealed record MyUpload(
    Guid Id,
    MyUploadKind Kind,
    MyUploadStatus Status,
    string Name,
    long? Size,
    Guid? LibraryId,
    string? LibraryName,
    Track? Track,
    string? ErrorCode,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    string? Source = null);

/// <param name="Active">How many are still processing, queued or uploading: worth asking again in a few seconds.</param>
public sealed record MyUploadPage(IReadOnlyList<MyUpload> Items, string? NextCursor, int TotalCount, int Active);

/// <param name="TrackIds">Up to 1000 songs of this server's catalog.</param>
public sealed record UploadTracksToMyCloudInput(IReadOnlyList<Guid> TrackIds);

/// <summary>
/// The caller's history of uploads: files they sent into libraries (and whether each is in the catalog yet) and
/// songs they sent to their own Ultimate Cloud library, which go one at a time in the background. Sending a song to
/// Ultimate Cloud gives away a copy of it, so it needs the DOWNLOAD permission and, for a library another server
/// shares, that server's permission to download.
/// </summary>
public sealed class MyUploadService(
    ArcanistDbContext db,
    AccessGuard guard,
    CatalogService catalog,
    UploadScans scans,
    UltimateCloudAccount server,
    UserCloudAccounts accounts,
    IServiceProvider services,
    TimeProvider time)
{
    public const int MaxTracks = 1000;

    public async Task<MyUploadPage> UploadsAsync(int? first, string? after, CancellationToken cancellationToken)
    {
        var access = guard.Authenticated();
        await ResolveAsync(access.UserId, cancellationToken);

        var mine = db.UserUploads.AsNoTracking().Where(u => u.UserId == access.UserId);
        var total = await mine.CountAsync(cancellationToken);
        var active = await mine.CountAsync(u => u.Status == UserUploadStatus.Processing || u.Status == UserUploadStatus.Queued
                                                || u.Status == UserUploadStatus.Uploading, cancellationToken);
        if (after is not null)
        {
            var (at, id) = TimeCursor.Decode(after);
            mine = mine.Where(u => u.CreatedAt < at || (u.CreatedAt == at && u.Id.CompareTo(id) < 0));
        }

        var size = ListeningService.PageSize(first);
        var page = await mine.OrderByDescending(u => u.CreatedAt).ThenByDescending(u => u.Id).Take(size + 1).ToListAsync(cancellationToken);
        var next = page.Count > size ? TimeCursor.Encode(page[size - 1].CreatedAt, page[size - 1].Id) : null;
        page = page.Take(size).ToList();

        var tracks = await catalog.VisibleTracksAsync(access, page.Where(u => u.TrackId is not null).Select(u => u.TrackId!.Value).Distinct().ToList(),
            cancellationToken);
        var libraryIds = page.Where(u => u.LibraryId is not null).Select(u => u.LibraryId!.Value).Distinct().ToList();
        var libraries = (await db.Libraries.AsNoTracking().Where(l => libraryIds.Contains(l.Id))
                .Select(l => new { l.Id, l.Name, l.VisibleToAll, l.OwnerUserId })
                .ToListAsync(cancellationToken))
            .Where(l => access.CanAccessLibrary(l.Id) || (l.VisibleToAll && l.OwnerUserId == null) || l.OwnerUserId == access.UserId)
            .ToDictionary(l => l.Id, l => l.Name);
        return new MyUploadPage(page.Select(u => new MyUpload(u.Id, (MyUploadKind)u.Kind, (MyUploadStatus)u.Status, u.Name, u.Size, u.LibraryId,
                u.LibraryId is { } library ? libraries.GetValueOrDefault(library) : null,
                u.TrackId is { } track ? tracks.GetValueOrDefault(track) : null,
                u.ErrorCode, u.CreatedAt, u.UpdatedAt, u.Source)).ToList(),
            next, total, active);
    }

    /// <summary>Forgets the finished ones; what is still on its way stays.</summary>
    /// <returns>How many were forgotten.</returns>
    public async Task<int> ClearAsync(CancellationToken cancellationToken)
    {
        var user = guard.Authenticated().UserId;
        await ResolveAsync(user, cancellationToken);
        return await db.UserUploads
            .Where(u => u.UserId == user && u.Status != UserUploadStatus.Processing && u.Status != UserUploadStatus.Queued
                        && u.Status != UserUploadStatus.Uploading)
            .ExecuteDeleteAsync(cancellationToken);
    }

    /// <summary>
    /// Queues songs for the caller's own Ultimate Cloud library. Each gets a row in their history: queued, or failed
    /// at once when it cannot go (only shared by a server that does not allow downloads, only part of a CUE album).
    /// </summary>
    public async Task<IReadOnlyList<MyUpload>> UploadToMyCloudAsync(UploadTracksToMyCloudInput input, CancellationToken cancellationToken)
    {
        var access = guard.Permission(Permissions.Download);
        if (!server.Enabled)
            throw new ArcanistException.Conflict(ErrorCodes.CloudDisabled);
        if (accounts.TierOf(access.UserId) == CloudTier.None)
            throw new ArcanistException.Conflict(ErrorCodes.CloudNotLinked);
        if (input.TrackIds is not { Count: > 0 and <= MaxTracks })
            throw ArcanistException.Field("trackIds", ErrorCodes.InvalidFormat);

        var ids = input.TrackIds.Distinct().ToList();
        var files = await catalog.VisibleFiles(access).AsNoTracking()
            .Where(f => ids.Contains(f.TrackId))
            .Select(f => new
            {
                f.Id, f.TrackId, f.CueTrack, f.BitDepth, f.SampleRate, f.BitRate, f.Size,
                f.LibrarySource.Kind, f.LibrarySource.Location, f.LibrarySource.LibraryId, Owner = f.LibrarySource.Library.OwnerUserId,
                f.Track.Title,
            })
            .ToListAsync(cancellationToken);
        var remotes = files.Where(f => f.Kind == LibrarySourceKind.Peer && Guid.TryParse(f.Location, out _)).Select(f => Guid.Parse(f.Location)).Distinct().ToList();
        var downloadable = (await db.P2PRemotes.AsNoTracking().Where(r => remotes.Contains(r.Id)).Select(r => new { r.Id, r.Rules }).ToListAsync(cancellationToken))
            .Where(r => r.Rules.AllowDownload).Select(r => r.Id).ToHashSet();

        var now = time.GetUtcNow().UtcDateTime;
        var rows = new List<UserUpload>();
        foreach (var id in ids)
        {
            var copies = files.Where(f => f.TrackId == id).ToList();
            if (copies.Count == 0)
                throw new ArcanistException.NotFound();

            var row = new UserUpload
            {
                UserId = access.UserId, Kind = UserUploadKind.UltimateCloud, Name = Cut(copies[0].Title), TrackId = id, CreatedAt = now, UpdatedAt = now,
            };
            // Already in their own Ultimate Cloud library.
            if (copies.FirstOrDefault(f => f.Kind == LibrarySourceKind.UltimateCloud && f.Owner == access.UserId) is { } own)
            {
                row.Status = UserUploadStatus.AlreadyInCloud;
                row.LibraryId = own.LibraryId;
            }
            else
            {
                // The best whole copy that can be read: local first, then S3, Ultimate Cloud, and other servers that allow downloads.
                var usable = copies
                    .Where(f => f.CueTrack == 0 && (f.Kind != LibrarySourceKind.Peer || (Guid.TryParse(f.Location, out var remote) && downloadable.Contains(remote))))
                    .OrderBy(f => f.Kind switch { LibrarySourceKind.Local => 0, LibrarySourceKind.S3 => 1, LibrarySourceKind.UltimateCloud => 2, _ => 3 })
                    .ThenByDescending(f => f.BitDepth).ThenByDescending(f => f.SampleRate).ThenByDescending(f => f.BitRate)
                    .FirstOrDefault();
                if (usable is null)
                {
                    row.Status = UserUploadStatus.Failed;
                    row.LibraryId = copies[0].LibraryId;
                    row.ErrorCode = copies.All(f => f.CueTrack != 0) ? ErrorCodes.CloudUploadCue : ErrorCodes.DownloadNotAllowed;
                }
                else
                {
                    row.Status = UserUploadStatus.Queued;
                    row.LibraryId = usable.LibraryId;
                    row.FileId = usable.Id;
                    row.Size = usable.Size;
                }
            }

            rows.Add(row);
        }

        db.UserUploads.AddRange(rows);
        await db.SaveChangesAsync(cancellationToken);
        services.GetService<MyCloudUploadWorker>()?.Wake();

        var tracks = await catalog.VisibleTracksAsync(access, ids, cancellationToken);
        var libraryIds = rows.Where(r => r.LibraryId is not null).Select(r => r.LibraryId!.Value).Distinct().ToList();
        var libraries = await db.Libraries.AsNoTracking().Where(l => libraryIds.Contains(l.Id)).ToDictionaryAsync(l => l.Id, l => l.Name, cancellationToken);
        return rows.Select(u => new MyUpload(u.Id, (MyUploadKind)u.Kind, (MyUploadStatus)u.Status, u.Name, u.Size, u.LibraryId,
            u.LibraryId is { } library ? libraries.GetValueOrDefault(library) : null, tracks.GetValueOrDefault(u.TrackId!.Value), u.ErrorCode,
            u.CreatedAt, u.UpdatedAt, u.Source)).ToList();
    }

    /// <summary>
    /// Brings the uploads into libraries up to date: one waiting for its scan is added once the library has its
    /// audio, and not added once the library has no scan coming and still lacks it.
    /// </summary>
    private async Task ResolveAsync(Guid user, CancellationToken cancellationToken)
    {
        var waiting = await db.UserUploads
            .Where(u => u.UserId == user && u.Kind == UserUploadKind.Library && u.ContentHash != null && u.LibraryId != null
                        && (u.Status == UserUploadStatus.Processing || (u.Status == UserUploadStatus.Duplicate && u.TrackId == null)))
            .ToListAsync(cancellationToken);
        if (waiting.Count == 0)
            return;

        var libraryIds = waiting.Select(u => u.LibraryId!.Value).Distinct().ToList();
        var hashes = waiting.Select(u => u.ContentHash!).Distinct().ToList();
        var found = await db.MediaFiles.AsNoTracking()
            .Where(f => f.MissingSince == null && hashes.Contains(f.ContentHash) && libraryIds.Contains(f.LibrarySource.LibraryId))
            .Select(f => new { f.ContentHash, f.LibrarySource.LibraryId, f.TrackId })
            .ToListAsync(cancellationToken);
        var scanning = (await db.ScanJobs.AsNoTracking()
                .Where(j => libraryIds.Contains(j.LibraryId) && (j.Status == ScanStatus.Queued || j.Status == ScanStatus.Running))
                .Select(j => j.LibraryId)
                .ToListAsync(cancellationToken))
            .ToHashSet();

        var now = time.GetUtcNow().UtcDateTime;
        foreach (var upload in waiting)
        {
            var library = upload.LibraryId!.Value;
            if (found.FirstOrDefault(f => f.ContentHash == upload.ContentHash && f.LibraryId == library) is { } file)
            {
                upload.TrackId = file.TrackId;
                if (upload.Status == UserUploadStatus.Processing)
                    upload.Status = UserUploadStatus.Added;
                upload.UpdatedAt = now;
            }
            else if (upload.Status == UserUploadStatus.Processing && !scanning.Contains(library) && !scans.IsPending(library, upload.ContentHash!))
            {
                upload.Status = UserUploadStatus.NotAdded;
                upload.UpdatedAt = now;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    internal static string Cut(string? name) =>
        string.IsNullOrEmpty(name) ? "?" : name.Length <= 500 ? name : name[..500];
}
