using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Arcanist.Core.Application;
using Arcanist.Core.Application.Listening;
using Arcanist.Core.Application.Mstream;
using Arcanist.Core.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Arcanist.Core.Extensions.Mstream;

/// <summary>
/// mStream 6.27's listening statistics (Stats API v2), over Arcanist's own play log: the apps report plays with their
/// own ids, and read their history, top lists, summaries and time series. Arcanist decides what counts as a play with
/// its own rules. Off in the panel, the routes do not exist and the apps report plays the older way.
/// </summary>
internal sealed partial class MstreamExtension
{
    /// <summary>The version of mStream's statistics API spoken here (<c>features.stats</c>).</summary>
    private const int StatsVersion = 2;

    private const int MaxPlaysPerReport = 200;

    private static readonly TimeSpan SessionGap = TimeSpan.FromMinutes(30);

    private static async Task RequireStatsAsync(MstreamCall call)
    {
        if (!await HasAsync(call, MstreamFeature.Stats))
            throw MstreamException.NotFound();
    }

    /// <summary>A batch of finished plays, each answered on its own so a client can retry safely.</summary>
    private static async Task<IResult> ReportPlaysAsync(MstreamCall call)
    {
        await RequireStatsAsync(call);
        if (call.Body.Root is not { ValueKind: JsonValueKind.Object } root || !root.TryGetProperty("plays", out var plays)
            || plays.ValueKind != JsonValueKind.Array || plays.GetArrayLength() is 0 or > MaxPlaysPerReport)
            throw MstreamException.Invalid("plays");
        var client = root.TryGetProperty("client", out var c) && c.ValueKind == JsonValueKind.Object && c.TryGetProperty("name", out var n)
            ? n.GetString()
            : "mStream";

        var library = new MstreamLibrary(call);
        var listening = call.Service<ListeningService>();
        var (accepted, duplicates, rejected) = (new JsonArray(), new JsonArray(), new JsonArray());
        foreach (var play in plays.EnumerateArray())
        {
            var id = Text(play, "id");
            string? reason = null;
            if (id is not { Length: > 0 and <= 128 } || play.ValueKind != JsonValueKind.Object)
                reason = "invalid";
            else if (play.TryGetProperty("peerId", out var peer) && peer.ValueKind != JsonValueKind.Null)
                reason = "unknown-peer";
            else if (Time(play, "startedAt") is not { } startedAt)
                reason = "bad-time";
            else if (await library.ResolveAsync(Text(play, "filePath")) is not { } file)
                reason = "unknown-track";
            else
            {
                var playedMs = play.TryGetProperty("playedMs", out var ms) && ms.TryGetInt32(out var value) ? Math.Max(0, value) : 0;
                try
                {
                    var recorded = await listening.RecordPlayAsync(call.Access, new RecordPlayInput(file.TrackId, playedMs, startedAt, client),
                        id, Text(play, "source") is { Length: <= 32 } source ? source : null, call.Cancellation);
                    (recorded.Duplicate ? duplicates : accepted).Add(id);
                    continue;
                }
                catch (ArcanistException.NotFound)
                {
                    reason = "unknown-track";
                }
                catch (ArcanistException.Invalid)
                {
                    reason = "bad-time";
                }
            }

            rejected.Add(new JsonObject { ["id"] = id, ["reason"] = reason });
        }

        return MstreamCall.Json(new JsonObject { ["accepted"] = accepted, ["duplicates"] = duplicates, ["rejected"] = rejected });
    }

    /// <summary>Forgets the plays in a range (<c>to</c> exclusive).</summary>
    private static async Task<IResult> ForgetPlaysAsync(MstreamCall call)
    {
        await RequireStatsAsync(call);
        if (ParseTime(call.Query("from")) is not { } from || ParseTime(call.Query("to")) is not { } to || to <= from)
            throw MstreamException.Invalid("from");
        var user = call.Access.UserId;
        var deleted = await ForgetAsync(call, p => p.UserId == user && p.PlayedAt >= from && p.PlayedAt < to);
        return MstreamCall.Json(new JsonObject { ["deleted"] = deleted });
    }

    /// <summary>Forgets one play, by the id the client gave it (or Arcanist's own).</summary>
    private static async Task<IResult> ForgetPlayAsync(MstreamCall call)
    {
        await RequireStatsAsync(call);
        var id = call.Route("id") ?? "";
        var user = call.Access.UserId;
        Guid? own = Guid.TryParseExact(id, "N", out var parsed) ? parsed : null;
        var deleted = await ForgetAsync(call, p => p.UserId == user && (p.ExternalId == id || own != null && p.ExternalId == null && p.Id == own));
        return deleted == 0 ? throw MstreamException.NotFound() : MstreamCall.Json(new JsonObject { ["deleted"] = deleted });
    }

