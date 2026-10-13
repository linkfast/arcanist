using Arcanist.Core.Application.Discovery;

namespace Arcanist.Core.Api.Rest;

public static class DiscoveryEndpoints
{
    public static IEndpointRouteBuilder MapDiscoveryEndpoints(this IEndpointRouteBuilder rest)
    {
        rest.MapGet("/home", (DiscoveryService service, CancellationToken ct) => service.HomeAsync(ct))
            .WithTags("Discovery")
            .WithName("Home")
            .WithSummary("The user's home page: recently played and added albums, mixes, most played, starred tracks, albums to rediscover or never played, and playlists. Empty sections are left out.");

        rest.MapGet("/mixes", (DiscoveryService service, CancellationToken ct) => service.MixesAsync(ct))
            .WithTags("Discovery")
            .WithName("Mixes")
            .WithSummary("Mixes made for the user from their favorite artists, genres and decades, their favorites and music they never played. They change once a day.");

        rest.MapGet("/mixes/{id}", (string id, DiscoveryService service, CancellationToken ct) => service.MixAsync(id, ct))
            .WithTags("Discovery")
            .WithName("Mix")
            .WithSummary("A mix's tracks in play order. Besides the ids from mixes, any artist:<id>, genre:<id>, decade:<year> or track:<id> (a radio started from a track) works, ids without dashes.");

        rest.MapGet("/artists/{id:guid}/overview", (Guid id, DiscoveryService service, CancellationToken ct) => service.ArtistOverviewAsync(id, ct))
            .WithTags("Discovery")
            .WithName("ArtistOverview")
            .WithSummary("Everything an artist page shows: top tracks, albums, albums the artist appears on, related artists and the artist's mix.");

        return rest;
    }
}
