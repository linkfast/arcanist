using System.IO.Compression;
using System.Text.Json;
using Arcanist.Core.Application;
using Arcanist.Core.Application.Backups;
using Arcanist.Core.Application.Libraries;
using Arcanist.Core.Backups.Targets;
using Arcanist.Core.Data;
using Arcanist.Core.Data.Entities;
using Arcanist.Core.Hosting;
using Arcanist.Core.Storage;
using Arcanist.Core.Storage.S3;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Arcanist.Core.Backups;

// Types in this file are part of the public API contracts (OpenAPI document): the setup wizard's restore.

/// <summary>Where the backups are, for the setup wizard: a folder of this server or an S3 bucket.</summary>
/// <param name="Path">For a folder: its full path on this server.</param>
public sealed record SetupRestoreSource(BackupDestinationKind Kind, string? Path, S3ConnectionInput? S3);

/// <summary>A snapshot that carries the server, and its password.</summary>
/// <param name="Folder">The backup's folder in the source.</param>
public sealed record SetupRestoreCheck(SetupRestoreSource Source, string Folder, string Snapshot, string Password);

/// <summary>Restores the server from a snapshot: its database, settings and keys; then, if asked, its music.</summary>
/// <param name="RestoreFiles">After the restart, also bring back the libraries' files that are missing.</param>
public sealed record SetupRestoreRequest(SetupRestoreSource Source, string Folder, string Snapshot, string Password, bool RestoreFiles);

/// <summary>Whether the password opens the snapshot's server data.</summary>
public sealed record SetupRestoreCheckResult(bool Success);

