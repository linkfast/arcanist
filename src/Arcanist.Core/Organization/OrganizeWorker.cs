using System.Collections.Concurrent;
using System.Threading.Channels;
using Arcanist.Core.Application;
using Arcanist.Core.Data;
using Arcanist.Core.Data.Entities;
using Arcanist.Core.Scanning;
using Arcanist.Core.Setup;
using Arcanist.Core.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Arcanist.Core.Organization;

/// <summary>Organizing runs and undos requested from the admin panel, carried out in the background.</summary>
public sealed class OrganizeQueue
{
    private readonly Channel<(Guid RunId, bool Undo)> _queue = Channel.CreateUnbounded<(Guid, bool)>();

    public void Enqueue(Guid runId, bool undo) => _queue.Writer.TryWrite((runId, undo));

    internal ChannelReader<(Guid RunId, bool Undo)> Reader => _queue.Reader;
}

/// <summary>
/// Runs queued organizing work one item at a time. On startup, a run the server stopped in the middle of is
/// reported as interrupted (its recorded moves can still be undone), and an undo is resumed.
/// </summary>
internal sealed class OrganizeWorker(
    OrganizeQueue queue,
    LibraryOrganizer organizer,
    IArcanistDbContextFactory databases,
    IOptions<SetupOptions> setup,
    TimeProvider time,
    ILogger<OrganizeWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();
        if (setup.Value.Completed && databases.IsConfigured)
            await RecoverAsync(stoppingToken);

        await foreach (var (runId, undo) in queue.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                if (undo)
                {
                    await organizer.UndoAsync(runId, stoppingToken);
                    continue;
                }

                await using var db = databases.CreateDbContext();
                var libraryId = await db.OrganizeRuns.Where(r => r.Id == runId).Select(r => (Guid?)r.LibraryId).SingleOrDefaultAsync(stoppingToken);
                if (libraryId is not null)
                    await organizer.RunAsync(libraryId.Value, OrganizeRunKind.Manual, runId, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception e)
            {
                logger.LogError(e, "Organizing run {Run} crashed", runId);
            }
        }
    }

    private async Task RecoverAsync(CancellationToken cancellationToken)
    {
        await using var db = databases.CreateDbContext();
        foreach (var run in await db.OrganizeRuns.Where(r => r.Status == OrganizeRunStatus.Running).ToListAsync(cancellationToken))
        {
            run.Status = OrganizeRunStatus.Failed;
            run.ErrorCode = ErrorCodes.Interrupted;
            run.FinishedAt ??= time.GetUtcNow().UtcDateTime;
        }

        await db.SaveChangesAsync(cancellationToken);

        foreach (var runId in await db.OrganizeRuns.Where(r => r.Status == OrganizeRunStatus.Undoing).Select(r => r.Id).ToListAsync(cancellationToken))
            queue.Enqueue(runId, undo: true);
    }
}

/// <summary>
/// Watches the locations of libraries that organize themselves, and scans a library once its files stop changing,
/// so files copied into it are organized without waiting for the next scheduled scan. Changes Arcanist makes itself
/// are ignored. Network shares often report nothing: their libraries rely on scheduled scans.
/// </summary>
internal sealed class LibraryWatcher(
    IArcanistDbContextFactory databases,
    ILibraryStorageResolver storages,
    ScanCoordinator scans,
    LibraryLocks locks,
    IOptionsMonitor<SetupOptions> setup,
    IOptions<OrganizationOptions> options,
    TimeProvider time,
    ILogger<LibraryWatcher> logger) : BackgroundService
{
    private static readonly TimeSpan Tick = TimeSpan.FromSeconds(1);

    private readonly Dictionary<Guid, (Guid LibraryId, string Location, IDisposable? Watch)> _watches = [];
    private readonly ConcurrentDictionary<Guid, DateTimeOffset> _changed = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var refreshed = DateTimeOffset.MinValue;
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                if (setup.CurrentValue.Completed && databases.IsConfigured)
                {
                    try
                    {
                        if (time.GetUtcNow() - refreshed >= TimeSpan.FromSeconds(options.Value.PollSeconds))
                        {
                            await RefreshAsync(stoppingToken);
                            refreshed = time.GetUtcNow();
                        }

                        await ScanSettledAsync(stoppingToken);
                    }
                    catch (Exception e) when (e is not OperationCanceledException)
                    {
                        logger.LogError(e, "Watching libraries failed");
                    }
                }

                await Task.Delay(Tick, time, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    /// <summary>Watches the sources of libraries that organize themselves, and stops watching the others.</summary>
    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        await using var db = databases.CreateDbContext();
        var sources = await db.LibrarySources.AsNoTracking().Where(s => s.Library.AutoOrganize).ToListAsync(cancellationToken);

        foreach (var (sourceId, watch) in _watches.Where(w => sources.All(s => s.Id != w.Key || s.Location != w.Value.Location)).ToList())
        {
            watch.Watch?.Dispose();
            _watches.Remove(sourceId);
        }

        foreach (var source in sources.Where(s => !_watches.ContainsKey(s.Id)))
        {
            var libraryId = source.LibraryId;
            var watch = storages.For(source.Kind).Watch(StorageLocation.From(source), () =>
            {
                if (!locks.IsWriting(libraryId))
                    _changed[libraryId] = time.GetUtcNow();
            });
            _watches[source.Id] = (libraryId, source.Location, watch);
        }
    }

    private async Task ScanSettledAsync(CancellationToken cancellationToken)
    {
        var quiet = TimeSpan.FromSeconds(options.Value.StableSeconds);
        foreach (var (libraryId, changedAt) in _changed)
        {
            if (time.GetUtcNow() - changedAt < quiet || !_changed.TryRemove(new KeyValuePair<Guid, DateTimeOffset>(libraryId, changedAt)))
                continue;

            logger.LogInformation("Files changed in library {Library}; scanning it", libraryId);
            await scans.EnqueueAsync(libraryId, ScanKind.Incremental, requestedBy: null, cancellationToken);
        }
    }

    public override void Dispose()
    {
        foreach (var watch in _watches.Values)
            watch.Watch?.Dispose();
        base.Dispose();
    }
}
