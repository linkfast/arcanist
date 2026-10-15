using Arcanist.Core.Data;
using Arcanist.Core.Data.Entities;
using Arcanist.Core.Setup;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Arcanist.Core.Scanning;

/// <summary>
/// Queues incremental scans for libraries with <see cref="Library.ScanIntervalMinutes"/> set, once that long has
/// passed since their last scan was requested (successful or not, so a failing source is not retried every minute).
/// </summary>
internal sealed class ScanScheduler(
    ScanCoordinator coordinator,
    IArcanistDbContextFactory databases,
    IOptionsMonitor<SetupOptions> setup,
    TimeProvider time,
    DatabaseMaintenance maintenance,
    Application.Libraries.CloudLibraries cloud,
    ILogger<ScanScheduler> logger) : BackgroundService
{
    public static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(CheckInterval, time);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            if (!setup.CurrentValue.Completed || !databases.IsConfigured)
                continue;

            try
            {
                using (await maintenance.WorkAsync(stoppingToken))
                    await QueueDueScansAsync(stoppingToken);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                logger.LogError(e, "Scheduling scans failed");
            }
        }
    }

    internal async Task QueueDueScansAsync(CancellationToken cancellationToken)
    {
        await using var db = databases.CreateDbContext();
        var now = time.GetUtcNow().UtcDateTime;

        var cloudOn = cloud.Enabled;
        var libraries = await db.Libraries.AsNoTracking()
            .Where(l => l.ScanIntervalMinutes != null)
            // Disabled while the Ultimate Cloud extension is off.
            .Where(l => cloudOn || !l.Sources.Any(s => s.Kind == Data.Entities.LibrarySourceKind.UltimateCloud || s.Kind == Data.Entities.LibrarySourceKind.S3))
            .Select(l => new
            {
                l.Id,
                Interval = l.ScanIntervalMinutes!.Value,
                LastRequested = db.ScanJobs.Where(j => j.LibraryId == l.Id).Max(j => (DateTime?)j.RequestedAt),
            })
            .ToListAsync(cancellationToken);

        foreach (var library in libraries)
        {
            if (library.LastRequested is null || now - library.LastRequested >= TimeSpan.FromMinutes(library.Interval))
                await coordinator.EnqueueAsync(library.Id, ScanKind.Incremental, requestedBy: null, cancellationToken);
        }
    }
}