/// <summary>
/// Restoring a whole server from the setup wizard: the snapshot's encrypted database, <c>arcanist.json</c> and keys
/// are decrypted into <c>restore-pending/</c>, and the server restarts to install them before reading anything
/// (<see cref="PendingRestore"/>). Backups in another server cannot be reached before setup (there is no P2P node yet).
/// </summary>
internal sealed class ServerRestore(
    ILibraryStorageResolver storages,
    IConfiguration configuration,
    IHostApplicationLifetime lifetime,
    ArcanistPaths paths,
    ILogger<ServerRestore> logger)
{
    /// <summary>The backups that carry the server, with only those snapshots.</summary>
    public async Task<IReadOnlyList<StoredBackup>> BrowseAsync(SetupRestoreSource source, CancellationToken cancellationToken)
    {
        var (target, _) = Open(source);
        if (await target.ProbeAsync(cancellationToken) is { } problem)
            throw new ArcanistException.Conflict(problem);
        return (await BackupReader.FindAsync(target, cancellationToken))
            .Select(b => new StoredBackup(b.Folder, b.Manifest.Id, b.Manifest.Name, b.Manifest.Scope, b.Manifest.Machine, b.Manifest.ArcanistVersion,
                b.Manifest.UpdatedAt,
                b.Manifest.Snapshots.Where(s => s.Server).Select(s => new StoredSnapshot(s.Name, s.CreatedAt, s.Files, s.Bytes, s.UserData, s.Server)).ToList(),
                b.Manifest.Sources.Select(s => new StoredLibrary(s.Folder, s.Library, null)).ToList()))
            .Where(b => b.Snapshots.Count > 0)
            .OrderByDescending(b => b.UpdatedAt)
            .ToList();
    }

    public async Task<SetupRestoreCheckResult> CheckAsync(SetupRestoreCheck request, CancellationToken cancellationToken)
    {
        var (target, _) = Open(request.Source);
        await using var stream = await target.OpenReadAsync(await ServerFileAsync(target, request.Folder, request.Snapshot, cancellationToken), cancellationToken);
        return new SetupRestoreCheckResult(await BackupCrypto.CheckAsync(stream, request.Password ?? "", cancellationToken));
    }

    /// <summary>Decrypts the snapshot's server into <c>restore-pending/</c> and restarts to install it.</summary>
    public async Task StartAsync(SetupRestoreRequest request, CancellationToken cancellationToken)
    {
        var (target, location) = Open(request.Source);
        var manifest = await BackupReader.ManifestAsync(target, request.Folder, cancellationToken);
        var serverFile = await ServerFileAsync(target, request.Folder, request.Snapshot, cancellationToken);

        var pending = PendingRestore.Folder(paths);
        if (Directory.Exists(pending))
            Directory.Delete(pending, recursive: true);
        Directory.CreateDirectory(pending);
        var zipFile = Path.Combine(pending, "server.zip");
        try
        {
            await using (var input = await target.OpenReadAsync(serverFile, cancellationToken))
            await using (var output = File.Create(zipFile))
                await BackupCrypto.DecryptAsync(input, output, request.Password ?? "", cancellationToken);
            Extract(zipFile, pending);
            File.Delete(zipFile);

            var marker = new PendingRestoreRequest(
                request.RestoreFiles ? new PendingRestoreFiles(request.Source.Kind, location, request.Source.S3, request.Folder, request.Snapshot, manifest.Name) : null,
                DateTime.UtcNow);
            await File.WriteAllTextAsync(Path.Combine(pending, PendingRestore.MarkerFile), JsonSerializer.Serialize(marker, JsonSerializerOptions.Web), cancellationToken);
        }
        catch
        {
            Directory.Delete(pending, recursive: true);
            throw;
        }

        logger.LogWarning("The server restarts to restore {Backup} ({Snapshot})", manifest.Name, request.Snapshot);
        if (!configuration.GetValue("Arcanist:Hosting:StopAfterRestore", true))
            return;
        // Let the answer go out, then stop: supervisors start the server again (exit code 75).
        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(1));
            Environment.ExitCode = FactoryReset.RestartExitCode;
            lifetime.StopApplication();
        });
    }

    private static async Task<string> ServerFileAsync(IBackupTarget target, string folder, string snapshot, CancellationToken cancellationToken)
    {
        var manifest = await BackupReader.ManifestAsync(target, folder, cancellationToken);
        if (manifest.Snapshots.FirstOrDefault(s => s.Name == snapshot) is not { Server: true })
            throw new ArcanistException.Conflict(ErrorCodes.BackupNotFound);
        return BackupPaths.Join(folder, BackupFormat.Snapshots, snapshot, BackupServerExtra.ServerFile);
    }

    /// <summary>
    /// The wizard reads the source directly: S3 without the Cloud plan too (the account is linked afterwards), since
    /// these are the server's own backups.
    /// </summary>
    private (IBackupTarget Target, string Location) Open(SetupRestoreSource source)
    {
        switch (source.Kind)
        {
            case BackupDestinationKind.Folder:
                if (string.IsNullOrWhiteSpace(source.Path) || !Path.IsPathFullyQualified(source.Path.Trim()))
                    throw ArcanistException.Field("source.path", ErrorCodes.InvalidFormat);
                var folder = storages.For(LibrarySourceKind.Local).NormalizeLocation(source.Path);
                if (!Directory.Exists(folder))
                    throw ArcanistException.Field("source.path", ErrorCodes.NotFound);
                return (new FolderTarget(folder), folder);
            case BackupDestinationKind.S3:
                var (connection, prefix) = S3LibraryService.Validate(source.S3, null, "source.s3.");
                var storage = (S3Storage)storages.For(LibrarySourceKind.S3);
                return (new S3Target(storage.Client(connection), prefix), storage.NormalizeLocation($"{S3Storage.Scheme}{connection.Bucket}/{prefix}"));
            default:
                throw ArcanistException.Field("source.kind", ErrorCodes.InvalidFormat);
        }
    }

    /// <summary>Unzips the server's files, refusing anything but the database, the settings and the keys.</summary>
    private static void Extract(string zipFile, string folder)
    {
        using var zip = ZipFile.OpenRead(zipFile);
        foreach (var entry in zip.Entries)
        {
            var name = entry.FullName.Replace('\\', '/');
            if (name.EndsWith('/'))
                continue;
            var allowed = name is BackupServerExtra.DatabaseEntry or ArcanistPaths.SettingsFileName
                          || (name.StartsWith(BackupServerExtra.KeysEntry, StringComparison.Ordinal) && BackupPaths.IsSafe(name));
            if (!allowed)
                throw new ArcanistException.Conflict(ErrorCodes.BackupFormatUnsupported);
            var target = Path.GetFullPath(Path.Combine(folder, name.Replace('/', Path.DirectorySeparatorChar)));
            if (!target.StartsWith(Path.GetFullPath(folder) + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                throw new ArcanistException.Conflict(ErrorCodes.BackupFormatUnsupported);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target);
        }

        if (!File.Exists(Path.Combine(folder, BackupServerExtra.DatabaseEntry)) || !File.Exists(Path.Combine(folder, ArcanistPaths.SettingsFileName)))
            throw new ArcanistException.Conflict(ErrorCodes.BackupFormatUnsupported);
    }
}

/// <summary>What <c>restore-pending/restore.json</c> asks the next start to do.</summary>
/// <param name="Files">Bring the libraries' missing files back afterwards, from there.</param>
internal sealed record PendingRestoreRequest(PendingRestoreFiles? Files, DateTime RequestedAt);

