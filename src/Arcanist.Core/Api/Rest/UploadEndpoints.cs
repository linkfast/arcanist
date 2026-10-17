using Arcanist.Core.Application.Uploads;

namespace Arcanist.Core.Api.Rest;

public static class UploadEndpoints
{
    public static IEndpointRouteBuilder MapUploadEndpoints(this IEndpointRouteBuilder rest)
    {
        rest.MapGet("/me/uploads", (string? after, int? first, MyUploadService service, CancellationToken ct) => service.UploadsAsync(first, after, ct))
            .WithTags("Uploads")
            .WithName("MyUploads")
            .WithSummary("The user's uploads, newest first: files sent into libraries (and whether each is in the catalog yet) and songs sent to their own Ultimate Cloud library.");

        rest.MapDelete("/me/uploads", (MyUploadService service, CancellationToken ct) => service.ClearAsync(ct))
            .WithTags("Uploads")
            .WithName("ClearMyUploads")
            .WithSummary("Forgets the user's finished uploads (what is still on its way stays). Returns how many were forgotten.");

        rest.MapPost("/me/uploads/ultimate-cloud", (UploadTracksToMyCloudInput input, MyUploadService service, CancellationToken ct) =>
                service.UploadToMyCloudAsync(input, ct))
            .WithTags("Uploads")
            .WithName("UploadTracksToMyCloud")
            .WithSummary("Sends songs of this server (at most 1000) to the user's own Ultimate Cloud library, in the background; their progress is in myUploads. Needs the DOWNLOAD permission, and for a library another server shares, its permission to download.");

        return rest;
    }
}
