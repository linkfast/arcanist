using Arcanist.Core.Application.Search;

namespace Arcanist.Core.Api.Rest;

public static class SearchEndpoints
{
    public static IEndpointRouteBuilder MapSearchEndpoints(this IEndpointRouteBuilder rest)
    {
        rest.MapGet("/search", (string query, int? first, SearchService service, CancellationToken ct) => service.SearchAsync(query, first, ct))
            .WithTags("Catalog")
            .WithName("Search")
            .WithSummary("Artists, albums, tracks and playlists matching every word of the query (as word starts, ignoring accents and case), best first. first is per kind: 10 by default, at most 50.");

        return rest;
    }
}
