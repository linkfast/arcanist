using System.Text.Json;
using System.Text.Json.Nodes;
using Arcanist.Core.Application.Listening;
using Arcanist.Core.Data;
using Arcanist.Core.Data.Entities;
using Arcanist.Core.Data.Search;
using Microsoft.EntityFrameworkCore;

namespace Arcanist.Core.Extensions.Mstream;

/// <summary>The library routes (<c>/api/v1/db/…</c>).</summary>
internal sealed partial class MstreamExtension
{
    private const int MaxList = 500;
    private const int SearchLimit = 30;

    private static async Task<IResult> MetadataAsync(MstreamCall call)
    {
        var path = call.Body.Required("filepath");
        var library = new MstreamLibrary(call);
        return MstreamCall.Json(await library.ObjectAsync(path, await library.ResolveAsync(path)));
    }

    private static async Task<IResult> MetadataBatchAsync(MstreamCall call)
    {
        if (call.Body.Root is not { ValueKind: JsonValueKind.Array } paths)
            throw MstreamException.Invalid("filepaths");

        var library = new MstreamLibrary(call);
        var result = new JsonObject();
        foreach (var path in paths.EnumerateArray().Where(p => p.ValueKind == JsonValueKind.String).Select(p => p.GetString()!).Distinct().Take(MaxList))
            result[path] = await library.ObjectAsync(path, await library.ResolveAsync(path));
        return MstreamCall.Json(result);
    }

    private static async Task<IResult> ArtistsAsync(MstreamCall call)
    {
        var library = new MstreamLibrary(call);
        var albums = await library.AlbumsAsync();
        var tracks = await library.TracksAsync();
        var names = await library.Db.Artists
            .Where(a => albums.Any(al => al.AlbumArtistId == a.Id)
                        || library.Db.TrackArtists.Any(ta => ta.ArtistId == a.Id && ta.Role == ArtistRole.Primary && tracks.Any(t => t.Id == ta.TrackId)))
            .Select(a => a.Name)
            .ToListAsync(call.Cancellation);
        return MstreamCall.Json(new JsonObject
        {
            ["artists"] = new JsonArray(names.Distinct().Order(StringComparer.OrdinalIgnoreCase).Select(n => (JsonNode?)n).ToArray()),
        });
    }

    /// <summary>The albums of an artist by name (as album artist or main performer), newest first.</summary>
    private static async Task<IResult> ArtistAlbumsAsync(MstreamCall call)
    {
        var artist = TextNormalizer.Normalize(call.Body.Required("artist"));
        var library = new MstreamLibrary(call);
        var albums = (await library.AlbumsAsync())
            .Where(a => a.AlbumArtist.NormalizedName == artist
                        || a.Tracks.Any(t => t.Artists.Any(ta => ta.Role == ArtistRole.Primary && ta.Artist.NormalizedName == artist)))
            .OrderByDescending(a => a.Year).ThenBy(a => a.NormalizedTitle).ThenBy(a => a.Id);
        return MstreamCall.Json(new JsonObject { ["albums"] = await library.AlbumCardsAsync(albums) });
    }

    private static async Task<IResult> AlbumsAsync(MstreamCall call)
    {
        var library = new MstreamLibrary(call);
        var albums = (await library.AlbumsAsync()).OrderBy(a => a.NormalizedTitle).ThenBy(a => a.Year).ThenBy(a => a.Id);
        return MstreamCall.Json(new JsonObject { ["albums"] = await library.AlbumCardsAsync(albums) });
    }

    /// <summary>The songs of the albums with a name (and artist and year, when given), in disc and track order.</summary>
    private static async Task<IResult> AlbumSongsAsync(MstreamCall call)
    {
        var library = new MstreamLibrary(call);
        if (call.Body.String("album") is not { Length: > 0 } name)
            return MstreamCall.Json(new JsonArray());

        var title = TextNormalizer.Normalize(name);
        var albums = (await library.AlbumsAsync()).Where(a => a.NormalizedTitle == title);
        if (call.Body.Int("year") is { } year)
            albums = albums.Where(a => a.Year == year);
        if (call.Body.String("artist") is { Length: > 0 } artistName)
        {
            var artist = TextNormalizer.Normalize(artistName);
            albums = albums.Where(a => a.AlbumArtist.NormalizedName == artist
                                       || a.Tracks.Any(t => t.Artists.Any(ta => ta.Role == ArtistRole.Primary && ta.Artist.NormalizedName == artist)));
        }

        var tracks = (await library.TracksAsync()).Where(t => albums.Any(a => a.Id == t.AlbumId))
            .OrderBy(t => t.AlbumId).ThenBy(t => t.DiscNumber ?? 1).ThenBy(t => t.TrackNumber).ThenBy(t => t.NormalizedTitle);
        return MstreamCall.Json(new JsonArray((await library.ObjectsAsync(await library.TracksAsync(tracks))).Cast<JsonNode?>().ToArray()));
    }

