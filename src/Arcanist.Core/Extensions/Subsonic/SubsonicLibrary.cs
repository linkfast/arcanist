using Arcanist.Core.Application.Catalog;
using Arcanist.Core.Application.Libraries;
using Arcanist.Core.Auth;
using Arcanist.Core.Data;
using Arcanist.Core.Data.Entities;
using Arcanist.Core.Scanning;
using Microsoft.EntityFrameworkCore;
using Album = Arcanist.Core.Application.Catalog.Album;
using AlbumEntity = Arcanist.Core.Data.Entities.Album;
using Artist = Arcanist.Core.Application.Catalog.Artist;
using ArtistEntity = Arcanist.Core.Data.Entities.Artist;
using Track = Arcanist.Core.Application.Catalog.Track;
using TrackEntity = Arcanist.Core.Data.Entities.Track;

namespace Arcanist.Core.Extensions.Subsonic;

/// <summary>What the user can see, optionally within one music folder (a library).</summary>
internal sealed record SubsonicScope(
    IQueryable<TrackEntity> Tracks,
    IQueryable<AlbumEntity> Albums,
    IQueryable<ArtistEntity> AlbumArtists,
    IQueryable<ArtistEntity> Artists);

/// <summary>
/// Reads the catalog for Subsonic calls and turns it into Subsonic elements (<c>Child</c>, <c>AlbumID3</c>,
/// <c>ArtistID3</c>). Visibility comes from <see cref="CatalogService"/>, so Subsonic apps see what Arcanist shows.
/// </summary>
internal sealed class SubsonicLibrary(SubsonicCall call)
{
    /// <summary>Leading words left out when indexing and sorting artists.</summary>
    public const string IgnoredArticles = "The El La Los Las Le Les";

    private static readonly string[] Articles = IgnoredArticles.Split(' ');

    public ArcanistDbContext Db { get; } = call.Service<ArcanistDbContext>();

    public CatalogService Catalog { get; } = call.Service<CatalogService>();

    public UserAccess Access => call.Access;

    private CancellationToken Cancellation => call.Cancellation;

    /// <summary>The user's catalog, or the part of it in the library numbered <paramref name="musicFolderId"/>.</summary>
    public async Task<SubsonicScope> ScopeAsync(int? musicFolderId)
    {
        var files = Catalog.VisibleFiles(Access);
        if (musicFolderId is { } number)
        {
            var library = await LibraryService.Visible(Db, Access).Where(l => l.Number == number).Select(l => (Guid?)l.Id)
                              .SingleOrDefaultAsync(Cancellation)
                          ?? throw SubsonicException.NotFound();
            files = files.Where(f => f.LibrarySource.LibraryId == library);
        }

        var tracks = Db.Tracks.Where(t => files.Any(f => f.TrackId == t.Id));
        var albums = Db.Albums.Where(a => tracks.Any(t => t.AlbumId == a.Id));
        return new SubsonicScope(
            tracks,
            albums,
            Db.Artists.Where(a => albums.Any(al => al.AlbumArtistId == a.Id)),
            Db.Artists.Where(a => albums.Any(al => al.AlbumArtistId == a.Id)
                                  || Db.TrackArtists.Any(ta => ta.ArtistId == a.Id
                                                               && (ta.Role == ArtistRole.Primary || ta.Role == ArtistRole.Featured)
                                                               && tracks.Any(t => t.Id == ta.TrackId))));
    }

    public Task<SubsonicScope> ScopeAsync() => ScopeAsync(call.Parameters.Int("musicFolderId"));

    public async Task<IReadOnlyList<Track>> TracksAsync(IQueryable<TrackEntity> query) =>
        await Catalog.ToModelsAsync(await query.AsNoTracking().ToListAsync(Cancellation), Cancellation);

    public async Task<IReadOnlyList<Album>> AlbumsAsync(IQueryable<AlbumEntity> query) =>
        await Catalog.ToModelsAsync(await query.AsNoTracking().ToListAsync(Cancellation), Access, Cancellation);

