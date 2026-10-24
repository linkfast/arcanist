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

        rest.MapPost("/uploads", async (string fileName, Guid? wishId, Guid? libraryId, string? client, HttpContext context, Application.Libraries.UploadService service,
                Microsoft.Extensions.Options.IOptionsMonitor<Application.Libraries.UploadOptions> options, CancellationToken ct) =>
            {
                Application.Libraries.UploadClient? kind = null;
                if (!string.IsNullOrEmpty(client))
                {
                    if (!Enum.TryParse<Application.Libraries.UploadClient>(client, ignoreCase: true, out var parsed) || !Enum.IsDefined(parsed))
                        throw ArcanistException.Field("client", ErrorCodes.InvalidFormat);
                    kind = parsed;
                }

                if (context.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } limit)
                    limit.MaxRequestBodySize = options.CurrentValue.MaxBytes;
                var result = libraryId is { } target
                    ? await service.UploadAsync(target, fileName, context.Request.Body, kind, ct)
                    : await service.UploadAsync(wishId, fileName, context.Request.Body, kind, ct);
                return result.Status == Application.Libraries.UploadStatus.Imported ? Results.Created((string?)null, result) : Results.Ok(result);
            })
            .WithTags("Uploads")
            .WithName("Upload")
            .WithSummary("Adds an audio file as `uploadToLibrary` does: into `libraryId` when given, else the target of the user's wish `wishId` when it has one, else the user's own choice (`myUploadSettings`), else the server's default for uploads (`uploadSettings`); `upload_library_required` without one. Requires UPLOAD.")
            .Accepts<Stream>("application/octet-stream")
            .Produces<Application.Libraries.UploadResult>(StatusCodes.Status201Created)
            .Produces<Application.Libraries.UploadResult>();

        rest.MapGet("/uploads/settings", (Application.Libraries.UploadDestinations service, CancellationToken ct) => service.GetAsync(ct))
            .WithTags("Uploads")
            .WithName("UploadSettings")
            .WithSummary("The library uploads go to when they name none (and wishes with no target of their own), or none.");

        rest.MapPut("/uploads/settings", (Application.Libraries.SetUploadSettingsInput input, Application.Libraries.UploadDestinations service,
                CancellationToken ct) => service.SetAsync(input, ct))
            .WithTags("Uploads")
            .WithName("SetUploadSettings")
            .WithSummary("Sets the library uploads go to when they name none; null leaves the choice to each upload. Requires MANAGE_LIBRARIES.");

        rest.MapGet("/me/upload-settings", (Application.Libraries.UploadDestinations service, CancellationToken ct) => service.MyAsync(ct))
            .WithTags("Uploads")
            .WithName("MyUploadSettings")
            .WithSummary("The library the user chose for their uploads that name none, and where such an upload goes now (their choice, else the server's default).");

        rest.MapPut("/me/upload-settings", (Application.Libraries.SetMyUploadSettingsInput input, Application.Libraries.UploadDestinations service,
                CancellationToken ct) => service.SetMyAsync(input, ct))
            .WithTags("Uploads")
            .WithName("SetMyUploadSettings")
            .WithSummary("Chooses the library the user's uploads go to when they name none (one they can upload to: libraryId not_found otherwise); null goes back to the server's default. For apps that use the whole server as one library.");

        rest.MapGet("/me/storage", (Application.Libraries.MyStorageService service, CancellationToken ct) => service.UsageAsync(ct))
            .WithTags("Libraries")
            .WithName("MyStorage")
            .WithSummary("How much music each library the user can access holds (files, tracks, bytes) and the free space of its disk when known, with the totals and the free space where the user's uploads go.");

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
