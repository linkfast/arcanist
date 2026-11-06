using Arcanist.Core.Application;
using Arcanist.Core.Application.Catalog;
using Arcanist.Core.Data.Entities;

namespace Arcanist.Core.Api.Rest;

public sealed record MergeDuplicateInput(Guid KeepTrackId);

public sealed record ShareDuplicateFileInput(Guid FileId);

public static class DuplicateEndpoints
{
    public static IEndpointRouteBuilder MapDuplicateEndpoints(this IEndpointRouteBuilder rest)
    {
        var duplicates = rest.MapGroup("/duplicates").WithTags("Duplicates");

        duplicates.MapGet("", (string? status, int? first, int? skip, DuplicateService service, CancellationToken ct) =>
                service.ListAsync(ParseStatus(status), first, skip, ct))
            .WithName("DuplicateCandidates")
            .WithSummary("Pairs of tracks that may be the same song and were not merged automatically (also the same song on two albums), most similar first. " +
                         "`status` is OPEN (default), DISMISSED or SHARED. Requires MANAGE_LIBRARIES.");

        duplicates.MapPost("/{id:guid}/share", async (Guid id, ShareDuplicateFileInput input, DuplicateService service, CancellationToken ct) =>
            {
                await service.ShareFileAsync(id, input.FileId, ct);
                return Results.NoContent();
            })
            .WithName("ShareDuplicateFile")
            .WithSummary("Both tracks of an open pair stay on their albums and play fileId, one of the pair's shareableFileIds; the other's copy in that folder goes to the trash. " +
                         "Requires MANAGE_LIBRARIES and DELETE_TRACKS.");

        duplicates.MapPost("/rescan", async (DuplicateService service, CancellationToken ct) =>
            {
                await service.RescanAsync(ct);
                return Results.NoContent();
            })
            .WithName("RescanDuplicates")
            .WithSummary("Looks through the whole catalog again, in the background, for pairs not proposed yet. Requires MANAGE_LIBRARIES.");

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

        duplicates.MapGet("/copies", (Guid? libraryId, string? after, int? first, LibraryDuplicateService service, CancellationToken ct) =>
                service.CopiesAsync(new LibraryDuplicatesInput(libraryId, after, first), ct))
            .WithName("TrackCopies")
            .WithSummary("Songs with several files of their own in one library (a FLAC and an MP3), by track id with a cursor. Conversions Arcanist keeps are not copies. Requires MANAGE_LIBRARIES.");

        duplicates.MapDelete("/copies/{fileId:guid}", async (Guid fileId, TrackEditService service, CancellationToken ct) =>
            {
                await service.DeleteFileAsync(fileId, ct);
                return Results.NoContent();
            })
            .WithName("DeleteTrackFile")
            .WithSummary("Moves one copy of a song to the trash; the song stays with the others (last_copy when it has none). Requires DELETE_TRACKS.");

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
