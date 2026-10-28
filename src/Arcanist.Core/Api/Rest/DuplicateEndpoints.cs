using Arcanist.Core.Application;
using Arcanist.Core.Application.Catalog;
using Arcanist.Core.Data.Entities;

namespace Arcanist.Core.Api.Rest;

public sealed record MergeDuplicateInput(Guid KeepTrackId);

public static class DuplicateEndpoints
{
    public static IEndpointRouteBuilder MapDuplicateEndpoints(this IEndpointRouteBuilder rest)
    {
        var duplicates = rest.MapGroup("/duplicates").WithTags("Duplicates");

        duplicates.MapGet("", (string? status, int? first, DuplicateService service, CancellationToken ct) =>
                service.ListAsync(ParseStatus(status), first, ct))
            .WithName("DuplicateCandidates")
            .WithSummary("Pairs of tracks whose audio is similar but not enough to merge them automatically, most similar first. " +
                         "`status` is OPEN (default) or DISMISSED. Requires MANAGE_LIBRARIES.");

        duplicates.MapPost("/{id:guid}/merge", (Guid id, MergeDuplicateInput input, DuplicateService service, CancellationToken ct) =>
                service.MergeAsync(id, input.KeepTrackId, ct))
            .WithName("MergeDuplicate")
            .WithSummary("Merges an open pair into keepTrackId, one of its two tracks, and returns it. Requires MANAGE_LIBRARIES.");

        duplicates.MapPost("/{id:guid}/dismiss", async (Guid id, DuplicateService service, CancellationToken ct) =>
            {
                await service.DismissAsync(id, ct);
                return Results.NoContent();
            })
            .WithName("DismissDuplicate")
            .WithSummary("Keeps the two tracks of an open pair apart; it is not proposed again. Requires MANAGE_LIBRARIES.");

        duplicates.MapGet("/libraries", (Guid? libraryId, string? after, int? first, LibraryDuplicateService service, CancellationToken ct) =>
                service.ListAsync(new LibraryDuplicatesInput(libraryId, after, first), ct))
            .WithName("LibraryDuplicates")
            .WithSummary("Songs with copies in more than one library (personal libraries aside), by track id with a cursor; with `libraryId`, " +
                         "those that library has among others. Each library says whether the caller can delete its copies " +
                         "(DELETE /rest/v1/tracks/{id}?libraryId=) and the users who would stop having the song if it did. Requires MANAGE_LIBRARIES.");

        return rest;
    }

    // Query binding of enums is case-sensitive; the API spells them in upper case.
    private static DuplicateStatus? ParseStatus(string? status) =>
        string.IsNullOrEmpty(status) ? null
        : Enum.TryParse<DuplicateStatus>(status, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed) ? parsed
        : throw ArcanistException.Field("status", ErrorCodes.InvalidFormat);
}
