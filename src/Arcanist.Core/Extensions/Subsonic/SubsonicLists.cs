using System.Collections.Concurrent;
using Arcanist.Core.Application.Catalog;
using Arcanist.Core.Data;
using Arcanist.Core.Data.Search;
using Microsoft.EntityFrameworkCore;
using AlbumEntity = Arcanist.Core.Data.Entities.Album;

namespace Arcanist.Core.Extensions.Subsonic;

internal static partial class SubsonicMethods
{
    private const int MaxListSize = 500;

    /// <summary>
    /// Album lists, which apps also page through to sync the whole library: every order ends on the id, so pages
    /// neither skip nor repeat albums.
    /// </summary>
    private static async Task<IResult> GetAlbumListAsync(SubsonicCall call, bool legacy)
    {
        var parameters = call.Parameters;
        var type = parameters.Required("type");
        var size = Math.Clamp(parameters.Int("size") ?? 10, 0, MaxListSize);
        var offset = Math.Max(0, parameters.Int("offset") ?? 0);
        var library = new SubsonicLibrary(call);
        var scope = await library.ScopeAsync();
        var db = library.Db;
        var user = call.Access.UserId;
        var albums = scope.Albums;

        IQueryable<AlbumEntity> ordered;
        switch (type)
        {
            case "random":
                ordered = albums.OrderBy(_ => EF.Functions.Random());
                offset = 0;
                break;
            case "newest":
                ordered = albums.OrderByDescending(a => a.Tracks.Min(t => t.AddedAt)).ThenBy(a => a.Id);
                break;
            case "highest":
                ordered = albums
                    .Where(a => db.TrackRatings.Any(r => r.UserId == user && r.Track.AlbumId == a.Id))
                    .OrderByDescending(a => db.TrackRatings.Where(r => r.UserId == user && r.Track.AlbumId == a.Id).Average(r => (double)r.Rating))
                    .ThenBy(a => a.Id);
                break;
            case "frequent":
                ordered = albums
                    .Where(a => db.TrackPlayStats.Any(p => p.UserId == user && p.PlayCount > 0 && p.Track.AlbumId == a.Id))
                    .OrderByDescending(a => db.TrackPlayStats.Where(p => p.UserId == user && p.Track.AlbumId == a.Id).Sum(p => p.PlayCount))
                    .ThenBy(a => a.Id);
                break;
            case "recent":
                ordered = albums
                    .Where(a => db.TrackPlayStats.Any(p => p.UserId == user && p.LastPlayedAt != null && p.Track.AlbumId == a.Id))
                    .OrderByDescending(a => db.TrackPlayStats.Where(p => p.UserId == user && p.Track.AlbumId == a.Id).Max(p => p.LastPlayedAt))
                    .ThenBy(a => a.Id);
                break;
            case "alphabeticalByName":
                ordered = albums.OrderBy(a => a.NormalizedTitle).ThenBy(a => a.Id);
                break;
            case "alphabeticalByArtist":
                ordered = albums.OrderBy(a => a.AlbumArtist.NormalizedName).ThenBy(a => a.Year).ThenBy(a => a.NormalizedTitle).ThenBy(a => a.Id);
                break;
            case "starred":
                // Arcanist stars tracks, not albums.
                ordered = albums.Where(_ => false).OrderBy(a => a.Id);
                break;
            case "byYear":
            {
                var from = parameters.Int("fromYear") ?? throw SubsonicException.Missing("fromYear");
                var to = parameters.Int("toYear") ?? throw SubsonicException.Missing("toYear");
                var (low, high) = from <= to ? (from, to) : (to, from);
                var inRange = albums.Where(a => a.Year >= low && a.Year <= high);
                ordered = from <= to
                    ? inRange.OrderBy(a => a.Year).ThenBy(a => a.NormalizedTitle).ThenBy(a => a.Id)
                    : inRange.OrderByDescending(a => a.Year).ThenBy(a => a.NormalizedTitle).ThenBy(a => a.Id);
                break;
            }
            case "byGenre":
            {
                var genre = TextNormalizer.Normalize(parameters.Required("genre"));
                ordered = albums.Where(a => a.Tracks.Any(t => t.Genres.Any(g => g.Genre.NormalizedName == genre)))
                    .OrderBy(a => a.NormalizedTitle).ThenBy(a => a.Id);
                break;
            }
            default:
                throw SubsonicException.Invalid("type");
        }

        var page = await library.AlbumsAsync(ordered.Skip(offset).Take(size));
        return legacy
            ? call.Ok(new SubsonicNode("albumList").List("album", await library.AlbumNodesAsync(page, "album", asDirectory: true)))
            : call.Ok(new SubsonicNode("albumList2").List("album", await library.AlbumNodesAsync(page)));
    }

