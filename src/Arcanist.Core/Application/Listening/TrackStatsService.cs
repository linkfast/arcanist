using Arcanist.Core.Ai;
using Arcanist.Core.Application.Catalog;
using Arcanist.Core.Auth;
using Arcanist.Core.Data;
using Arcanist.Core.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Arcanist.Core.Application.Listening;

// Types in this file are part of the public API contracts (GraphQL schema and OpenAPI document).

public enum TrackStatsScope
{
    /// <summary>The user's own listening.</summary>
    Me,

    /// <summary>Everyone's on this server together, without saying who.</summary>
    Server,
}

/// <summary>How a song is listened to, for a DJ's memory.</summary>
/// <param name="Plays">Counted plays (30 seconds, and half the song or four minutes).</param>
/// <param name="Skips">Stopped within the first 30 seconds of a song longer than a minute.</param>
/// <param name="AvgSkipPosition">Seconds into the song where skips happen, on average.</param>
/// <param name="Completions">Listened to the end (90% of it, or to its last 10 seconds).</param>
/// <param name="AvgListenRatio">0–1: how much of the song is listened to, on average.</param>
/// <param name="HourHistogram">Counted plays by hour of the day (24 values, in the time zone asked for).</param>
public sealed record TrackListeningStats(
    Guid TrackId,
    int Plays,
    int Skips,
    double? AvgSkipPosition,
    int Completions,
    double? AvgListenRatio,
    DateTime? LastPlayedAt,
    IReadOnlyList<int> HourHistogram);

/// <param name="NextCursor">Pass as <c>after</c> for the next page; null on the last.</param>
public sealed record TrackStatsPage(IReadOnlyList<TrackListeningStats> Items, string? NextCursor);

/// <summary>
/// Listening statistics per song, from the plays players report: the user's own, or the server's (everyone's
/// together, nobody's in particular; administrators may turn that off for clients in the AI settings, except for
/// those with VIEW_ALL_STATS). Only songs the user can see. Pages go from the least to the most recently played, so a
/// client keeps up by asking with <c>updatedSince</c> set to the last <c>lastPlayedAt</c> it has.
/// </summary>
public sealed class TrackStatsService(ArcanistDbContext db, AccessGuard guard, CatalogService catalog, AiSettings settings)
{
    public const int MaxPageSize = 500;

    public async Task<TrackStatsPage> StatsAsync(TrackStatsScope? scope, DateTime? updatedSince, string? after, int? first, string? timeZone,
        CancellationToken cancellationToken)
    {
        var access = guard.Authenticated();
        var server = scope == TrackStatsScope.Server;
        await RequireScopeAsync(access, server, cancellationToken);
        var zone = StatsService.TimeZone(timeZone);

        var plays = Plays(access, server);
        var tracks = plays.GroupBy(p => p.TrackId!.Value).Select(g => new { TrackId = g.Key, Last = g.Max(p => p.PlayedAt) });
        if (updatedSince is { } since)
        {
            var utc = since.ToUniversalTime();
            tracks = tracks.Where(t => t.Last > utc);
        }

        if (after is not null)
        {
            var (at, id) = TimeCursor.Decode(after);
            tracks = tracks.Where(t => t.Last > at || (t.Last == at && t.TrackId.CompareTo(id) > 0));
        }

        var size = Math.Clamp(first ?? 100, 1, MaxPageSize);
        var page = await tracks.OrderBy(t => t.Last).ThenBy(t => t.TrackId).Take(size + 1).ToListAsync(cancellationToken);
        var next = page.Count > size ? TimeCursor.Encode(page[size - 1].Last, page[size - 1].TrackId) : null;
        page = page.Take(size).ToList();

        var stats = await ComputeAsync(access, server, page.Select(p => p.TrackId).ToList(), zone, cancellationToken);
        return new TrackStatsPage(page.Select(p => stats[p.TrackId]).ToList(), next);
    }

    /// <summary>The user's statistics for some songs (for analyses with <c>include=stats</c>).</summary>
    internal Task<Dictionary<Guid, TrackListeningStats>> ForTracksAsync(UserAccess access, IReadOnlyCollection<Guid> trackIds, CancellationToken cancellationToken) =>
        ComputeAsync(access, false, trackIds, TimeZoneInfo.Utc, cancellationToken);

    private async Task RequireScopeAsync(UserAccess access, bool server, CancellationToken cancellationToken)
    {
        if (server && !access.Has(Permissions.ViewAllStats) && !(await settings.GetAsync(cancellationToken)).ShareServerStats)
            throw new ArcanistException.Forbidden();
    }

    private IQueryable<PlayEvent> Plays(UserAccess access, bool server)
    {
        var visible = catalog.Visible(access);
        var plays = db.PlayEvents.AsNoTracking().Where(p => p.TrackId != null && visible.Any(t => t.Id == p.TrackId));
        return server ? plays : plays.Where(p => p.UserId == access.UserId);
    }

    private async Task<Dictionary<Guid, TrackListeningStats>> ComputeAsync(UserAccess access, bool server, IReadOnlyCollection<Guid> trackIds, TimeZoneInfo zone,
        CancellationToken cancellationToken)
    {
        var ids = trackIds.ToList();
        var durations = await db.Tracks.AsNoTracking().Where(t => ids.Contains(t.Id)).ToDictionaryAsync(t => t.Id, t => t.DurationMs, cancellationToken);
        var plays = await Plays(access, server)
            .Where(p => ids.Contains(p.TrackId!.Value))
            .Select(p => new { TrackId = p.TrackId!.Value, p.PlayedAt, p.PlayedMs, p.Counted, p.Skipped })
            .ToListAsync(cancellationToken);
        var byTrack = plays.ToLookup(p => p.TrackId);

        return ids.ToDictionary(id => id, id =>
        {
            var events = byTrack[id].ToList();
            var duration = Math.Max(1, durations.GetValueOrDefault(id));
            var skips = events.Where(p => p.Skipped).ToList();
            var hours = new int[24];
            foreach (var play in events.Where(p => p.Counted))
                hours[TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(play.PlayedAt, DateTimeKind.Utc), zone).Hour]++;
            return new TrackListeningStats(
                id,
                events.Count(p => p.Counted),
                skips.Count,
                skips.Count > 0 ? Math.Round(skips.Average(p => p.PlayedMs) / 1000.0, 1) : null,
                events.Count(p => p.PlayedMs >= duration * 0.9 || duration - p.PlayedMs <= 10_000),
                events.Count > 0 ? Math.Round(events.Average(p => Math.Min(1.0, p.PlayedMs / (double)duration)), 3) : null,
                events.Count > 0 ? events.Max(p => p.PlayedAt) : null,
                hours);
        });
    }
}
