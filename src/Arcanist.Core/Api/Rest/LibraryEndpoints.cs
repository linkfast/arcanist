using Arcanist.Core.Application.Libraries;

namespace Arcanist.Core.Api.Rest;

public static class LibraryEndpoints
{
    public static IEndpointRouteBuilder MapLibraryEndpoints(this IEndpointRouteBuilder rest)
    {
        var libraries = rest.MapGroup("/libraries").WithTags("Libraries");

        libraries.MapGet("", (bool? includeVirtual, string? language, LibraryService service, CancellationToken ct) =>
                service.ListAsync(ct, includeVirtual ?? false, language))
            .WithName("Libraries")
            .WithSummary("Libraries the signed-in user can access. With `includeVirtual`, first \"All\" (`virtual`), every one of them at once, named in `language` (or Accept-Language).");

        libraries.MapGet("/{id:guid}", (Guid id, string? language, LibraryService service, CancellationToken ct) => service.GetAsync(id, ct, language))
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

        libraries.MapPost("/{id:guid}/uploads", async (Guid id, string fileName, string? client, HttpContext context, UploadService service,
                Microsoft.Extensions.Options.IOptionsMonitor<UploadOptions> options, CancellationToken ct) =>
            {
                // Parsed here rather than bound: query binding of enums is case-sensitive.
                UploadClient? kind = null;
                if (!string.IsNullOrEmpty(client))
                {
                    if (!Enum.TryParse<UploadClient>(client, ignoreCase: true, out var parsed) || !Enum.IsDefined(parsed))
                        throw Application.ArcanistException.Field("client", Application.ErrorCodes.InvalidFormat);
                    kind = parsed;
                }

                if (context.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } limit)
                    limit.MaxRequestBodySize = options.CurrentValue.MaxBytes;
                var result = await service.UploadAsync(id, fileName, context.Request.Body, kind, ct);
                return result.Status == UploadStatus.Imported ? Results.Created((string?)null, result) : Results.Ok(result);
            })
            .WithName("UploadToLibrary")
            .WithSummary("Adds an audio file (the request body, `application/octet-stream`; its name in `fileName`) to a library the user can access, placed with the library's template and never overwriting. 201 when it entered the library (catalogued by a scan shortly after the last upload), 200 when the library already had that audio. `client` (PANEL, DESKTOP, WEB or MOBILE) says what sends it, for the log of uploads administrators see. Every upload goes through here (extensions too, in the server). Requires UPLOAD.")
            .Accepts<Stream>("application/octet-stream")
            .Produces<UploadResult>(StatusCodes.Status201Created)
            .Produces<UploadResult>();

        libraries.MapGet("/s3/availability", (S3LibraryService service, CancellationToken ct) => service.AvailabilityAsync(ct))
            .WithName("S3LibraryAvailability")
            .WithSummary("Whether an S3 library can be created now, and why not: the Ultimate Cloud Plus extension with an Ultimate Cloud account on the Cloud plan, and another library besides. Administrators only.");

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

        libraries.MapPut("/{id:guid}/name", (Guid id, RenameLibraryInput input, LibraryService service, CancellationToken ct) =>
                service.RenameAsync(id, input, ct))
            .WithName("RenameLibrary")
            .WithSummary("Renames a library (only its name). Requires MANAGE_LIBRARIES, or to own it (a personal library). duplicate when another library has that name.");

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
            .WithSummary("Removes a location and its tracks from the catalog; files on disk are not touched. A local library's only folder cannot be removed (last_source): move it instead. Requires MANAGE_LIBRARIES.");

        rest.MapPost("/library-sources/{id:guid}/move", (Guid id, MoveLibrarySourceInput input, LibraryService service, CancellationToken ct) =>
                service.MoveSourceAsync(id, input, ct))
            .WithTags("Libraries")
            .WithName("MoveLibrarySource")
            .WithSummary("Moves a local library's folder to another folder of the server in the background: copies every file, points the library at the new folder and, unless keepOriginals, deletes the old files. Nothing is overwritten. Administrators only, with a recent authentication.");

        rest.MapGet("/library-sources/moves", (LibraryService service) => service.Moves())
            .WithTags("Libraries")
            .WithName("LibrarySourceMoves")
            .WithSummary("Library folder moves since the server started, the latest first, with their progress. Administrators only.");

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
