using Arcanist.Core.Application.Catalog;
using Arcanist.Core.Application.Discovery;
using Arcanist.Core.Application.Libraries;
using Arcanist.Core.Data;
using Arcanist.Core.Data.Entities;
using Microsoft.EntityFrameworkCore;
using AlbumEntity = Arcanist.Core.Data.Entities.Album;
using Artist = Arcanist.Core.Application.Catalog.Artist;

namespace Arcanist.Core.Extensions.Subsonic;

internal static partial class SubsonicMethods
{
    /// <summary>Album artists grouped by index letter, sorted as Subsonic apps expect.</summary>
    private static async Task<List<(string Name, List<Artist> Artists)>> IndexAsync(SubsonicScope scope, CancellationToken cancellationToken)
    {
        var artists = (await scope.AlbumArtists.AsNoTracking().ToListAsync(cancellationToken)).Select(CatalogService.ToModel).ToList();
        return artists
            .GroupBy(SubsonicLibrary.IndexOf)
            .OrderBy(g => g.Key == "#" ? 1 : 0).ThenBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => (g.Key, g.OrderBy(SubsonicLibrary.SortKey, StringComparer.Ordinal).ThenBy(a => a.Id).ToList()))
            .ToList();
    }

    private static async Task<IResult> GetArtistsAsync(SubsonicCall call)
    {
        var library = new SubsonicLibrary(call);
        var scope = await library.ScopeAsync();
        var index = new List<SubsonicNode>();
        foreach (var (name, artists) in await IndexAsync(scope, call.Cancellation))
            index.Add(new SubsonicNode("index").Attr("name", name).List("artist", await library.ArtistNodesAsync(artists, scope)));

        return call.Ok(new SubsonicNode("artists").Attr("ignoredArticles", SubsonicLibrary.IgnoredArticles).List("index", index));
    }

    /// <summary>The folder view of older apps: album artists as top folders, their albums inside, songs inside those.</summary>
    private static async Task<IResult> GetIndexesAsync(SubsonicCall call)
    {
        var library = new SubsonicLibrary(call);
        var scope = await library.ScopeAsync();
        var lastScan = await LibraryService.Visible(library.Db, call.Access).MaxAsync(l => l.LastScanAt, call.Cancellation);
        var lastModified = lastScan is { } at ? new DateTimeOffset(DateTime.SpecifyKind(at, DateTimeKind.Utc)).ToUnixTimeMilliseconds() : 0;
        var node = new SubsonicNode("indexes").Attr("lastModified", lastModified).Attr("ignoredArticles", SubsonicLibrary.IgnoredArticles);

        // Unchanged since the app last asked: nothing to send.
        if (call.Parameters.Long("ifModifiedSince") is { } since && lastModified <= since)
            return call.Ok(node);

        var index = (await IndexAsync(scope, call.Cancellation))
            .Select(group => new SubsonicNode("index").Attr("name", group.Name)
                .List("artist", group.Artists.Select(a => new SubsonicNode("artist").Attr("id", a.Id).Attr("name", a.Name))));
        return call.Ok(node.List("index", index));
    }

    /// <summary>A folder of the folder view: an artist (its albums) or an album (its songs).</summary>
    private static async Task<IResult> GetMusicDirectoryAsync(SubsonicCall call)
    {
        var id = call.Parameters.RequiredId("id");
        var library = new SubsonicLibrary(call);
        var scope = await library.ScopeAsync(null);

        var album = await scope.Albums.AsNoTracking().SingleOrDefaultAsync(a => a.Id == id, call.Cancellation);
        if (album is not null)
        {
            var tracks = await library.Catalog.TracksAsync(null, id, null, null, null, call.Cancellation);
            return call.Ok(new SubsonicNode("directory")
                .Attr("id", id)
                .Attr("parent", album.AlbumArtistId)
                .Attr("name", album.Title)
                .List("child", await library.SongsAsync(tracks.Items, "child")));
        }

        var artist = await scope.Artists.AsNoTracking().SingleOrDefaultAsync(a => a.Id == id, call.Cancellation)
                     ?? throw SubsonicException.NotFound();
        var albums = await library.AlbumsAsync(await ArtistAlbumsAsync(scope, id, call.Cancellation));
        return call.Ok(new SubsonicNode("directory")
            .Attr("id", id)
            .Attr("name", artist.Name)
            .List("child", await library.AlbumNodesAsync(albums, "child", asDirectory: true)));
    }

    /// <summary>Albums by the artist, oldest first; those they only appear on when they have none of their own.</summary>
    private static async Task<IQueryable<AlbumEntity>> ArtistAlbumsAsync(SubsonicScope scope, Guid artistId, CancellationToken cancellationToken)
    {
        var own = scope.Albums.Where(a => a.AlbumArtistId == artistId);
        var albums = await own.AnyAsync(cancellationToken)
            ? own
            : scope.Albums.Where(a => a.Tracks.Any(t => t.Artists.Any(ta => ta.ArtistId == artistId && ta.Role == ArtistRole.Primary)));
        return albums.OrderBy(a => a.Year ?? int.MaxValue).ThenBy(a => a.NormalizedTitle).ThenBy(a => a.Id);
    }

    private static async Task<IResult> GetGenresAsync(SubsonicCall call)
    {
        var library = new SubsonicLibrary(call);
        var scope = await library.ScopeAsync(null);
        var rows = await library.Db.TrackGenres.AsNoTracking()
            .Where(tg => scope.Tracks.Any(t => t.Id == tg.TrackId))
            .Select(tg => new { tg.Genre.Name, tg.TrackId, tg.Track.AlbumId })
            .ToListAsync(call.Cancellation);

        var genres = rows.GroupBy(r => r.Name)
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g => new SubsonicNode("genre")
                .Attr("songCount", g.Select(r => r.TrackId).Distinct().Count())
                .Attr("albumCount", g.Select(r => r.AlbumId).Distinct().Count())
                .Text(g.Key));
        return call.Ok(new SubsonicNode("genres").List("genre", genres));
    }

    private static async Task<IResult> GetArtistAsync(SubsonicCall call)
    {
        var id = call.Parameters.RequiredId("id");
        var library = new SubsonicLibrary(call);
        var scope = await library.ScopeAsync(null);
        var artist = await scope.Artists.AsNoTracking().SingleOrDefaultAsync(a => a.Id == id, call.Cancellation)
                     ?? throw SubsonicException.NotFound();

        var node = (await library.ArtistNodesAsync([CatalogService.ToModel(artist)], scope))[0];
        var albums = await library.AlbumsAsync(await ArtistAlbumsAsync(scope, id, call.Cancellation));
        return call.Ok(node.List("album", await library.AlbumNodesAsync(albums)));
    }

    private static async Task<IResult> GetAlbumAsync(SubsonicCall call)
    {
        var id = call.Parameters.RequiredId("id");
        var library = new SubsonicLibrary(call);
        var album = await library.Catalog.AlbumAsync(id, call.Cancellation);
        var tracks = await library.Catalog.TracksAsync(null, id, null, null, null, call.Cancellation);
        var node = (await library.AlbumNodesAsync([album]))[0];
        return call.Ok(node.List("song", await library.SongsAsync(tracks.Items)));
    }

    private static async Task<IResult> GetSongAsync(SubsonicCall call)
    {
        var library = new SubsonicLibrary(call);
        var track = await library.Catalog.TrackAsync(call.Parameters.RequiredId("id"), call.Cancellation);
        return call.Ok((await library.SongsAsync([track]))[0]);
    }

    /// <summary>What Arcanist knows about an artist: its MusicBrainz id and related artists. No biographies.</summary>
    private static async Task<IResult> GetArtistInfoAsync(SubsonicCall call, string element)
    {
        var library = new SubsonicLibrary(call);
        var id = await ArtistOfAsync(library, call.Parameters.RequiredId("id"));
        var overview = await call.Service<DiscoveryService>().ArtistOverviewAsync(id, call.Cancellation);
        var count = Math.Clamp(call.Parameters.Int("count") ?? 20, 0, 100);
        var scope = await library.ScopeAsync(null);

        var node = new SubsonicNode(element);
        if (overview.Artist.MusicBrainzId is { } mbid)
            node.Child(new SubsonicNode("musicBrainzId").Text(mbid.ToString()));
        return call.Ok(node.List("similarArtist", await library.ArtistNodesAsync(overview.RelatedArtists.Take(count).ToList(), scope, "similarArtist")));
    }

    private static async Task<IResult> GetAlbumInfoAsync(SubsonicCall call)
    {
        var library = new SubsonicLibrary(call);
        var id = call.Parameters.RequiredId("id");
        var scope = await library.ScopeAsync(null);
        var albumId = await scope.Albums.AnyAsync(a => a.Id == id, call.Cancellation)
            ? id
            : await scope.Tracks.Where(t => t.Id == id).Select(t => (Guid?)t.AlbumId).SingleOrDefaultAsync(call.Cancellation)
              ?? throw SubsonicException.NotFound();

        var album = await library.Catalog.AlbumAsync(albumId, call.Cancellation);
        var node = new SubsonicNode("albumInfo");
        if (album.MusicBrainzId is { } mbid)
            node.Child(new SubsonicNode("musicBrainzId").Text(mbid.ToString()));
        return call.Ok(node);
    }

    /// <summary>An artist "radio" from Arcanist's artist mix, or a track radio when given a song.</summary>
    private static async Task<IResult> GetSimilarSongsAsync(SubsonicCall call, string element)
    {
        var library = new SubsonicLibrary(call);
        var id = call.Parameters.RequiredId("id");
        var count = Math.Clamp(call.Parameters.Int("count") ?? 50, 0, 500);
        var scope = await library.ScopeAsync(null);
        var mix = await scope.Tracks.AnyAsync(t => t.Id == id, call.Cancellation)
            ? $"track:{id:N}"
            : $"artist:{await ArtistOfAsync(library, id):N}";

        var tracks = (await call.Service<DiscoveryService>().MixAsync(mix, call.Cancellation)).Tracks.Where(t => t.Id != id).Take(count).ToList();
        return call.Ok(new SubsonicNode(element).List("song", await library.SongsAsync(tracks)));
    }

    /// <summary>The artist's most played tracks, by the user and then by everyone.</summary>
    private static async Task<IResult> GetTopSongsAsync(SubsonicCall call)
    {
        var library = new SubsonicLibrary(call);
        var count = Math.Clamp(call.Parameters.Int("count") ?? 50, 0, 500);
        var scope = await library.ScopeAsync(null);
        Guid? artistId;
        if (call.Parameters["id"] is { Length: > 0 } id)
            artistId = SubsonicParameters.Id(id);
        else
        {
            var name = TextNormalizer.Normalize(call.Parameters.Required("artist"));
            artistId = await scope.Artists.Where(a => a.NormalizedName == name).OrderBy(a => a.Id).Select(a => (Guid?)a.Id).FirstOrDefaultAsync(call.Cancellation);
        }

        if (artistId is null || !await scope.Artists.AnyAsync(a => a.Id == artistId, call.Cancellation))
            return call.Ok(new SubsonicNode("topSongs").List("song", []));

        var overview = await call.Service<DiscoveryService>().ArtistOverviewAsync(artistId.Value, call.Cancellation);
        return call.Ok(new SubsonicNode("topSongs").List("song", await library.SongsAsync(overview.TopTracks.Take(count).ToList())));
    }

    /// <summary>The artist itself, or the album artist of an album or the main artist of a song.</summary>
    private static async Task<Guid> ArtistOfAsync(SubsonicLibrary library, Guid id)
    {
        var scope = await library.ScopeAsync(null);
        if (await scope.Artists.AnyAsync(a => a.Id == id))
            return id;
        if (await scope.Albums.Where(a => a.Id == id).Select(a => (Guid?)a.AlbumArtistId).SingleOrDefaultAsync() is { } albumArtist)
            return albumArtist;
        return await scope.Tracks.Where(t => t.Id == id)
                   .Select(t => (Guid?)(t.Artists.Where(a => a.Role == ArtistRole.Primary).OrderBy(a => a.Position).Select(a => (Guid?)a.ArtistId).FirstOrDefault()
                                        ?? t.Album.AlbumArtistId))
                   .SingleOrDefaultAsync()
               ?? throw SubsonicException.NotFound();
    }
}
