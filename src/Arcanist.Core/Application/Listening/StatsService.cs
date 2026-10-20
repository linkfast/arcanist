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
    /// <param name="libraryId">Only plays of songs of that library (and, for the server, what it holds); "All" or null for every one.</param>
    public async Task<ListeningStats> ListeningAsync(StatsPeriod period, string? timeZone, Guid? userId, CancellationToken cancellationToken, Guid? libraryId = null)
    {
        var access = guard.Authenticated();
        var library = Library(access, libraryId);
        if (userId is { } other && other != access.UserId)
        {
            guard.Permission(Permissions.ViewAllStats);
            if (!await db.Users.AnyAsync(u => u.Id == other, cancellationToken))
                throw new ArcanistException.NotFound();
        }

        return await ComputeAsync(access, period, TimeZone(timeZone), userId ?? access.UserId, library, cancellationToken);
    }

    public async Task<ServerStats> ServerAsync(StatsPeriod period, string? timeZone, CancellationToken cancellationToken, Guid? libraryId = null)
    {
        var access = guard.Permission(Permissions.ViewAllStats);
        var only = Library(access, libraryId);
        var zone = TimeZone(timeZone);
        var listening = await ComputeAsync(access, period, zone, userId: null, only, cancellationToken);

        var present = db.MediaFiles.Where(f => f.MissingSince == null && (only == null || f.LibrarySource.LibraryId == only));
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
        var plays = db.PlayEvents.Where(p => (from == null || p.PlayedAt >= from) && (only == null || db.MediaFiles.Any(f => f.TrackId == p.TrackId && f.LibrarySource.LibraryId == only)));
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

        // Users of other servers that count here, by their name there.
        var remotePlays = db.RemotePlayEvents.Where(p => (from == null || p.PlayedAt >= from)
                                                         && (only == null || db.MediaFiles.Any(f => f.TrackId == p.TrackId && f.LibrarySource.LibraryId == only)));
        var remote = await remotePlays
            .GroupBy(p => new { p.PeerId, p.Peer.Name, p.RemoteUser })
            .Select(g => new UserListening(g.Key.PeerId, g.Key.RemoteUser, null, g.Count(p => p.Counted), g.Sum(p => (long)p.PlayedMs),
                g.Where(p => p.Counted).Max(p => (DateTime?)p.PlayedAt), true, g.Key.Name))
            .ToListAsync(cancellationToken);

        return new ServerStats(
            library,
            listening,
            users.Select(u => perUser.TryGetValue(u.Id, out var p)
                    ? new UserListening(u.Id, u.UserName, u.DisplayName, p.Plays, p.ListenedMs, p.Last)
                    : new UserListening(u.Id, u.UserName, u.DisplayName, 0, 0, null))
                .Concat(remote)
                .OrderByDescending(u => u.ListenedMs)
                .ToList(),
            await tracks.CountAsync(t => from == null || t.AddedAt >= from, cancellationToken),
            remote.Sum(r => r.Plays),
            remote.Sum(r => r.ListenedMs));
    }

    /// <summary>Each library the caller sees, at a glance; plays and listeners only with VIEW_ALL_STATS.</summary>
    public async Task<IReadOnlyList<LibrarySummary>> LibrarySummariesAsync(CancellationToken cancellationToken)
    {
        var access = guard.Authenticated();
        var visible = await Libraries.LibraryService.Visible(db, access).Select(l => l.Id).ToListAsync(cancellationToken);
        var present = db.MediaFiles.AsNoTracking().Where(f => f.MissingSince == null && visible.Contains(f.LibrarySource.LibraryId));
        var files = await present.GroupBy(f => f.LibrarySource.LibraryId)
            .Select(g => new { LibraryId = g.Key, Files = g.Count(), Size = g.Sum(f => f.Size) })
            .ToListAsync(cancellationToken);
        var tracks = await present.Select(f => new { f.LibrarySource.LibraryId, f.TrackId, f.Track.AlbumId, f.Track.DurationMs }).Distinct()
            .GroupBy(t => t.LibraryId)
            .Select(g => new { LibraryId = g.Key, Tracks = g.Count(), Albums = g.Select(t => t.AlbumId).Distinct().Count(), Duration = g.Sum(t => (long)t.DurationMs) })
            .ToListAsync(cancellationToken);

        Dictionary<Guid, (int Plays, int Listeners, DateTime? Last)>? listening = null;
        if (access.Has(Permissions.ViewAllStats))
        {
            var from = time.GetUtcNow().UtcDateTime.AddDays(-30);
            var rows = await db.PlayEvents.AsNoTracking().Where(p => p.Counted && p.PlayedAt >= from)
                .Join(db.MediaFiles.Where(f => visible.Contains(f.LibrarySource.LibraryId)), p => p.TrackId, f => (Guid?)f.TrackId,
                    (p, f) => new { f.LibrarySource.LibraryId, PlayId = p.Id, p.UserId, p.PlayedAt })
                .Distinct()
                .ToListAsync(cancellationToken);
            listening = rows.GroupBy(r => r.LibraryId).ToDictionary(g => g.Key,
                g => (g.Select(r => r.PlayId).Distinct().Count(), g.Select(r => r.UserId).Distinct().Count(), (DateTime?)g.Max(r => r.PlayedAt)));
        }

        var byFiles = files.ToDictionary(f => f.LibraryId);
        var byTracks = tracks.ToDictionary(t => t.LibraryId);
        return visible.Select(id =>
        {
            var f = byFiles.GetValueOrDefault(id);
            var t = byTracks.GetValueOrDefault(id);
            var l = listening?.GetValueOrDefault(id);
            return new LibrarySummary(id, t?.Tracks ?? 0, t?.Albums ?? 0, f?.Files ?? 0, f?.Size ?? 0, t?.Duration ?? 0,
                listening is null ? null : l?.Plays ?? 0, listening is null ? null : l?.Listeners ?? 0, l?.Last);
        }).ToList();
    }

    /// <summary>A library to limit statistics to: one the caller can use; "All" and null are every one.</summary>
    private Guid? Library(UserAccess access, Guid? libraryId)
    {
        if (Libraries.VirtualLibraries.Filter(libraryId) is not { } library)
            return null;
        if (!access.CanAccessLibrary(library) || !db.Libraries.Any(l => l.Id == library))
            throw new ArcanistException.NotFound();
        return library;
    }

    /// <param name="userId">One user's plays, or everyone's when null.</param>
    private async Task<ListeningStats> ComputeAsync(
        UserAccess access, StatsPeriod period, TimeZoneInfo zone, Guid? userId, Guid? library, CancellationToken cancellationToken)
    {
        var from = Start(period, zone);
        var plays = db.PlayEvents.AsNoTracking().Where(p => (userId == null || p.UserId == userId) && (from == null || p.PlayedAt >= from)
            && (library == null || db.MediaFiles.Any(f => f.TrackId == p.TrackId && f.LibrarySource.LibraryId == library)));

        // Only what the timeline needs is read; grouping by local day happens here, where time zones are known.
        var rows = await plays.Select(p => new { p.PlayedAt, p.PlayedMs, p.Counted, p.Skipped, p.UserId }).ToListAsync(cancellationToken);
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
            byWeekday,
            rows.Count(r => r.Skipped),
            rows.Where(r => r.Counted).Select(r => r.UserId).Distinct().Count(),
            rows.Where(r => r.Counted).Select(r => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(r.PlayedAt, zone))).Distinct().Count(),
            library);
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

    internal static TimeZoneInfo TimeZone(string? id)
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