    /// <summary>Visible tracks with these ids, in the order given; unknown or hidden ones are left out.</summary>
    public async Task<IReadOnlyList<Track>> TracksAsync(IReadOnlyList<Guid> ids)
    {
        var tracks = await Catalog.VisibleTracksAsync(Access, ids.Distinct().ToList(), Cancellation);
        return ids.Where(tracks.ContainsKey).Select(id => tracks[id]).ToList();
    }

    /// <summary>The sort key of an artist: its sort name, or its name without a leading article.</summary>
    public static string SortKey(Artist artist)
    {
        if (!string.IsNullOrWhiteSpace(artist.SortName))
            return TextNormalizer.Normalize(artist.SortName);
        var name = artist.Name.Trim();
        foreach (var article in Articles)
        {
            if (name.Length > article.Length + 1 && name.StartsWith(article + " ", StringComparison.OrdinalIgnoreCase))
                return TextNormalizer.Normalize(name[(article.Length + 1)..]);
        }

        return TextNormalizer.Normalize(name);
    }

    /// <summary>The index letter of an artist: its first letter without accents, or <c>#</c>.</summary>
    public static string IndexOf(Artist artist)
    {
        var key = SortKey(artist);
        return key.Length > 0 && char.IsAsciiLetter(key[0]) ? char.ToUpperInvariant(key[0]).ToString() : "#";
    }

    /// <summary>Songs (<c>Child</c> elements) for tracks the user can see.</summary>
    public async Task<List<SubsonicNode>> SongsAsync(IReadOnlyList<Track> tracks, string element = "song")
    {
        if (tracks.Count == 0)
            return [];

        var albumIds = tracks.Select(t => t.Album.Id).Distinct().ToList();
        var albums = await Db.Albums.AsNoTracking().Where(a => albumIds.Contains(a.Id))
            .Select(a => new { a.Id, a.Year, a.AlbumArtistId, ArtistName = a.AlbumArtist.Name })
            .ToDictionaryAsync(a => a.Id, Cancellation);

        var fileIds = tracks.Where(t => t.Files.Count > 0).Select(t => t.Files[0].Id).ToList();
        var files = await Db.MediaFiles.AsNoTracking().Where(f => fileIds.Contains(f.Id))
            .Select(f => new { f.Id, f.RelativePath, f.StartMs })
            .ToDictionaryAsync(f => f.Id, Cancellation);

        var starredIds = tracks.Where(t => t.Starred).Select(t => t.Id).ToList();
        var starred = starredIds.Count == 0
            ? []
            : await Db.TrackRatings.AsNoTracking().Where(r => r.UserId == Access.UserId && starredIds.Contains(r.TrackId))
                .ToDictionaryAsync(r => r.TrackId, r => r.RatedAt, Cancellation);

        return tracks.Select(t =>
        {
            var album = albums[t.Album.Id];
            var file = t.Files.Count > 0 ? t.Files[0] : null;
            var stored = file is null ? null : files.GetValueOrDefault(file.Id);
            var suffix = stored is null ? null : Path.GetExtension(stored.RelativePath).TrimStart('.').ToLowerInvariant();
            var primary = t.Artists.Where(a => a.Role == ArtistRole.Primary).ToList();
            var artist = primary.Count > 0 ? string.Join(", ", primary.Select(a => a.Name)) : album.ArtistName;
            // A cue track is part of a file: it is always sent converted.
            var cueLossless = stored?.StartMs is not null && AudioFormats.IsLossless(stored.RelativePath);
            (string Suffix, string ContentType)? converted = stored?.StartMs is null ? null : cueLossless ? ("flac", "audio/flac") : ("mp3", "audio/mpeg");

            return new SubsonicNode(element)
                .Attr("id", t.Id)
                .Attr("parent", t.Album.Id)
                .Attr("isDir", false)
                .Attr("title", t.Title)
                .Attr("album", t.Album.Title)
                .Attr("artist", artist)
                .Attr("track", t.TrackNumber)
                .Attr("discNumber", t.DiscNumber)
                .Attr("year", album.Year)
                .Attr("genre", t.Genres.FirstOrDefault())
                .Attr("coverArt", t.ArtworkId)
                .Attr("size", file?.Size)
                .Attr("contentType", stored is null ? null : AudioFormats.MimeType(stored.RelativePath))
                .Attr("suffix", suffix)
                .Attr("transcodedSuffix", converted?.Suffix)
                .Attr("transcodedContentType", converted?.ContentType)
                .Attr("duration", Seconds(t.DurationMs))
                .Attr("bitRate", file?.BitRate)
                .Attr("path", SongPath(album.ArtistName, t, suffix))
                .Attr("isVideo", false)
                .Attr("userRating", t.Rating)
                .Attr("playCount", t.PlayCount)
                .Attr("created", t.AddedAt)
                .Attr("starred", starred.TryGetValue(t.Id, out var at) ? at : null)
                .Attr("albumId", t.Album.Id)
                .Attr("artistId", primary.Count > 0 ? primary[0].ArtistId : album.AlbumArtistId)
                .Attr("type", "music")
                .Attr("mediaType", "song")
                .Attr("played", t.LastPlayedAt)
                .Attr("bpm", t.Bpm is { } bpm ? (int)Math.Round(bpm) : 0)
                .Attr("samplingRate", file?.SampleRate)
                .Attr("bitDepth", file?.BitDepth)
                .Attr("channelCount", file?.Channels)
                .Attr("displayArtist", artist)
                .Attr("displayAlbumArtist", album.ArtistName)
                .List("genres", t.Genres.Select(g => new SubsonicNode("genres").Attr("name", g)))
                .List("artists", primary.Select(a => Reference("artists", a.ArtistId, a.Name)))
                .List("albumArtists", [Reference("albumArtists", album.AlbumArtistId, album.ArtistName)])
                .Child(ReplayGain(file));
        }).ToList();
    }

