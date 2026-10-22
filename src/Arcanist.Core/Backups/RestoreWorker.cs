using System.Security.Cryptography;
using System.Text.Json;
using Arcanist.Core.Application;
using Arcanist.Core.Application.Backups;
using Arcanist.Core.Application.Catalog;
using Arcanist.Core.Backups.Targets;
using Arcanist.Core.Data;
using Arcanist.Core.Data.Entities;
using Arcanist.Core.Data.Search;
using Arcanist.Core.Hosting;
using Arcanist.Core.Organization;
using Arcanist.Core.Scanning;
using Arcanist.Core.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Arcanist.Core.Backups;

/// <summary>
/// Restores snapshots, one at a time and never while a backup runs (<see cref="BackupGate"/>). Files go into library
/// folders without overwriting anything: one already there and the same is skipped, a different one stays and the
/// restored file gets another name. Then, once the libraries are scanned, the user data: ratings fill gaps, play
/// counts keep the highest, plays are added once, and playlists are created when their owner has none of that name.
/// Restores interrupted by a restart start their step again (what was done is found done).
/// </summary>
internal sealed class RestoreWorker(
    IArcanistDbContextFactory databases,
    BackupTargets targets,
    RestoreEvents events,
    BackupGate gate,
    ILibraryStorageResolver storages,
    LibraryLocks locks,
    ScanCoordinator scans,
    SettingsStore settings,
    DatabaseMaintenance maintenance,
    IOptionsMonitor<Setup.SetupOptions> setup,
    ArcanistPaths paths,
    TimeProvider time,
    ILogger<RestoreWorker> logger) : BackgroundService
{
    private const int MaxFailures = 50;
    private static readonly TimeSpan SaveEvery = TimeSpan.FromSeconds(1);

    private readonly SemaphoreSlim _wake = new(0);
    private readonly Lock _lock = new();
    private (Guid Id, CancellationTokenSource Cancel)? _running;

    private static readonly RestoreStatus[] Active = [RestoreStatus.Queued, RestoreStatus.Copying, RestoreStatus.WaitingForScans, RestoreStatus.Applying];

    public void Wake()
    {
        if (_wake.CurrentCount == 0)
            _wake.Release();
    }

    public bool Cancel(Guid jobId)
    {
        lock (_lock)
        {
            if (_running is not { } running || running.Id != jobId)
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
                if (await NextAsync(stoppingToken) is { } jobId)
                {
                    using (await gate.EnterAsync(stoppingToken))
                        await RunAsync(jobId, stoppingToken);
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
                logger.LogError(e, "Restores failed");
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
            return await db.RestoreJobs.Where(j => Active.Contains(j.Status)).OrderBy(j => j.RequestedAt).Select(j => (Guid?)j.Id).FirstOrDefaultAsync(cancellationToken);
        }
    }

    private async Task RunAsync(Guid jobId, CancellationToken stoppingToken)
    {
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        lock (_lock)
            _running = (jobId, cancel);
        var failures = new List<BackupJobFailure>();
        RestoreJob job;
        BackupDestination destination;
        using (await maintenance.WorkAsync(cancel.Token))
        {
            await using var db = databases.CreateDbContext();
            job = await db.RestoreJobs.AsNoTracking().SingleAsync(j => j.Id == jobId, cancel.Token);
            destination = await db.BackupDestinations.AsNoTracking().SingleAsync(d => d.Id == job.DestinationId, cancel.Token);
        }

        try
        {
            job.StartedAt ??= Now;
            var target = targets.Open(destination);
            if (await target.ProbeAsync(cancel.Token) is { } problem)
                throw new ArcanistException.Conflict(problem);
            var manifest = await BackupReader.ManifestAsync(target, job.BackupFolder, cancel.Token);
            var index = await BackupReader.IndexAsync(target, job.BackupFolder, manifest, job.Snapshot, cancel.Token);

            if (job.Files && job.Status is RestoreStatus.Queued or RestoreStatus.Copying)
            {
                job.Status = RestoreStatus.Copying;
                await SaveAsync(job, failures, cancel.Token);
                await CopyAsync(job, target, manifest, index, failures, cancel.Token);
            }

            if (job.UserData)
            {
                if (job.Status is RestoreStatus.Queued or RestoreStatus.Copying or RestoreStatus.WaitingForScans)
                {
                    job.Status = RestoreStatus.WaitingForScans;
                    await SaveAsync(job, failures, cancel.Token);
                    await WaitForScansAsync(job, cancel.Token);
                }

                job.Status = RestoreStatus.Applying;
                await SaveAsync(job, failures, cancel.Token);
                await ApplyAsync(job, target, cancel.Token);
            }

            await FinishAsync(job, failures, job.FilesFailed > 0 ? RestoreStatus.CompletedWithErrors : RestoreStatus.Completed, null);
            logger.LogInformation("Restored {Snapshot} of {Backup}: {Restored} files, {Ratings} ratings, {Playlists} playlists",
                job.Snapshot, job.BackupName, job.Restored, job.Ratings, job.Playlists);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Goes on after the restart.
        }
        catch (OperationCanceledException)
        {
            await FinishAsync(job, failures, RestoreStatus.Cancelled, null);
        }
        catch (ArcanistException e)
        {
            logger.LogWarning("Restore of {Backup} failed: {Error}", job.BackupName, e.Code);
            await FinishAsync(job, failures, RestoreStatus.Failed, e.Code);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            logger.LogError(e, "Restore of {Backup} failed", job.BackupName);
            await FinishAsync(job, failures, RestoreStatus.Failed, ErrorCodes.BackupFailed);
        }
        finally
        {
            lock (_lock)
                _running = null;
            DeleteTemporaryFiles();
        }
    }

    // ── Files ───────────────────────────────────────────────────────────────

    private async Task CopyAsync(
        RestoreJob job, IBackupTarget target, BackupManifest manifest, BackupIndex index, List<BackupJobFailure> failures, CancellationToken cancellationToken)
    {
        var selected = JsonSerializer.Deserialize<List<string>>(job.Paths, JsonSerializerOptions.Web) ?? [];
        var entries = index.Files.Where(f => selected.Count == 0 || selected.Any(p => f.Path == p || f.Path.StartsWith(p.TrimEnd('/') + "/", StringComparison.Ordinal)))
            .OrderBy(f => f.Path, StringComparer.Ordinal)
            .ToList();
        job.FilesTotal = entries.Count;
        var located = await BackupReader.LocateAsync(target, job.BackupFolder, manifest, index, cancellationToken);

        // Where each library folder of the backup goes: one chosen folder, or back where it came from.
        var sources = new Dictionary<string, LibrarySource?>(StringComparer.Ordinal);
        await using (var db = databases.CreateDbContext())
        {
            if (job.TargetSourceId is { } targetId)
            {
                var chosen = await db.LibrarySources.AsNoTracking().SingleOrDefaultAsync(s => s.Id == targetId, cancellationToken)
                             ?? throw new ArcanistException.Conflict(ErrorCodes.NotFound);
                foreach (var folder in entries.Select(Top).Distinct())
                    sources[folder] = chosen;
            }
            else
            {
                var ids = manifest.Sources.Select(s => s.SourceId).ToList();
                var existing = await db.LibrarySources.AsNoTracking().Where(s => ids.Contains(s.Id)).ToDictionaryAsync(s => s.Id, cancellationToken);
                foreach (var source in manifest.Sources)
                    sources[source.Folder] = existing.GetValueOrDefault(source.SourceId);
            }
        }

        // Several libraries into one folder keep apart, each in a folder named after it.
        var keepLibraryFolder = job.TargetSourceId is not null && entries.Select(Top).Distinct().Count() > 1;
        var language = await settings.GetAsync<string?>(SettingKeys.DefaultLanguage, null, cancellationToken);
        var suffix = language?.StartsWith("es", StringComparison.OrdinalIgnoreCase) == true ? "restaurado" : "restored";
        var day = time.GetUtcNow().UtcDateTime.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        var libraries = new HashSet<Guid>(JsonSerializer.Deserialize<List<Guid>>(job.Libraries, JsonSerializerOptions.Web) ?? []);
        var lastSave = time.GetTimestamp();
        job.Restored = job.Identical = job.Renamed = job.FilesFailed = 0;

        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var top = Top(entry);
            if (sources.GetValueOrDefault(top) is not { } source || !BackupPlanner.IsBackedUp(source.Kind))
            {
                Fail(job, failures, entry.Path, ErrorCodes.NotFound);
                continue;
            }

            var relative = keepLibraryFolder ? entry.Path : entry.Path[(top.Length + 1)..];
            try
            {
                await RestoreFileAsync(job, target, located[entry.Path], entry, source, relative, suffix, day, cancellationToken);
                libraries.Add(source.LibraryId);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or HttpRequestException or ArcanistException)
            {
                logger.LogWarning("Could not restore {Path}: {Error}", entry.Path, e.Message);
                Fail(job, failures, entry.Path, e is ArcanistException a ? a.Code : ErrorCodes.WriteFailed);
            }

            if (time.GetElapsedTime(lastSave) >= SaveEvery)
            {
                lastSave = time.GetTimestamp();
                await SaveAsync(job, failures, cancellationToken);
            }
        }

        job.Libraries = JsonSerializer.Serialize(libraries, JsonSerializerOptions.Web);
        using (await maintenance.WorkAsync(cancellationToken))
        {
            foreach (var library in libraries)
                await scans.EnqueueAsync(library, ScanKind.Incremental, job.RequestedByUserId, cancellationToken);
        }

        await SaveAsync(job, failures, cancellationToken);
    }

    private async Task RestoreFileAsync(
        RestoreJob job, IBackupTarget target, string located, BackupIndexEntry entry, LibrarySource source, string relative,
        string suffix, string day, CancellationToken cancellationToken)
    {
        var storage = storages.For(source.Kind);
        var location = StorageLocation.From(source);
        var path = relative;
        var renamed = false;
        if (await storage.ExistsAsync(location, path, cancellationToken))
        {
            string existing;
            await using (var stream = await storage.OpenReadAsync(location, path, cancellationToken))
                existing = Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, cancellationToken));
            if (existing == entry.Sha256)
            {
                job.Identical++;
                return;
            }

            // Never overwritten: the restored file takes another name beside it.
            var stem = AudioFormats.WithoutExtension(relative);
            var extension = relative[stem.Length..];
            path = $"{stem} ({suffix} {day}){extension}";
            for (var copy = 2; await storage.ExistsAsync(location, path, cancellationToken); copy++)
                path = $"{stem} ({suffix} {day} {copy}){extension}";
            renamed = true;
        }

        var folder = Path.Combine(paths.Cache, "restore");
        Directory.CreateDirectory(folder);
        var temporary = Path.Combine(folder, $"{Guid.NewGuid():N}{Path.GetExtension(relative)}");
        try
        {
            await using (var input = await target.OpenReadAsync(located, cancellationToken))
            await using (var hashing = new HashingStream(input))
            await using (var output = File.Create(temporary))
            {
                await hashing.CopyToAsync(output, cancellationToken);
                if (hashing.Hash() != entry.Sha256)
                    throw new ArcanistException.Conflict(ErrorCodes.BackupFormatUnsupported);
            }

            // With its time, so scans and the next backup see it as it was.
            File.SetLastWriteTimeUtc(temporary, entry.Modified);
            using (await locks.AcquireForWritingAsync(source.LibraryId, cancellationToken))
            using (await maintenance.WorkAsync(cancellationToken))
                await storage.ImportAsync(location, path, temporary, cancellationToken);
            job.Restored++;
            if (renamed)
                job.Renamed++;
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    private static string Top(BackupIndexEntry entry) => entry.Path[..Math.Max(0, entry.Path.IndexOf('/'))];

    private static void Fail(RestoreJob job, List<BackupJobFailure> failures, string path, string code)
    {
        job.FilesFailed++;
        if (failures.Count < MaxFailures)
            failures.Add(new BackupJobFailure(path, code));
    }

    private async Task WaitForScansAsync(RestoreJob job, CancellationToken cancellationToken)
    {
        var libraries = JsonSerializer.Deserialize<List<Guid>>(job.Libraries, JsonSerializerOptions.Web) ?? [];
        if (libraries.Count == 0)
            return;
        while (true)
        {
            await using (var db = databases.CreateDbContext())
            {
                if (!await db.ScanJobs.AnyAsync(j => libraries.Contains(j.LibraryId) && (j.Status == ScanStatus.Queued || j.Status == ScanStatus.Running), cancellationToken))
                    return;
            }

            await Task.Delay(TimeSpan.FromSeconds(1), time, cancellationToken);
        }
    }

    // ── User data ───────────────────────────────────────────────────────────

    private async Task ApplyAsync(RestoreJob job, IBackupTarget target, CancellationToken cancellationToken)
    {
        var catalog = await BackupReader.SnapshotFileAsync<BackupCatalog>(target, job.BackupFolder, job.Snapshot, BackupDataExtra.CatalogFile, cancellationToken);
        var userData = await BackupReader.SnapshotFileAsync<BackupUserData>(target, job.BackupFolder, job.Snapshot, BackupDataExtra.UserDataFile, cancellationToken);
        if (catalog is null || userData is null)
            return;

        Dictionary<Guid, Guid> matches;
        await using (var db = databases.CreateDbContext())
            matches = await UserDataApplier.MatchAsync(db, catalog, cancellationToken);
        var map = JsonSerializer.Deserialize<Dictionary<string, Guid?>>(job.UserMap, JsonSerializerOptions.Web) ?? [];
        Dictionary<string, Guid> local;
        await using (var db = databases.CreateDbContext())
            local = (await db.Users.AsNoTracking().Select(u => new { u.Id, u.NormalizedUserName }).ToListAsync(cancellationToken))
                .ToDictionary(u => u.NormalizedUserName, u => u.Id);

        Guid? UserOf(string name) =>
            map.TryGetValue(name, out var chosen) ? chosen : local.TryGetValue(TextNormalizer.Normalize(name), out var same) ? same : null;

        var counts = new UserDataCounts();
        foreach (var user in userData.Users)
        {
            if (UserOf(user.UserName) is not { } userId)
                continue;
            using (await maintenance.WorkAsync(cancellationToken))
            {
                await using var db = databases.CreateDbContext();
                await UserDataApplier.ApplyUserAsync(db, counts, user, userId, matches, UserOf, "backup:", "backup", cancellationToken);
                await db.SaveChangesAsync(cancellationToken);
            }
        }

        (job.Ratings, job.PlayCounts, job.Plays, job.Playlists, job.Unmatched) = (counts.Ratings, counts.PlayCounts, counts.Plays, counts.Playlists, counts.Unmatched);
    }

    // ── State ───────────────────────────────────────────────────────────────

    private async Task SaveAsync(RestoreJob job, List<BackupJobFailure> failures, CancellationToken cancellationToken)
    {
        job.Failures = JsonSerializer.Serialize(failures, JsonSerializerOptions.Web);
        using (await maintenance.WorkAsync(cancellationToken))
        {
            await using var db = databases.CreateDbContext();
            db.RestoreJobs.Update(job);
            await db.SaveChangesAsync(cancellationToken);
        }

        events.Publish(RestoreService.ToModel(job));
    }

    private async Task FinishAsync(RestoreJob job, List<BackupJobFailure> failures, RestoreStatus status, string? errorCode)
    {
        job.Status = status;
        job.ErrorCode = errorCode;
        job.FinishedAt = Now;
        await SaveAsync(job, failures, CancellationToken.None);
    }

    private void DeleteTemporaryFiles()
    {
        try
        {
            var folder = Path.Combine(paths.Cache, "restore");
            if (Directory.Exists(folder))
                Directory.Delete(folder, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            logger.LogDebug(e, "Could not delete the temporary files of restores");
        }
    }

    private DateTime Now => time.GetUtcNow().UtcDateTime;
}