    private static async Task<IResult> GenresAsync(MstreamCall call)
    {
        var library = new MstreamLibrary(call);
        var tracks = await library.TracksAsync();
        var genres = await library.Db.TrackGenres
            .Where(tg => tracks.Any(t => t.Id == tg.TrackId))
            .GroupBy(tg => tg.Genre.Name)
            .Select(g => new { Name = g.Key, Count = g.Count() })
            .ToListAsync(call.Cancellation);
        return MstreamCall.Json(new JsonObject
        {
            ["genres"] = new JsonArray(genres.OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase)
                .Select(g => (JsonNode?)new JsonObject { ["name"] = g.Name, ["track_count"] = g.Count }).ToArray()),
        });
    }

    private static async Task<IResult> GenreSongsAsync(MstreamCall call)
    {
        var genre = TextNormalizer.Normalize(call.Body.Required("genre"));
        var library = new MstreamLibrary(call);
        var tracks = (await library.TracksAsync())
            .Where(t => t.Genres.Any(g => g.Genre.NormalizedName == genre))
            .OrderBy(t => t.NormalizedTitle).ThenBy(t => t.Id)
            .Skip(Math.Max(0, call.Body.Int("offset") ?? 0))
            .Take(Math.Clamp(call.Body.Int("limit") ?? MaxList, 1, MaxList));
        return await TrackListAsync(library, tracks);
    }

    /// <summary>
    /// Artists, albums, songs ("artist - title") and file paths matching the text; at most 30 of each. Words match
    /// as in Arcanist's own search; file paths match as a substring.
    /// </summary>
    private static async Task<IResult> SearchAsync(MstreamCall call)
    {
        var query = call.Body.Required("search");
        var library = new MstreamLibrary(call);
        var db = library.Db;
        var tracks = await library.TracksAsync();
        var albums = await library.AlbumsAsync();
        var result = new JsonObject { ["artists"] = new JsonArray(), ["albums"] = new JsonArray(), ["title"] = new JsonArray(), ["files"] = new JsonArray() };

        if (!call.Body.Bool("noArtists") && FullTextSearch.Match(db, SearchKind.Artists, query) is { } artistHits)
        {
            var names = await db.Artists
                .Where(a => artistHits.Any(h => h.Id == a.Id)
                            && (albums.Any(al => al.AlbumArtistId == a.Id)
                                || db.TrackArtists.Any(ta => ta.ArtistId == a.Id && ta.Role == ArtistRole.Primary && tracks.Any(t => t.Id == ta.TrackId))))
                .OrderBy(a => a.NormalizedName).Select(a => a.Name).Take(SearchLimit).ToListAsync(call.Cancellation);
            result["artists"] = new JsonArray(names.Select(n => (JsonNode?)new JsonObject { ["name"] = n, ["album_art_file"] = null, ["filepath"] = false }).ToArray());
        }

        if (!call.Body.Bool("noAlbums") && FullTextSearch.Match(db, SearchKind.Albums, query) is { } albumHits)
        {
            var found = await albums.Where(a => albumHits.Any(h => h.Id == a.Id)).OrderBy(a => a.NormalizedTitle)
                .Select(a => new { a.Title, a.ArtworkId }).Take(SearchLimit).ToListAsync(call.Cancellation);
            result["albums"] = new JsonArray(found.Select(a => (JsonNode?)new JsonObject
            {
                ["name"] = a.Title, ["album_art_file"] = MstreamLibrary.ArtFile(a.ArtworkId), ["filepath"] = false,
            }).ToArray());
        }

        if (!call.Body.Bool("noTitles") && FullTextSearch.Match(db, SearchKind.Tracks, query) is { } trackHits)
        {
            var found = await library.TracksAsync(tracks.Where(t => trackHits.Any(h => h.Id == t.Id)).OrderBy(t => t.NormalizedTitle).ThenBy(t => t.Id).Take(SearchLimit));
            var objects = await library.ObjectsAsync(found, lite: true);
            result["title"] = new JsonArray(objects.Select(o =>
            {
                var metadata = o["metadata"]!.AsObject();
                return (JsonNode?)new JsonObject
                {
                    ["name"] = $"{metadata["artist"]?.GetValue<string>()} - {metadata["title"]?.GetValue<string>()}",
                    ["album_art_file"] = metadata["album-art"]?.DeepClone(),
                    ["filepath"] = o["filepath"]!.DeepClone(),
                    ["metadata"] = metadata.DeepClone(),
                };
            }).ToArray());
        }

        if (!call.Body.Bool("noFiles"))
        {
            var lower = query.ToLowerInvariant();
            var files = await (await library.FilesAsync()).Where(f => f.RelativePath.ToLower().Contains(lower))
                .OrderBy(f => f.RelativePath).Select(f => new { f.LibrarySourceId, f.RelativePath, f.CueTrack }).Take(SearchLimit).ToListAsync(call.Cancellation);
            var paths = new List<JsonNode?>();
            foreach (var file in files)
            {
                if (await library.PathOfAsync(file.LibrarySourceId, file.RelativePath, file.CueTrack) is { } path)
                    paths.Add(new JsonObject { ["name"] = path, ["filepath"] = path });
            }

            result["files"] = new JsonArray(paths.ToArray());
        }

        return MstreamCall.Json(result);
    }

    /// <summary>Rated songs, best first. In single-star mode, the starred ones (lower ratings are kept, unseen).</summary>
    private static async Task<IResult> RatedAsync(MstreamCall call)
    {
        var library = new MstreamLibrary(call);
        var tracks = await library.TracksAsync();
        var user = call.Access.UserId;
        var lowest = call.Service<RatingSettings>().Current == RatingMode.Star ? 5 : 1;
        var ids = await library.Db.TrackRatings.Where(r => r.UserId == user && r.Rating >= lowest && tracks.Any(t => t.Id == r.TrackId))
            .OrderByDescending(r => r.Rating).ThenByDescending(r => r.RatedAt).Select(r => r.TrackId).Take(5000).ToListAsync(call.Cancellation);
        return await TrackListAsync(library, ids);
    }

    /// <summary>
    /// Rates a song from 0 to 10 (half stars), or clears its rating with null or 0. In single-star mode, from the
    /// panel's threshold up it gets the star; below it, the rating is kept as such (not shown in that mode).
    /// </summary>
    private static async Task<IResult> RateSongAsync(MstreamCall call)
    {
        var library = new MstreamLibrary(call);
        var file = await library.ResolveAsync(call.Body.Required("filepath")) ?? throw MstreamException.NotFound();
        int? rating = call.Body.IsNull("rating") ? null : call.Body.Int("rating") ?? throw MstreamException.Invalid("rating");
        if (rating is < 0 or > 10)
            throw MstreamException.Invalid("rating");

        int? stars = rating is null or 0 ? null : (rating.Value + 1) / 2;
        var singleStar = call.Service<RatingSettings>().Current == RatingMode.Star;
        if (singleStar && stars >= (await call.Service<MstreamSettings>().GetAsync(call.Cancellation)).StarThreshold)
            stars = 5;
        await call.Service<ListeningService>().RateTrackAsync(file.TrackId, stars, keepUnseen: singleStar, call.Cancellation);
        return MstreamCall.Json(new JsonObject());
    }

    private static async Task<IResult> RecentlyAddedAsync(MstreamCall call)
    {
        var limit = call.Body.Int("limit") ?? throw MstreamException.Invalid("limit");
        var library = new MstreamLibrary(call);
        var tracks = (await library.TracksAsync()).OrderByDescending(t => t.AddedAt).ThenBy(t => t.Id).Take(Math.Clamp(limit, 1, MaxList));
        return await TrackListAsync(library, tracks);
    }

    private static async Task<IResult> RecentlyPlayedAsync(MstreamCall call)
    {
        var limit = Math.Clamp(call.Body.Int("limit") ?? 20, 1, MaxList);
        var library = new MstreamLibrary(call);
        var tracks = await library.TracksAsync();
        var user = call.Access.UserId;
        var ids = await library.Db.TrackPlayStats
            .Where(p => p.UserId == user && p.LastPlayedAt != null && tracks.Any(t => t.Id == p.TrackId))
            .OrderByDescending(p => p.LastPlayedAt).Select(p => p.TrackId).Take(limit).ToListAsync(call.Cancellation);
        return await TrackListAsync(library, ids);
    }

    private static async Task<IResult> MostPlayedAsync(MstreamCall call)
    {
        var limit = Math.Clamp(call.Body.Int("limit") ?? 20, 1, MaxList);
        var library = new MstreamLibrary(call);
        var tracks = await library.TracksAsync();
        var user = call.Access.UserId;
        var ids = await library.Db.TrackPlayStats
            .Where(p => p.UserId == user && p.PlayCount > 0 && tracks.Any(t => t.Id == p.TrackId))
            .OrderByDescending(p => p.PlayCount).ThenByDescending(p => p.LastPlayedAt).Select(p => p.TrackId).Take(limit).ToListAsync(call.Cancellation);
        return await TrackListAsync(library, ids);
    }

    private static async Task<IResult> TrackListAsync(MstreamLibrary library, IQueryable<Data.Entities.Track> tracks) =>
        MstreamCall.Json(new JsonArray((await library.ObjectsAsync(await library.TracksAsync(tracks))).Cast<JsonNode?>().ToArray()));

    private static async Task<IResult> TrackListAsync(MstreamLibrary library, IReadOnlyList<Guid> ids) =>
        MstreamCall.Json(new JsonArray((await library.ObjectsAsync(await library.TracksAsync(ids))).Cast<JsonNode?>().ToArray()));
}
