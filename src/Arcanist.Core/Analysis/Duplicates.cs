using System.Runtime.InteropServices;
using Arcanist.Core.Data;
using Arcanist.Core.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Arcanist.Core.Analysis;

/// <summary>
/// Finds tracks with the same audio by comparing Chromaprint fingerprints, after analysis. Tags decide most
/// duplicates at scan time; fingerprints catch the copies whose tags differ (a "(Remastered)" title, a missing track
/// number) and the same recording on several albums.
/// <list type="bullet">
/// <item>Same album, same length (±3 s), similarity ≥ <see cref="MergeThreshold"/>, and no conflicting track
/// numbers: merged into one track.</item>
/// <item>Other albums, similarity ≥ <see cref="MergeThreshold"/>: the tracks share one <see cref="Recording"/>
/// (unless their MusicBrainz recordings differ), which keeps statistics together.</item>
/// <item>Similarity from <see cref="CandidateThreshold"/>: proposed to administrators as a
/// <see cref="DuplicateCandidate"/>.</item>
/// </list>
/// </summary>
public sealed class DuplicateDetector(TimeProvider time, ILogger<DuplicateDetector> logger)
{
    /// <summary>Different encodings of the same audio score 0.95–1; unrelated songs around 0.1–0.3.</summary>
    public const double MergeThreshold = 0.85;

    /// <summary>A 15 % bit error rate: below it, quiet or sustained music produces too many false matches.</summary>
    public const double CandidateThreshold = 0.7;

    private const int DurationToleranceMs = 3000;
    private const int CandidateDurationToleranceMs = 7000;

    public sealed record Outcome(int Merged, int Linked, int Proposed);

    /// <summary>Looks for duplicates of the tracks that have audio with these content hashes.</summary>
    public async Task<Outcome> ProcessAsync(ArcanistDbContext db, IReadOnlyCollection<string> contentHashes, CancellationToken cancellationToken)
    {
        int merged = 0, linked = 0, proposed = 0;
        var trackIds = await db.MediaFiles.AsNoTracking()
            .Where(f => f.MissingSince == null && contentHashes.Contains(f.ContentHash))
            .Select(f => f.TrackId)
            .Distinct()
            .ToListAsync(cancellationToken);

        foreach (var trackId in trackIds)
        {
            var track = await db.Tracks.AsNoTracking().Where(t => t.Id == trackId)
                .Select(t => new TrackFacts(t.Id, t.AlbumId, t.DurationMs, t.TrackNumber, t.DiscNumber, t.AddedAt, t.RecordingId))
                .SingleOrDefaultAsync(cancellationToken);
            // Merged away earlier in this run.
            if (track is null)
                continue;

            var own = await FingerprintsAsync(db, [track.Id], cancellationToken);
            if (!own.TryGetValue(track.Id, out var fingerprints))
                continue;

            foreach (var candidate in await CandidatesAsync(db, track, cancellationToken))
            {
                var theirs = await FingerprintsAsync(db, [candidate.Id], cancellationToken);
                if (!theirs.TryGetValue(candidate.Id, out var candidatePrints))
                    continue;

                var similarity = fingerprints.SelectMany(a => candidatePrints.Select(b => AudioAnalyzer.Compare(a, b))).Max();
                if (similarity >= MergeThreshold && CanMerge(track, candidate))
                {
                    var (keep, remove) = Prefer(track, candidate) ? (track.Id, candidate.Id) : (candidate.Id, track.Id);
                    await TrackMerger.MergeAsync(db, keep, remove, cancellationToken);
                    logger.LogInformation("Merged track {Removed} into {Kept} (fingerprint similarity {Similarity:0.00})", remove, keep, similarity);
                    merged++;
                    if (remove == track.Id)
                        break;
                }
                else if (similarity >= MergeThreshold && candidate.AlbumId != track.AlbumId)
                {
                    if (await LinkRecordingsAsync(db, track.RecordingId, candidate.RecordingId, cancellationToken))
                        linked++;
                }
                else if (similarity >= CandidateThreshold && await ProposeAsync(db, track.Id, candidate.Id, similarity, cancellationToken))
                {
                    proposed++;
                }
            }
        }

        return new Outcome(merged, linked, proposed);
    }

    /// <summary>
    /// Whether <paramref name="a"/> should be the track that stays: the one added first, so the one users may already
    /// know keeps its identity; among tracks added together, the one whose tags place it on the album.
    /// </summary>
    private static bool Prefer(TrackFacts a, TrackFacts b) =>
        a.AddedAt != b.AddedAt ? a.AddedAt < b.AddedAt
        : (a.TrackNumber is null) != (b.TrackNumber is null) ? a.TrackNumber is not null
        : a.Id.CompareTo(b.Id) < 0;

    private sealed record TrackFacts(Guid Id, Guid AlbumId, int DurationMs, int? TrackNumber, int? DiscNumber, DateTime AddedAt, Guid RecordingId);

    private static bool CanMerge(TrackFacts a, TrackFacts b) =>
        a.AlbumId == b.AlbumId
        && Math.Abs(a.DurationMs - b.DurationMs) <= DurationToleranceMs
        // An album listing the same audio twice (two numbers) means it, so that is for an administrator to decide.
        && (a.TrackNumber is null || b.TrackNumber is null || (a.TrackNumber == b.TrackNumber && (a.DiscNumber ?? 1) == (b.DiscNumber ?? 1)));