    /// <summary>Albums as <c>AlbumID3</c> elements, or as directory <c>Child</c> elements for folder-based calls.</summary>
    public async Task<List<SubsonicNode>> AlbumNodesAsync(IReadOnlyList<Album> albums, string element = "album", bool asDirectory = false)
    {
        if (albums.Count == 0)
            return [];

        var ids = albums.Select(a => a.Id).ToList();
        var created = await Db.Tracks.AsNoTracking().Where(t => ids.Contains(t.AlbumId))
            .GroupBy(t => t.AlbumId)
            .Select(g => new { AlbumId = g.Key, Created = g.Min(t => t.AddedAt) })
            .ToDictionaryAsync(a => a.AlbumId, a => a.Created, Cancellation);
        var plays = await Db.TrackPlayStats.AsNoTracking().Where(p => p.UserId == Access.UserId && ids.Contains(p.Track.AlbumId))
            .GroupBy(p => p.Track.AlbumId)
            .Select(g => new { AlbumId = g.Key, Count = g.Sum(p => p.PlayCount), Last = g.Max(p => p.LastPlayedAt) })
            .ToDictionaryAsync(p => p.AlbumId, Cancellation);
        var genres = (await Db.TrackGenres.AsNoTracking().Where(tg => ids.Contains(tg.Track.AlbumId))
                .Select(tg => new { tg.Track.AlbumId, tg.Genre.Name })
                .Distinct()
                .ToListAsync(Cancellation))
            .ToLookup(g => g.AlbumId, g => g.Name);

        return albums.Select(a =>
        {
            var albumGenres = genres[a.Id].Order().ToList();
            var play = plays.GetValueOrDefault(a.Id);
            var node = new SubsonicNode(element).Attr("id", a.Id);
            if (asDirectory)
            {
                node.Attr("parent", a.Artist.Id)
                    .Attr("isDir", true)
                    .Attr("title", a.Title)
                    .Attr("album", a.Title);
            }
            else
                node.Attr("name", a.Title);

            node.Attr("artist", a.Artist.Name)
                .Attr("artistId", a.Artist.Id)
                .Attr("coverArt", a.ArtworkId)
                .Attr("songCount", a.TrackCount)
                .Attr("duration", Seconds(a.DurationMs))
                .Attr("playCount", play?.Count ?? 0)
                .Attr("created", created.TryGetValue(a.Id, out var at) ? at : DateTime.UnixEpoch)
                .Attr("year", a.Year)
                .Attr("genre", albumGenres.FirstOrDefault());
            if (asDirectory)
                return node;

            return node
                .Attr("played", play?.Last)
                .Attr("isCompilation", a.IsCompilation)
                .Attr("displayArtist", a.Artist.Name)
                .Attr("mediaType", "album")
                .Attr("musicBrainzId", a.MusicBrainzId?.ToString() ?? "")
                .List("genres", albumGenres.Select(g => new SubsonicNode("genres").Attr("name", g)))
                .List("artists", [Reference("artists", a.Artist.Id, a.Artist.Name)]);
        }).ToList();
    }

