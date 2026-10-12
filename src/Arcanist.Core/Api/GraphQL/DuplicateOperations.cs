using System.ComponentModel;
using Arcanist.Core.Application.Catalog;
using DuplicateStatus = Arcanist.Core.Data.Entities.DuplicateStatus;
using GraphQL;

namespace Arcanist.Core.Api.GraphQL;

public sealed partial class Query
{
    [Name("duplicateCandidates")]
    [Description("Pairs of tracks whose audio is similar but not enough to merge them automatically, most similar first. Open ones by default, or DISMISSED. Requires MANAGE_LIBRARIES.")]
    public static Task<IReadOnlyList<DuplicatePair>> DuplicateCandidates(DuplicateStatus? status, int? first,
        [FromServices] DuplicateService duplicates, CancellationToken cancellationToken) =>
        duplicates.ListAsync(status, first, cancellationToken);
}

public sealed partial class Mutation
{
    [Name("mergeDuplicate")]
    [Description("Merges an open pair into keepTrackId, one of its two tracks, and returns it. Requires MANAGE_LIBRARIES.")]
    public static Task<Track> MergeDuplicate([Id] Guid id, [Id] Guid keepTrackId, [FromServices] DuplicateService duplicates, CancellationToken cancellationToken) =>
        duplicates.MergeAsync(id, keepTrackId, cancellationToken);

    [Name("dismissDuplicate")]
    [Description("Keeps the two tracks of an open pair apart; it is not proposed again. Requires MANAGE_LIBRARIES.")]
    public static async Task<bool> DismissDuplicate([Id] Guid id, [FromServices] DuplicateService duplicates, CancellationToken cancellationToken)
    {
        await duplicates.DismissAsync(id, cancellationToken);
        return true;
    }
}