    /// <summary>Tracks that could plausibly be the same song: same album, title or artist, and a similar length.</summary>
    private static async Task<List<TrackFacts>> CandidatesAsync(ArcanistDbContext db, TrackFacts track, CancellationToken cancellationToken)
    {
        var title = await db.Tracks.Where(t => t.Id == track.Id).Select(t => t.NormalizedTitle).SingleAsync(cancellationToken);
        var artists = await db.TrackArtists.Where(ta => ta.TrackId == track.Id).Select(ta => ta.ArtistId).ToListAsync(cancellationToken);
        var (shortest, longest) = (track.DurationMs - CandidateDurationToleranceMs, track.DurationMs + CandidateDurationToleranceMs);

        return await db.Tracks.AsNoTracking()
            .Where(t => t.Id != track.Id && t.DurationMs >= shortest && t.DurationMs <= longest)
            .Where(t => t.AlbumId == track.AlbumId || t.NormalizedTitle == title
                        || db.TrackArtists.Any(ta => ta.TrackId == t.Id && artists.Contains(ta.ArtistId)))
            .Where(t => t.Files.Any(f => f.MissingSince == null))
            .Select(t => new TrackFacts(t.Id, t.AlbumId, t.DurationMs, t.TrackNumber, t.DiscNumber, t.AddedAt, t.RecordingId))
            .Take(200)
            .ToListAsync(cancellationToken);
    }

    private static async Task<Dictionary<Guid, List<uint[]>>> FingerprintsAsync(ArcanistDbContext db, List<Guid> trackIds, CancellationToken cancellationToken)
    {
        var rows = await db.MediaFiles.AsNoTracking()
            .Where(f => trackIds.Contains(f.TrackId) && f.MissingSince == null)
            .Join(db.AudioAnalyses.Where(a => a.Fingerprint != null), f => f.ContentHash, a => a.ContentHash, (f, a) => new { f.TrackId, a.ContentHash, a.Fingerprint })
            .ToListAsync(cancellationToken);

        return rows.DistinctBy(r => (r.TrackId, r.ContentHash))
            .GroupBy(r => r.TrackId)
            .ToDictionary(g => g.Key, g => g.Select(r => MemoryMarshal.Cast<byte, uint>(r.Fingerprint!).ToArray()).ToList());
    }

    /// <summary>Makes two tracks share a recording, unless MusicBrainz says they are different recordings.</summary>
    private static async Task<bool> LinkRecordingsAsync(ArcanistDbContext db, Guid a, Guid b, CancellationToken cancellationToken)
    {
        if (a == b)
            return false;

        var mbids = await db.Recordings.Where(r => r.Id == a || r.Id == b).ToDictionaryAsync(r => r.Id, r => r.MusicBrainzId, cancellationToken);
        var (mbidA, mbidB) = (mbids.GetValueOrDefault(a), mbids.GetValueOrDefault(b));
        if (mbidA is not null && mbidB is not null && mbidA != mbidB)
            return false;

        // Keep the recording that MusicBrainz identifies, if one does.
        var (keep, drop) = mbidB is not null && mbidA is null ? (b, a) : (a, b);
        await db.Tracks.Where(t => t.RecordingId == drop).ExecuteUpdateAsync(s => s.SetProperty(t => t.RecordingId, keep), cancellationToken);
        return true;
    }

    private async Task<bool> ProposeAsync(ArcanistDbContext db, Guid a, Guid b, double similarity, CancellationToken cancellationToken)
    {
        var (first, second) = a.CompareTo(b) < 0 ? (a, b) : (b, a);
        if (await db.DuplicateCandidates.AnyAsync(c => c.TrackAId == first && c.TrackBId == second, cancellationToken))
            return false;

        db.DuplicateCandidates.Add(new DuplicateCandidate
        {
            TrackAId = first,
            TrackBId = second,
            Similarity = Math.Round(similarity, 3),
            Status = DuplicateStatus.Open,
            DetectedAt = time.GetUtcNow().UtcDateTime,
        });
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }
}

/// <summary>Merges one track into another: its files move over (locked there) and it disappears.</summary>
public static class TrackMerger
{
    public static async Task MergeAsync(ArcanistDbContext db, Guid keep, Guid remove, CancellationToken cancellationToken)
    {
        if (keep == remove)
            return;

        await using var transaction = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(cancellationToken) : null;

        // User data attached to tracks (plays, ratings, playlists) must move here too once it exists.
        await db.MediaFiles.Where(f => f.TrackId == remove)
            .ExecuteUpdateAsync(s => s.SetProperty(f => f.TrackId, keep).SetProperty(f => f.TrackLocked, true), cancellationToken);
        await db.DuplicateCandidates.Where(c => c.Status == DuplicateStatus.Open && (c.TrackAId == remove || c.TrackBId == remove))
            .ExecuteDeleteAsync(cancellationToken);
        await db.TrackArtists.Where(ta => ta.TrackId == remove).ExecuteDeleteAsync(cancellationToken);
        await db.TrackGenres.Where(tg => tg.TrackId == remove).ExecuteDeleteAsync(cancellationToken);
        await db.Tracks.Where(t => t.Id == remove).ExecuteDeleteAsync(cancellationToken);

        if (transaction is not null)
            await transaction.CommitAsync(cancellationToken);
    }
}
