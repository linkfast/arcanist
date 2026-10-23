using System.ComponentModel;
using Arcanist.Core.Application.Uploads;
using GraphQL;

namespace Arcanist.Core.Api.GraphQL;

public sealed partial class Query
{
    [Name("uploads")]
    [Description("Every upload into the server's libraries, by any user and from any client (the admin panel, Ultimate Desktop, web players, phones, extensions), newest first, with optional filters. Requires MANAGE_LIBRARIES (only libraries the caller can access, unless administrator).")]
    public static Task<UploadLogPage> Uploads(UploadLogInput? input, [FromServices] UploadLogService log, CancellationToken cancellationToken) =>
        log.ListAsync(input ?? new UploadLogInput(), cancellationToken);

    [Name("uploadSettings")]
    [Description("The library uploads go to when they name none (and wishes with no target of their own), or none.")]
    public static Task<Application.Libraries.UploadSettings> UploadSettings([FromServices] Application.Libraries.UploadDestinations service,
        CancellationToken cancellationToken) => service.GetAsync(cancellationToken);

    [Name("myUploads")]
    [Description("The user's uploads, newest first: files sent into libraries (and whether each is in the catalog yet) and songs sent to their own Ultimate Cloud library.")]
    public static Task<MyUploadPage> MyUploads(string? after, int? first, [FromServices] MyUploadService uploads, CancellationToken cancellationToken) =>
        uploads.UploadsAsync(first, after, cancellationToken);
}

public sealed partial class Mutation
{
    [Name("setUploadSettings")]
    [Description("Sets the library uploads go to when they name none; null leaves the choice to each upload. Requires MANAGE_LIBRARIES.")]
    public static Task<Application.Libraries.UploadSettings> SetUploadSettings(Application.Libraries.SetUploadSettingsInput input,
        [FromServices] Application.Libraries.UploadDestinations service, CancellationToken cancellationToken) =>
        service.SetAsync(input, cancellationToken);

    [Name("clearMyUploads")]
    [Description("Forgets the user's finished uploads (what is still on its way stays). Returns how many were forgotten.")]
    public static Task<int> ClearMyUploads([FromServices] MyUploadService uploads, CancellationToken cancellationToken) =>
        uploads.ClearAsync(cancellationToken);

    [Name("uploadTracksToMyCloud")]
    [Description("Sends songs of this server (at most 1000) to the user's own Ultimate Cloud library, in the background; their progress is in myUploads. Needs the DOWNLOAD permission, and for a library another server shares, its permission to download.")]
    public static Task<IReadOnlyList<MyUpload>> UploadTracksToMyCloud(UploadTracksToMyCloudInput input, [FromServices] MyUploadService uploads,
        CancellationToken cancellationToken) =>
        uploads.UploadToMyCloudAsync(input, cancellationToken);
}
