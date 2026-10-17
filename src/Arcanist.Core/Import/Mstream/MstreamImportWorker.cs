using System.Security.Cryptography;
using System.Text.Json;
using Arcanist.Core.Application;
using Arcanist.Core.Auth;
using Arcanist.Core.Data;
using Arcanist.Core.Data.Entities;
using Arcanist.Core.Hosting;
using Arcanist.Core.Organization;
using Arcanist.Core.Scanning;
using Arcanist.Core.Storage;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Arcanist.Core.Import.Mstream;

/// <summary>Wakes the import worker when an import is created.</summary>
public sealed class MstreamImportSignal
{
    private readonly SemaphoreSlim _signal = new(0);

    public void Notify()
    {
        if (_signal.CurrentCount == 0)
            _signal.Release();
    }

    public Task WaitAsync(TimeSpan timeout, CancellationToken cancellationToken) => _signal.WaitAsync(timeout, cancellationToken);
}

/// <summary>
/// The importing account's data, kept in the import until the scans let it find the files, and while some files can
/// be copied again (it is applied again then, which is safe).
/// </summary>
/// <param name="Unmatched">What was not found the last time it was applied, to count it once.</param>
internal sealed record MstreamPendingData(Guid UserId, MstreamUserContent Content, int Unmatched = 0);

