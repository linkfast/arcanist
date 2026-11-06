using Arcanist.Core.Analysis;
using Arcanist.Core.Data;
using Arcanist.Core.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Arcanist.Core.Application.Catalog;

/// <summary>Why the two tracks of a pair cannot play one file.</summary>
public enum DuplicateShareBlock
{
    /// <summary>They are on the same album: joining them is what applies.</summary>
    SameAlbum,

    /// <summary>The caller may not delete tracks.</summary>
    NoPermission,

    /// <summary>They have no copies in one library folder: the file of one would leave the other's listeners without the song.</summary>
    DifferentLibraries,

    /// <summary>The copies cannot be changed: another server's, Ultimate Cloud's, a library the caller only listens to, or split by a cue sheet.</summary>
    ReadOnly,
}

/// <summary>Two tracks that may be the same song, for an administrator to decide about. Part of the API contract.</summary>
/// <param name="Similarity">Fingerprint similarity, 0–1 (1 for the very same audio). Pairs above 0.85 on the same album are
/// merged automatically. 0 when only their name, artist and length say so: there was no fingerprint to compare.</param>
/// <param name="ShareableFileIds">The files, of either track, that both could play: see <c>shareDuplicateFile</c>.</param>
/// <param name="ShareBlocked">Why none can, when <paramref name="ShareableFileIds"/> is empty.</param>
public sealed record DuplicatePair(
    Guid Id, Track TrackA, Track TrackB, double Similarity, DuplicateStatus Status, DateTime DetectedAt,
    IReadOnlyList<Guid> ShareableFileIds, DuplicateShareBlock? ShareBlocked);

