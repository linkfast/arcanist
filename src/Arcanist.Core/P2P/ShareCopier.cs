using System.Collections.Concurrent;
using System.Threading.Channels;
using Arcanist.Core.Application;
using Arcanist.Core.Data;
using Arcanist.Core.Data.Entities;
using Arcanist.Core.Hosting;
using Arcanist.Core.Organization;
using Arcanist.Core.Scanning;
using Arcanist.Core.Storage;
using Microsoft.EntityFrameworkCore;

namespace Arcanist.Core.P2P;

public enum ShareCopyStatus
{
    Queued,
    Running,
    Completed,
    Failed,
}

/// <summary>A copy of a received share into a library of this server, as it goes.</summary>
/// <param name="Skipped">Files already there (copied before, for example).</param>
public sealed record ShareCopyProgress(ShareCopyStatus Status, int Total, int Copied, int Skipped, string? ErrorCode);

/// <summary>
/// Copies the files of a share received from another server into a folder of one of this server's libraries, when
/// the owner allows downloads: the originals arrive through the P2P network one at a time, keep their paths (under
/// a folder named after the shared library) and never overwrite anything. The library is scanned afterwards.
/// Copies run one at a time and are not resumed after a restart; starting again skips what was copied.
/// </summary>
public sealed class ShareCopier(
    IArcanistDbContextFactory databases,
    ILibraryStorageResolver storages,
    ScanCoordinator scans,
    LibraryLocks locks,
    ArcanistPaths paths,
    DatabaseMaintenance maintenance,
    ILogger<ShareCopier> logger) : BackgroundService
{
    private readonly Channel<(Guid RemoteId, Guid SourceId)> _queue = Channel.CreateUnbounded<(Guid, Guid)>();
    private readonly ConcurrentDictionary<Guid, ShareCopyProgress> _progress = new();

    public ShareCopyProgress? Progress(Guid remoteId) => _progress.GetValueOrDefault(remoteId);

    /// <summary>A copy is queued or running.</summary>
    public bool IsBusy => _progress.Values.Any(p => p.Status is ShareCopyStatus.Queued or ShareCopyStatus.Running);

    /// <returns>False when a copy of that share is already queued or running.</returns>
    public bool Enqueue(Guid remoteId, Guid targetSourceId)
    {
        if (_progress.TryGetValue(remoteId, out var current) && current.Status is ShareCopyStatus.Queued or ShareCopyStatus.Running)
            return false;
        _progress[remoteId] = new ShareCopyProgress(ShareCopyStatus.Queued, 0, 0, 0, null);
        _queue.Writer.TryWrite((remoteId, targetSourceId));
        return true;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var (remoteId, sourceId) in _queue.Reader.ReadAllAsync(stoppingToken))
            {
                try
                {
                    using var work = await maintenance.WorkAsync(stoppingToken);
                    await CopyAsync(remoteId, sourceId, stoppingToken);
                }
                catch (Exception e) when (e is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
                {
                    logger.LogWarning(e, "Copying share {Remote} failed", remoteId);
                    var last = _progress.GetValueOrDefault(remoteId) ?? new ShareCopyProgress(ShareCopyStatus.Failed, 0, 0, 0, null);
                    _progress[remoteId] = last with
                    {
                        Status = ShareCopyStatus.Failed,
                        ErrorCode = e is IOException { Message: ErrorCodes.DownloadNotAllowed } ? ErrorCodes.DownloadNotAllowed : ErrorCodes.PeerUnreachable,
                    };
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    private async Task CopyAsync(Guid remoteId, Guid sourceId, CancellationToken cancellationToken)
    {
        await using var db = databases.CreateDbContext();
        var remote = await db.P2PRemotes.AsNoTracking().Include(r => r.Library).ThenInclude(l => l.Sources).SingleAsync(r => r.Id == remoteId, cancellationToken);
        var target = await db.LibrarySources.AsNoTracking().SingleAsync(s => s.Id == sourceId, cancellationToken);
        var shared = StorageLocation.From(remote.Library.Sources.Single(s => s.Kind == LibrarySourceKind.Peer));
        var peerStorage = storages.For(LibrarySourceKind.Peer);
        var targetStorage = storages.For(target.Kind);
        var location = StorageLocation.From(target);

        var files = new List<string>();
        await foreach (var entry in peerStorage.EnumerateAsync(shared, cancellationToken))
            files.Add(entry.RelativePath);
        _progress[remoteId] = new ShareCopyProgress(ShareCopyStatus.Running, files.Count, 0, 0, null);

        var temp = Path.Combine(paths.Cache, "p2p-copy");
        Directory.CreateDirectory(temp);
        int copied = 0, skipped = 0;
        using (await locks.AcquireForWritingAsync(target.LibraryId, cancellationToken))
        {
            foreach (var path in files)
            {
                if (await targetStorage.ExistsAsync(location, path, cancellationToken))
                {
                    skipped++;
                }
                else
                {
                    var file = Path.Combine(temp, Guid.NewGuid().ToString("N") + Path.GetExtension(path));
                    try
                    {
                        await using (var input = await peerStorage.OpenReadAsync(shared, path, cancellationToken))
                        await using (var output = File.Create(file))
                            await input.CopyToAsync(output, cancellationToken);
                        await targetStorage.ImportAsync(location, path, file, cancellationToken);
                        copied++;
                    }
                    catch (StorageConflictException)
                    {
                        skipped++;
                    }
                    finally
                    {
                        File.Delete(file);
                    }
                }

                _progress[remoteId] = new ShareCopyProgress(ShareCopyStatus.Running, files.Count, copied, skipped, null);
            }
        }

        // Queued before the copy shows as done, so whoever waits for it then finds the scan.
        await scans.EnqueueAsync(target.LibraryId, ScanKind.Incremental, null, CancellationToken.None);
        _progress[remoteId] = new ShareCopyProgress(ShareCopyStatus.Completed, files.Count, copied, skipped, null);
        logger.LogInformation("Copied share {Remote} into {Location}: {Copied} copied, {Skipped} already there", remote.Name, target.Location, copied, skipped);
    }
}