    /// <summary>Deletes plays and takes them off their tracks' totals.</summary>
    private static async Task<int> ForgetAsync(MstreamCall call, System.Linq.Expressions.Expression<Func<PlayEvent, bool>> which)
    {
        var db = call.Service<Data.ArcanistDbContext>();
        var plays = await db.PlayEvents.Where(which).ToListAsync(call.Cancellation);
        if (plays.Count == 0)
            return 0;

        var user = call.Access.UserId;
        var trackIds = plays.Where(p => p.TrackId != null).Select(p => p.TrackId!.Value).Distinct().ToList();
        db.PlayEvents.RemoveRange(plays);
        await db.SaveChangesAsync(call.Cancellation);

        var stats = await db.TrackPlayStats.Where(s => s.UserId == user && trackIds.Contains(s.TrackId)).ToListAsync(call.Cancellation);
        foreach (var stat in stats)
        {
            var gone = plays.Where(p => p.TrackId == stat.TrackId).ToList();
            stat.PlayCount = Math.Max(0, stat.PlayCount - gone.Count(p => p.Counted));
            stat.SkipCount = Math.Max(0, stat.SkipCount - gone.Count(p => p.Skipped));
            if (gone.Any(p => p.Counted && p.PlayedAt == stat.LastPlayedAt))
            {
                var trackId = stat.TrackId;
                stat.LastPlayedAt = await db.PlayEvents.Where(p => p.UserId == user && p.TrackId == trackId && p.Counted)
                    .MaxAsync(p => (DateTime?)p.PlayedAt, call.Cancellation) ?? (stat.PlayCount > 0 ? stat.LastPlayedAt : null);
            }
        }

        await db.SaveChangesAsync(call.Cancellation);
        return plays.Count;
    }

    /// <summary>Zeroes the play counts (<c>counts</c>), drops the log (<c>history</c>), or both (<c>all</c>). Ratings stay.</summary>
    private static async Task<IResult> ResetStatsAsync(MstreamCall call)
    {
        await RequireStatsAsync(call);
        var scope = call.Body.Required("scope");
        if (scope is not ("counts" or "history" or "all"))
            throw MstreamException.Invalid("scope");
        var db = call.Service<Data.ArcanistDbContext>();
        var user = call.Access.UserId;
        var tracks = scope is "counts" or "all" ? await db.TrackPlayStats.Where(s => s.UserId == user).ExecuteDeleteAsync(call.Cancellation) : 0;
        var events = scope is "history" or "all" ? await db.PlayEvents.Where(p => p.UserId == user).ExecuteDeleteAsync(call.Cancellation) : 0;
        return MstreamCall.Json(new JsonObject { ["scope"] = scope, ["tracks"] = tracks, ["events"] = events });
    }

    /// <summary>What a player is playing now: kept in memory ten minutes, one entry per player session.</summary>
    private static async Task<IResult> AnnounceNowPlayingAsync(MstreamCall call)
    {
        await RequireStatsAsync(call);
        var session = call.Body.Required("sessionId");
        var path = call.Body.Required("filePath");
        if (session.Length > 128 || path.Length > 2048)
            throw MstreamException.Invalid("sessionId");
        if (call.Body.Has("peerId"))
            return MstreamCall.Json(new JsonObject { ["accepted"] = false, ["reason"] = "unknown-peer" });

        var library = new MstreamLibrary(call);
        if (await library.ResolveAsync(path) is not { } file)
            return MstreamCall.Json(new JsonObject { ["accepted"] = false, ["reason"] = "unknown-track" });
        var item = await library.ObjectAsync(path, file);
        var metadata = item["metadata"] as JsonObject;
        var expires = call.Service<MstreamNowPlaying>().Set(call.Access.UserId, session, path, new JsonObject
        {
            ["title"] = metadata?["title"]?.DeepClone(),
            ["artist"] = metadata?["artist"]?.DeepClone(),
            ["album"] = metadata?["album"]?.DeepClone(),
            ["durationMs"] = metadata?["duration"] is { } seconds ? (int)Math.Round(seconds.GetValue<double>() * 1000) : null,
            ["hash"] = metadata?["hash"]?.DeepClone(),
        });
        return MstreamCall.Json(new JsonObject { ["accepted"] = true, ["expiresAt"] = expires });
    }

    private static async Task<IResult> NowPlayingAsync(MstreamCall call)
    {
        await RequireStatsAsync(call);
        var entries = call.Service<MstreamNowPlaying>().Of(call.Access.UserId);
        return MstreamCall.Json(new JsonObject
        {
            ["entries"] = new JsonArray(entries.Select(e => (JsonNode?)new JsonObject
            {
                ["sessionId"] = e.SessionId,
                ["filePath"] = e.FilePath,
                ["peerId"] = null,
                ["track"] = e.Track.DeepClone(),
                ["since"] = e.Since,
                ["expiresAt"] = e.ExpiresAt,
            }).ToArray()),
        });
    }

