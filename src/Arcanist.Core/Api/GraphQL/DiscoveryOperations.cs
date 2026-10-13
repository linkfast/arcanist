using System.ComponentModel;
using Arcanist.Core.Application.Discovery;
using GraphQL;

namespace Arcanist.Core.Api.GraphQL;

public sealed partial class Query
{
    [Name("home")]
    [Description("The user's home page: recently played and added albums, mixes, most played, starred tracks, albums to rediscover or never played, and playlists. Empty sections are left out.")]
    public static Task<Home> Home([FromServices] DiscoveryService discovery, CancellationToken cancellationToken) =>
        discovery.HomeAsync(cancellationToken);

    [Name("mixes")]
    [Description("Mixes made for the user from their favorite artists, genres and decades, their favorites and music they never played. They change once a day.")]
    public static Task<IReadOnlyList<Mix>> Mixes([FromServices] DiscoveryService discovery, CancellationToken cancellationToken) =>
        discovery.MixesAsync(cancellationToken);

    [Name("mix")]
    [Description("A mix's tracks in play order. Besides the ids from mixes, any artist:<id>, genre:<id>, decade:<year> or track:<id> (a radio started from a track) works, ids without dashes.")]
    public static Task<MixTracks> Mix(string id, [FromServices] DiscoveryService discovery, CancellationToken cancellationToken) =>
        discovery.MixAsync(id, cancellationToken);

    [Name("artistOverview")]
    [Description("Everything an artist page shows: top tracks, albums, albums the artist appears on, related artists and the artist's mix.")]
    public static Task<ArtistOverview> ArtistOverview([Id] Guid id, [FromServices] DiscoveryService discovery, CancellationToken cancellationToken) =>
        discovery.ArtistOverviewAsync(id, cancellationToken);
}
