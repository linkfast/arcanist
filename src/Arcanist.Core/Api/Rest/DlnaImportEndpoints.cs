using Arcanist.Core.Application.Import;

namespace Arcanist.Core.Api.Rest;

public static class DlnaImportEndpoints
{
    public static IEndpointRouteBuilder MapDlnaImportEndpoints(this IEndpointRouteBuilder rest)
    {
        var dlna = rest.MapGroup("/dlna-import").WithTags("Import");

        dlna.MapGet("/servers", (DlnaImportService service, CancellationToken ct) => service.SearchAsync(ct))
            .WithName("DlnaMediaServers")
            .WithSummary("Searches the local networks for DLNA/UPnP media servers for a few seconds. Administrators only.");

        dlna.MapGet("/server", (string location, DlnaImportService service, CancellationToken ct) => service.DescribeAsync(location, ct))
            .WithName("DlnaMediaServer")
            .WithSummary("The media server at a description URL, for servers the search does not find. Administrators only.");

        dlna.MapGet("/browse", (string location, string? folderId, DlnaImportService service, CancellationToken ct) =>
                service.BrowseAsync(location, folderId, ct))
            .WithName("BrowseDlnaMediaServer")
            .WithSummary("A folder of a media server (0 or none is the root): its subfolders and how much music it holds. Administrators only.");

        dlna.MapGet("/imports", (DlnaImportService service, CancellationToken ct) => service.ListAsync(ct))
            .WithName("DlnaImports")
            .WithSummary("The latest copies from media servers and how far they got. Administrators only.");

        dlna.MapPost("/imports", (StartDlnaImportInput input, DlnaImportService service, CancellationToken ct) => service.StartAsync(input, ct))
            .WithName("StartDlnaImport")
            .WithSummary("Copies the music of folders of a media server into a library folder, in the background. " +
                         "Administrators only, with a recent authentication.");

        dlna.MapPost("/imports/{id:guid}/cancel", (Guid id, DlnaImportService service, CancellationToken ct) => service.CancelAsync(id, ct))
            .WithName("CancelDlnaImport")
            .WithSummary("Stops a copy from a media server; what was copied stays. Administrators only.");

        dlna.MapPost("/imports/{id:guid}/retry", (Guid id, DlnaImportService service, CancellationToken ct) => service.RetryAsync(id, ct))
            .WithName("RetryDlnaImport")
            .WithSummary("Walks the media server again and copies what is missing. Administrators only, with a recent authentication.");

        return rest;
    }
}