    private static async Task<IResult> GetRandomSongsAsync(SubsonicCall call)
    {
        var parameters = call.Parameters;
        var library = new SubsonicLibrary(call);
        var scope = await library.ScopeAsync();
        var tracks = scope.Tracks;
        if (parameters["genre"] is { Length: > 0 } genre)
        {
            var normalized = TextNormalizer.Normalize(genre);
            tracks = tracks.Where(t => t.Genres.Any(g => g.Genre.NormalizedName == normalized));
        }

        if (parameters.Int("fromYear") is { } from)
            tracks = tracks.Where(t => t.Album.Year >= from);
        if (parameters.Int("toYear") is { } to)
            tracks = tracks.Where(t => t.Album.Year <= to);

        var size = Math.Clamp(parameters.Int("size") ?? 10, 0, MaxListSize);
        var songs = await library.TracksAsync(tracks.OrderBy(_ => EF.Functions.Random()).Take(size));
        return call.Ok(new SubsonicNode("randomSongs").List("song", await library.SongsAsync(songs)));
    }

    private static async Task<IResult> GetSongsByGenreAsync(SubsonicCall call)
    {
        var parameters = call.Parameters;
        var genre = TextNormalizer.Normalize(parameters.Required("genre"));
        var count = Math.Clamp(parameters.Int("count") ?? 10, 0, MaxListSize);
        var offset = Math.Max(0, parameters.Int("offset") ?? 0);
        var library = new SubsonicLibrary(call);
        var scope = await library.ScopeAsync();

        var songs = await library.TracksAsync(scope.Tracks
            .Where(t => t.Genres.Any(g => g.Genre.NormalizedName == genre))
            .OrderBy(t => t.NormalizedTitle).ThenBy(t => t.Id)
            .Skip(offset).Take(count));
        return call.Ok(new SubsonicNode("songsByGenre").List("song", await library.SongsAsync(songs)));
    }

    /// <summary>Starred songs, most recent first. Arcanist stars tracks only, so albums and artists are always empty.</summary>
    private static async Task<IResult> GetStarredAsync(SubsonicCall call, string element)
    {
        var library = new SubsonicLibrary(call);
        var scope = await library.ScopeAsync();
        var user = call.Access.UserId;
        var ids = await library.Db.TrackRatings.AsNoTracking()
            .Where(r => r.UserId == user && r.Rating == 5 && scope.Tracks.Any(t => t.Id == r.TrackId))
            .OrderByDescending(r => r.RatedAt).ThenBy(r => r.TrackId)
            .Select(r => r.TrackId)
            .ToListAsync(call.Cancellation);

        return call.Ok(new SubsonicNode(element)
            .List("artist", [])
            .List("album", [])
            .List("song", await library.SongsAsync(await library.TracksAsync(ids))));
    }

