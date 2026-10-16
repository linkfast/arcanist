using Arcanist.Core.Application;
using Arcanist.Core.Application.Backups;
using Arcanist.Core.Data;
using Arcanist.Core.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Arcanist.Core.Backups;

/// <summary>
/// Runs backups, one at a time (restores wait for it too: they share <see cref="BackupGate"/>). Runs interrupted by a
/// restart go on where they were; the destination keeps a journal of each run until its snapshot is complete.
/// </summary>
internal sealed class BackupWorker(
    IArcanistDbContextFactory databases,
    BackupPlanner planner,
    BackupTargets targets,
    BackupEvents events,
    BackupGate gate,
    IEnumerable<IBackupExtra> extras,
    DatabaseMaintenance maintenance,
    IOptionsMonitor<Setup.SetupOptions> setup,
    TimeProvider time,
    ILogger<BackupWorker> logger) : BackgroundService
{
    private static readonly TimeSpan SaveEvery = TimeSpan.FromSeconds(1);

    private readonly SemaphoreSlim _wake = new(0);
    private readonly Lock _lock = new();
    private (Guid Id, CancellationTokenSource Cancel)? _running;

    public void Wake()
    {
        if (_wake.CurrentCount == 0)
            _wake.Release();
    }

    /// <summary>Stops the run if it is the one running; returns whether it was.</summary>
    public bool Cancel(Guid runId)
    {
        lock (_lock)
        {
            if (_running is not { } running || running.Id != runId)
                return false;
            running.Cancel.Cancel();
            return true;
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (await NextAsync(stoppingToken) is { } runId)
                {
                    using (await gate.EnterAsync(stoppingToken))
                        await RunAsync(runId, stoppingToken);
                    continue;
                }

                await _wake.WaitAsync(TimeSpan.FromMinutes(1), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception e)
            {
                logger.LogError(e, "Backups failed");
                await Task.Delay(TimeSpan.FromSeconds(30), time, stoppingToken).ContinueWith(_ => { }, TaskScheduler.Default);
            }
        }
    }

    private async Task<Guid?> NextAsync(CancellationToken cancellationToken)
    {
        if (!databases.IsConfigured || !setup.CurrentValue.Completed)
            return null;
        using (await maintenance.WorkAsync(cancellationToken))
        {
            await using var db = databases.CreateDbContext();
            return await db.BackupRuns
                .Where(r => r.Status == BackupRunStatus.Queued || r.Status == BackupRunStatus.Running)
                .OrderBy(r => r.Status == BackupRunStatus.Running ? 0 : 1).ThenBy(r => r.RequestedAt)
                .Select(r => (Guid?)r.Id)
                .FirstOrDefaultAsync(cancellationToken);
        }
    }

    private async Task RunAsync(Guid runId, CancellationToken stoppingToken)
    {
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        lock (_lock)
            _running = (runId, cancel);
        var counters = new BackupCounters();
        BackupRun run;
        Backup backup;
        BackupPlan plan;
        using (await maintenance.WorkAsync(cancel.Token))
        {
            await using var db = databases.CreateDbContext();
            run = await db.BackupRuns.SingleAsync(r => r.Id == runId, cancel.Token);
            backup = await db.Backups.AsNoTracking().Include(b => b.Destination).SingleAsync(b => b.Id == run.BackupId, cancel.Token);
            plan = await planner.PlanAsync(db, backup, cancel.Token);
            run.Status = BackupRunStatus.Running;
            run.StartedAt ??= Now;
            run.Snapshot ??= BackupFormat.SnapshotName(Now);
            await db.SaveChangesAsync(cancel.Token);
        }

        Publish(run, backup.Name, counters);
        var lastSave = time.GetTimestamp();
        try
        {
            var runner = new BackupRunner(logger, time);
            var target = targets.Open(backup.Destination);
            var snapshot = await runner.RunAsync(plan, target, run.Snapshot!, extras.ToList(), counters,
                started: name => run.Snapshot = name,
                changed: () =>
                {
                    if (time.GetElapsedTime(lastSave) < SaveEvery)
                        return;
                    lastSave = time.GetTimestamp();
                    Publish(run, backup.Name, counters);
                },
                cancel.Token);
            run.Snapshot = snapshot;
            await FinishAsync(run, backup.Name, counters, counters.Failed > 0 ? BackupRunStatus.CompletedWithErrors : BackupRunStatus.Completed, null);
            logger.LogInformation("Backup {Backup} done: {Copied} copied, {Unchanged} unchanged, {Failed} failed",
                backup.Name, counters.Copied, counters.Unchanged, counters.Failed);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Goes on after the restart.
        }
        catch (OperationCanceledException)
        {
            await FinishAsync(run, backup.Name, counters, BackupRunStatus.Cancelled, null);
        }
        catch (ArcanistException e)
        {
            logger.LogWarning("Backup {Backup} failed: {Error}", backup.Name, e.Code);
            await FinishAsync(run, backup.Name, counters, BackupRunStatus.Failed, e.Code);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            logger.LogError(e, "Backup {Backup} failed", backup.Name);
            await FinishAsync(run, backup.Name, counters, BackupRunStatus.Failed, ErrorCodes.BackupFailed);
        }
        finally
        {
            lock (_lock)
                _running = null;
        }
    }

    private void Publish(BackupRun run, string backupName, BackupCounters counters)
    {
        Copy(run, counters);
        events.Publish(BackupService.ToModel(run, backupName, counters.Failures.Take(50).Select(f => new BackupJobFailure(f.Path, f.Code)).ToList()));
    }

    private static void Copy(BackupRun run, BackupCounters counters)
    {
        run.Files = counters.Files == 0 ? counters.Copied + counters.Unchanged : counters.Files;
        run.Copied = counters.Copied;
        run.Unchanged = counters.Unchanged;
        run.Versioned = counters.Versioned;
        run.Skipped = counters.Skipped;
        run.Failed = counters.Failed;
        run.Bytes = counters.Bytes;
    }

    /// <summary>Ends a run (also when the server is stopping its other work).</summary>
    private async Task FinishAsync(BackupRun run, string backupName, BackupCounters counters, BackupRunStatus status, string? errorCode)
    {
        Copy(run, counters);
        run.Status = status;
        run.ErrorCode = errorCode;
        run.FinishedAt = Now;
        using (await maintenance.WorkAsync(CancellationToken.None))
        {
            await using var db = databases.CreateDbContext();
            db.BackupRuns.Update(run);
            db.BackupRunFailures.AddRange(counters.Failures.Select(f => new BackupRunFailure { RunId = run.Id, Path = Cut(f.Path), Code = f.Code }));
            if (status is BackupRunStatus.Completed or BackupRunStatus.CompletedWithErrors)
            {
                await db.Backups.Where(b => b.Id == run.BackupId)
                    .ExecuteUpdateAsync(s => s.SetProperty(b => b.LastRunAt, run.FinishedAt));
            }

            await db.SaveChangesAsync();
        }

        Publish(run, backupName, counters);
    }

    private static string Cut(string path) => path.Length > 1000 ? path[..1000] : path;

    private DateTime Now => time.GetUtcNow().UtcDateTime;
}

