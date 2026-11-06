using System.Runtime.InteropServices;
using Arcanist.Core.Data;
using Arcanist.Core.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Arcanist.Core.Analysis;

/// <summary>
/// Finds tracks that are the same song, after analysis and in a sweep of the catalog (<see cref="DuplicateSweepWorker"/>).
/// Tags decide most duplicates at scan time; this catches the copies whose tags differ (a "(Remastered)" title, a missing
/// track number) and the same song on several albums.
/// <list type="bullet">
/// <item>Same album, same length (±3 s), similarity ≥ <see cref="MergeThreshold"/>, and no conflicting track
/// numbers: merged into one track.</item>
/// <item>Other albums, similarity ≥ <see cref="MergeThreshold"/>: the tracks share one <see cref="Recording"/>
/// (unless their MusicBrainz recordings differ), which keeps statistics together, and are proposed: keeping both copies
/// or one file for both albums is an administrator's choice.</item>
/// <item>Similarity from <see cref="CandidateThreshold"/>, or the very same audio: proposed.</item>
/// <item>The same name (<see cref="DuplicateNames"/>), artist and length (±3 s), with a similarity from
/// <see cref="SameNameThreshold"/> or no fingerprint to compare (S3, Ultimate Cloud): proposed, with similarity 0 when
/// nothing could be compared.</item>
/// </list>
/// </summary>
public sealed class DuplicateDetector(TimeProvider time, ILogger<DuplicateDetector> logger)
{
    /// <summary>Different encodings of the same audio score 0.95–1; unrelated songs around 0.1–0.3.</summary>
    public const double MergeThreshold = 0.85;

    /// <summary>A 15 % bit error rate: below it, quiet or sustained music produces too many false matches.</summary>
    public const double CandidateThreshold = 0.7;

    /// <summary>Another master of a song falls below <see cref="CandidateThreshold"/>; with its name and length to back it, this is enough to ask.</summary>
    public const double SameNameThreshold = 0.5;

    private const int DurationToleranceMs = 3000;
    private const int CandidateDurationToleranceMs = 7000;

    public sealed record Outcome(int Merged, int Linked, int Proposed);

    /// <summary>Looks for duplicates of the tracks that have audio with these content hashes.</summary>
    public async Task<Outcome> ProcessAsync(ArcanistDbContext db, IReadOnlyCollection<string> contentHashes, CancellationToken cancellationToken)
    {
        var trackIds = await db.MediaFiles.AsNoTracking()
            .Where(f => f.MissingSince == null && contentHashes.Contains(f.ContentHash))
            .Select(f => f.TrackId)
            .Distinct()
            .ToListAsync(cancellationToken);
        return await ProcessTracksAsync(db, trackIds, cancellationToken);
    }

    /// <summary>Looks for duplicates of these tracks.</summary>
    public async Task<Outcome> ProcessTracksAsync(ArcanistDbContext db, IReadOnlyCollection<Guid> trackIds, CancellationToken cancellationToken)
    {
        int merged = 0, linked = 0, proposed = 0;
        foreach (var trackId in trackIds)
        {
            var track = await db.Tracks.AsNoTracking().Where(t => t.Id == trackId)
                .Select(t => new TrackFacts(t.Id, t.AlbumId, t.DurationMs, t.TrackNumber, t.DiscNumber, t.AddedAt, t.RecordingId))
                .SingleOrDefaultAsync(cancellationToken);
            // Merged away earlier in this run.
            if (track is null)
                continue;

            var fingerprints = (await FingerprintsAsync(db, [track.Id], cancellationToken)).GetValueOrDefault(track.Id);
            var hashes = await HashesAsync(db, track.Id, cancellationToken);

            foreach (var candidate in await CandidatesAsync(db, track, hashes, cancellationToken))
            {
                var candidatePrints = (await FingerprintsAsync(db, [candidate.Id], cancellationToken)).GetValueOrDefault(candidate.Id);
                var theirHashes = await HashesAsync(db, candidate.Id, cancellationToken);
                double? similarity = hashes.Overlaps(theirHashes) ? 1
                    : fingerprints is null || candidatePrints is null || !AudioAnalyzer.IsAvailable ? null
                    : fingerprints.SelectMany(a => candidatePrints.Select(b => AudioAnalyzer.Compare(a, b))).Max();

                if (similarity >= MergeThreshold && CanMerge(track, candidate))
                {
                    var (keep, remove) = Prefer(track, candidate) ? (track.Id, candidate.Id) : (candidate.Id, track.Id);
                    await TrackMerger.MergeAsync(db, keep, remove, cancellationToken);
                    logger.LogInformation("Merged track {Removed} into {Kept} (fingerprint similarity {Similarity:0.00})", remove, keep, similarity);
                    merged++;
                    if (remove == track.Id)
                        break;
                    continue;
                }

                if (similarity >= MergeThreshold && candidate.AlbumId != track.AlbumId
                    && await LinkRecordingsAsync(db, track.RecordingId, candidate.RecordingId, cancellationToken))
                    linked++;

                if (similarity >= CandidateThreshold
                    || ((similarity is null || similarity >= SameNameThreshold) && await SameSongByNameAsync(db, track, candidate, similarity is null, cancellationToken)))
                {
                    if (await ProposeAsync(db, track.Id, candidate.Id, similarity ?? 0, cancellationToken))
                        proposed++;
                }
            }
        }

        return new Outcome(merged, linked, proposed);
    }

