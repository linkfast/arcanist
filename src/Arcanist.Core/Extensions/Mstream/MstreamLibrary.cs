using System.Globalization;
using System.Text.Json.Nodes;
using Arcanist.Core.Application.Catalog;
using Arcanist.Core.Application.Libraries;
using Arcanist.Core.Auth;
using Arcanist.Core.Data;
using Arcanist.Core.Data.Entities;
using Microsoft.EntityFrameworkCore;
using AlbumEntity = Arcanist.Core.Data.Entities.Album;
using Track = Arcanist.Core.Application.Catalog.Track;
using TrackEntity = Arcanist.Core.Data.Entities.Track;

namespace Arcanist.Core.Extensions.Mstream;

/// <summary>A library folder as mStream names it: the first segment of every file path.</summary>
internal sealed record Vpath(string Name, Guid LibraryId, Guid SourceId);

/// <summary>
/// The user's catalog seen the mStream way: files addressed as <c>&lt;vpath&gt;/&lt;path&gt;</c>, and tracks as
/// <c>{filepath, metadata}</c>. Each folder of a library is a vpath named after the library (with <c>-2</c>, <c>-3</c>…
/// for its other folders). A track of a cue sheet is addressed as <c>&lt;file&gt;@&lt;n&gt;</c>.
/// </summary>
internal sealed class MstreamLibrary(MstreamCall call)
{
    private IReadOnlyList<Vpath>? _vpaths;

    public ArcanistDbContext Db { get; } = call.Service<ArcanistDbContext>();

    public CatalogService Catalog { get; } = call.Service<CatalogService>();

    private UserAccess Access => call.Access;

    public CancellationToken Cancellation => call.Cancellation;

    public async Task<IReadOnlyList<Vpath>> VpathsAsync()
    {
        if (_vpaths is not null)
            return _vpaths;

        var libraries = await LibraryService.Visible(Db, Access).AsNoTracking().Include(l => l.Sources).OrderBy(l => l.Number).ToListAsync(Cancellation);
        var vpaths = new List<Vpath>();
        foreach (var library in libraries)
        {
            var name = library.Name.Replace('/', '-').Replace('\\', '-').Trim();
            var sources = library.Sources.OrderBy(s => s.Id).ToList();
            vpaths.AddRange(sources.Select((source, i) => new Vpath(i == 0 ? name : $"{name}-{i + 1}", library.Id, source.Id)));
        }

        return _vpaths = vpaths;
    }

    /// <summary>The user's files, without those in the vpaths the request asks to leave out (<c>ignoreVPaths</c>).</summary>
    public async Task<IQueryable<MediaFile>> FilesAsync()
    {
        var files = Catalog.VisibleFiles(Access);
        var ignored = call.Body.Strings("ignoreVPaths");
        if (ignored.Count > 0)
        {
            var excluded = (await VpathsAsync()).Where(v => ignored.Contains(v.Name)).Select(v => v.SourceId).ToList();
            files = files.Where(f => !excluded.Contains(f.LibrarySourceId));
        }

        return files;
    }

    public async Task<IQueryable<TrackEntity>> TracksAsync()
    {
        var files = await FilesAsync();
        return Db.Tracks.Where(t => files.Any(f => f.TrackId == t.Id));
    }

    public async Task<IQueryable<AlbumEntity>> AlbumsAsync()
    {
        var tracks = await TracksAsync();
        return Db.Albums.Where(a => tracks.Any(t => t.AlbumId == a.Id));
    }

