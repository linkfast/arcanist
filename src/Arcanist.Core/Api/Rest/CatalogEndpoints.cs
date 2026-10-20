using Arcanist.Core.Application;
using Arcanist.Core.Application.Catalog;

namespace Arcanist.Core.Api.Rest;

public static class CatalogEndpoints
{
    public static IEndpointRouteBuilder MapCatalogEndpoints(this IEndpointRouteBuilder rest)
    {
        var catalog = rest.MapGroup("").WithTags("Catalog");

        catalog.MapGet("/artists", (string? search, string? after, int? first, Guid? libraryId, CatalogService service, CancellationToken ct) =>
                service.ArtistsAsync(search, after, first, ct, libraryId))
            .WithName("Artists")
            .WithSummary("Performers and album artists with music the user can play, by name; optionally only those with music in a library. Pages of `first` items (default 50, at most 500) after the `after` cursor.");

        catalog.MapGet("/artists/{id:guid}", (Guid id, CatalogService service, CancellationToken ct) => service.ArtistAsync(id, ct))
            .WithName("Artist");

        catalog.MapGet("/albums", (string? search, Guid? artistId, string? after, int? first, Guid? libraryId, string? sort, int? seed, CatalogService service,
                CancellationToken ct) =>
                service.AlbumsAsync(search, artistId, after, first, ct, libraryId,
                    sort is null ? AlbumSort.Title
                    : Enum.TryParse<AlbumSort>(sort.Replace("_", ""), ignoreCase: true, out var parsed) ? parsed
                    : throw ArcanistException.Field("sort", ErrorCodes.InvalidFormat), seed))
            .WithName("Albums")
            .WithSummary("Albums with music the user can play, by title (or `sort`: `RECENTLY_ADDED` first, or `RANDOM`, the same order all day for the user and `seed`); optionally only those of an artist, or with music in a library.");

        catalog.MapGet("/albums/{id:guid}", (Guid id, CatalogService service, CancellationToken ct) => service.AlbumAsync(id, ct))
            .WithName("Album");

        catalog.MapGet("/tracks", (string? search, Guid? albumId, Guid? artistId, string? after, int? first, Guid? libraryId, CatalogService service, CancellationToken ct) =>
                service.TracksAsync(search, albumId, artistId, after, first, ct, libraryId))
            .WithName("Tracks")
            .WithSummary("Tracks the user can play, by title; optionally only those with a copy in a library. With albumId, all of the album's tracks in disc and track order, without paging.");

        catalog.MapPost("/tracks/match", (MatchTracksInput input, TrackMatchService service, CancellationToken ct) => service.MatchAsync(input, ct))
            .WithName("MatchTracks")
            .WithSummary("Finds the server's tracks for an app's own (at most 1000 per call): by audio hash, MusicBrainz recording, AcoustID, and last by title, artist and album.");

        catalog.MapGet("/tracks/{id:guid}", (Guid id, CatalogService service, CancellationToken ct) => service.TrackAsync(id, ct))
            .WithName("Track");

        catalog.MapGet("/tracks/{trackId:guid}/lyrics", (Guid trackId, CatalogService service, CancellationToken ct) =>
                service.LyricsAsync(trackId, ct))
            .WithName("TrackLyrics")
            .WithSummary("A track's lyrics, with synchronized lines when known. not_found when it has none.");

        catalog.MapGet("/tracks/{trackId:guid}/waveform", (Guid trackId, Guid? fileId, CatalogService service, CancellationToken ct) =>
                service.WaveformAsync(trackId, fileId, ct))
            .WithName("TrackWaveform")
            .WithSummary("Overview of a track's audio for seek bars, from its best copy or the given file. not_found until the copy is analyzed.");

        return rest;
    }
}