/// <summary>
/// Finishes imports from mStream, one at a time. From a server, it first copies the files of the chosen vpaths into
/// their library folders (at the same paths, never overwriting; one file at a time, each step a short unit of work);
/// then, once the scans of the libraries are over, so mStream's files can be found in the catalog, it brings the users'
/// playlists, ratings and plays: all of them from a folder, the importing account's from a server. Imports
/// interrupted by a restart go on where they were, which is safe.
/// </summary>
internal sealed class MstreamImportWorker(
    IArcanistDbContextFactory databases,
    MstreamImporter importer,
    MstreamServerClient client,
    MstreamImportSignal signal,
    ILibraryStorageResolver storages,
    LibraryLocks locks,
    ScanCoordinator scans,
    ArcanistPaths paths,
    IDataProtectionProvider protection,
    TimeProvider time,
    IOptionsMonitor<Setup.SetupOptions> setup,
    DatabaseMaintenance maintenance,
    ILogger<MstreamImportWorker> logger) : BackgroundService
{
    private const int ScanEvery = 500;
    private const int Batch = 50;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (await NextAsync(stoppingToken) is { } next)
                {
                    bool finished;
                    if (next.Status == MstreamImportStatus.Copying)
                    {
                        await CopyAsync(next.Id, stoppingToken);
                        finished = true;
                    }
                    else
                    {
                        using (await maintenance.WorkAsync(stoppingToken))
                            finished = await BringDataAsync(next.Id, stoppingToken);
                    }

                    if (finished)
                        continue;
                }

                // Nothing to do yet: check again soon (scans finishing are not signalled).
                await signal.WaitAsync(TimeSpan.FromSeconds(2), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception e)
            {
                logger.LogError(e, "mStream import failed");
                await Task.Delay(TimeSpan.FromSeconds(30), time, stoppingToken).ContinueWith(_ => { }, TaskScheduler.Default);
            }
        }
    }

    private async Task<(Guid Id, MstreamImportStatus Status)?> NextAsync(CancellationToken cancellationToken)
    {
        // Before setup there is no database yet, and while it runs the database is being created.
        if (!databases.IsConfigured || !setup.CurrentValue.Completed)
            return null;
        using (await maintenance.WorkAsync(cancellationToken))
        {
            await using var db = databases.CreateDbContext();
            var next = await db.MstreamImports
                .Where(i => i.Status == MstreamImportStatus.Copying || i.Status == MstreamImportStatus.Waiting || i.Status == MstreamImportStatus.Running)
                .OrderBy(i => i.CreatedAt)
                .Select(i => new { i.Id, i.Status })
                .FirstOrDefaultAsync(cancellationToken);
            return next is null ? null : (next.Id, next.Status);
        }
    }

    /// <returns>Whether the import's data was brought (false while its libraries are still to be scanned).</returns>
    private async Task<bool> BringDataAsync(Guid importId, CancellationToken cancellationToken)
    {
        await using var db = databases.CreateDbContext();
        var import = await db.MstreamImports.SingleAsync(i => i.Id == importId, cancellationToken);
        var vpaths = JsonSerializer.Deserialize<Dictionary<string, Guid>>(import.VpathMap) ?? [];
        var sources = vpaths.Values.ToList();
        var libraries = await db.LibrarySources.Where(s => sources.Contains(s.Id)).Select(s => s.LibraryId).Distinct().ToListAsync(cancellationToken);
        var jobs = await db.ScanJobs.Where(j => libraries.Contains(j.LibraryId)).Select(j => new { j.LibraryId, j.Status }).ToListAsync(cancellationToken);
        // Every library is scanned when it is created (setup queues those scans just after recording its import), and
        // scan jobs are kept: a library without any has its first scan still to come.
        if (jobs.Any(j => j.Status is ScanStatus.Queued or ScanStatus.Running) || libraries.Any(id => jobs.All(j => j.LibraryId != id)))
            return false;

        import.Status = MstreamImportStatus.Running;
        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Importing mStream user data from {Source}", import.Source);

        try
        {
            int playlists = 0, ratings = 0, plays = 0, unmatched = 0;
            var contents = new List<(Guid UserId, MstreamUserContent Content)>();
            var pending = import.PendingData is null ? null : JsonSerializer.Deserialize<MstreamPendingData>(import.PendingData);
            if (import.Kind == MstreamImportKind.Folder)
            {
                var data = MstreamFolder.Read(import.Source);
                unmatched = data.Unresolved;
                var users = JsonSerializer.Deserialize<Dictionary<string, Guid>>(import.UserMap) ?? [];
                contents.AddRange(data.Content.Where(c => users.ContainsKey(c.Key)).Select(c => (users[c.Key], c.Value)));
            }
            else if (pending is not null)
            {
                contents.Add((pending.UserId, pending.Content));
                // What it did not find last time is counted again now.
                unmatched -= pending.Unmatched;
            }

            foreach (var (userId, content) in contents)
            {
                if (!await db.Users.AnyAsync(u => u.Id == userId, cancellationToken))
                    continue;
                var counts = await importer.ApplyAsync(db, userId, content, vpaths, cancellationToken);
                (playlists, ratings, plays, unmatched) = (playlists + counts.Playlists, ratings + counts.Ratings, plays + counts.Plays, unmatched + counts.Unmatched);
            }

            (import.Playlists, import.Ratings, import.Plays, import.Unmatched) =
                (import.Playlists + playlists, import.Ratings + ratings, import.Plays + plays, import.Unmatched + unmatched);
            import.Status = MstreamImportStatus.Completed;
            // The session and the data are kept only while some files can be tried again.
            if (import.FilesFailed == 0)
            {
                import.Session = null;
                import.PendingData = null;
            }
            else if (pending is not null)
            {
                import.PendingData = JsonSerializer.Serialize(pending with { Unmatched = unmatched + pending.Unmatched });
            }
        }
        catch (ArcanistException e)
        {
            // The installation was moved or changed since it was analyzed.
            import.Status = MstreamImportStatus.Failed;
            import.ErrorCode = e is ArcanistException.Invalid invalid ? invalid.Errors.ToDictionary().Values.First()[0] : e.Code;
        }

        import.FinishedAt = time.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("mStream import from {Source} {Status}: {Playlists} playlists, {Ratings} ratings, {Plays} played tracks, {Unmatched} not found",
            import.Source, import.Status, import.Playlists, import.Ratings, import.Plays, import.Unmatched);
        return true;
    }

    /// <summary>Copies the files of the chosen vpaths from the mStream server, then lets the import wait for the scans.</summary>
    private async Task CopyAsync(Guid importId, CancellationToken cancellationToken)
    {
        MstreamImport import;
        using (await maintenance.WorkAsync(cancellationToken))
        {
            await using var db = databases.CreateDbContext();
            import = await db.MstreamImports.AsNoTracking().SingleAsync(i => i.Id == importId, cancellationToken);
        }

        var copied = new HashSet<Guid>();
        try
        {
            if (MstreamServerClient.ParseUrl(import.Source) is not { } server || import.Session is null || import.CopyMap is null)
                throw new ArcanistException.Conflict(ErrorCodes.MstreamSessionExpired);
            string token;
            try
            {
                token = protection.CreateProtector(Application.Import.MstreamImportService.SessionPurpose).Unprotect(import.Session);
            }
            catch (CryptographicException)
            {
                throw new ArcanistException.Conflict(ErrorCodes.MstreamSessionExpired);
            }

            var map = JsonSerializer.Deserialize<Dictionary<string, Guid>>(import.CopyMap) ?? [];
            await ListAsync(import, server, token, map.Keys, cancellationToken);

            var targets = new Dictionary<Guid, CopyTarget>();
            using (await maintenance.WorkAsync(cancellationToken))
            {
                await using var db = databases.CreateDbContext();
                foreach (var sourceId in map.Values.Distinct())
                {
                    var source = await db.LibrarySources.AsNoTracking().SingleOrDefaultAsync(s => s.Id == sourceId, cancellationToken)
                                 ?? throw new ArcanistException.Conflict(ErrorCodes.NotFound);
                    targets[sourceId] = new CopyTarget(source.LibraryId, storages.For(source.Kind), StorageLocation.From(source));
                }
            }

            foreach (var target in targets.Values)
            {
                if (!await target.Storage.CanWriteAsync(target.Location, cancellationToken))
                    throw new ArcanistException.Conflict(ErrorCodes.ReadOnly);
            }

            var writeFailures = 0;
            var sinceScan = 0;
            while (true)
            {
                List<MstreamImportFile> files;
                using (await maintenance.WorkAsync(cancellationToken))
                {
                    await using var db = databases.CreateDbContext();
                    files = await db.MstreamImportFiles.AsNoTracking()
                        .Where(f => f.ImportId == importId && f.Status == MstreamImportFileStatus.Pending)
                        .OrderBy(f => f.Vpath).ThenBy(f => f.Path).ThenBy(f => f.Id)
                        .Take(Batch)
                        .ToListAsync(cancellationToken);
                }

                if (files.Count == 0)
                    break;

                foreach (var file in files)
                {
                    var target = targets[map[file.Vpath]];
                    var (status, reason, bytes) = await CopyOneAsync(server, token, file, target, cancellationToken);
                    writeFailures = reason == ErrorCodes.WriteFailed ? writeFailures + 1 : 0;
                    await SettleAsync(file, status, reason, bytes, cancellationToken);
                    if (status == MstreamImportFileStatus.Copied)
                    {
                        copied.Add(target.LibraryId);
                        if (++sinceScan >= ScanEvery)
                        {
                            // Long copies show up in the library as they go.
                            sinceScan = 0;
                            using (await maintenance.WorkAsync(cancellationToken))
                                await scans.EnqueueAsync(target.LibraryId, ScanKind.Incremental, import.RequestedByUserId, cancellationToken);
                        }
                    }

                    if (writeFailures >= 5)
                        throw new ArcanistException.Conflict(ErrorCodes.WriteFailed);
                }
            }

            await EndCopyAsync(importId, copied, null, cancellationToken);
            logger.LogInformation("Copied the files of {Source}", import.Source);
        }
        catch (ArcanistException e)
        {
            logger.LogWarning("Copying the files of {Source} failed: {Error}", import.Source, e.Code);
            await EndCopyAsync(importId, copied, e.Code, CancellationToken.None);
        }
        finally
        {
            DeleteTemporaryFiles();
        }
    }

    /// <summary>Records the server's files of each vpath to copy, the first time (a restart goes on with them).</summary>
    private async Task ListAsync(MstreamImport import, Uri server, string token, IEnumerable<string> vpaths, CancellationToken cancellationToken)
    {
        using (await maintenance.WorkAsync(cancellationToken))
        {
            await using var db = databases.CreateDbContext();
            if (await db.MstreamImportFiles.AnyAsync(f => f.ImportId == import.Id, cancellationToken))
                return;
        }

        var files = new List<MstreamImportFile>();
        foreach (var vpath in vpaths)
        {
            var prefix = vpath + "/";
            foreach (var path in (await client.ListFilesAsync(server, token, vpath, cancellationToken)).Distinct(StringComparer.Ordinal))
            {
                if (path.StartsWith(prefix, StringComparison.Ordinal) && path.Length > prefix.Length)
                    files.Add(new MstreamImportFile { ImportId = import.Id, Vpath = vpath, Path = path[prefix.Length..] });
            }
        }

        using (await maintenance.WorkAsync(cancellationToken))
        {
            await using var db = databases.CreateDbContext();
            foreach (var chunk in files.Chunk(1000))
            {
                db.MstreamImportFiles.AddRange(chunk);
                await db.SaveChangesAsync(cancellationToken);
                db.ChangeTracker.Clear();
            }

            await db.MstreamImports.Where(i => i.Id == import.Id).ExecuteUpdateAsync(s => s.SetProperty(i => i.FilesTotal, files.Count), cancellationToken);
        }
    }

    private async Task<(MstreamImportFileStatus Status, string? Reason, long Bytes)> CopyOneAsync(
        Uri server, string token, MstreamImportFile file, CopyTarget target, CancellationToken cancellationToken)
    {
        // Never overwritten: a file already at that place is kept (the scan reads it like the others).
        if (await target.Storage.ExistsAsync(target.Location, file.Path, cancellationToken))
            return (MstreamImportFileStatus.Present, null, 0);

        var temporary = TemporaryPath(Path.GetExtension(file.Path));
        try
        {
            switch (await client.DownloadAsync(server, token, $"{file.Vpath}/{file.Path}", temporary, cancellationToken))
            {
                case MstreamDownload.Refused:
                    throw new ArcanistException.Conflict(ErrorCodes.MstreamSessionExpired);
                case MstreamDownload.Failed:
                    return (MstreamImportFileStatus.Failed, ErrorCodes.DownloadFailed, 0);
            }

            var length = new FileInfo(temporary).Length;
            // The library's lock first: a scan may hold it for a while, and maintenance must not wait for that.
            using (await locks.AcquireForWritingAsync(target.LibraryId, cancellationToken))
            using (await maintenance.WorkAsync(cancellationToken))
            {
                try
                {
                    await target.Storage.ImportAsync(target.Location, file.Path, temporary, cancellationToken);
                }
                catch (StorageConflictException)
                {
                    return (MstreamImportFileStatus.Present, null, 0);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
                {
                    logger.LogWarning(e, "Could not write {Path} from mStream into the library", file.Path);
                    return (MstreamImportFileStatus.Failed, ErrorCodes.WriteFailed, 0);
                }
            }

            return (MstreamImportFileStatus.Copied, null, length);
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    private async Task SettleAsync(MstreamImportFile file, MstreamImportFileStatus status, string? reason, long bytes, CancellationToken cancellationToken)
    {
        using (await maintenance.WorkAsync(cancellationToken))
        {
            await using var db = databases.CreateDbContext();
            await db.MstreamImportFiles.Where(f => f.Id == file.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(f => f.Status, status).SetProperty(f => f.Reason, reason), cancellationToken);
            await db.MstreamImports.Where(i => i.Id == file.ImportId).ExecuteUpdateAsync(s => s
                .SetProperty(i => i.FilesCopied, i => i.FilesCopied + (status == MstreamImportFileStatus.Copied ? 1 : 0))
                .SetProperty(i => i.FilesPresent, i => i.FilesPresent + (status == MstreamImportFileStatus.Present ? 1 : 0))
                .SetProperty(i => i.FilesFailed, i => i.FilesFailed + (status == MstreamImportFileStatus.Failed ? 1 : 0))
                .SetProperty(i => i.CopiedBytes, i => i.CopiedBytes + bytes), cancellationToken);
        }
    }

    /// <summary>
    /// Scans the libraries that got files (before anyone sees the copy over) and lets the import wait for those scans;
    /// a failure ends it, keeping the session so it can be tried again.
    /// </summary>
    private async Task EndCopyAsync(Guid importId, IReadOnlySet<Guid> copied, string? errorCode, CancellationToken cancellationToken)
    {
        using (await maintenance.WorkAsync(cancellationToken))
        {
            await using var db = databases.CreateDbContext();
            var import = await db.MstreamImports.SingleAsync(i => i.Id == importId, cancellationToken);
            foreach (var libraryId in copied)
                await scans.EnqueueAsync(libraryId, ScanKind.Incremental, import.RequestedByUserId, cancellationToken);
            if (errorCode is null)
            {
                import.Status = MstreamImportStatus.Waiting;
            }
            else
            {
                import.Status = MstreamImportStatus.Failed;
                import.ErrorCode = errorCode;
                import.FinishedAt = time.GetUtcNow().UtcDateTime;
            }

            await db.SaveChangesAsync(cancellationToken);
        }
    }

    private string TemporaryPath(string extension)
    {
        var folder = Path.Combine(paths.Cache, "import");
        Directory.CreateDirectory(folder);
        return Path.Combine(folder, $"mstream-{Guid.NewGuid():N}{extension}");
    }

    /// <summary>Leftovers of a stop in the middle of a download.</summary>
    private void DeleteTemporaryFiles()
    {
        try
        {
            var folder = Path.Combine(paths.Cache, "import");
            if (Directory.Exists(folder))
            {
                foreach (var file in Directory.EnumerateFiles(folder, "mstream-*"))
                    File.Delete(file);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            logger.LogDebug(e, "Could not delete the temporary files of mStream imports");
        }
    }

    private sealed record CopyTarget(Guid LibraryId, ILibraryStorage Storage, StorageLocation Location);
}

/// <summary>
/// Signs in users imported from an mStream server through its API, whose password hashes could not be read: the
/// first time, the password is checked with that server, kept as the Arcanist password, and the user's playlists,
/// ratings and plays are brought in the background.
/// </summary>
internal sealed class MstreamServerSignIn(
    IArcanistDbContextFactory databases,
    MstreamServerClient client,
    MstreamImporter importer,
    DatabaseMaintenance maintenance,
    ILogger<MstreamServerSignIn> logger) : IExternalSignIn
{
    public async Task<bool> VerifyAsync(User user, string password, CancellationToken cancellationToken)
    {
        if (MstreamPasswords.ServerImport(user.PasswordHash) is not { } importId)
            return false;

        await using var db = databases.CreateDbContext();
        var import = await db.MstreamImports.AsNoTracking().SingleOrDefaultAsync(i => i.Id == importId, cancellationToken);
        if (import is null || MstreamServerClient.ParseUrl(import.Source) is not { } server)
            return false;

        string? token;
        try
        {
            token = await client.LoginAsync(server, user.UserName, password, cancellationToken);
        }
        catch (ArcanistException)
        {
            logger.LogWarning("mStream server {Server} did not answer to sign in {User}", import.Source, user.UserName);
            return false;
        }

        if (token is null)
            return false;

        var vpaths = JsonSerializer.Deserialize<Dictionary<string, Guid>>(import.VpathMap) ?? [];
        Pending = Task.Run(() => BringDataAsync(import.Id, user.Id, server, token, vpaths));
        return true;
    }

    /// <summary>The last data transfer started, for tests to wait on.</summary>
    internal Task Pending { get; private set; } = Task.CompletedTask;

    private async Task BringDataAsync(Guid importId, Guid userId, Uri server, string token, Dictionary<string, Guid> vpaths)
    {
        try
        {
            var content = await client.ReadUserAsync(server, token, CancellationToken.None);
            using var work = await maintenance.WorkAsync(CancellationToken.None);
            await using var db = databases.CreateDbContext();
            var counts = await importer.ApplyAsync(db, userId, content, vpaths, CancellationToken.None);
            await db.MstreamImports.Where(i => i.Id == importId).ExecuteUpdateAsync(s => s
                .SetProperty(i => i.Playlists, i => i.Playlists + counts.Playlists)
                .SetProperty(i => i.Ratings, i => i.Ratings + counts.Ratings)
                .SetProperty(i => i.Plays, i => i.Plays + counts.Plays)
                .SetProperty(i => i.Unmatched, i => i.Unmatched + counts.Unmatched));
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Could not bring the mStream data of user {UserId}", userId);
        }
    }
}

public static class MstreamImportServiceCollectionExtensions
{
    public static IServiceCollection AddMstreamImport(this IServiceCollection services)
    {
        services.AddSingleton<MstreamImportSignal>();
        services.AddSingleton<MstreamImporter>();
        services.AddSingleton<MstreamServerClient>();
        services.AddSingleton<MstreamReader>();
        services.AddSingleton<IExternalSignIn, MstreamServerSignIn>();
        services.AddHostedService<MstreamImportWorker>();
        services.AddScoped<Application.Import.MstreamImportService>();
        services.AddHttpClient(MstreamServerClient.HttpClientName, client => client.Timeout = TimeSpan.FromSeconds(60));
        services.AddHttpClient(MstreamServerClient.DownloadClientName, client => client.Timeout = TimeSpan.FromHours(2));
        return services;
    }
}
