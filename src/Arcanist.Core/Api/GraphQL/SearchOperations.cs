using System.ComponentModel;
using Arcanist.Core.Application.Search;
using GraphQL;

namespace Arcanist.Core.Api.GraphQL;

public sealed partial class Query
{
    [Name("search")]
    [Description("Artists, albums, tracks and playlists matching every word of the query (as word starts, ignoring accents and case), best first. first is per kind: 10 by default, at most 50.")]
    public static Task<SearchResults> Search(string query, int? first, [FromServices] SearchService search, CancellationToken cancellationToken) =>
        search.SearchAsync(query, first, cancellationToken);
}
