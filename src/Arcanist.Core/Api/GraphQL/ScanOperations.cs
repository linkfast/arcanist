using System.ComponentModel;
using Arcanist.Core.Application.Scanning;
using GraphQL;

namespace Arcanist.Core.Api.GraphQL;

public sealed partial class Query
{
    [Name("scanJobs")]
    [Description("Most recent scans first, of one library or of all libraries the user can see. Requires RUN_SCANS or MANAGE_LIBRARIES.")]
    public static Task<IReadOnlyList<ScanJob>> ScanJobs([Id] Guid? libraryId, int? limit, [FromServices] ScanService scans, CancellationToken cancellationToken) =>
        scans.ListAsync(libraryId, limit, cancellationToken);

    [Name("scanJob")]
    [Description("A scan by id, including live progress while it runs. Requires RUN_SCANS or MANAGE_LIBRARIES.")]
    public static Task<ScanJob> ScanJob([Id] Guid id, [FromServices] ScanService scans, CancellationToken cancellationToken) =>
        scans.GetAsync(id, cancellationToken);
}

public sealed partial class Mutation
{
    [Name("startScan")]
    [Description("Queues a scan, or returns the one already queued or running for the library. A full scan re-reads every file. Requires RUN_SCANS.")]
    public static Task<ScanJob> StartScan([Id] Guid libraryId, bool full, [FromServices] ScanService scans, CancellationToken cancellationToken) =>
        scans.StartAsync(libraryId, full, cancellationToken);

    [Name("cancelScan")]
    [Description("Requires RUN_SCANS.")]
    public static async Task<bool> CancelScan([Id] Guid id, [FromServices] ScanService scans, CancellationToken cancellationToken)
    {
        await scans.CancelAsync(id, cancellationToken);
        return true;
    }
}
