using Arcanist.Core.Data;
using Arcanist.Core.Data.Entities;
using Arcanist.Core.Storage;
using Microsoft.EntityFrameworkCore;

namespace Arcanist.Core.Application.Libraries;

// Types in this file are part of the public API contracts (GraphQL schema and OpenAPI document).

/// <summary>The music of one library the user can access.</summary>
/// <param name="Files">Files present (an album in one file split by a cue sheet counts once).</param>
/// <param name="Tracks">Tracks with a copy in it.</param>
/// <param name="Bytes">What those files take.</param>
/// <param name="FreeBytes">Free space of the disk its folder is on; null when unknown (S3, other servers, Ultimate Cloud).</param>
/// <param name="TotalBytes">Size of that disk; null when unknown.</param>
/// <param name="CanUpload">The user may upload to it (see <c>Library.canUpload</c>).</param>
public sealed record LibraryStorageUsage(
    Guid LibraryId, string Name, LibrarySourceKind Kind, int Files, int Tracks, long Bytes, long? FreeBytes, long? TotalBytes, bool CanUpload);

/// <summary>How much music the libraries the user can access hold, for a "storage used" display.</summary>
/// <param name="Libraries">Each library, in the order of <c>libraries</c>.</param>
/// <param name="Files">Files of all of them.</param>
/// <param name="Bytes">What they take (a file in two libraries counts twice: they are two files).</param>
/// <param name="UploadLibraryId">Where the user's uploads that name no library go now (<c>myUploadSettings</c>), or null.</param>
/// <param name="UploadFreeBytes">Free space for uploads there, when known.</param>
public sealed record MyStorage(IReadOnlyList<LibraryStorageUsage> Libraries, int Files, long Bytes, Guid? UploadLibraryId, long? UploadFreeBytes);

/// <summary>The storage used by the music the caller can play, library by library (any signed-in user).</summary>
public sealed class MyStorageService(ArcanistDbContext db, AccessGuard guard, ILibraryStorageResolver storages, CloudLibraries cloud,
    UploadDestinations uploads)
{
    public async Task<MyStorage> UsageAsync(CancellationToken cancellationToken)
    {
        var access = guard.Authenticated();
        var libraries = await LibraryService.Visible(db, access).AsNoTracking().Include(l => l.Sources).OrderBy(l => l.Number)
            .ToListAsync(cancellationToken);
        var ids = libraries.Select(l => l.Id).ToList();

        var files = await db.MediaFiles.AsNoTracking()
            .Where(f => f.MissingSince == null && ids.Contains(f.LibrarySource.LibraryId))
            .GroupBy(f => f.LibrarySource.LibraryId)
            .Select(g => new
            {
                LibraryId = g.Key,
                Files = g.Count(f => f.CueTrack <= 1),
                Bytes = g.Where(f => f.CueTrack <= 1).Sum(f => f.Size),
                Tracks = g.Select(f => f.TrackId).Distinct().Count(),
            })
            .ToDictionaryAsync(g => g.LibraryId, cancellationToken);

        var usage = libraries.Select(library =>
        {
            var music = files.GetValueOrDefault(library.Id);
            var local = library.Sources.FirstOrDefault(s => s.Kind == LibrarySourceKind.Local);
            var space = local is null ? null : storages.For(local.Kind).Space(StorageLocation.From(local));
            var model = library.ToModel(false, access, cloud);
            return new LibraryStorageUsage(library.Id, library.Name, library.Sources.OrderBy(s => s.Kind).FirstOrDefault()?.Kind ?? LibrarySourceKind.Local,
                music?.Files ?? 0, music?.Tracks ?? 0, music?.Bytes ?? 0, space?.FreeBytes, space?.TotalBytes, model.CanUpload);
        }).ToList();

        var target = await uploads.EffectiveAsync(access, cancellationToken);
        return new MyStorage(usage, usage.Sum(l => l.Files), usage.Sum(l => l.Bytes), target,
            usage.FirstOrDefault(l => l.LibraryId == target)?.FreeBytes);
    }
}