    /// <summary>
    /// Searches artists, albums and songs, each with its own count and offset. An empty query (or <c>""</c>) matches
    /// everything, which apps use to sync the whole library page by page.
    /// </summary>
    private static async Task<IResult> SearchAsync(SubsonicCall call, bool legacy)
    {
        var parameters = call.Parameters;
        var query = (parameters["query"] ?? "").Trim();
        if (query is "\"\"" or "''" or "*")
            query = "";

        var library = new SubsonicLibrary(call);
        var scope = await library.ScopeAsync();
        var db = library.Db;

        var artists = scope.Artists;
        var albums = scope.Albums;
        var tracks = scope.Tracks;
        if (query.Length > 0)
        {
            artists = Matching(db, artists, SearchKind.Artists, query);
            albums = Matching(db, albums, SearchKind.Albums, query);
            tracks = Matching(db, tracks, SearchKind.Tracks, query);
        }

        var artistPage = await artists.OrderBy(a => a.NormalizedName).ThenBy(a => a.Id)
            .Skip(Offset("artistOffset")).Take(Count("artistCount")).AsNoTracking().ToListAsync(call.Cancellation);
        var albumPage = await library.AlbumsAsync(albums.OrderBy(a => a.NormalizedTitle).ThenBy(a => a.Id)
            .Skip(Offset("albumOffset")).Take(Count("albumCount")));
        var songPage = await library.TracksAsync(tracks.OrderBy(t => t.NormalizedTitle).ThenBy(t => t.Id)
            .Skip(Offset("songOffset")).Take(Count("songCount")));

        var artistModels = artistPage.Select(CatalogService.ToModel).ToList();
        var node = new SubsonicNode(legacy ? "searchResult2" : "searchResult3")
            .List("artist", legacy
                ? artistModels.Select(a => new SubsonicNode("artist").Attr("id", a.Id).Attr("name", a.Name))
                : await library.ArtistNodesAsync(artistModels, scope))
            .List("album", await library.AlbumNodesAsync(albumPage, "album", asDirectory: legacy))
            .List("song", await library.SongsAsync(songPage));
        return call.Ok(node);

        int Count(string name) => Math.Clamp(parameters.Int(name) ?? 20, 0, MaxListSize);
        int Offset(string name) => Math.Max(0, parameters.Int(name) ?? 0);
    }

    /// <summary>Rows matching every word of the query, as Arcanist's own search does.</summary>
    private static IQueryable<T> Matching<T>(ArcanistDbContext db, IQueryable<T> rows, SearchKind kind, string query) where T : Data.Entities.Entity
    {
        var hits = FullTextSearch.Match(db, kind, query);
        return hits is null ? rows.Where(_ => false) : rows.Where(r => hits.Any(h => h.Id == r.Id));
    }

    private static async Task<IResult> GetNowPlayingAsync(SubsonicCall call)
    {
        var library = new SubsonicLibrary(call);
        var playing = call.Service<SubsonicNowPlaying>().Current(call.Service<TimeProvider>().GetUtcNow().UtcDateTime);
        var tracks = await library.Catalog.VisibleTracksAsync(call.Access, playing.Select(p => p.TrackId).Distinct().ToList(), call.Cancellation);
        var visible = playing.Where(p => tracks.ContainsKey(p.TrackId)).ToList();
        var songs = await library.SongsAsync(visible.Select(p => tracks[p.TrackId]).ToList(), "entry");
        var now = call.Service<TimeProvider>().GetUtcNow().UtcDateTime;

        return call.Ok(new SubsonicNode("nowPlaying").List("entry", songs.Select((song, i) => song
            .Attr("username", visible[i].UserName)
            .Attr("minutesAgo", (int)(now - visible[i].StartedAt).TotalMinutes)
            .Attr("playerId", visible[i].PlayerId)
            .Attr("playerName", visible[i].Client))));
    }
}

/// <summary>What Subsonic apps report as playing (<c>scrobble</c> with <c>submission=false</c>), kept in memory.</summary>
internal sealed class SubsonicNowPlaying
{
    public sealed record Entry(Guid UserId, string UserName, string Client, int PlayerId, Guid TrackId, DateTime StartedAt, DateTime Until);

    private readonly ConcurrentDictionary<(Guid UserId, string Client), Entry> _entries = new();

    public void Set(Guid userId, string userName, string client, Guid trackId, int durationMs, DateTime now)
    {
        var key = (userId, client);
        // Entries last as long as the song, and a bit more for pauses.
        _entries[key] = new Entry(userId, userName, client, (HashCode.Combine(userId, client) & int.MaxValue) % 1_000_000, trackId, now,
            now.AddMilliseconds(durationMs).AddMinutes(10));
    }

    public void Clear(Guid userId, string client) => _entries.TryRemove((userId, client), out _);

    public IReadOnlyList<Entry> Current(DateTime now)
    {
        foreach (var (key, entry) in _entries.Where(e => e.Value.Until < now))
            _entries.TryRemove(key, out _);
        return _entries.Values.OrderByDescending(e => e.StartedAt).ToList();
    }
}