/// <param name="S3">For S3: the connection, with its secret until the restored server protects it with its keys.</param>
internal sealed record PendingRestoreFiles(
    BackupDestinationKind Kind, string Location, S3ConnectionInput? S3, string Folder, string Snapshot, string BackupName);

/// <summary>
/// A server restore ends with a restart, and the files are installed at the next start, before anything reads them:
/// the database (as SQLite), <c>arcanist.json</c> and the keys replace what the setup wizard left. The server then
/// starts as the one that was backed up, with its accounts, settings and libraries.
/// </summary>
public static class PendingRestore
{
    public const string MarkerFile = "restore.json";
    internal const string FilesMarker = "restore-files.json";

    public static string Folder(ArcanistPaths paths) => Path.Combine(paths.Root, "restore-pending");

    /// <summary>Installs a restore left by the previous run. Called at startup, before the settings are read.</summary>
    public static void ApplyIfRequested(ArcanistPaths paths, Action<string>? log = null)
    {
        var pending = Folder(paths);
        var marker = Path.Combine(pending, MarkerFile);
        if (!File.Exists(marker))
            return;

        PendingRestoreRequest? request;
        try
        {
            request = JsonSerializer.Deserialize<PendingRestoreRequest>(File.ReadAllText(marker), JsonSerializerOptions.Web);
        }
        catch (JsonException)
        {
            request = null;
        }

        var database = Path.Combine(paths.Root, "arcanist.db");
        foreach (var file in new[] { database, database + "-wal", database + "-shm" })
        {
            if (File.Exists(file))
                File.Delete(file);
        }

        File.Move(Path.Combine(pending, BackupServerExtra.DatabaseEntry), database);
        if (Directory.Exists(paths.Keys))
            Directory.Delete(paths.Keys, recursive: true);
        if (Directory.Exists(Path.Combine(pending, "keys")))
            Directory.Move(Path.Combine(pending, "keys"), paths.Keys);
        File.Move(Path.Combine(pending, ArcanistPaths.SettingsFileName), paths.SettingsFile, overwrite: true);
        if (Directory.Exists(paths.Cache))
            Directory.Delete(paths.Cache, recursive: true);
        if (request?.Files is { } files)
            File.WriteAllText(Path.Combine(paths.Root, FilesMarker), JsonSerializer.Serialize(files, JsonSerializerOptions.Web));

        Directory.Delete(pending, recursive: true);
        log?.Invoke("Server restored from a backup: it starts as the server that was backed up.");
    }
}

/// <summary>After a server restore that asked for it, queues bringing the libraries' missing files back.</summary>
internal sealed class PendingRestoreFilesStarter(
    IArcanistDbContextFactory databases,
    ILibraryStorageResolver storages,
    RestoreWorker worker,
    IOptionsMonitor<Setup.SetupOptions> setup,
    ArcanistPaths paths,
    TimeProvider time,
    ILogger<PendingRestoreFilesStarter> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var marker = Path.Combine(paths.Root, PendingRestore.FilesMarker);
        if (!File.Exists(marker) || !databases.IsConfigured || !setup.CurrentValue.Completed)
            return;
        try
        {
            var files = JsonSerializer.Deserialize<PendingRestoreFiles>(await File.ReadAllTextAsync(marker, stoppingToken), JsonSerializerOptions.Web)!;
            await using var db = databases.CreateDbContext();
            var destination = await db.BackupDestinations.FirstOrDefaultAsync(d => d.Kind == files.Kind && d.Location == files.Location, stoppingToken);
            if (destination is null)
            {
                destination = new BackupDestination
                {
                    Name = files.BackupName,
                    Kind = files.Kind,
                    Location = files.Location,
                    Settings = files.Kind == BackupDestinationKind.S3 && files.S3 is { } s3
                        ? S3LibraryService.SettingsJson((S3Storage)storages.For(LibrarySourceKind.S3), S3LibraryService.Validate(s3, null).Connection)
                        : null,
                    CreatedAt = time.GetUtcNow().UtcDateTime,
                };
                db.BackupDestinations.Add(destination);
            }

            db.RestoreJobs.Add(new RestoreJob
            {
                DestinationId = destination.Id,
                BackupFolder = files.Folder,
                BackupName = files.BackupName,
                Snapshot = files.Snapshot,
                Files = true,
                UserData = false,
                Paths = "[]",
                UserMap = "{}",
                Status = RestoreStatus.Queued,
                RequestedAt = time.GetUtcNow().UtcDateTime,
            });
            await db.SaveChangesAsync(stoppingToken);
            File.Delete(marker);
            worker.Wake();
            logger.LogInformation("Bringing back the libraries' files from {Backup}", files.BackupName);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            logger.LogError(e, "Could not queue the restore of the libraries' files");
        }
    }
}