/// <summary>Reviews what duplicate detection found. Requires MANAGE_LIBRARIES.</summary>
public sealed class DuplicateService(
    ArcanistDbContext db, AccessGuard guard, CatalogService catalog, TrackEditService edits, DuplicateSweep sweep, TimeProvider time)
{
    public const int MaxResults = 200;

    /// <summary>
    /// Pairs in the given state (open by default), most similar first, where the caller can see both tracks. Merged
    /// pairs never appear: one of their tracks no longer exists.
    /// </summary>
    public async Task<IReadOnlyList<DuplicatePair>> ListAsync(DuplicateStatus? status, int? first, int? skip, CancellationToken cancellationToken)
    {
        var access = guard.Permission(Permissions.ManageLibraries);
        var wanted = status ?? DuplicateStatus.Open;
        var candidates = await db.DuplicateCandidates.AsNoTracking()
            .Where(c => c.Status == wanted)
            .OrderByDescending(c => c.Similarity).ThenBy(c => c.Id)
            .Skip(Math.Max(skip ?? 0, 0))
            .Take(Math.Clamp(first ?? 50, 1, MaxResults))
            .ToListAsync(cancellationToken);

        var ids = candidates.SelectMany(c => new[] { c.TrackAId, c.TrackBId }).Distinct().ToList();
        var tracks = await catalog.VisibleTracksAsync(access, ids, cancellationToken);
        var files = (await catalog.VisibleFiles(access).AsNoTracking().Where(f => ids.Contains(f.TrackId))
                .Select(f => new ShareFile(f.Id, f.TrackId, f.LibrarySourceId, f.LibrarySource.LibraryId, f.LibrarySource.Kind, f.CueTrack))
                .ToListAsync(cancellationToken))
            .ToLookup(f => f.TrackId);

        return candidates
            .Where(c => tracks.ContainsKey(c.TrackAId) && tracks.ContainsKey(c.TrackBId))
            .Select(c =>
            {
                var (shareable, blocked) = ShareOptions(access, tracks[c.TrackAId], tracks[c.TrackBId], files[c.TrackAId].ToList(), files[c.TrackBId].ToList());
                return new DuplicatePair(c.Id, tracks[c.TrackAId], tracks[c.TrackBId], c.Similarity, c.Status, c.DetectedAt, shareable, blocked);
            })
            .ToList();
    }

    private sealed record ShareFile(Guid Id, Guid TrackId, Guid SourceId, Guid LibraryId, LibrarySourceKind Kind, int CueTrack);

    private static (IReadOnlyList<Guid> Files, DuplicateShareBlock? Blocked) ShareOptions(Auth.UserAccess access, Track a, Track b, List<ShareFile> filesA, List<ShareFile> filesB)
    {
        if (a.Album.Id == b.Album.Id)
            return ([], DuplicateShareBlock.SameAlbum);
        if (!access.Has(Permissions.DeleteTracks))
            return ([], DuplicateShareBlock.NoPermission);

        var inOneFolder = filesA.Any(x => filesB.Any(y => x.SourceId == y.SourceId));
        bool Can(ShareFile f, List<ShareFile> others) =>
            f.CueTrack == 0 && f.Kind is LibrarySourceKind.Local or LibrarySourceKind.S3 && access.CanChange(f.LibraryId)
            && others.Any(o => o.SourceId == f.SourceId && o.CueTrack == 0);
        var shareable = filesA.Where(f => Can(f, filesB)).Concat(filesB.Where(f => Can(f, filesA))).Select(f => f.Id).ToList();
        return shareable.Count > 0 ? (shareable, null) : ([], inOneFolder ? DuplicateShareBlock.ReadOnly : DuplicateShareBlock.DifferentLibraries);
    }

    /// <summary>
    /// Both tracks of the pair stay, each on its album, and play <paramref name="fileId"/> (a file of either): the other's
    /// copy in that folder goes to the trash. Needs DELETE_TRACKS too.
    /// </summary>
    public async Task ShareFileAsync(Guid id, Guid fileId, CancellationToken cancellationToken)
    {
        var (access, candidate) = await OpenAsync(id, cancellationToken);
        var owner = await db.MediaFiles.AsNoTracking().Where(f => f.Id == fileId).Select(f => (Guid?)f.TrackId).SingleOrDefaultAsync(cancellationToken);
        if (owner != candidate.TrackAId && owner != candidate.TrackBId)
            throw ArcanistException.Field("fileId", ErrorCodes.InvalidFormat);

        await edits.ShareFileAsync(fileId, owner == candidate.TrackAId ? candidate.TrackBId : candidate.TrackAId, cancellationToken);
        Resolve(candidate, DuplicateStatus.Shared, access.UserId);
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Looks through the whole catalog again, in the background, for pairs not proposed yet (dismissed ones stay so).</summary>
    public async Task RescanAsync(CancellationToken cancellationToken)
    {
        guard.Permission(Permissions.ManageLibraries);
        await sweep.RestartAsync(cancellationToken);
    }

    /// <summary>Merges the pair into <paramref name="keepTrackId"/> (one of its two tracks), which keeps its metadata.</summary>
    public async Task<Track> MergeAsync(Guid id, Guid keepTrackId, CancellationToken cancellationToken)
    {
        var (access, candidate) = await OpenAsync(id, cancellationToken);
        if (keepTrackId != candidate.TrackAId && keepTrackId != candidate.TrackBId)
            throw ArcanistException.Field("keepTrackId", ErrorCodes.InvalidFormat);

        var remove = keepTrackId == candidate.TrackAId ? candidate.TrackBId : candidate.TrackAId;
        Resolve(candidate, DuplicateStatus.Merged, access.UserId);
        await db.SaveChangesAsync(cancellationToken);
        await TrackMerger.MergeAsync(db, keepTrackId, remove, cancellationToken);

        var albumId = await db.Tracks.Where(t => t.Id == keepTrackId).Select(t => t.AlbumId).SingleAsync(cancellationToken);
        await AlbumLoudness.UpdateAsync(db, [albumId], cancellationToken);
        return await catalog.TrackAsync(keepTrackId, cancellationToken);
    }

    /// <summary>Keeps the two tracks apart; the pair is not proposed again.</summary>
    public async Task DismissAsync(Guid id, CancellationToken cancellationToken)
    {
        var (access, candidate) = await OpenAsync(id, cancellationToken);
        Resolve(candidate, DuplicateStatus.Dismissed, access.UserId);
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<(Auth.UserAccess Access, DuplicateCandidate Candidate)> OpenAsync(Guid id, CancellationToken cancellationToken)
    {
        var access = guard.Permission(Permissions.ManageLibraries);
        var candidate = await db.DuplicateCandidates.SingleOrDefaultAsync(c => c.Id == id && c.Status == DuplicateStatus.Open, cancellationToken)
                        ?? throw new ArcanistException.NotFound();

        // Both tracks must be visible to the caller, or the pair does not exist for them.
        var visible = await catalog.VisibleTracksAsync(access, [candidate.TrackAId, candidate.TrackBId], cancellationToken);
        return visible.Count == 2 ? (access, candidate) : throw new ArcanistException.NotFound();
    }

    private void Resolve(DuplicateCandidate candidate, DuplicateStatus status, Guid userId)
    {
        candidate.Status = status;
        candidate.ResolvedAt = time.GetUtcNow().UtcDateTime;
        candidate.ResolvedByUserId = userId;
    }
}
