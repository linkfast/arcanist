using Arcanist.Core.Data;
using Arcanist.Core.Data.Entities;
using Arcanist.Core.Scanning;
using Arcanist.Core.Setup;
using Arcanist.Core.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Arcanist.Core.Organization;

/// <summary>Wakes the drop folder worker and reports what it is doing (for the admin panel and tests).</summary>
public sealed class ImportMonitor
{
    private readonly SemaphoreSlim _signal = new(0, 1);
    private long _runs;
    private int _unsettled;

    /// <summary>Passes over the drop folders completed so far.</summary>
    public long Runs => Interlocked.Read(ref _runs);

    /// <summary>Files that were still changing at the last pass.</summary>
    public int Unsettled => Volatile.Read(ref _unsettled);

    /// <summary>Asks for a pass now (a file changed, or an administrator acted on an item).</summary>
    public void RequestRun()
    {
        try
        {
            _signal.Release();
        }
        catch (SemaphoreFullException)
        {
            // A pass is already requested.
        }
    }

    internal Task<bool> WaitAsync(TimeSpan timeout, CancellationToken cancellationToken) => _signal.WaitAsync(timeout, cancellationToken);

    internal void Completed(int unsettled)
    {
        Volatile.Write(ref _unsettled, unsettled);
        Interlocked.Increment(ref _runs);
    }
}

