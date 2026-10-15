using Arcanist.Core.Application.Libraries;

namespace Arcanist.Core.Api.Rest;

public static class LibraryEndpoints
{
    public static IEndpointRouteBuilder MapLibraryEndpoints(this IEndpointRouteBuilder rest)
    {
        var libraries = rest.MapGroup("/libraries").WithTags("Libraries");

        libraries.MapGet("", (LibraryService service, CancellationToken ct) => service.ListAsync(ct))
            .WithName("Libraries")
            .WithSummary("Libraries the signed-in user can access.");

        libraries.MapGet("/{id:guid}", (Guid id, LibraryService service, CancellationToken ct) => service.GetAsync(id, ct))
            .WithName("Library")
            .WithSummary("A library by id. Libraries the user cannot access are reported as not found.");

        libraries.MapPost("", async (CreateLibraryInput input, LibraryService service, CancellationToken ct) =>
            {
                var library = await service.CreateAsync(input, ct);
                return Results.Created($"/rest/v1/libraries/{library.Id}", library);
            })
            .WithName("CreateLibrary")
            .WithSummary("Requires MANAGE_LIBRARIES.")
            .Produces<Library>(StatusCodes.Status201Created);

        libraries.MapPost("/s3/test", (S3ConnectionInput input, S3LibraryService service, CancellationToken ct) => service.TestAsync(input, ct))
            .WithName("TestS3Connection")
            .WithSummary("Checks that an S3 bucket answers with those keys, and how much music it holds. Needs the Cloud plan. Administrators only.");

        libraries.MapPost("/s3", async (CreateS3LibraryInput input, S3LibraryService service, CancellationToken ct) =>
            {
                var library = await service.CreateAsync(input, ct);
                return Results.Created($"/rest/v1/libraries/{library.Id}", library);
            })
            .WithName("CreateS3Library")
            .WithSummary("A library in an S3 bucket (any S3-compatible service). Needs the Cloud plan. Administrators only, with a recent authentication.")
            .Produces<Library>(StatusCodes.Status201Created);

        libraries.MapPut("/{id:guid}", (Guid id, UpdateLibraryInput input, LibraryService service, CancellationToken ct) =>
                service.UpdateAsync(id, input, ct))
            .WithName("UpdateLibrary")
            .WithSummary("Requires MANAGE_LIBRARIES.");

        libraries.MapDelete("/{id:guid}", async (Guid id, LibraryService service, CancellationToken ct) =>
            {
                await service.DeleteAsync(id, ct);
                return Results.NoContent();
            })
            .WithName("DeleteLibrary")
            .WithSummary("Removes the library and its tracks from the catalog; files on disk are not touched. Requires MANAGE_LIBRARIES and a recent authentication.");

        libraries.MapPost("/{libraryId:guid}/sources", (Guid libraryId, LibrarySourceInput input, LibraryService service, CancellationToken ct) =>
                service.AddSourceAsync(libraryId, input, ct))
            .WithName("AddLibrarySource")
            .WithSummary("Adds a location to a library. Requires MANAGE_LIBRARIES.");

        rest.MapDelete("/library-sources/{id:guid}", (Guid id, LibraryService service, CancellationToken ct) =>
                service.RemoveSourceAsync(id, ct))
            .WithTags("Libraries")
            .WithName("RemoveLibrarySource")
            .WithSummary("Removes a location and its tracks from the catalog; files on disk are not touched. Requires MANAGE_LIBRARIES.");

        rest.MapGet("/server/directories", (string? path, LibraryService service) => service.BrowseServerDirectories(path))
            .WithTags("Server")
            .WithName("ServerDirectories")
            .WithSummary("Folders on the server, for choosing library locations. Lists the file system roots when path is omitted. Administrators only.");

        rest.MapPost("/server/directories", (CreateServerDirectoryInput input, LibraryService service) =>
                Results.Created((string?)null, service.CreateServerDirectory(input)))
            .WithTags("Server")
            .WithName("CreateServerDirectory")
            .WithSummary("Creates a folder on the server, to pick it as a location. Administrators only.")
            .Produces<ServerDirectory>(StatusCodes.Status201Created);

        return rest;
    }
}