    /// <summary>Artists as <c>ArtistID3</c> elements, with how many albums of theirs the scope has.</summary>
    public async Task<List<SubsonicNode>> ArtistNodesAsync(IReadOnlyList<Artist> artists, SubsonicScope scope, string element = "artist")
    {
        if (artists.Count == 0)
            return [];

        var ids = artists.Select(a => a.Id).ToList();
        var albumCounts = await scope.Albums.Where(a => ids.Contains(a.AlbumArtistId))
            .GroupBy(a => a.AlbumArtistId)
            .Select(g => new { ArtistId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(a => a.ArtistId, a => a.Count, Cancellation);

        return artists.Select(a => new SubsonicNode(element)
            .Attr("id", a.Id)
            .Attr("name", a.Name)
            // Resolved by getCoverArt to one of the artist's album covers.
            .Attr("coverArt", a.Id)
            .Attr("albumCount", albumCounts.GetValueOrDefault(a.Id))
            .Attr("sortName", a.SortName ?? "")
            .Attr("musicBrainzId", a.MusicBrainzId?.ToString() ?? "")).ToList();
    }

    public static int Seconds(long milliseconds) => (int)((milliseconds + 500) / 1000);

    private static SubsonicNode Reference(string element, Guid id, string name) => new SubsonicNode(element).Attr("id", id).Attr("name", name);

    /// <summary>Gains measured by Arcanist (ReplayGain 2.0, −18 LUFS), or those in the file's tags.</summary>
    private static SubsonicNode ReplayGain(TrackFile? file)
    {
        var node = new SubsonicNode("replayGain");
        if (file?.Loudness is { } loudness)
        {
            node.Attr("trackGain", loudness.TrackGain)
                .Attr("albumGain", loudness.AlbumGain)
                .Attr("trackPeak", loudness.TruePeak)
                .Attr("albumPeak", loudness.AlbumPeak);
        }
        else if (file is not null)
            node.Attr("trackGain", file.ReplayGainTrackGain).Attr("albumGain", file.ReplayGainAlbumGain);

        return node;
    }

    /// <summary>A made-up path (artist/album/track) for apps that name offline copies after it; real paths stay private.</summary>
    private static string SongPath(string albumArtist, Track track, string? suffix)
    {
        var number = track.TrackNumber is { } n ? $"{(track.DiscNumber is > 1 ? $"{track.DiscNumber}-" : "")}{n:00} - " : "";
        return $"{Safe(albumArtist)}/{Safe(track.Album.Title)}/{number}{Safe(track.Title)}{(suffix is null ? "" : "." + suffix)}";

        static string Safe(string part) => string.Concat(part.Select(c => c is '/' or '\\' || char.IsControl(c) ? '_' : c)).Trim();
    }
}