    /// <summary>The period's totals: plays, listening time, skips, sessions, streaks, the top day and the peaks.</summary>
    private static async Task<IResult> StatsSummaryAsync(MstreamCall call)
    {
        await RequireStatsAsync(call);
        var range = StatsRange.Read(call, required: true, call.Service<TimeProvider>())!;
        var library = new MstreamLibrary(call);
        var plays = await PlaysAsync(call, library, range);
        var counted = plays.Where(p => p.Counted).ToList();
        var trackIds = counted.Where(p => p.TrackId != null).Select(p => p.TrackId!.Value).Distinct().ToList();
        var tracks = await library.Db.Tracks.AsNoTracking().Where(t => trackIds.Contains(t.Id))
            .Select(t => new { t.Id, t.AlbumId, Artist = t.Artists.Where(a => a.Role == ArtistRole.Primary).OrderBy(a => a.Position).Select(a => (Guid?)a.ArtistId).FirstOrDefault() })
            .ToListAsync(call.Cancellation);

        var user = call.Access.UserId;
        var firstPlays = await library.Db.PlayEvents.AsNoTracking()
            .Where(p => p.UserId == user && p.Counted && p.TrackId != null && trackIds.Contains(p.TrackId.Value))
            .GroupBy(p => p.TrackId)
            .Select(g => g.Min(p => p.PlayedAt))
            .ToListAsync(call.Cancellation);
        var libraryTracks = await (await library.TracksAsync()).CountAsync(call.Cancellation);

        var sessions = Sessions(plays);
        var longest = sessions.MaxBy(s => s.Sum(p => p.PlayedMs));
        var days = counted.GroupBy(p => range.LocalDate(p.PlayedAt)).ToDictionary(g => g.Key, g => (Plays: g.Count(), ListenedMs: g.Sum(p => (long)p.PlayedMs)));
        var topDay = days.OrderByDescending(d => d.Value.Plays).ThenBy(d => d.Key).Select(d => (KeyValuePair<DateOnly, (int Plays, long ListenedMs)>?)d).FirstOrDefault();
        var listened = plays.Sum(p => (long)p.PlayedMs);

        return MstreamCall.Json(new JsonObject
        {
            ["period"] = range.ToJson(),
            ["events"] = plays.Count,
            ["plays"] = counted.Count,
            ["uniqueTracks"] = trackIds.Count,
            ["uniqueArtists"] = tracks.Where(t => t.Artist != null).Select(t => t.Artist).Distinct().Count(),
            ["uniqueAlbums"] = tracks.Select(t => t.AlbumId).Distinct().Count(),
            ["listenedMs"] = listened,
            ["skips"] = plays.Count(p => p.Skipped),
            ["skipRate"] = plays.Count == 0 ? 0 : Math.Round((double)plays.Count(p => p.Skipped) / plays.Count, 4),
            ["completionRate"] = plays.Count == 0 ? 0 : Math.Round((double)counted.Count / plays.Count, 4),
            ["pauses"] = 0,
            ["discoveries"] = firstPlays.Count(at => at >= range.From && at < range.To),
            ["libraryCoveragePct"] = libraryTracks == 0 ? 0 : Math.Round(100.0 * trackIds.Count / libraryTracks, 2),
            ["sessions"] = new JsonObject
            {
                ["count"] = sessions.Count,
                ["avgMs"] = sessions.Count == 0 ? null : (long)sessions.Average(s => s.Sum(p => (long)p.PlayedMs)),
                ["longest"] = longest is null ? null : new JsonObject
                {
                    ["startedAt"] = Iso(longest[0].PlayedAt),
                    ["endedAt"] = Iso(longest.Max(p => p.PlayedAt.AddMilliseconds(p.PlayedMs))),
                    ["tracks"] = longest.Where(p => p.TrackId != null).Select(p => p.TrackId).Distinct().Count(),
                    ["plays"] = longest.Count(p => p.Counted),
                    ["listenedMs"] = longest.Sum(p => (long)p.PlayedMs),
                },
            },
            ["streakDays"] = Streaks(days.Keys, range.LocalDate(range.Now)),
            ["topDay"] = topDay is { } top
                ? new JsonObject { ["date"] = top.Key.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), ["plays"] = top.Value.Plays, ["listenedMs"] = top.Value.ListenedMs }
                : null,
            ["peakHour"] = counted.Count == 0 ? null : counted.GroupBy(p => range.Local(p.PlayedAt).Hour).OrderByDescending(g => g.Count()).ThenBy(g => g.Key).First().Key,
            ["peakWeekday"] = counted.Count == 0 ? null : (int)counted.GroupBy(p => range.Local(p.PlayedAt).DayOfWeek).OrderByDescending(g => g.Count()).ThenBy(g => g.Key).First().Key,
            ["origins"] = new JsonObject
            {
                ["local"] = new JsonObject { ["plays"] = counted.Count, ["listenedMs"] = listened },
                ["peers"] = new JsonObject { ["plays"] = 0, ["listenedMs"] = 0 },
            },
        });
    }

    /// <summary>The most played tracks, artists, albums or genres of a period, by plays or by listening time.</summary>
    private static async Task<IResult> StatsTopAsync(MstreamCall call)
    {
        await RequireStatsAsync(call);
        var range = StatsRange.Read(call, required: true, call.Service<TimeProvider>())!;
        var entity = call.Query("entity") ?? "tracks";
        var metric = call.Query("metric") ?? "plays";
        if (entity is not ("tracks" or "artists" or "albums" or "genres") || metric is not ("plays" or "time"))
            throw MstreamException.Invalid(entity is "tracks" or "artists" or "albums" or "genres" ? "metric" : "entity");
        var limit = Math.Clamp(int.TryParse(call.Query("limit"), out var l) ? l : 20, 1, 200);

        var library = new MstreamLibrary(call);
        var plays = (await PlaysAsync(call, library, range)).Where(p => p.TrackId != null).ToList();
        var trackIds = plays.Select(p => p.TrackId!.Value).Distinct().ToList();
        var facts = await library.Db.Tracks.AsNoTracking().Where(t => trackIds.Contains(t.Id))
            .Select(t => new
            {
                t.Id,
                t.AlbumId,
                Album = t.Album.Title,
                AlbumArtist = t.Album.AlbumArtist.Name,
                Artists = t.Artists.Where(a => a.Role == ArtistRole.Primary).Select(a => new { a.ArtistId, a.Artist.Name }).ToList(),
                Genres = t.Genres.Select(g => new { g.GenreId, g.Genre.Name }).ToList(),
            })
            .ToDictionaryAsync(t => t.Id, call.Cancellation);

        // Each play goes to the rows of its entity: one track or album, every artist or genre of the track.
        var rows = plays.Where(p => facts.ContainsKey(p.TrackId!.Value))
            .SelectMany(p =>
            {
                var t = facts[p.TrackId!.Value];
                return entity switch
                {
                    "tracks" => [(Key: t.Id.ToString(), Name: (string?)null, Artist: (string?)null, Play: p)],
                    "albums" => [(Key: t.AlbumId.ToString(), Name: (string?)t.Album, Artist: t.AlbumArtist, Play: p)],
                    "artists" => t.Artists.Select(a => (Key: a.ArtistId.ToString(), Name: (string?)a.Name, Artist: (string?)null, Play: p)),
                    _ => t.Genres.Select(g => (Key: g.GenreId.ToString(), Name: (string?)g.Name, Artist: (string?)null, Play: p)),
                };
            })
            .GroupBy(r => r.Key)
            .Select(g => (
                g.Key,
                g.First().Name,
                g.First().Artist,
                Plays: g.Count(r => r.Play.Counted),
                ListenedMs: g.Sum(r => (long)r.Play.PlayedMs),
                Events: g.Count(),
                Tracks: g.Select(r => r.Play.TrackId).Distinct().Count(),
                LastPlayed: g.Where(r => r.Play.Counted).Select(r => (DateTime?)r.Play.PlayedAt).Max()))
            .ToList();
        var total = rows.Sum(r => metric == "plays" ? r.Plays : (double)r.ListenedMs);
        var ranked = rows.OrderByDescending(r => metric == "plays" ? r.Plays : (double)r.ListenedMs).ThenByDescending(r => r.ListenedMs).ThenBy(r => r.Key)
            .Where(r => metric == "time" || r.Plays > 0)
            .Take(limit)
            .ToList();

        var objects = entity == "tracks"
            ? await TrackObjectsAsync(library, ranked.Select(r => Guid.Parse(r.Key)).ToList())
            : [];
        var items = new JsonArray();
        foreach (var (row, index) in ranked.Select((r, i) => (r, i)))
        {
            var item = new JsonObject
            {
                ["rank"] = index + 1,
                ["plays"] = row.Plays,
                ["listenedMs"] = row.ListenedMs,
                ["events"] = row.Events,
                ["share"] = total == 0 ? 0 : Math.Round((metric == "plays" ? row.Plays : (double)row.ListenedMs) / total, 4),
            };
            if (entity == "tracks")
            {
                if (!objects.TryGetValue(Guid.Parse(row.Key), out var track))
                    continue;
                item["lastPlayed"] = row.LastPlayed is { } last ? Iso(last) : null;
                item["origin"] = "local";
                item["peerId"] = null;
                item["peerName"] = null;
                item["track"] = track.DeepClone();
            }
            else
            {
                item["name"] = row.Name;
                if (entity == "albums")
                    item["artist"] = row.Artist;
                item["tracks"] = row.Tracks;
            }

            items.Add(item);
        }

        return MstreamCall.Json(new JsonObject { ["period"] = range.ToJson(), ["entity"] = entity, ["metric"] = metric, ["items"] = items });
    }

    /// <summary>Plays and listening time per hour, day, week, month, hour of the day or day of the week.</summary>
    private static async Task<IResult> StatsTimeSeriesAsync(MstreamCall call)
    {
        await RequireStatsAsync(call);
        var range = StatsRange.Read(call, required: true, call.Service<TimeProvider>())!;
        var bucket = call.Query("bucket") ?? "day";
        if (bucket is not ("hour" or "day" or "week" or "month" or "hourOfDay" or "weekday"))
            throw MstreamException.Invalid("bucket");
        var plays = await PlaysAsync(call, new MstreamLibrary(call), range);

        string Key(PlayEvent play)
        {
            var at = range.Local(play.PlayedAt);
            return bucket switch
            {
                "hour" => at.ToString("yyyy-MM-dd'T'HH", CultureInfo.InvariantCulture),
                "day" => at.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                "week" => at.Date.AddDays(-(((int)at.DayOfWeek + 6) % 7)).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                "month" => at.ToString("yyyy-MM", CultureInfo.InvariantCulture),
                "hourOfDay" => at.Hour.ToString(CultureInfo.InvariantCulture),
                _ => ((int)at.DayOfWeek).ToString(CultureInfo.InvariantCulture),
            };
        }

        var groups = plays.GroupBy(Key).ToDictionary(g => g.Key, g => g.ToList());
        // Profiles list every bucket; calendars only those with plays.
        IEnumerable<string> keys = bucket switch
        {
            "hourOfDay" => Enumerable.Range(0, 24).Select(h => h.ToString(CultureInfo.InvariantCulture)),
            "weekday" => Enumerable.Range(0, 7).Select(d => d.ToString(CultureInfo.InvariantCulture)),
            _ => groups.Keys.Order(StringComparer.Ordinal),
        };
        var items = keys.Select(key =>
        {
            var group = groups.GetValueOrDefault(key) ?? [];
            return (JsonNode?)new JsonObject
            {
                ["bucket"] = bucket is "hourOfDay" or "weekday" ? int.Parse(key, CultureInfo.InvariantCulture) : key,
                ["events"] = group.Count,
                ["plays"] = group.Count(p => p.Counted),
                ["skips"] = group.Count(p => p.Skipped),
                ["listenedMs"] = group.Sum(p => (long)p.PlayedMs),
            };
        });
        return MstreamCall.Json(new JsonObject { ["period"] = range.ToJson(), ["bucket"] = bucket, ["items"] = new JsonArray(items.ToArray()) });
    }

    /// <summary>The listening log, newest first, a page at a time (<c>before</c> is the previous page's <c>next</c>).</summary>
    private static async Task<IResult> StatsHistoryAsync(MstreamCall call)
    {
        await RequireStatsAsync(call);
        var range = StatsRange.Read(call, required: false, call.Service<TimeProvider>());
        var limit = Math.Clamp(int.TryParse(call.Query("limit"), out var l) ? l : 50, 1, 200);
        var library = new MstreamLibrary(call);
        var query = await PlayQueryAsync(call, library, range);
        if (call.Query("before") is { Length: > 0 } before)
        {
            if (!TryCursor(before, out var at, out var id))
                throw MstreamException.Invalid("before");
            query = query.Where(p => p.PlayedAt < at || p.PlayedAt == at && p.Id.CompareTo(id) < 0);
        }

        if (call.Query("track") is { Length: > 0 } hash)
        {
            var files = await library.FilesAsync();
            query = query.Where(p => files.Any(f => f.TrackId == p.TrackId && f.ContentHash == hash));
        }

        var page = await query.OrderByDescending(p => p.PlayedAt).ThenByDescending(p => p.Id).Take(limit + 1).ToListAsync(call.Cancellation);
        var more = page.Count > limit;
        page = page.Take(limit).ToList();
        var objects = await TrackObjectsAsync(library, page.Where(p => p.TrackId != null).Select(p => p.TrackId!.Value).Distinct().ToList());
        var durations = await library.Db.Tracks.AsNoTracking().Where(t => objects.Keys.Contains(t.Id)).ToDictionaryAsync(t => t.Id, t => t.DurationMs, call.Cancellation);

        var items = new JsonArray(page.Select(p => (JsonNode?)new JsonObject
        {
            ["id"] = PlayId(p),
            ["startedAt"] = Iso(p.PlayedAt),
            ["endedAt"] = Iso(p.PlayedAt.AddMilliseconds(p.PlayedMs)),
            ["playedMs"] = p.PlayedMs,
            ["durationMs"] = p.TrackId is { } t && durations.TryGetValue(t, out var d) ? d : null,
            ["outcome"] = Outcome(p),
            ["counted"] = p.Counted,
            ["source"] = p.Source,
            ["sessionId"] = null,
            ["pauseCount"] = 0,
            ["client"] = p.Client,
            ["origin"] = "local",
            ["peerId"] = null,
            ["peerName"] = null,
            ["track"] = p.TrackId is { } track && objects.TryGetValue(track, out var o) ? o.DeepClone() : null,
        }).ToArray());

        var result = new JsonObject { ["items"] = items, ["next"] = more ? Cursor(page[^1]) : null };
        if (range is not null)
            result["period"] = range.ToJson();
        return MstreamCall.Json(result);
    }

    /// <summary>Per-track totals for a batch of tracks, by path or by hash; unknown tracks are left out.</summary>
    private static async Task<IResult> StatsTracksAsync(MstreamCall call)
    {
        await RequireStatsAsync(call);
        var paths = call.Body.Strings("filePaths").Take(500).ToList();
        var hashes = call.Body.Strings("hashes").Take(500).ToList();
        if (paths.Count == 0 && hashes.Count == 0)
            throw MstreamException.Invalid("filePaths");

        var library = new MstreamLibrary(call);
        var wanted = new List<(string Key, bool ByPath, Guid TrackId)>();
        foreach (var path in paths)
        {
            if (await library.ResolveAsync(path) is { } file)
                wanted.Add((path, true, file.TrackId));
        }

        var files = await library.FilesAsync();
        var byHash = await files.Where(f => hashes.Contains(f.ContentHash)).Select(f => new { f.ContentHash, f.TrackId }).Distinct().ToListAsync(call.Cancellation);
        wanted.AddRange(byHash.Select(f => (f.ContentHash, false, f.TrackId)));

        var user = call.Access.UserId;
        var trackIds = wanted.Select(w => w.TrackId).Distinct().ToList();
        var totals = await library.Db.PlayEvents.AsNoTracking()
            .Where(p => p.UserId == user && p.TrackId != null && trackIds.Contains(p.TrackId.Value))
            .GroupBy(p => p.TrackId!.Value)
            .Select(g => new
            {
                TrackId = g.Key,
                Plays = g.Count(p => p.Counted),
                Skips = g.Count(p => p.Skipped),
                ListenedMs = g.Sum(p => (long)p.PlayedMs),
                First = g.Where(p => p.Counted).Min(p => (DateTime?)p.PlayedAt),
                Last = g.Where(p => p.Counted).Max(p => (DateTime?)p.PlayedAt),
            })
            .ToDictionaryAsync(t => t.TrackId, call.Cancellation);

        var items = new JsonArray();
        foreach (var (key, byPath, trackId) in wanted)
        {
            if (!totals.TryGetValue(trackId, out var total))
                continue;
            var item = new JsonObject
            {
                ["hash"] = key,
                ["plays"] = total.Plays,
                ["skips"] = total.Skips,
                ["listenedMs"] = total.ListenedMs,
                ["firstPlayed"] = total.First is { } first ? Iso(first) : null,
                ["lastPlayed"] = total.Last is { } last ? Iso(last) : null,
            };
            if (byPath)
                item["filePath"] = key;
            items.Add(item);
        }

        return MstreamCall.Json(new JsonObject { ["items"] = items });
    }

    /// <summary>The log's first and last plays, and the periods with plays.</summary>
    private static async Task<IResult> StatsPeriodsAsync(MstreamCall call)
    {
        await RequireStatsAsync(call);
        var zone = StatsRange.Zone(call.Query("tz"));
        var user = call.Access.UserId;
        var db = call.Service<Data.ArcanistDbContext>();
        var earliest = await db.PlayEvents.Where(p => p.UserId == user).MinAsync(p => (DateTime?)p.PlayedAt, call.Cancellation);
        var latest = await db.PlayEvents.Where(p => p.UserId == user).MaxAsync(p => (DateTime?)p.PlayedAt, call.Cancellation);

        var periods = new JsonArray();
        if (earliest is { } first)
        {
            var now = call.Service<TimeProvider>().GetUtcNow().UtcDateTime;
            foreach (var period in new[] { "week", "month", "quarter", "half", "year" })
            {
                for (var offset = 0; offset > -1000; offset--)
                {
                    var range = StatsRange.For(period, offset, zone, call.Query("tz") ?? "UTC", now);
                    if (range.To <= first)
                        break;
                    periods.Add(new JsonObject
                    {
                        ["period"] = period,
                        ["offset"] = offset,
                        ["label"] = range.Label,
                        ["from"] = Iso(range.From),
                        ["to"] = Iso(range.To),
                    });
                }
            }
        }

        return MstreamCall.Json(new JsonObject
        {
            ["earliest"] = earliest is { } e ? Iso(e) : null,
            ["latest"] = latest is { } l ? Iso(l) : null,
            ["tz"] = call.Query("tz") ?? "UTC",
            ["periods"] = periods,
        });
    }

    /// <summary>The whole log as NDJSON, oldest first.</summary>
    private static async Task<IResult> StatsExportAsync(MstreamCall call)
    {
        await RequireStatsAsync(call);
        var library = new MstreamLibrary(call);
        var user = call.Access.UserId;
        var plays = await library.Db.PlayEvents.AsNoTracking().Where(p => p.UserId == user).OrderBy(p => p.PlayedAt).ThenBy(p => p.Id).ToListAsync(call.Cancellation);
        var paths = new Dictionary<Guid, (string? Path, string? Hash)>();
        var builder = new StringBuilder();
        foreach (var play in plays)
        {
            (string? Path, string? Hash) where = default;
            if (play.TrackId is { } trackId && !paths.TryGetValue(trackId, out where))
            {
                var file = await (await library.FilesAsync()).AsNoTracking().Where(f => f.TrackId == trackId).OrderBy(f => f.Id).FirstOrDefaultAsync(call.Cancellation);
                where = paths[trackId] = file is null ? default : (await library.PathOfAsync(file.LibrarySourceId, file.RelativePath, file.CueTrack), file.ContentHash);
            }

            builder.Append(new JsonObject
            {
                ["id"] = PlayId(play),
                ["startedAt"] = Iso(play.PlayedAt),
                ["endedAt"] = Iso(play.PlayedAt.AddMilliseconds(play.PlayedMs)),
                ["playedMs"] = play.PlayedMs,
                ["durationMs"] = null,
                ["outcome"] = Outcome(play),
                ["counted"] = play.Counted,
                ["source"] = play.Source,
                ["sessionId"] = null,
                ["pauseCount"] = 0,
                ["client"] = play.Client,
                ["filePath"] = where.Path,
                ["peerId"] = null,
                ["trackHash"] = where.Hash,
            }.ToJsonString()).Append('\n');
        }

        return Results.Text(builder.ToString(), "application/x-ndjson", Encoding.UTF8);
    }

    /// <summary>The user's plays in a range (and, if asked, outside the ignored vpaths); none from peers.</summary>
    private static async Task<List<PlayEvent>> PlaysAsync(MstreamCall call, MstreamLibrary library, StatsRange range) =>
        await (await PlayQueryAsync(call, library, range)).ToListAsync(call.Cancellation);

    private static async Task<IQueryable<PlayEvent>> PlayQueryAsync(MstreamCall call, MstreamLibrary library, StatsRange? range)
    {
        var user = call.Access.UserId;
        var plays = library.Db.PlayEvents.AsNoTracking().Where(p => p.UserId == user);
        if (call.Query("origin") == "peers")
            return plays.Where(_ => false);
        if (range is not null)
            plays = plays.Where(p => p.PlayedAt >= range.From && p.PlayedAt < range.To);
        if (call.Http.Request.Query["ignoreVPaths"].Count > 0)
        {
            var files = await library.FilesAsync();
            plays = plays.Where(p => files.Any(f => f.TrackId == p.TrackId));
        }

        return plays.OrderBy(p => p.PlayedAt);
    }

    private static async Task<Dictionary<Guid, JsonObject>> TrackObjectsAsync(MstreamLibrary library, IReadOnlyList<Guid> trackIds)
    {
        var models = (await library.TracksAsync(trackIds)).Where(t => t.Files.Count > 0).ToList();
        var objects = await library.ObjectsByFileAsync(models.Select(t => (t, t.Files[0].Id)).ToList());
        return models.Where(t => objects.ContainsKey(t.Files[0].Id)).ToDictionary(t => t.Id, t => objects[t.Files[0].Id]);
    }

    /// <summary>Plays split where the user stopped listening for half an hour.</summary>
    private static List<List<PlayEvent>> Sessions(IReadOnlyList<PlayEvent> plays)
    {
        var sessions = new List<List<PlayEvent>>();
        var end = DateTime.MinValue;
        foreach (var play in plays.OrderBy(p => p.PlayedAt))
        {
            if (sessions.Count == 0 || play.PlayedAt - end > SessionGap)
                sessions.Add([]);
            sessions[^1].Add(play);
            var finished = play.PlayedAt.AddMilliseconds(play.PlayedMs);
            end = finished > end ? finished : end;
        }

        return sessions;
    }

    /// <summary>Days in a row with plays: the run that reaches today (or yesterday), and the longest.</summary>
    private static JsonObject Streaks(IEnumerable<DateOnly> days, DateOnly today)
    {
        var set = days.ToHashSet();
        var longest = 0;
        foreach (var day in set.Where(d => !set.Contains(d.AddDays(-1))))
        {
            var length = 1;
            while (set.Contains(day.AddDays(length)))
                length++;
            longest = Math.Max(longest, length);
        }

        var current = 0;
        var from = set.Contains(today) ? today : today.AddDays(-1);
        while (set.Contains(from.AddDays(-current)))
            current++;
        return new JsonObject { ["current"] = current, ["longest"] = longest };
    }

    private static string Outcome(PlayEvent play) => play.Skipped ? "skipped" : play.Counted ? "completed" : "stopped";

    private static string PlayId(PlayEvent play) => play.ExternalId ?? play.Id.ToString("N");

    private static string Cursor(PlayEvent play) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes($"{play.PlayedAt.Ticks}:{play.Id:N}")).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static bool TryCursor(string cursor, out DateTime at, out Guid id)
    {
        (at, id) = (default, default);
        try
        {
            var padded = cursor.Replace('-', '+').Replace('_', '/');
            padded += new string('=', (4 - padded.Length % 4) % 4);
            var parts = Encoding.UTF8.GetString(Convert.FromBase64String(padded)).Split(':');
            if (parts.Length != 2 || !long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks) || !Guid.TryParseExact(parts[1], "N", out id))
                return false;
            at = new DateTime(ticks, DateTimeKind.Utc);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    internal static string Iso(DateTime utc) => DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

    private static string? Text(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    /// <summary>A time as an ISO 8601 string or epoch milliseconds.</summary>
    private static DateTime? Time(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var value))
            return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var ms))
            return ms is > 0 and < 253402300799999 ? DateTime.UnixEpoch.AddMilliseconds(ms) : null;
        return value.ValueKind == JsonValueKind.String ? ParseTime(value.GetString()) : null;
    }

    private static DateTime? ParseTime(string? text) =>
        DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed) ? parsed.UtcDateTime : null;
}

