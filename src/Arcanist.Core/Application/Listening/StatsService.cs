using Arcanist.Core.Application.Catalog;
using Arcanist.Core.Auth;
using Arcanist.Core.Data;
using Arcanist.Core.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Arcanist.Core.Application.Listening;

/// <summary>
/// Listening statistics. Everyone sees their own; the whole server's, and other users', require VIEW_ALL_STATS.
/// Days and months are counted in the time zone the client asks for (UTC by default).
/// </summary>
public sealed class StatsService(ArcanistDbContext db, AccessGuard guard, CatalogService catalog, TimeProvider time)
{
    public const int TopCount = 10;

    /// <param name="userId">Another user's statistics (VIEW_ALL_STATS); the caller's own when null.</param>
    /// <param name="timeZone">IANA (or Windows) time zone id for days and months; UTC when null.</param>
    public async Task<ListeningStats> ListeningAsync(StatsPeriod period, string? timeZone, Guid? userId, CancellationToken cancellationToken)
    {
        var access = guard.Authenticated();
        if (userId is { } other && other != access.UserId)
        {
            guard.Permission(Permissions.ViewAllStats);
            if (!await db.Users.AnyAsync(u => u.Id == other, cancellationToken))
                throw new ArcanistException.NotFound();
        }

        return await ComputeAsync(access, period, TimeZone(timeZone), userId ?? access.UserId, cancellationToken);
    }

    public async Task<ServerStats> ServerAsync(StatsPeriod period, string? timeZone, CancellationToken cancellationToken)
    {
        var access = guard.Permission(Permissions.ViewAllStats);
        var zone = TimeZone(timeZone);
        var listening = await ComputeAsync(access, period, zone, userId: null, cancellationToken);

        var present = db.MediaFiles.Where(f => f.MissingSince == null);
        var tracks = db.Tracks.Where(t => present.Any(f => f.TrackId == t.Id));
        var formats = await present
            .GroupBy(f => f.Format ?? "")
            .Select(g => new { Format = g.Key, Files = g.Count(), Size = g.Sum(f => f.Size) })
            .ToListAsync(cancellationToken);
        var library = new LibraryTotals(
            await db.Artists.CountAsync(a => db.Albums.Any(al => al.AlbumArtistId == a.Id && tracks.Any(t => t.AlbumId == al.Id))
                                             || db.TrackArtists.Any(ta => ta.ArtistId == a.Id && tracks.Any(t => t.Id == ta.TrackId)), cancellationToken),
            await db.Albums.CountAsync(a => tracks.Any(t => t.AlbumId == a.Id), cancellationToken),
            await tracks.CountAsync(cancellationToken),
            formats.Sum(f => f.Files),
            await tracks.SumAsync(t => (long)t.DurationMs, cancellationToken),
            formats.Sum(f => f.Size),
            formats.OrderByDescending(f => f.Files).Select(f => new FormatTotals(f.Format, f.Files, f.Size)).ToList());

        var from = Start(period, zone);
        var plays = db.PlayEvents.Where(p => from == null || p.PlayedAt >= from);
        var perUser = await plays.GroupBy(p => p.UserId)
            .Select(g => new
            {
                UserId = g.Key,
                Plays = g.Count(p => p.Counted),
                ListenedMs = g.Sum(p => (long)p.PlayedMs),
                Last = g.Where(p => p.Counted).Max(p => (DateTime?)p.PlayedAt),
            })
            .ToDictionaryAsync(u => u.UserId, cancellationToken);
        var users = await db.Users.AsNoTracking().OrderBy(u => u.NormalizedUserName)
            .Select(u => new { u.Id, u.UserName, u.DisplayName })
            .ToListAsync(cancellationToken);

        return new ServerStats(
            library,
            listening,
            users.Select(u => perUser.TryGetValue(u.Id, out var p)
                    ? new UserListening(u.Id, u.UserName, u.DisplayName, p.Plays, p.ListenedMs, p.Last)
                    : new UserListening(u.Id, u.UserName, u.DisplayName, 0, 0, null))
                .OrderByDescending(u => u.ListenedMs)
                .ToList());
    }

    /// <param name="userId">One user's plays, or everyone's when null.</param>
    private async Task<ListeningStats> ComputeAsync(UserAccess access, StatsPeriod period, TimeZoneInfo zone, Guid? userId, CancellationToken cancellationToken)
    {
        var from = Start(period, zone);
        var plays = db.PlayEvents.AsNoTracking().Where(p => (userId == null || p.UserId == userId) && (from == null || p.PlayedAt >= from));

        // Only what the timeline needs is read; grouping by local day happens here, where time zones are known.
        var rows = await plays.Select(p => new { p.PlayedAt, p.PlayedMs, p.Counted }).ToListAsync(cancellationToken);
        var bucket = period is StatsPeriod.Week or StatsPeriod.Month ? StatsBucket.Day : StatsBucket.Month;
        DateOnly BucketOf(DateTime utc)
        {
            var local = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(utc, zone));
            return bucket == StatsBucket.Day ? local : new DateOnly(local.Year, local.Month, 1);
        }

        var buckets = rows.GroupBy(r => BucketOf(r.PlayedAt))
            .ToDictionary(g => g.Key, g => new ListeningBucket(g.Key, g.Count(r => r.Counted), g.Sum(r => (long)r.PlayedMs)));