/// <summary>
/// Imports what is dropped into drop folders. Watches them (and lists them every
/// <see cref="OrganizationOptions.PollSeconds"/>, since watching misses changes on network shares), waits until a
/// folder's files stop changing, imports them, then scans the library so they appear at once. Also empties old trash
/// and forgets old history.
/// </summary>
internal sealed class DropFolderWorker(
    IArcanistDbContextFactory databases,
    ILibraryStorageResolver storages,
    DropFolderImporter importer,
    ScanCoordinator scans,
    ImportMonitor monitor,
    IOptionsMonitor<SetupOptions> setup,
    IOptions<OrganizationOptions> options,
    TimeProvider time,
    DatabaseMaintenance maintenance,
    IServiceProvider services,
    ILogger<DropFolderWorker> logger) : BackgroundService
{
    /// <summary>Imported and discarded items are kept this long as history.</summary>
    public static readonly TimeSpan HistoryRetention = TimeSpan.FromDays(30);

    /// <summary>Organizing runs kept per library, to undo them.</summary>
    public const int RunsKept = 20;

    private static readonly TimeSpan MaintenanceInterval = TimeSpan.FromHours(1);
    private static readonly TimeSpan ModifiedTolerance = TimeSpan.FromSeconds(2);

    private readonly Dictionary<(Guid Folder, string Path), (long Size, DateTime ModifiedAt, DateTimeOffset Since)> _observed = [];
    private readonly Dictionary<Guid, (string Path, IDisposable? Watch)> _watches = [];
    private DateTimeOffset _maintained = DateTimeOffset.MinValue;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();
        while (!stoppingToken.IsCancellationRequested)
        {
            var wait = TimeSpan.FromSeconds(Math.Max(1, options.Value.PollSeconds));
            if (setup.CurrentValue.Completed && databases.IsConfigured)
            {
                try
                {
                    using (await maintenance.WorkAsync(stoppingToken))
                    {
                        var unsettled = await RunOnceAsync(stoppingToken);
                        if (unsettled > 0)
                            wait = TimeSpan.FromSeconds(Math.Clamp(options.Value.StableSeconds, 1, options.Value.PollSeconds));
                        await MaintainAsync(stoppingToken);
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception e)
                {
                    logger.LogError(e, "Importing from drop folders failed");
                    monitor.Completed(0);
                }
            }

            try
            {
                await monitor.WaitAsync(wait, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>One pass over every drop folder. Returns how many files are still changing.</summary>
    internal async Task<int> RunOnceAsync(CancellationToken cancellationToken)
    {
        await using var db = databases.CreateDbContext();
        var folders = await db.DropFolders.AsNoTracking().Include(d => d.LibrarySource).Where(d => d.Enabled).ToListAsync(cancellationToken);
        RefreshWatches(folders);

        var unsettled = 0;
        var libraries = new HashSet<Guid>();
        foreach (var folder in folders)
        {
            var (imported, changing) = await ProcessAsync(db, folder, cancellationToken);
            unsettled += changing;
            if (imported)
                libraries.Add(folder.LibrarySource.LibraryId);
        }

        // Scanned before the next pass, so the library knows what was just imported when checking for duplicates.
        foreach (var libraryId in libraries)
        {
            var job = await scans.EnqueueAsync(libraryId, ScanKind.Incremental, requestedBy: null, cancellationToken);
            await WaitForScanAsync(job.Id, cancellationToken);
        }

        monitor.Completed(unsettled);
        return unsettled;
    }

    private async Task<(bool Imported, int Unsettled)> ProcessAsync(ArcanistDbContext db, DropFolder folder, CancellationToken cancellationToken)
    {
        var storage = storages.For(LibrarySourceKind.Local);
        var location = new StorageLocation(LibrarySourceKind.Local, folder.Path);
        if (!(await storage.ProbeAsync(location, cancellationToken)).IsAvailable)
            return (false, 0);

        var entries = new List<StorageEntry>();
        try
        {
            await foreach (var entry in storage.EnumerateAsync(location, cancellationToken))
                entries.Add(entry);
        }
        catch (IOException e)
        {
            logger.LogWarning(e, "Listing drop folder {Path} failed", folder.Path);
            return (false, 0);
        }

        // A folder (and everything below it) is imported once all of it stopped changing: a multi-disc album
        // copied in arrives whole.
        var now = time.GetUtcNow();
        var stable = TimeSpan.FromSeconds(options.Value.StableSeconds);
        var unsettledGroups = new HashSet<string>(StringComparer.Ordinal);
        var present = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            present.Add(entry.RelativePath);
            var key = (folder.Id, entry.RelativePath);
            if (!_observed.TryGetValue(key, out var seen) || seen.Size != entry.Size || seen.ModifiedAt != entry.ModifiedAt)
                _observed[key] = seen = (entry.Size, entry.ModifiedAt, now);
            if (stable > TimeSpan.Zero && now - seen.Since < stable)
                unsettledGroups.Add(Group(entry.RelativePath));
        }

        foreach (var key in _observed.Keys.Where(k => k.Folder == folder.Id && !present.Contains(k.Path)).ToList())
            _observed.Remove(key);

        // Copying, an imported file stays: it is not imported again unless it changes.
        var copying = DropFolderImporter.KeepsFiles(folder);
        var items = await db.ImportItems
            .Where(i => i.DropFolderId == folder.Id && (i.Status != ImportStatus.Imported || copying) && i.Status != ImportStatus.Discarded)
            .ToListAsync(cancellationToken);

        // Items whose file is gone (deleted or moved away by hand) are forgotten.
        var gone = items.Where(i => !present.Contains(i.RelativePath)).ToList();
        if (gone.Count > 0)
        {
            db.ImportItems.RemoveRange(gone);
            await db.SaveChangesAsync(cancellationToken);
        }

        var byPath = items.Where(i => present.Contains(i.RelativePath)).GroupBy(i => i.RelativePath, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(i => i.UpdatedAt).First(), StringComparer.Ordinal);
        var ready = entries
            .Where(e => AudioFormats.IsAudio(e.RelativePath) && !unsettledGroups.Contains(Group(e.RelativePath)))
            .Where(e => !byPath.TryGetValue(e.RelativePath, out var item)
                        || item.Status == ImportStatus.Pending
                        // Waiting for an administrator, unless the file changed (re-tagged by hand, replaced).
                        || item.Size != e.Size || (item.ModifiedAt - e.ModifiedAt).Duration() > ModifiedTolerance)
            .Select(e => e.RelativePath)
            .ToList();

        var unsettled = entries.Count(e => unsettledGroups.Contains(Group(e.RelativePath)));
        if (ready.Count == 0)
            return (false, unsettled);

        logger.LogInformation("Importing {Count} files from {DropFolder}", ready.Count, folder.Path);
        var imported = await importer.ImportAsync(folder.Id, entries, ready, cancellationToken);
        // Cloud Drop learns at once what became of its songs (off Cloud Drop, or kept there).
        if (folder.Kind == DropFolderKind.UltimateCloudDrop)
            services.GetService<Cloud.CloudDropWorker>()?.Wake();
        return (imported, unsettled);
    }

    /// <summary>Files directly in the drop folder are each their own group; everything else groups by top folder.</summary>
    private static string Group(string relativePath)
    {
        var slash = relativePath.IndexOf('/');
        return slash < 0 ? relativePath : relativePath[..slash];
    }

    private async Task WaitForScanAsync(Guid jobId, CancellationToken cancellationToken)
    {
        while (true)
        {
            await using var db = databases.CreateDbContext();
            var status = await db.ScanJobs.Where(j => j.Id == jobId).Select(j => (ScanStatus?)j.Status).SingleOrDefaultAsync(cancellationToken);
            if (status is not (ScanStatus.Queued or ScanStatus.Running))
                return;
            await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken);
        }
    }

    private void RefreshWatches(List<DropFolder> folders)
    {
        foreach (var (id, watch) in _watches.Where(w => folders.All(f => f.Id != w.Key || f.Path != w.Value.Path)).ToList())
        {
            watch.Watch?.Dispose();
            _watches.Remove(id);
        }

        foreach (var folder in folders.Where(f => !_watches.ContainsKey(f.Id)))
        {
            var watch = storages.For(LibrarySourceKind.Local).Watch(new StorageLocation(LibrarySourceKind.Local, folder.Path), monitor.RequestRun);
            _watches[folder.Id] = (folder.Path, watch);
        }
    }

    /// <summary>Hourly: empties trash older than the retention, and forgets old history.</summary>
    private async Task MaintainAsync(CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        if (now - _maintained < MaintenanceInterval)
            return;
        _maintained = now;

        await using var db = databases.CreateDbContext();
        var purgeBefore = now.UtcDateTime.AddDays(-options.Value.TrashRetentionDays);
        var locations = (await db.DropFolders.AsNoTracking().Select(d => d.Path).ToListAsync(cancellationToken))
            .Select(path => new StorageLocation(LibrarySourceKind.Local, path))
            .Concat((await db.LibrarySources.AsNoTracking().ToListAsync(cancellationToken)).Select(StorageLocation.From));
        foreach (var location in locations)
        {
            try
            {
                var storage = storages.For(location.Kind);
                if ((await storage.ProbeAsync(location, cancellationToken)).IsAvailable)
                    await storage.PurgeTrashAsync(location, purgeBefore, cancellationToken);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(e, "Emptying the trash of {Location} failed", location.Location);
            }
        }

        var historyBefore = now.UtcDateTime - HistoryRetention;
        await db.ImportItems
            .Where(i => (i.Status == ImportStatus.Imported || i.Status == ImportStatus.Discarded) && i.UpdatedAt < historyBefore
                        // What a copying folder imported is remembered while it is there, or it would be imported again.
                        && !(i.Status == ImportStatus.Imported && i.DropFolder.Mode == DropFolderMode.Copy && i.DropFolder.Kind == DropFolderKind.Folder))
            .ExecuteDeleteAsync(cancellationToken);

        foreach (var libraryId in await db.OrganizeRuns.Select(r => r.LibraryId).Distinct().ToListAsync(cancellationToken))
        {
            var old = await db.OrganizeRuns
                .Where(r => r.LibraryId == libraryId && r.Status != OrganizeRunStatus.Running && r.Status != OrganizeRunStatus.Undoing)
                .OrderByDescending(r => r.StartedAt).Skip(RunsKept).Select(r => r.Id)
                .ToListAsync(cancellationToken);
            await db.OrganizeRuns.Where(r => old.Contains(r.Id)).ExecuteDeleteAsync(cancellationToken);
        }
    }

    public override void Dispose()
    {
        foreach (var watch in _watches.Values)
            watch.Watch?.Dispose();
        base.Dispose();
    }
}
