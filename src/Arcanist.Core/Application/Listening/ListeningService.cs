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
        return await catalog.TrackAsync(trackId, cancellationToken);
    }

    /// <summary>Stars a track (rates it 5) or unstars it (clears a 5). Works in both rating modes.</summary>
    public async Task<Track> StarTrackAsync(Guid trackId, bool starred, CancellationToken cancellationToken)
    {
        var access = guard.Authenticated();
        if (starred)
            return await RateTrackAsync(trackId, 5, cancellationToken);

        await EnsureVisibleAsync(access, trackId, cancellationToken);
        await db.TrackRatings.Where(r => r.UserId == access.UserId && r.TrackId == trackId && r.Rating == 5).ExecuteDeleteAsync(cancellationToken);
        return await catalog.TrackAsync(trackId, cancellationToken);
    }

    /// <summary>
    /// The user's rated tracks with at least <paramref name="minRating"/> (5 for starred ones), most recently rated
    /// first. In single-star mode only starred tracks are listed.
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

    private async Task EnsureVisibleAsync(UserAccess access, Guid trackId, CancellationToken cancellationToken)
    {
        if (!await catalog.Visible(access).AnyAsync(t => t.Id == trackId, cancellationToken))
            throw new ArcanistException.NotFound();
    }

    internal static int PageSize(int? first) => Math.Clamp(first ?? DefaultPageSize, 1, MaxPageSize);

    private DateTime Now() => time.GetUtcNow().UtcDateTime;
}