        // Every day (or month) of the period, quiet ones included, so clients can chart it as it is.
        var timeline = new List<ListeningBucket>();
        if (from is not null || buckets.Count > 0)
        {
            var last = BucketOf(time.GetUtcNow().UtcDateTime);
            for (var day = from is { } start ? BucketOf(start) : buckets.Keys.Min(); day <= last;
                 day = bucket == StatsBucket.Day ? day.AddDays(1) : day.AddMonths(1))
            {
                timeline.Add(buckets.GetValueOrDefault(day) ?? new ListeningBucket(day, 0, 0));
            }
        }

        var counted = plays.Where(p => p.Counted && p.TrackId != null);
        var topTracks = await counted.GroupBy(p => p.TrackId!.Value)
            .Select(g => new { Id = g.Key, Plays = g.Count() })
            .OrderByDescending(g => g.Plays).ThenBy(g => g.Id)
            .Take(TopCount * 2)
            .ToListAsync(cancellationToken);
        var topAlbums = await counted.Join(db.Tracks, p => p.TrackId, t => t.Id, (p, t) => t.AlbumId)
            .GroupBy(albumId => albumId)
            .Select(g => new { Id = g.Key, Plays = g.Count() })
            .OrderByDescending(g => g.Plays).ThenBy(g => g.Id)
            .Take(TopCount * 2)
            .ToListAsync(cancellationToken);
        var topArtists = await counted
            .Join(db.TrackArtists.Where(ta => ta.Role == ArtistRole.Primary), p => p.TrackId, ta => ta.TrackId, (p, ta) => new { ta.ArtistId, ta.Artist.Name })
            .GroupBy(a => new { a.ArtistId, a.Name })
            .Select(g => new { g.Key.ArtistId, g.Key.Name, Plays = g.Count() })
            .OrderByDescending(g => g.Plays).ThenBy(g => g.ArtistId)
            .Take(TopCount)
            .ToListAsync(cancellationToken);

        var topGenres = await counted
            .Join(db.TrackGenres, p => p.TrackId, tg => tg.TrackId, (p, tg) => tg.Genre.Name)
            .GroupBy(name => name)
            .Select(g => new { Name = g.Key, Plays = g.Count() })
            .OrderByDescending(g => g.Plays).ThenBy(g => g.Name)
            .Take(TopCount)
            .ToListAsync(cancellationToken);

        var byHour = new int[24];
        var byWeekday = new int[7];
        foreach (var r in rows.Where(r => r.Counted))
        {
            var local = TimeZoneInfo.ConvertTimeFromUtc(r.PlayedAt, zone);
            byHour[local.Hour]++;
            byWeekday[(int)local.DayOfWeek]++;
        }

        // Tops show what the viewer can see; a track in a library they lost access to is left out.
        var tracks = await catalog.VisibleTracksAsync(access, topTracks.Select(t => t.Id).ToList(), cancellationToken);
        var albums = await catalog.VisibleAlbumsAsync(access, topAlbums.Select(a => a.Id).ToList(), cancellationToken);

        return new ListeningStats(
            period,
            from,
            rows.Count(r => r.Counted),
            rows.Sum(r => (long)r.PlayedMs),
            await counted.Select(p => p.TrackId).Distinct().CountAsync(cancellationToken),
            bucket,
            timeline,
            topTracks.Where(t => tracks.ContainsKey(t.Id)).Take(TopCount).Select(t => new TrackPlays(tracks[t.Id], t.Plays)).ToList(),
            topArtists.Select(a => new ArtistPlays(new ArtistReference(a.ArtistId, a.Name), a.Plays)).ToList(),
            topAlbums.Where(a => albums.ContainsKey(a.Id)).Take(TopCount).Select(a => new AlbumPlays(albums[a.Id], a.Plays)).ToList(),
            topGenres.Select(g => new GenrePlays(g.Name, g.Plays)).ToList(),
            byHour,
            byWeekday);
    }

    /// <summary>Start of the period in UTC: the beginning of the local day 7 or 30 days back, or of the month a year back.</summary>
    private DateTime? Start(StatsPeriod period, TimeZoneInfo zone)
    {
        var today = TimeZoneInfo.ConvertTimeFromUtc(time.GetUtcNow().UtcDateTime, zone).Date;
        DateTime? local = period switch
        {
            StatsPeriod.Week => today.AddDays(-6),
            StatsPeriod.Month => today.AddDays(-29),
            StatsPeriod.Year => new DateTime(today.Year, today.Month, 1).AddMonths(-11),
            _ => null,
        };
        if (local is not { } start)
            return null;

        // Where clocks jump forward at midnight, the day starts at the first hour that exists.
        start = DateTime.SpecifyKind(start, DateTimeKind.Unspecified);
        while (zone.IsInvalidTime(start))
            start = start.AddMinutes(30);
        return TimeZoneInfo.ConvertTimeToUtc(start, zone);
    }

    private static TimeZoneInfo TimeZone(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return TimeZoneInfo.Utc;
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id.Trim());
        }
        catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            throw ArcanistException.Field("timeZone", ErrorCodes.InvalidFormat);
        }
    }
}
