using Arcanist.Core.Application.Catalog;
using Arcanist.Core.Auth;
using Arcanist.Core.Data;
using Arcanist.Core.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Track = Arcanist.Core.Application.Catalog.Track;

namespace Arcanist.Core.Application.Listening;

/// <summary>
/// What a user does with tracks: ratings and stars, plays reported by players and their history. Every user
/// manages their own; only tracks they can see can be rated or played.
/// </summary>
public sealed class ListeningService(
    ArcanistDbContext db,
    AccessGuard guard,
    CatalogService catalog,
    RatingSettings ratings,
    ListeningSignals signals,
    TimeProvider time)
{
    public const int DefaultPageSize = 50;
    public const int MaxPageSize = 500;

    /// <summary>Plays reported later than this (from a player that was offline) are refused.</summary>
    public static readonly TimeSpan MaxReportDelay = TimeSpan.FromDays(14);

    /// <summary>Plays reported within this of an identical one are the same play sent twice.</summary>
    private static readonly TimeSpan DuplicateWindow = TimeSpan.FromSeconds(2);

    private const int MinCountedMs = 30_000;
    private const int CountedAfterMs = 240_000;

    public Task<RatingMode> RatingModeAsync(CancellationToken cancellationToken) => ratings.GetAsync(cancellationToken);

    /// <summary>Changes how users rate tracks. Nothing stored is lost: switching back shows lower ratings again.</summary>
    public async Task<RatingMode> SetRatingModeAsync(RatingMode mode, CancellationToken cancellationToken)
    {
        guard.Permission(Permissions.ManageSettings);
        await ratings.SetAsync(mode, cancellationToken);
        return mode;
    }

    /// <summary>Rates a track 1–5, or clears its rating with null. In single-star mode only 5 (a star) is accepted.</summary>
    public Task<Track> RateTrackAsync(Guid trackId, int? rating, CancellationToken cancellationToken) =>
        RateTrackAsync(trackId, rating, keepUnseen: false, cancellationToken);

    /// <param name="keepUnseen">
    /// In single-star mode, also accept ratings below 5, which are kept but not shown (apps that rate in stars, like
    /// mStream's, with a rating below the server's star threshold).
    /// </param>
    internal async Task<Track> RateTrackAsync(Guid trackId, int? rating, bool keepUnseen, CancellationToken cancellationToken)
    {
        var access = guard.Authenticated();
        if (rating is not null && (rating is < 1 or > 5 || !keepUnseen && await ratings.GetAsync(cancellationToken) == RatingMode.Star && rating != 5))
            throw ArcanistException.Field("rating", ErrorCodes.InvalidFormat);

        await EnsureVisibleAsync(access, trackId, cancellationToken);
        var existing = await db.TrackRatings.SingleOrDefaultAsync(r => r.UserId == access.UserId && r.TrackId == trackId, cancellationToken);
        if (rating is null)
        {
            if (existing is not null)
                db.TrackRatings.Remove(existing);
        }
        else if (existing is null)
            db.TrackRatings.Add(new TrackRating { UserId = access.UserId, TrackId = trackId, Rating = rating.Value, RatedAt = Now() });
        else
        {
            existing.Rating = rating.Value;
            existing.RatedAt = Now();
        }

        await db.SaveChangesAsync(cancellationToken);
        signals.OnRated(access.UserId);
        return await catalog.TrackAsync(trackId, cancellationToken);
    }

    /// <summary>Makes a track one of the user's StarTracks (rates it 5), or not (clears a 5). Works in both rating modes.</summary>
    public async Task<Track> StarTrackAsync(Guid trackId, bool starred, CancellationToken cancellationToken)
    {
        var access = guard.Authenticated();
        if (starred)
            return await RateTrackAsync(trackId, 5, cancellationToken);

        await EnsureVisibleAsync(access, trackId, cancellationToken);
        await db.TrackRatings.Where(r => r.UserId == access.UserId && r.TrackId == trackId && r.Rating == 5).ExecuteDeleteAsync(cancellationToken);
        signals.OnRated(access.UserId);
        return await catalog.TrackAsync(trackId, cancellationToken);
    }

    public const int MaxStarTracksChange = 1000;

    /// <summary>The user's StarTracks, most recently starred first.</summary>
    public async Task<StarTrackPage> StarTracksAsync(string? after, int? first, CancellationToken cancellationToken)
    {
        var access = guard.Authenticated();
        var visible = catalog.Visible(access);
        var starred = db.TrackRatings.AsNoTracking()
            .Where(r => r.UserId == access.UserId && r.Rating == 5 && visible.Any(t => t.Id == r.TrackId));
        var total = await starred.CountAsync(cancellationToken);
        if (after is not null)
        {
            var (at, id) = TimeCursor.Decode(after);
            starred = starred.Where(r => r.RatedAt < at || (r.RatedAt == at && r.TrackId.CompareTo(id) < 0));
        }

        var size = PageSize(first);
        var page = await starred.OrderByDescending(r => r.RatedAt).ThenByDescending(r => r.TrackId)
            .Select(r => new { r.TrackId, r.RatedAt, r.Track.Recording.AcoustId })
            .Take(size + 1)
            .ToListAsync(cancellationToken);
        var next = page.Count > size ? TimeCursor.Encode(page[size - 1].RatedAt, page[size - 1].TrackId) : null;
        page = page.Take(size).ToList();

        var tracks = await catalog.VisibleTracksAsync(access, page.Select(r => r.TrackId).ToList(), cancellationToken);
        return new StarTrackPage(
            page.Where(r => tracks.ContainsKey(r.TrackId)).Select(r => new StarTrack(tracks[r.TrackId], r.AcoustId?.ToString(), r.RatedAt)).ToList(),
            next,
            total);
    }

    /// <summary>
    /// Adds songs to the user's StarTracks, by track or by AcoustID (one of the song's tracks; none if one already is).
    /// What an Ultimate app brings from Ultimate Cloud's StarTracks matches by AcoustID.
    /// </summary>
    public Task<StarTracksResult> AddStarTracksAsync(StarTracksInput input, CancellationToken cancellationToken) =>
        ChangeStarTracksAsync(input, star: true, cancellationToken);

    /// <summary>Takes songs from the user's StarTracks, by track or by AcoustID (every track of the song).</summary>
    public Task<StarTracksResult> RemoveStarTracksAsync(StarTracksInput input, CancellationToken cancellationToken) =>
        ChangeStarTracksAsync(input, star: false, cancellationToken);

    /// <summary>Follows the user's StarTracks: a notice each time they (or other ratings of theirs) change.</summary>
    public IObservable<StarTracksChange> WatchStarTracks() => signals.StarTracksOf(guard.Authenticated().UserId, time);

    private async Task<StarTracksResult> ChangeStarTracksAsync(StarTracksInput input, bool star, CancellationToken cancellationToken)
    {
        var access = guard.Authenticated();
        if (input.Items.Count > MaxStarTracksChange)
            throw ArcanistException.Field("items", ErrorCodes.TooMany);
        var acoustIds = new Dictionary<Guid, string>();
        foreach (var item in input.Items.Where(i => i.TrackId is null))
        {
            if (!Guid.TryParse(item.AcoustId, out var acoustId))
                throw ArcanistException.Field("items", ErrorCodes.InvalidFormat);
            acoustIds[acoustId] = item.AcoustId!;
        }

        var ids = input.Items.Where(i => i.TrackId is not null).Select(i => i.TrackId!.Value).Distinct().ToList();
        var songs = acoustIds.Keys.ToList();
        var visible = catalog.Visible(access);
        var found = (await visible.Where(t => ids.Contains(t.Id)).Select(t => t.Id).ToListAsync(cancellationToken)).ToHashSet();
        var bySong = (await visible.Where(t => t.Recording.AcoustId != null && songs.Contains(t.Recording.AcoustId.Value))
                .Select(t => new { t.Id, AcoustId = t.Recording.AcoustId!.Value, t.AddedAt })
                .ToListAsync(cancellationToken))
            .GroupBy(t => t.AcoustId)
            .ToDictionary(g => g.Key, g => g.OrderBy(t => t.AddedAt).Select(t => t.Id).ToList());
        var notFound = ids.Where(id => !found.Contains(id)).Select(id => id.ToString())
            .Concat(acoustIds.Where(a => !bySong.ContainsKey(a.Key)).Select(a => a.Value))
            .ToList();

        var everyTrack = found.Concat(bySong.Values.SelectMany(t => t)).Distinct().ToList();
        var ratings = await db.TrackRatings.Where(r => r.UserId == access.UserId && everyTrack.Contains(r.TrackId))
            .ToDictionaryAsync(r => r.TrackId, cancellationToken);
        var changed = 0;
        if (star)
        {
            // By song: the track that came first, unless one of them already is a StarTrack (or is starred in this call).
            var targets = found.Concat(bySong.Values
                    .Where(tracks => !tracks.Any(id => found.Contains(id) || ratings.TryGetValue(id, out var r) && r.Rating == 5))
                    .Select(tracks => tracks[0]))
                .Distinct();
            foreach (var trackId in targets)
            {
                if (ratings.TryGetValue(trackId, out var rating))
                {
                    if (rating.Rating == 5)
                        continue;
                    rating.Rating = 5;
                    rating.RatedAt = Now();
                }
                else
                    db.TrackRatings.Add(new TrackRating { UserId = access.UserId, TrackId = trackId, Rating = 5, RatedAt = Now() });
                changed++;
            }
        }
        else
        {
            foreach (var rating in ratings.Values.Where(r => r.Rating == 5))
            {
                db.TrackRatings.Remove(rating);
                changed++;
            }
        }

        if (changed > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
            signals.OnRated(access.UserId);
        }

        return new StarTracksResult(changed, notFound);
    }

    /// <summary>
    /// The user's rated tracks with at least <paramref name="minRating"/> (5 for StarTracks), most recently rated
    /// first. In single-star mode only StarTracks are listed.
    /// </summary>
    public async Task<RatedTrackPage> RatedTracksAsync(int? minRating, string? after, int? first, CancellationToken cancellationToken)
    {
        var access = guard.Authenticated();
        var min = Math.Clamp(minRating ?? 1, 1, 5);
        if (await ratings.GetAsync(cancellationToken) == RatingMode.Star)
            min = 5;

        var visible = catalog.Visible(access);
        var rated = db.TrackRatings.AsNoTracking()
            .Where(r => r.UserId == access.UserId && r.Rating >= min && visible.Any(t => t.Id == r.TrackId));
        var total = await rated.CountAsync(cancellationToken);
        if (after is not null)
        {
            var (at, id) = TimeCursor.Decode(after);
            rated = rated.Where(r => r.RatedAt < at || (r.RatedAt == at && r.TrackId.CompareTo(id) < 0));
        }

        var size = PageSize(first);
        var page = await rated.OrderByDescending(r => r.RatedAt).ThenByDescending(r => r.TrackId).Take(size + 1).ToListAsync(cancellationToken);
        var next = page.Count > size ? TimeCursor.Encode(page[size - 1].RatedAt, page[size - 1].TrackId) : null;
        page = page.Take(size).ToList();

        var tracks = await catalog.VisibleTracksAsync(access, page.Select(r => r.TrackId).ToList(), cancellationToken);
        return new RatedTrackPage(
            page.Where(r => tracks.ContainsKey(r.TrackId)).Select(r => new RatedTrack(tracks[r.TrackId], r.Rating, r.RatedAt)).ToList(),
            next,
            total);
    }

    /// <summary>
    /// Records that the user listened to a track. Whether it counts as a play is decided here, so every player
    /// counts the same way: at least 30 seconds, and half the track or four minutes.
    /// </summary>
    public Task<RecordedPlay> RecordPlayAsync(RecordPlayInput input, CancellationToken cancellationToken) =>
        RecordPlayAsync(guard.Authenticated(), input, cancellationToken);

    /// <summary>Records a play for a user outside a request of theirs (a renderer playing their queue).</summary>
    internal Task<RecordedPlay> RecordPlayAsync(UserAccess access, RecordPlayInput input, CancellationToken cancellationToken) =>
        RecordPlayAsync(access, input, null, null, cancellationToken);

    /// <param name="externalId">The id the player gave the play: one already recorded for the user is a duplicate.</param>
    /// <param name="source">How the play was queued, as the player says.</param>
    internal async Task<RecordedPlay> RecordPlayAsync(UserAccess access, RecordPlayInput input, string? externalId, string? source, CancellationToken cancellationToken)
    {
        var now = Now();
        var playedAt = input.PlayedAt?.ToUniversalTime() ?? now;

        var errors = new ValidationErrors();
        if (playedAt > now.AddMinutes(5) || playedAt < now - MaxReportDelay)
            errors.Add("playedAt", ErrorCodes.InvalidFormat);
        if (input.PlayedMs < 0)
            errors.Add("playedMs", ErrorCodes.InvalidFormat);
        if (input.Client is { Length: > 128 })
            errors.Add("client", ErrorCodes.InvalidFormat);
        errors.ThrowIfAny();

        var durationMs = await catalog.Visible(access).Where(t => t.Id == input.TrackId).Select(t => (int?)t.DurationMs).SingleOrDefaultAsync(cancellationToken)
                         ?? throw new ArcanistException.NotFound();

        // A player resending a report it did not see acknowledged.
        var duplicate = await db.PlayEvents.AsNoTracking()
            .Where(p => p.UserId == access.UserId
                        && (externalId != null && p.ExternalId == externalId
                            || p.TrackId == input.TrackId && p.PlayedAt >= playedAt - DuplicateWindow && p.PlayedAt <= playedAt + DuplicateWindow))
            .FirstOrDefaultAsync(cancellationToken);
        if (duplicate is not null)
            return new RecordedPlay(duplicate.Id, duplicate.Counted, duplicate.Skipped, Duplicate: true);

        // Never more than the track (a paused player may report wall time).
        var playedMs = durationMs > 0 ? Math.Min(input.PlayedMs, durationMs) : input.PlayedMs;
        var counted = playedMs >= Math.Min(MinCountedMs, durationMs) && playedMs >= Math.Min(durationMs / 2, CountedAfterMs);
        var skipped = !counted && playedMs < MinCountedMs && durationMs > 2 * MinCountedMs;

        var play = new PlayEvent
        {
            UserId = access.UserId,
            TrackId = input.TrackId,
            PlayedAt = playedAt,
            PlayedMs = playedMs,
            Counted = counted,
            Skipped = skipped,
            Client = string.IsNullOrWhiteSpace(input.Client) ? null : input.Client.Trim(),
            ExternalId = externalId,
            Source = source,
        };
        db.PlayEvents.Add(play);

        if (counted || skipped)
        {
            var stats = await db.TrackPlayStats.SingleOrDefaultAsync(s => s.UserId == access.UserId && s.TrackId == input.TrackId, cancellationToken);
            if (stats is null)
                db.TrackPlayStats.Add(stats = new TrackPlayStats { UserId = access.UserId, TrackId = input.TrackId });
            if (counted)
            {
                stats.PlayCount++;
                if (stats.LastPlayedAt is null || stats.LastPlayedAt < playedAt)
                    stats.LastPlayedAt = playedAt;
            }
            else
                stats.SkipCount++;
        }

        await db.SaveChangesAsync(cancellationToken);
        signals.OnPlayed();
        return new RecordedPlay(play.Id, counted, skipped);
    }

    /// <summary>The user's counted plays, newest first.</summary>
    public async Task<PlayHistoryPage> HistoryAsync(string? after, int? first, CancellationToken cancellationToken)
    {
        var access = guard.Authenticated();
        var plays = db.PlayEvents.AsNoTracking().Where(p => p.UserId == access.UserId && p.Counted && p.TrackId != null);
        if (after is not null)
        {
            var (at, id) = TimeCursor.Decode(after);
            plays = plays.Where(p => p.PlayedAt < at || (p.PlayedAt == at && p.Id.CompareTo(id) < 0));
        }

        var size = PageSize(first);
        var page = await plays.OrderByDescending(p => p.PlayedAt).ThenByDescending(p => p.Id).Take(size + 1).ToListAsync(cancellationToken);
        var next = page.Count > size ? TimeCursor.Encode(page[size - 1].PlayedAt, page[size - 1].Id) : null;
        page = page.Take(size).ToList();

        // Tracks the user can no longer see leave gaps rather than errors.
        var tracks = await catalog.VisibleTracksAsync(access, page.Select(p => p.TrackId!.Value).Distinct().ToList(), cancellationToken);
        return new PlayHistoryPage(
            page.Where(p => tracks.ContainsKey(p.TrackId!.Value)).Select(p => new PlayHistoryItem(p.Id, tracks[p.TrackId!.Value], p.PlayedAt, p.PlayedMs)).ToList(),
            next);
    }

    public const int MaxImportEntries = 1000;

    /// <summary>
    /// Brings ratings and play counts an app kept on its own. Ratings only fill gaps (what the user rated here
    /// stays); play counts keep the highest, so importing twice changes nothing. Plays add no history.
    /// </summary>
    public async Task<ListeningImportResult> ImportAsync(ImportListeningInput input, CancellationToken cancellationToken)
    {
        var access = guard.Authenticated();
        var importedRatings = input.Ratings ?? [];
        var importedPlays = input.PlayCounts ?? [];
        if (importedRatings.Count > MaxImportEntries)
            throw ArcanistException.Field("ratings", ErrorCodes.TooMany);
        if (importedPlays.Count > MaxImportEntries)
            throw ArcanistException.Field("playCounts", ErrorCodes.TooMany);
        if (importedRatings.Any(r => r.Rating is < 1 or > 5))
            throw ArcanistException.Field("ratings", ErrorCodes.InvalidFormat);
        if (importedPlays.Any(p => p.PlayCount < 0))
            throw ArcanistException.Field("playCounts", ErrorCodes.InvalidFormat);

        var ids = importedRatings.Select(r => r.TrackId).Concat(importedPlays.Select(p => p.TrackId)).Distinct().ToList();
        var visible = (await catalog.Visible(access).Where(t => ids.Contains(t.Id)).Select(t => t.Id).ToListAsync(cancellationToken)).ToHashSet();
        var ignored = importedRatings.Count(r => !visible.Contains(r.TrackId)) + importedPlays.Count(p => !visible.Contains(p.TrackId));

        var rated = (await db.TrackRatings.Where(r => r.UserId == access.UserId && ids.Contains(r.TrackId)).Select(r => r.TrackId).ToListAsync(cancellationToken)).ToHashSet();
        var added = 0;
        foreach (var rating in importedRatings.Where(r => visible.Contains(r.TrackId)).DistinctBy(r => r.TrackId))
        {
            if (!rated.Add(rating.TrackId))
                continue;
            db.TrackRatings.Add(new TrackRating { UserId = access.UserId, TrackId = rating.TrackId, Rating = rating.Rating, RatedAt = Now() });
            added++;
        }

        var stats = await db.TrackPlayStats.Where(p => p.UserId == access.UserId && ids.Contains(p.TrackId)).ToDictionaryAsync(p => p.TrackId, cancellationToken);
        var raised = 0;
        foreach (var plays in importedPlays.Where(p => visible.Contains(p.TrackId)).GroupBy(p => p.TrackId))
        {
            var count = plays.Max(p => p.PlayCount);
            var last = plays.Max(p => p.LastPlayedAt);
            if (!stats.TryGetValue(plays.Key, out var existing))
            {
                if (count == 0)
                    continue;
                db.TrackPlayStats.Add(new TrackPlayStats { UserId = access.UserId, TrackId = plays.Key, PlayCount = count, LastPlayedAt = last });
                raised++;
                continue;
            }

            if (last > existing.LastPlayedAt || existing.LastPlayedAt is null && last is not null)
                existing.LastPlayedAt = last;
            if (count > existing.PlayCount)
            {
                existing.PlayCount = count;
                raised++;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        if (added > 0)
            signals.OnRated(access.UserId);
        return new ListeningImportResult(added, raised, ignored);
    }

    private async Task EnsureVisibleAsync(UserAccess access, Guid trackId, CancellationToken cancellationToken)
    {
        if (!await catalog.Visible(access).AnyAsync(t => t.Id == trackId, cancellationToken))
            throw new ArcanistException.NotFound();
    }

    internal static int PageSize(int? first) => Math.Clamp(first ?? DefaultPageSize, 1, MaxPageSize);

    private DateTime Now() => time.GetUtcNow().UtcDateTime;
}
