using System.ComponentModel;
using Arcanist.Core.Application.Catalog;
using GraphQL;

namespace Arcanist.Core.Api.GraphQL;

public sealed partial class Query
{
    [Name("artists")]
    [Description("Performers and album artists with music the user can play, by name. Pages of `first` items (default 50, at most 500) after the `after` cursor.")]
    public static Task<ArtistPage> Artists(string? search, string? after, int? first, [FromServices] CatalogService catalog, CancellationToken cancellationToken) =>
        catalog.ArtistsAsync(search, after, first, cancellationToken);

    [Name("artist")]
    public static Task<Artist> Artist([Id] Guid id, [FromServices] CatalogService catalog, CancellationToken cancellationToken) =>
        catalog.ArtistAsync(id, cancellationToken);

    [Name("albums")]
    [Description("Albums with music the user can play, by title; optionally only those of an artist.")]
    public static Task<AlbumPage> Albums(string? search, [Id] Guid? artistId, string? after, int? first, [FromServices] CatalogService catalog, CancellationToken cancellationToken) =>
        catalog.AlbumsAsync(search, artistId, after, first, cancellationToken);

    [Name("album")]
    public static Task<Album> Album([Id] Guid id, [FromServices] CatalogService catalog, CancellationToken cancellationToken) =>
        catalog.AlbumAsync(id, cancellationToken);

    [Name("tracks")]
    [Description("Tracks the user can play, by title. With albumId, all of the album's tracks in disc and track order, without paging.")]
    public static Task<TrackPage> Tracks(string? search, [Id] Guid? albumId, [Id] Guid? artistId, string? after, int? first,
        [FromServices] CatalogService catalog, CancellationToken cancellationToken) =>
        catalog.TracksAsync(search, albumId, artistId, after, first, cancellationToken);

    [Name("track")]
    public static Task<Track> Track([Id] Guid id, [FromServices] CatalogService catalog, CancellationToken cancellationToken) =>
        catalog.TrackAsync(id, cancellationToken);

    [Name("trackLyrics")]
    [Description("A track's lyrics, with synchronized lines when known. not_found when it has none.")]
    public static Task<TrackLyrics> TrackLyrics([Id] Guid trackId, [FromServices] CatalogService catalog, CancellationToken cancellationToken) =>
        catalog.LyricsAsync(trackId, cancellationToken);

    [Name("trackWaveform")]
    [Description("Overview of a track's audio for seek bars, from its best copy or the given file. not_found until the copy is analyzed.")]
    public static Task<Waveform> TrackWaveform([Id] Guid trackId, [Id] Guid? fileId, [FromServices] CatalogService catalog, CancellationToken cancellationToken) =>
        catalog.WaveformAsync(trackId, fileId, cancellationToken);
}