/// <summary>
/// The range a statistics read covers: a preset period (<c>week</c>, <c>month</c>, <c>quarter</c>, <c>half</c>,
/// <c>year</c>, <c>all</c>) some periods back, or an explicit <c>from</c>/<c>to</c>; days begin in <c>tz</c>.
/// </summary>
internal sealed record StatsRange(DateTime From, DateTime To, string Label, TimeZoneInfo TimeZone, string TimeZoneId, DateTime Now)
{
    public DateTime Local(DateTime utc) => TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), TimeZone);

    public DateOnly LocalDate(DateTime utc) => DateOnly.FromDateTime(Local(utc));

    public JsonObject ToJson() => new()
    {
        ["label"] = Label,
        ["from"] = MstreamExtension.Iso(From),
        ["to"] = MstreamExtension.Iso(To),
        ["tz"] = TimeZoneId,
    };

    /// <param name="required">Without a period or from/to, read the month (true) or nothing (false).</param>
    public static StatsRange? Read(MstreamCall call, bool required, TimeProvider time)
    {
        var tzId = call.Query("tz") ?? "UTC";
        var zone = Zone(tzId);
        var now = time.GetUtcNow().UtcDateTime;
        var (from, to) = (call.Query("from"), call.Query("to"));
        if (from is not null || to is not null)
        {
            if (!DateTimeOffset.TryParse(from, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var start)
                || !DateTimeOffset.TryParse(to, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var end) || end <= start)
                throw MstreamException.Invalid("from");
            return new StatsRange(start.UtcDateTime, end.UtcDateTime, "Custom", zone, tzId, now);
        }

        var period = call.Query("period");
        if (period is null && !required)
            return null;
        var offset = int.TryParse(call.Query("offset"), out var o) ? o : 0;
        if (offset > 0)
            throw MstreamException.Invalid("offset");
        return For(period ?? "month", offset, zone, tzId, now);
    }

    public static StatsRange For(string period, int offset, TimeZoneInfo zone, string tzId, DateTime now)
    {
        var today = TimeZoneInfo.ConvertTimeFromUtc(now, zone).Date;
        (DateTime start, DateTime end, string label) = period switch
        {
            "week" => Week(today.AddDays(-(((int)today.DayOfWeek + 6) % 7) + 7 * offset)),
            "month" => Month(new DateTime(today.Year, today.Month, 1).AddMonths(offset), 1),
            "quarter" => Month(new DateTime(today.Year, (today.Month - 1) / 3 * 3 + 1, 1).AddMonths(3 * offset), 3),
            "half" => Month(new DateTime(today.Year, today.Month <= 6 ? 1 : 7, 1).AddMonths(6 * offset), 6),
            "year" => (new DateTime(today.Year + offset, 1, 1), new DateTime(today.Year + offset + 1, 1, 1), (today.Year + offset).ToString(CultureInfo.InvariantCulture)),
            "all" => (new DateTime(2000, 1, 1), today.AddDays(1), "All time"),
            _ => throw MstreamException.Invalid("period"),
        };
        return new StatsRange(Utc(start, zone), Utc(end, zone), label, zone, tzId, now);

        static (DateTime, DateTime, string) Week(DateTime monday) =>
            (monday, monday.AddDays(7), $"{ISOWeek.GetYear(monday)}-W{ISOWeek.GetWeekOfYear(monday):00}");

        static (DateTime, DateTime, string) Month(DateTime first, int months) => (first, first.AddMonths(months), months switch
        {
            1 => first.ToString("yyyy-MM", CultureInfo.InvariantCulture),
            3 => $"{first.Year} Q{(first.Month - 1) / 3 + 1}",
            _ => $"{first.Year} H{(first.Month - 1) / 6 + 1}",
        });
    }

    public static TimeZoneInfo Zone(string? id)
    {
        if (string.IsNullOrWhiteSpace(id) || id == "UTC")
            return TimeZoneInfo.Utc;
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            throw MstreamException.Invalid("tz");
        }
    }

    /// <summary>A local midnight in UTC; where clocks jump forward at midnight, the first hour that exists.</summary>
    private static DateTime Utc(DateTime local, TimeZoneInfo zone)
    {
        local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        while (zone.IsInvalidTime(local))
            local = local.AddMinutes(30);
        return TimeZoneInfo.ConvertTimeToUtc(local, zone);
    }
}

