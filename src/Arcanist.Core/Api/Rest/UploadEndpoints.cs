using Arcanist.Core.Application;
using Arcanist.Core.Application.Uploads;

namespace Arcanist.Core.Api.Rest;

public static class UploadEndpoints
{
    public static IEndpointRouteBuilder MapUploadEndpoints(this IEndpointRouteBuilder rest)
    {
        rest.MapGet("/uploads", (Guid? libraryId, Guid? userId, string? origin, string? status, string? after, int? first, UploadLogService service,
                CancellationToken ct) =>
            {
                // Parsed here rather than bound: query binding of enums is case-sensitive.
                UploadOrigin? from = null;
                if (!string.IsNullOrEmpty(origin))
                    from = Enum.TryParse<UploadOrigin>(origin, true, out var o) && Enum.IsDefined(o) ? o : throw ArcanistException.Field("origin", ErrorCodes.InvalidFormat);
                MyUploadStatus? state = null;
                if (!string.IsNullOrEmpty(status))
                    state = Enum.TryParse<MyUploadStatus>(status, true, out var s) && Enum.IsDefined(s) ? s : throw ArcanistException.Field("status", ErrorCodes.InvalidFormat);
                return service.ListAsync(new UploadLogInput(libraryId, userId, from, state, after, first), ct);
            })
            .WithName("Uploads")
            .WithSummary("Every upload into the server's libraries, by any user and from any client (the admin panel, Ultimate Desktop, web players, phones, extensions), newest first, with optional filters. Requires MANAGE_LIBRARIES (only libraries the caller can access, unless administrator).");

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
