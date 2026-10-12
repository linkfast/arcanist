using Arcanist.Core.Application;
using Arcanist.Core.Application.Libraries;
using Arcanist.Core.Application.Organization;
using Arcanist.Core.Data.Entities;
using Library = Arcanist.Core.Application.Libraries.Library;
using DropFolder = Arcanist.Core.Application.Organization.DropFolder;
using ImportItem = Arcanist.Core.Application.Organization.ImportItem;
using OrganizeRun = Arcanist.Core.Application.Organization.OrganizeRun;

namespace Arcanist.Core.Api.Rest;

/// <param name="Tags">Written into the file before importing it.</param>
public sealed record AcceptImportItemInput(ImportTagsInput? Tags);

public static class OrganizationEndpoints
{
    public static IEndpointRouteBuilder MapOrganizationEndpoints(this IEndpointRouteBuilder rest)
    {
        var libraries = rest.MapGroup("/libraries/{libraryId:guid}").WithTags("Organization");

        libraries.MapPut("/organization", (Guid libraryId, UpdateLibraryOrganizationInput input, OrganizationService service, CancellationToken ct) =>
                service.UpdateAsync(libraryId, input, ct))
            .WithName("UpdateLibraryOrganization")
            .WithSummary("Sets the library's path template and whether files added to it are organized automatically. Requires MANAGE_LIBRARIES.")
            .Produces<Library>();

        libraries.MapGet("/organize/preview", (Guid libraryId, string? template, OrganizationService service, CancellationToken ct) =>
                service.PreviewAsync(libraryId, template, ct))
            .WithName("OrganizePreview")
            .WithSummary("What organizing the library would move, with the given template or the library's own. Requires MANAGE_LIBRARIES.");

        libraries.MapPost("/organize", (Guid libraryId, OrganizationService service, CancellationToken ct) =>
                service.StartAsync(libraryId, ct))
            .WithName("OrganizeLibrary")
            .WithSummary("Moves the library's files to the paths its template gives them, in the background. Requires MANAGE_LIBRARIES and a recent authentication.")
            .Produces<OrganizeRun>();

        libraries.MapGet("/organize/runs", (Guid libraryId, OrganizationService service, CancellationToken ct) =>
                service.RunsAsync(libraryId, ct))
            .WithName("OrganizeRuns")
            .WithSummary("The library's latest organizing runs, newest first. Requires MANAGE_LIBRARIES.");

        rest.MapPost("/organize-runs/{id:guid}/undo", (Guid id, OrganizationService service, CancellationToken ct) =>
                service.UndoAsync(id, ct))
            .WithTags("Organization")
            .WithName("UndoOrganizeRun")
            .WithSummary("Moves the files of a finished organizing run back where they were, in the background. Requires MANAGE_LIBRARIES and a recent authentication.");

        var drops = rest.MapGroup("/drop-folders").WithTags("Import");

        drops.MapGet("", (ImportService service, CancellationToken ct) => service.DropFoldersAsync(ct))
            .WithName("DropFolders")
            .WithSummary("Folders whose files are imported into libraries. Administrators only.");

        drops.MapPost("", async (DropFolderInput input, ImportService service, CancellationToken ct) =>
            {
                var folder = await service.CreateDropFolderAsync(input, ct);
                return Results.Created($"/rest/v1/drop-folders/{folder.Id}", folder);
            })
            .WithName("CreateDropFolder")
            .WithSummary("Administrators only.")
            .Produces<DropFolder>(StatusCodes.Status201Created);

        drops.MapPut("/{id:guid}", (Guid id, DropFolderInput input, ImportService service, CancellationToken ct) =>
                service.UpdateDropFolderAsync(id, input, ct))
            .WithName("UpdateDropFolder")
            .WithSummary("Administrators only.");

        drops.MapDelete("/{id:guid}", async (Guid id, ImportService service, CancellationToken ct) =>
            {
                await service.DeleteDropFolderAsync(id, ct);
                return Results.NoContent();
            })
            .WithName("DeleteDropFolder")
            .WithSummary("Stops importing from the folder and forgets its history; its files are not touched. Administrators only.");

        var items = rest.MapGroup("/import-items").WithTags("Import");

        items.MapGet("", (string? status, Guid? dropFolderId, int? first, ImportService service, CancellationToken ct) =>
                service.ItemsAsync(ParseStatus(status), dropFolderId, first, ct))
            .WithName("ImportItems")
            .WithSummary("Files found in drop folders, most recently changed first: in the given `status`, or else those still in a drop folder. Requires MANAGE_LIBRARIES.");

        items.MapGet("/summary", (ImportService service, CancellationToken ct) => service.SummaryAsync(ct))
            .WithName("ImportSummary")
            .WithSummary("How many files of the drop folders are in each state. Requires MANAGE_LIBRARIES.");

        items.MapPost("/{id:guid}/accept", (Guid id, AcceptImportItemInput? input, ImportService service, CancellationToken ct) =>
                service.AcceptAsync(id, input?.Tags, ct))
            .WithName("AcceptImportItem")
            .WithSummary("Imports a file even though it is incomplete or a duplicate (then as another copy of its track), first writing the given tags into it. Requires MANAGE_LIBRARIES.");

        items.MapPost("/{id:guid}/retry", (Guid id, ImportService service, CancellationToken ct) => service.RetryAsync(id, ct))
            .WithName("RetryImportItem")
            .WithSummary("Looks at a file again, as if it had just been dropped. Requires MANAGE_LIBRARIES.");

        items.MapPost("/{id:guid}/ignore", (Guid id, ImportService service, CancellationToken ct) => service.IgnoreAsync(id, ct))
            .WithName("IgnoreImportItem")
            .WithSummary("Leaves a file in its drop folder; it is looked at again only if it changes. Requires MANAGE_LIBRARIES.");

        items.MapPost("/{id:guid}/discard", (Guid id, ImportService service, CancellationToken ct) => service.DiscardAsync(id, ct))
            .WithName("DiscardImportItem")
            .WithSummary("Moves a file to its drop folder's trash, from which it is deleted after a while. Requires MANAGE_LIBRARIES.");

        return rest;
    }

    // Query binding of enums is case-sensitive and ignores the API's spelling (NEEDS_REVIEW).
    private static ImportStatus? ParseStatus(string? status) =>
        string.IsNullOrEmpty(status) ? null
        : Enum.TryParse<ImportStatus>(status.Replace("_", ""), ignoreCase: true, out var parsed) && Enum.IsDefined(parsed) ? parsed
        : throw ArcanistException.Field("status", ErrorCodes.InvalidFormat);
}
