using Arcanist.Core.Application.Scanning;

namespace Arcanist.Core.Api.Rest;

public sealed record StartScanInput(bool Full = false);

public static class ScanEndpoints
{
    public static IEndpointRouteBuilder MapScanEndpoints(this IEndpointRouteBuilder rest)
    {
        var scans = rest.MapGroup("/scans").WithTags("Scans");

        scans.MapGet("", (Guid? libraryId, int? limit, ScanService service, CancellationToken ct) => service.ListAsync(libraryId, limit, ct))
            .WithName("ScanJobs")
            .WithSummary("Most recent scans first, of one library or of all libraries the user can see. Requires RUN_SCANS or MANAGE_LIBRARIES.");

        scans.MapGet("/{id:guid}", (Guid id, ScanService service, CancellationToken ct) => service.GetAsync(id, ct))
            .WithName("ScanJob")
            .WithSummary("A scan by id, including live progress while it runs. Poll it to follow a scan. Requires RUN_SCANS or MANAGE_LIBRARIES.");

        scans.MapPost("/{id:guid}/cancel", async (Guid id, ScanService service, CancellationToken ct) =>
            {
                await service.CancelAsync(id, ct);
                return Results.NoContent();
            })
            .WithName("CancelScan")
            .WithSummary("Requires RUN_SCANS.");

        rest.MapPost("/libraries/{libraryId:guid}/scans", (Guid libraryId, StartScanInput? input, ScanService service, CancellationToken ct) =>
                service.StartAsync(libraryId, input?.Full ?? false, ct))
            .WithTags("Scans")
            .WithName("StartScan")
            .WithSummary("Queues a scan, or returns the one already queued or running for the library. A full scan re-reads every file. Requires RUN_SCANS.");

        return rest;
    }
}