/// <summary>What each player is playing, announced by the apps; in memory, ten minutes per announcement.</summary>
internal sealed class MstreamNowPlaying(TimeProvider time)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

    public sealed record Entry(string SessionId, string FilePath, JsonObject Track, string Since, string ExpiresAt, DateTime Expires);

    private readonly ConcurrentDictionary<(Guid UserId, string SessionId), Entry> _entries = new();

    /// <returns>When the entry expires.</returns>
    public string Set(Guid userId, string sessionId, string filePath, JsonObject track)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var expires = now + Lifetime;
        var since = _entries.TryGetValue((userId, sessionId), out var existing) && existing.FilePath == filePath ? existing.Since : MstreamExtension.Iso(now);
        _entries[(userId, sessionId)] = new Entry(sessionId, filePath, track, since, MstreamExtension.Iso(expires), expires);
        return MstreamExtension.Iso(expires);
    }

    public IReadOnlyList<Entry> Of(Guid userId)
    {
        var now = time.GetUtcNow().UtcDateTime;
        foreach (var expired in _entries.Where(e => e.Value.Expires <= now).Select(e => e.Key).ToList())
            _entries.TryRemove(expired, out _);
        return _entries.Where(e => e.Key.UserId == userId).Select(e => e.Value).OrderBy(e => e.SessionId, StringComparer.Ordinal).ToList();
    }
}