/// <summary>Backups and restores take turns: never two at once, so they never race on a destination or a library.</summary>
internal sealed class BackupGate
{
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public async Task<IDisposable> EnterAsync(CancellationToken cancellationToken)
    {
        await _semaphore.WaitAsync(cancellationToken);
        return new Release(_semaphore);
    }

    private sealed class Release(SemaphoreSlim semaphore) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
                semaphore.Release();
        }
    }
}

/// <summary>Queues the backups whose time came, every minute.</summary>
internal sealed class BackupScheduler(
    IArcanistDbContextFactory databases,
    BackupWorker worker,
    DatabaseMaintenance maintenance,
    IOptionsMonitor<Setup.SetupOptions> setup,
    TimeProvider time,
    ILogger<BackupScheduler> logger) : BackgroundService
{
    public static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(CheckInterval, time);
        do
        {
            if (!databases.IsConfigured || !setup.CurrentValue.Completed)
                continue;
            try
            {
                using (await maintenance.WorkAsync(stoppingToken))
                    await QueueDueAsync(stoppingToken);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                logger.LogError(e, "Could not queue the scheduled backups");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    /// <summary>Queues every enabled backup that is due and not running, and sets when each runs next.</summary>
    internal async Task<int> QueueDueAsync(CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow().UtcDateTime;
        await using var db = databases.CreateDbContext();
        var due = await db.Backups
            .Where(b => b.Enabled && b.NextRunAt != null && b.NextRunAt <= now)
            .ToListAsync(cancellationToken);
        var queued = 0;
        foreach (var backup in due)
        {
            backup.NextRunAt = BackupSchedules.Next(backup, now);
            if (await db.BackupRuns.AnyAsync(r => r.BackupId == backup.Id && (r.Status == BackupRunStatus.Queued || r.Status == BackupRunStatus.Running), cancellationToken))
                continue;
            db.BackupRuns.Add(new BackupRun
            {
                BackupId = backup.Id,
                Status = BackupRunStatus.Queued,
                Trigger = BackupTrigger.Scheduled,
                RequestedAt = now,
            });
            queued++;
        }

        await db.SaveChangesAsync(cancellationToken);
        if (queued > 0)
            worker.Wake();
        return queued;
    }
}

public static class BackupServiceCollectionExtensions
{
    public static IServiceCollection AddBackups(this IServiceCollection services)
    {
        services.AddSingleton<BackupEvents>();
        services.AddSingleton<BackupGate>();
        services.AddSingleton<BackupPlanner>();
        services.AddSingleton<IBackupExtra, BackupDataExtra>();
        services.AddSingleton<IBackupExtra, BackupServerExtra>();
        services.AddSingleton<BackupTargets>();
        services.AddSingleton<Targets.S3BackupTargets>();
        services.AddSingleton<PeerBackupTargets>();
        services.AddSingleton<PeerBackupStore>();
        services.AddSingleton<BackupWorker>();
        services.AddHostedService(provider => provider.GetRequiredService<BackupWorker>());
        services.AddSingleton<BackupScheduler>();
        services.AddHostedService(provider => provider.GetRequiredService<BackupScheduler>());
        services.AddScoped<BackupPeers>();
        services.AddScoped<BackupService>();
        return services;
    }
}