    private static async Task<HashSet<string>> HashesAsync(ArcanistDbContext db, Guid trackId, CancellationToken cancellationToken) =>
        (await db.MediaFiles.AsNoTracking().Where(f => f.TrackId == trackId && f.MissingSince == null).Select(f => f.ContentHash).ToListAsync(cancellationToken)).ToHashSet();

    /// <summary>
    /// The same name, artist and length. With nothing to compare (<paramref name="unheard"/>), not while a local file of
    /// either is still to be analyzed: its fingerprint will decide, in a moment.
    /// </summary>
    private static async Task<bool> SameSongByNameAsync(ArcanistDbContext db, TrackFacts a, TrackFacts b, bool unheard, CancellationToken cancellationToken)
    {
        if (Math.Abs(a.DurationMs - b.DurationMs) > DurationToleranceMs)
            return false;

        Guid[] ids = [a.Id, b.Id];
        if (unheard && await db.MediaFiles.AnyAsync(f => ids.Contains(f.TrackId) && f.MissingSince == null && f.LibrarySource.Kind == LibrarySourceKind.Local
                                                         && !db.AudioAnalyses.Any(x => x.ContentHash == f.ContentHash), cancellationToken))
            return false;

        var titles = await db.Tracks.AsNoTracking().Where(t => ids.Contains(t.Id)).Select(t => t.NormalizedTitle).ToListAsync(cancellationToken);
        if (titles.Count != 2 || DuplicateNames.CoreTitle(titles[0]) != DuplicateNames.CoreTitle(titles[1]))
            return false;

        var artists = await db.TrackArtists.AsNoTracking().Where(ta => ids.Contains(ta.TrackId))
            .Select(ta => new { ta.TrackId, ta.Artist.NormalizedName }).ToListAsync(cancellationToken);
        return DuplicateNames.SameArtist(
            artists.Where(x => x.TrackId == a.Id).Select(x => x.NormalizedName).ToList(),
            artists.Where(x => x.TrackId == b.Id).Select(x => x.NormalizedName).ToList());
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

    /// <summary>
    /// Tracks that could plausibly be the same song: the same audio, or the same album, name or artist and a similar
    /// length. The name, without what says which pressing it is, so tags that differ do not hide a song on another album.
    /// </summary>
    private static async Task<List<TrackFacts>> CandidatesAsync(ArcanistDbContext db, TrackFacts track, HashSet<string> hashes, CancellationToken cancellationToken)
    {
        var title = await db.Tracks.Where(t => t.Id == track.Id).Select(t => t.NormalizedTitle).SingleAsync(cancellationToken);
        var core = DuplicateNames.CoreTitle(title);
        var coreAndMore = core + " ";
        var artists = await db.TrackArtists.Where(ta => ta.TrackId == track.Id).Select(ta => ta.ArtistId).ToListAsync(cancellationToken);
        var (shortest, longest) = (track.DurationMs - CandidateDurationToleranceMs, track.DurationMs + CandidateDurationToleranceMs);

        return await db.Tracks.AsNoTracking()
            .Where(t => t.Id != track.Id)
            .Where(t => t.Files.Any(f => hashes.Contains(f.ContentHash))
                        || (t.DurationMs >= shortest && t.DurationMs <= longest
                            && (t.AlbumId == track.AlbumId || t.NormalizedTitle == title || t.NormalizedTitle == core || t.NormalizedTitle.StartsWith(coreAndMore)
                                || db.TrackArtists.Any(ta => ta.TrackId == t.Id && artists.Contains(ta.ArtistId)))))
            .Where(t => t.Files.Any(f => f.MissingSince == null))
            // Tracks of the same album first (only those can be merged automatically), then those of the same name.
            .OrderByDescending(t => t.AlbumId == track.AlbumId).ThenByDescending(t => t.NormalizedTitle == title).ThenBy(t => t.Id)
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

        await MoveUserDataAsync(db, keep, remove, cancellationToken);
        await db.MediaFiles.Where(f => f.TrackId == remove)
            .ExecuteUpdateAsync(s => s.SetProperty(f => f.TrackId, keep).SetProperty(f => f.TrackLocked, true), cancellationToken);
        // A track that played the other's file has that file itself now.
        var own = await db.MediaFiles.AsNoTracking().Where(f => f.TrackId == keep)
            .Select(f => new { f.Id, f.LibrarySourceId, f.RelativePath, f.CueTrack }).ToListAsync(cancellationToken);
        var redundant = own.Where(f => f.CueTrack < 0 && own.Any(o => o.CueTrack == 0 && o.LibrarySourceId == f.LibrarySourceId && o.RelativePath == f.RelativePath))
            .Select(f => f.Id).ToList();
        if (redundant.Count > 0)
            await db.MediaFiles.Where(f => redundant.Contains(f.Id)).ExecuteDeleteAsync(cancellationToken);
        await db.DuplicateCandidates.Where(c => c.Status == DuplicateStatus.Open && (c.TrackAId == remove || c.TrackBId == remove))
            .ExecuteDeleteAsync(cancellationToken);
        await db.TrackArtists.Where(ta => ta.TrackId == remove).ExecuteDeleteAsync(cancellationToken);
        await db.TrackGenres.Where(tg => tg.TrackId == remove).ExecuteDeleteAsync(cancellationToken);
        await db.Tracks.Where(t => t.Id == remove).ExecuteDeleteAsync(cancellationToken);

        if (transaction is not null)
            await transaction.CommitAsync(cancellationToken);
    }

    /// <summary>
    /// Moves what users did with <paramref name="remove"/> to <paramref name="keep"/>: playlist entries and plays
    /// move; ratings and play counts combine (the best rating, the sum of plays) where a user has both.
    /// </summary>
    private static async Task MoveUserDataAsync(ArcanistDbContext db, Guid keep, Guid remove, CancellationToken cancellationToken)
    {
        await db.PlaylistEntries.Where(e => e.TrackId == remove).ExecuteUpdateAsync(s => s.SetProperty(e => e.TrackId, keep), cancellationToken);
        await db.PlayEvents.Where(p => p.TrackId == remove).ExecuteUpdateAsync(s => s.SetProperty(p => p.TrackId, keep), cancellationToken);

        var ratings = await db.TrackRatings.Where(r => r.TrackId == remove || r.TrackId == keep).ToListAsync(cancellationToken);
        foreach (var user in ratings.GroupBy(r => r.UserId))
        {
            var removed = user.FirstOrDefault(r => r.TrackId == remove);
            if (removed is null)
                continue;

            db.TrackRatings.Remove(removed);
            if (user.FirstOrDefault(r => r.TrackId == keep) is { } kept)
                kept.Rating = Math.Max(kept.Rating, removed.Rating);
            else
                db.TrackRatings.Add(new TrackRating { UserId = removed.UserId, TrackId = keep, Rating = removed.Rating, RatedAt = removed.RatedAt });
        }

        var stats = await db.TrackPlayStats.Where(s => s.TrackId == remove || s.TrackId == keep).ToListAsync(cancellationToken);
        foreach (var user in stats.GroupBy(s => s.UserId))
        {
            var removed = user.FirstOrDefault(s => s.TrackId == remove);
            if (removed is null)
                continue;

            db.TrackPlayStats.Remove(removed);
            if (user.FirstOrDefault(s => s.TrackId == keep) is { } kept)
            {
                kept.PlayCount += removed.PlayCount;
                kept.SkipCount += removed.SkipCount;
                kept.LastPlayedAt = new[] { kept.LastPlayedAt, removed.LastPlayedAt }.Max();
            }
            else
            {
                db.TrackPlayStats.Add(new TrackPlayStats
                {
                    UserId = removed.UserId, TrackId = keep, PlayCount = removed.PlayCount, SkipCount = removed.SkipCount, LastPlayedAt = removed.LastPlayedAt,
                });
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
