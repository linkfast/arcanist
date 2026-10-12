using Arcanist.Core.Analysis;
using Arcanist.Core.Data;
using Arcanist.Core.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Arcanist.Core.Application.Catalog;

/// <summary>Two tracks that may be the same audio, for an administrator to merge or keep apart. Part of the API contract.</summary>
/// <param name="Similarity">Fingerprint similarity, 0–1. Pairs above 0.85 on the same album are merged automatically.</param>
public sealed record DuplicatePair(Guid Id, Track TrackA, Track TrackB, double Similarity, DuplicateStatus Status, DateTime DetectedAt);

/// <summary>Reviews what duplicate detection was not sure about. Requires MANAGE_LIBRARIES.</summary>
public sealed class DuplicateService(ArcanistDbContext db, AccessGuard guard, CatalogService catalog, TimeProvider time)
{
    public const int MaxResults = 200;

    /// <summary>
    /// Pairs in the given state (open by default), most similar first, where the caller can see both tracks. Merged
    /// pairs never appear: one of their tracks no longer exists.
    /// </summary>
    public async Task<IReadOnlyList<DuplicatePair>> ListAsync(DuplicateStatus? status, int? first, CancellationToken cancellationToken)
    {
        var access = guard.Permission(Permissions.ManageLibraries);
        var wanted = status ?? DuplicateStatus.Open;
        var candidates = await db.DuplicateCandidates.AsNoTracking()
            .Where(c => c.Status == wanted)
            .OrderByDescending(c => c.Similarity).ThenBy(c => c.Id)
            .Take(Math.Clamp(first ?? 50, 1, MaxResults))
            .ToListAsync(cancellationToken);

        var tracks = await catalog.VisibleTracksAsync(access, candidates.SelectMany(c => new[] { c.TrackAId, c.TrackBId }).Distinct().ToList(), cancellationToken);
        return candidates
            .Where(c => tracks.ContainsKey(c.TrackAId) && tracks.ContainsKey(c.TrackBId))
            .Select(c => new DuplicatePair(c.Id, tracks[c.TrackAId], tracks[c.TrackBId], c.Similarity, c.Status, c.DetectedAt))
            .ToList();
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