    /// <summary>The file a vpath path names, if the user can see it.</summary>
    public async Task<MediaFile?> ResolveAsync(string? filepath)
    {
        if (string.IsNullOrEmpty(filepath))
            return null;
        var path = filepath.TrimStart('/');
        var slash = path.IndexOf('/');
        if (slash <= 0)
            return null;

        var vpath = (await VpathsAsync()).FirstOrDefault(v => v.Name == path[..slash]);
        var relative = path[(slash + 1)..];
        if (vpath is null || relative.Split('/').Any(segment => segment is "." or ".."))
            return null;

        var files = Catalog.VisibleFiles(Access).AsNoTracking().Where(f => f.LibrarySourceId == vpath.SourceId);
        var file = await files.FirstOrDefaultAsync(f => f.RelativePath == relative && f.CueTrack == 0, Cancellation);
        var at = relative.LastIndexOf('@');
        if (file is null && at > 0 && int.TryParse(relative[(at + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out var cue) && cue > 0)
        {
            var whole = relative[..at];
            file = await files.FirstOrDefaultAsync(f => f.RelativePath == whole && f.CueTrack == cue, Cancellation);
        }

        return file;
    }

    public async Task<string?> PathOfAsync(Guid sourceId, string relativePath, int cueTrack)
    {
        var vpath = (await VpathsAsync()).FirstOrDefault(v => v.SourceId == sourceId);
        return vpath is null ? null : $"{vpath.Name}/{relativePath}{(cueTrack > 0 ? $"@{cueTrack}" : "")}";
    }

    /// <summary>Track objects for tracks the user can see, each with its best copy.</summary>
    public Task<List<JsonObject>> ObjectsAsync(IReadOnlyList<Track> tracks, bool lite = false) =>
        ObjectsAsync(tracks.Where(t => t.Files.Count > 0).Select(t => (t, t.Files[0].Id)).ToList(), lite);

    /// <summary>Track objects for given copies of tracks the user can see, in the same order.</summary>
    public async Task<List<JsonObject>> ObjectsAsync(IReadOnlyList<(Track Track, Guid FileId)> items, bool lite = false)
    {
        var objects = await ObjectsByFileAsync(items, lite);
        return items.Where(i => objects.ContainsKey(i.FileId)).Select(i => (JsonObject)objects[i.FileId].DeepClone()).ToList();
    }

    /// <summary>Track objects for given copies of tracks the user can see, by copy.</summary>
    public async Task<Dictionary<Guid, JsonObject>> ObjectsByFileAsync(IReadOnlyList<(Track Track, Guid FileId)> items, bool lite = false)
    {
        if (items.Count == 0)
            return [];

        var fileIds = items.Select(i => i.FileId).ToList();
        var files = await Db.MediaFiles.AsNoTracking().Where(f => fileIds.Contains(f.Id))
            .Select(f => new { f.Id, f.LibrarySourceId, f.RelativePath, f.CueTrack, f.ContentHash })
            .ToDictionaryAsync(f => f.Id, Cancellation);
        var albumIds = items.Select(i => i.Track.Album.Id).Distinct().ToList();
        var years = await Db.Albums.AsNoTracking().Where(a => albumIds.Contains(a.Id)).ToDictionaryAsync(a => a.Id, a => a.Year, Cancellation);

        var objects = new Dictionary<Guid, JsonObject>();
        foreach (var (track, fileId) in items)
        {
            if (objects.ContainsKey(fileId) || !files.TryGetValue(fileId, out var file)
                || await PathOfAsync(file.LibrarySourceId, file.RelativePath, file.CueTrack) is not { } path)
                continue;
            var copy = track.Files.FirstOrDefault(f => f.Id == fileId);
            objects[fileId] = new JsonObject
            {
                ["filepath"] = path,
                ["metadata"] = Metadata(track, copy, file.ContentHash, file.RelativePath, years.GetValueOrDefault(track.Album.Id), lite),
            };
        }

        return objects;
    }

    /// <summary>The track object of one copy, or <c>metadata: null</c> when the user cannot see it.</summary>
    public async Task<JsonObject> ObjectAsync(string filepath, MediaFile? file)
    {
        if (file is not null)
        {
            var tracks = await Catalog.VisibleTracksAsync(Access, [file.TrackId], Cancellation);
            if (tracks.TryGetValue(file.TrackId, out var track) && (await ObjectsAsync([(track, file.Id)])) is [var found])
            {
                found["filepath"] = filepath;
                return found;
            }
        }

        return new JsonObject { ["filepath"] = filepath, ["metadata"] = null };
    }

    /// <summary>
    /// mStream's metadata keys. Ratings go from 0 to 10 (twice Arcanist's stars); zero counts are null, as mStream
    /// sends them.
    /// </summary>
    private static JsonObject Metadata(Track track, TrackFile? file, string hash, string relativePath, int? year, bool lite)
    {
        var primary = track.Artists.Where(a => a.Role == ArtistRole.Primary).Select(a => a.Name).ToList();
        var artist = primary.Count > 0 ? string.Join(", ", primary) : null;
        var metadata = new JsonObject
        {
            ["artist"] = artist,
            ["album"] = track.Album.Title,
            ["title"] = track.Title,
            ["track"] = track.TrackNumber,
            ["disk"] = track.DiscNumber,
            ["year"] = year,
            ["album-art"] = ArtFile(track.ArtworkId),
            ["rating"] = track.Rating * 2,
            ["replaygain-track"] = file?.Loudness?.TrackGain ?? file?.ReplayGainTrackGain,
            ["artist-display"] = artist,
            ["artists"] = new JsonArray(primary.Select(name => (JsonNode?)name).ToArray()),
            ["duration"] = Math.Round(track.DurationMs / 1000.0, 3),
            ["genres"] = new JsonArray(track.Genres.Select(name => (JsonNode?)name).ToArray()),
            // mStream gives the tempo as a whole number, and its apps read it as one.
            ["bpm"] = track.Bpm is { } bpm ? (int)Math.Round(bpm) : null,
            ["musical-key"] = track.Key?.Notation,
            ["has-lyrics"] = track.HasLyrics,
        };
        if (lite)
            return metadata;

        metadata["hash"] = hash;
        metadata["play-count"] = track.PlayCount > 0 ? track.PlayCount : null;
        metadata["last-played"] = track.LastPlayedAt?.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
        metadata["bitrate"] = file?.BitRate * 1000;
        metadata["format"] = Path.GetExtension(relativePath).TrimStart('.').ToLowerInvariant();
        metadata["sample-rate"] = file?.SampleRate;
        metadata["channels"] = file?.Channels;
        metadata["bit-depth"] = file?.BitDepth;
        metadata["file-size"] = file?.Size;
        return metadata;
    }

    /// <summary>The album-art file name of an artwork: its id, served by <c>/album-art</c>.</summary>
    public static string? ArtFile(Guid? artworkId) => artworkId is { } id ? $"{id:N}.jpg" : null;

    /// <summary>Album cards: name, year and cover, plus the album artist.</summary>
    public async Task<JsonArray> AlbumCardsAsync(IQueryable<AlbumEntity> albums)
    {
        var cards = await albums.AsNoTracking()
            .Select(a => new { a.Title, a.Year, a.ArtworkId, Artist = a.AlbumArtist.Name, a.IsCompilation })
            .ToListAsync(Cancellation);
        return new JsonArray(cards.Select(a => (JsonNode?)new JsonObject
        {
            ["name"] = a.Title,
            ["year"] = a.Year,
            ["album_art_file"] = ArtFile(a.ArtworkId),
            ["album_artist"] = a.Artist,
            ["compilation"] = a.IsCompilation,
        }).ToArray());
    }

    /// <summary>Visible tracks with these ids, in the order given.</summary>
    public async Task<IReadOnlyList<Track>> TracksAsync(IReadOnlyList<Guid> ids)
    {
        var tracks = await Catalog.VisibleTracksAsync(Access, ids.Distinct().ToList(), Cancellation);
        return ids.Where(tracks.ContainsKey).Select(id => tracks[id]).ToList();
    }

    public async Task<IReadOnlyList<Track>> TracksAsync(IQueryable<TrackEntity> query) =>
        await Catalog.ToModelsAsync(await query.AsNoTracking().ToListAsync(Cancellation), Cancellation);
}
