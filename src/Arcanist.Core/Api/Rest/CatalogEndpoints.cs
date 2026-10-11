using Arcanist.Core.Application.Catalog;

namespace Arcanist.Core.Api.Rest;

public static class CatalogEndpoints
{
    public static IEndpointRouteBuilder MapCatalogEndpoints(this IEndpointRouteBuilder rest)
    {
        var catalog = rest.MapGroup("").WithTags("Catalog");

        catalog.MapGet("/artists", (string? search, string? after, int? first, CatalogService service, CancellationToken ct) =>
                service.ArtistsAsync(search, after, first, ct))
            .WithName("Artists")
            .WithSummary("Performers and album artists with music the user can play, by name. Pages of `first` items (default 50, at most 500) after the `after` cursor.");

        catalog.MapGet("/artists/{id:guid}", (Guid id, CatalogService service, CancellationToken ct) => service.ArtistAsync(id, ct))
            .WithName("Artist");

        catalog.MapGet("/albums", (string? search, Guid? artistId, string? after, int? first, CatalogService service, CancellationToken ct) =>
                service.AlbumsAsync(search, artistId, after, first, ct))
            .WithName("Albums")
            .WithSummary("Albums with music the user can play, by title; optionally only those of an artist.");

        catalog.MapGet("/albums/{id:guid}", (Guid id, CatalogService service, CancellationToken ct) => service.AlbumAsync(id, ct))
            .WithName("Album");

        catalog.MapGet("/tracks", (string? search, Guid? albumId, Guid? artistId, string? after, int? first, CatalogService service, CancellationToken ct) =>
                service.TracksAsync(search, albumId, artistId, after, first, ct))
            .WithName("Tracks")
            .WithSummary("Tracks the user can play, by title. With albumId, all of the album's tracks in disc and track order, without paging.");

        catalog.MapGet("/tracks/{id:guid}", (Guid id, CatalogService service, CancellationToken ct) => service.TrackAsync(id, ct))
            .WithName("Track");

        return rest;
    }
}
