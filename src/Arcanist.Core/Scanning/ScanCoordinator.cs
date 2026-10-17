using System.Collections.Concurrent;
using System.Threading.Channels;
using Arcanist.Core.Application;
using ScanEvents = Arcanist.Core.Application.Scanning.ScanEvents;
using Arcanist.Core.Data;
using Arcanist.Core.Data.Entities;
using Arcanist.Core.Organization;
using Arcanist.Core.Setup;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Arcanist.Core.Scanning;

/// <summary>
/// Queue of scan jobs. A library never has two active jobs: asking again returns the one already queued or
/// running. Jobs run one at a time, so scans never compete for the disk or the database writer.
/// </summary>
public sealed class ScanCoordinator(IArcanistDbContextFactory databases, ScanEvents events, TimeProvider time)
{
    private readonly Channel<Guid> _queue = Channel.CreateUnbounded<Guid>();
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _running = new();
    private readonly SemaphoreSlim _enqueueLock = new(1, 1);

    internal ChannelReader<Guid> Queue => _queue.Reader;

    public async Task<ScanJob> EnqueueAsync(Guid libraryId, ScanKind kind, Guid? requestedBy, CancellationToken cancellationToken)
    {
        await _enqueueLock.WaitAsync(cancellationToken);
        try
        {
            await using var db = databases.CreateDbContext();
            var active = await db.ScanJobs.AsNoTracking()
                .Where(j => j.LibraryId == libraryId && (j.Status == ScanStatus.Queued || j.Status == ScanStatus.Running))
                .OrderBy(j => j.RequestedAt)
                .FirstOrDefaultAsync(cancellationToken);
            if (active is not null)
                return active;

            var job = new ScanJob
            {
                LibraryId = libraryId,
                Kind = kind,
                Status = ScanStatus.Queued,
                RequestedByUserId = requestedBy,
                RequestedAt = time.GetUtcNow().UtcDateTime,
            };
            db.ScanJobs.Add(job);
            await db.SaveChangesAsync(cancellationToken);

            // Published before the worker can take it: otherwise its RUNNING could reach clients before this QUEUED.
            events.Publish(job);
            _queue.Writer.TryWrite(job.Id);
            return job;
        }
        finally
        {
            _enqueueLock.Release();
        }
    }

    /// <summary>Cancels a queued or running job. Returns false when it already finished.</summary>
    public async Task<bool> CancelAsync(Guid jobId, CancellationToken cancellationToken)
    {
        if (_running.TryGetValue(jobId, out var running))
        {
            await running.CancelAsync();
            return true;
        }

        await using var db = databases.CreateDbContext();
        var job = await db.ScanJobs.SingleOrDefaultAsync(j => j.Id == jobId && j.Status == ScanStatus.Queued, cancellationToken);
        if (job is null)
            return false;

        job.Status = ScanStatus.Cancelled;
        job.FinishedAt = time.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(cancellationToken);
        events.Publish(job);
        return true;
    }

    /// <summary>Stops any scan of a library, e.g. before deleting it.</summary>
    public async Task CancelLibraryAsync(Guid libraryId, CancellationToken cancellationToken)
    {
        await using var db = databases.CreateDbContext();
        var active = await db.ScanJobs
            .Where(j => j.LibraryId == libraryId && (j.Status == ScanStatus.Queued || j.Status == ScanStatus.Running))
            .Select(j => j.Id)
            .ToListAsync(cancellationToken);

        foreach (var jobId in active)
            await CancelAsync(jobId, cancellationToken);
    }

    /// <summary>Puts back a job that is already queued in the database (after a restart).</summary>
    internal ValueTask RequeueAsync(Guid jobId) => _queue.Writer.WriteAsync(jobId);

    internal CancellationToken BeginRun(Guid jobId, CancellationToken stopping)
    {
        var source = CancellationTokenSource.CreateLinkedTokenSource(stopping);
        _running[jobId] = source;
        return source.Token;
    }

    internal void EndRun(Guid jobId)
    {
        if (_running.TryRemove(jobId, out var source))
            source.Dispose();
    }
}

/// <summary>Runs queued scans in the background. On startup, re-queues jobs left over by a previous run.</summary>
internal sealed class ScanWorker(
    ScanCoordinator coordinator,
    LibraryScanner scanner,
    LibraryOrganizer organizer,
    IArcanistDbContextFactory databases,
    IOptions<SetupOptions> setup,
    TimeProvider time,
    DatabaseMaintenance maintenance,
    ILogger<ScanWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();

        if (setup.Value.Completed && databases.IsConfigured)
            await RecoverAsync(stoppingToken);

        await foreach (var jobId in coordinator.Queue.ReadAllAsync(stoppingToken))
        {
            // A queued scan waits while the database is migrated.
            using var work = await maintenance.WorkAsync(stoppingToken);
            var token = coordinator.BeginRun(jobId, stoppingToken);
            try
            {
                await scanner.RunAsync(jobId, token);
                await organizer.AfterScanAsync(jobId, token);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception e)
            {
                logger.LogError(e, "Scan job {JobId} crashed", jobId);
            }
            finally
            {
                coordinator.EndRun(jobId);
            }
        }
    }

    private async Task RecoverAsync(CancellationToken cancellationToken)
    {
        await using var db = databases.CreateDbContext();
        var now = time.GetUtcNow().UtcDateTime;

        var queued = await db.ScanJobs.Where(j => j.Status == ScanStatus.Queued).OrderBy(j => j.RequestedAt).Select(j => j.Id).ToListAsync(cancellationToken);
        foreach (var jobId in queued)
            await coordinator.RequeueAsync(jobId);

        // A scan that was running when the server stopped cannot be resumed; it is reported and queued again.
        var interrupted = await db.ScanJobs.Where(j => j.Status == ScanStatus.Running).ToListAsync(cancellationToken);
        foreach (var job in interrupted)
        {
            job.Status = ScanStatus.Failed;
            job.ErrorCode = ErrorCodes.Interrupted;
            job.FinishedAt = now;
        }

        await db.SaveChangesAsync(cancellationToken);

        foreach (var job in interrupted)
            await coordinator.EnqueueAsync(job.LibraryId, job.Kind, job.RequestedByUserId, cancellationToken);
    }
}

public static class ScanningServiceCollectionExtensions
{
    public static IServiceCollection AddScanning(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<ScannerOptions>(configuration.GetSection(ScannerOptions.Section));
        services.AddSingleton<ArtworkStore>();
        services.AddSingleton<ScanProgressTracker>();
        services.AddSingleton<ScanEvents>();
        services.AddHostedService<ScanScheduler>();
        services.AddSingleton<ScanCoordinator>();
        services.AddSingleton<LibraryScanner>();
        services.AddHostedService<ScanWorker>();
        return services;
    }
}
