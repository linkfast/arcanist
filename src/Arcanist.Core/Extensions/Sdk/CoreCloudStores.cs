using System.Text;
using System.Text.Json;
using Arcanist.Core.Data;
using Arcanist.Core.Data.Entities;
using Arcanist.Core.P2P;
using Arcanist.Extensibility;
using Microsoft.EntityFrameworkCore;

namespace Arcanist.Core.Extensions.Sdk;

/// <summary>What a database write of the stores needs: its own context, inside a unit of work (maintenance waits for it).</summary>
internal abstract class CoreStore(IArcanistDbContextFactory databases, DatabaseMaintenance maintenance, TimeProvider time)
{
    protected async Task<T> ReadAsync<T>(Func<ArcanistDbContext, Task<T>> read)
    {
        await using var db = databases.CreateDbContext();
        return await read(db);
    }

    protected async Task<T> WriteAsync<T>(Func<ArcanistDbContext, Task<T>> write, CancellationToken cancellationToken)
    {
        using (await maintenance.WorkAsync(cancellationToken))
        {
            await using var db = databases.CreateDbContext();
            return await write(db);
        }
    }

    protected DateTime Now() => time.GetUtcNow().UtcDateTime;
}

/// <summary>The libraries' syncs with Ultimate Cloud, as the server keeps them (<see cref="ICloudSyncStore"/>).</summary>
internal sealed class CoreCloudSyncStore(IArcanistDbContextFactory databases, DatabaseMaintenance maintenance, TimeProvider time)
    : CoreStore(databases, maintenance, time), ICloudSyncStore
{
    public Task<CloudLibrarySync?> NextDueAsync(TimeSpan interval, CancellationToken cancellationToken)
    {
        var due = Now() - interval;
        return ReadAsync(async db => (await db.LibraryCloudSyncs.AsNoTracking()
            .Where(s => s.Mode != CloudSyncMode.Off && (s.Requested || s.LastRunAt == null || s.LastRunAt < due))
            .OrderByDescending(s => s.Requested).ThenBy(s => s.LastRunAt)
            .FirstOrDefaultAsync(cancellationToken)) is { } sync ? ToModel(sync) : null);
    }

    public Task<CloudLibrarySync?> GetAsync(Guid libraryId, CancellationToken cancellationToken) =>
        ReadAsync(async db => await db.LibraryCloudSyncs.AsNoTracking().SingleOrDefaultAsync(s => s.LibraryId == libraryId, cancellationToken) is { } sync
            ? ToModel(sync)
            : null);

    public Task StartRunAsync(Guid libraryId, CancellationToken cancellationToken) =>
        WriteAsync(db => db.LibraryCloudSyncs.Where(s => s.LibraryId == libraryId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Requested, false), cancellationToken), cancellationToken);

    public Task FinishRunAsync(Guid libraryId, string? errorCode, CancellationToken cancellationToken)
    {
        var now = Now();
        return WriteAsync(db => db.LibraryCloudSyncs.Where(s => s.LibraryId == libraryId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.LastRunAt, now).SetProperty(x => x.ErrorCode, errorCode), cancellationToken), cancellationToken);
    }

    public Task<IReadOnlyList<CloudPairing>> PairingsAsync(Guid libraryId, CancellationToken cancellationToken) =>
        ReadAsync<IReadOnlyList<CloudPairing>>(async db =>
            (await db.CloudSyncItems.AsNoTracking().Where(i => i.LibraryId == libraryId).ToListAsync(cancellationToken)).Select(ToModel).ToList());

    public Task<CloudPairing?> PairingAsync(Guid libraryId, string contentHash, CancellationToken cancellationToken) =>
        ReadAsync(async db => await db.CloudSyncItems.AsNoTracking().FirstOrDefaultAsync(i => i.LibraryId == libraryId && i.ContentHash == contentHash, cancellationToken)
            is { } item ? ToModel(item) : null);

    public Task SaveAsync(CloudPairing pairing, CancellationToken cancellationToken) =>
        WriteAsync(async db =>
        {
            var item = await db.CloudSyncItems.SingleOrDefaultAsync(i => i.Id == pairing.Id, cancellationToken);
            if (item is null)
            {
                item = new CloudSyncItem { Id = pairing.Id, LibraryId = pairing.LibraryId, Title = pairing.Title };
                db.CloudSyncItems.Add(item);
            }

            item.ContentHash = pairing.ContentHash;
            item.AudioHash = pairing.AudioHash;
            item.CloudTrackId = pairing.CloudTrackId;
            item.Path = pairing.Path;
            item.MatchKey = pairing.MatchKey;
            item.Title = pairing.Title.Length > 500 ? pairing.Title[..500] : pairing.Title;
            item.Status = (CloudSyncItemStatus)(int)pairing.State;
            item.Reason = pairing.Reason;
            item.UpdatedAt = Now();
            return await db.SaveChangesAsync(cancellationToken);
        }, cancellationToken);

    public Task DeleteAsync(Guid pairingId, CancellationToken cancellationToken) =>
        WriteAsync(db => db.CloudSyncItems.Where(i => i.Id == pairingId).ExecuteDeleteAsync(cancellationToken), cancellationToken);

    private static CloudLibrarySync ToModel(LibraryCloudSync sync) =>
        new(sync.LibraryId, (CloudSyncDirection)(int)sync.Mode, sync.AccountUserId, sync.DownloadSourceId, sync.Requested, sync.LastRunAt);

    private static CloudPairing ToModel(CloudSyncItem item) => new()
    {
        Id = item.Id,
        LibraryId = item.LibraryId,
        ContentHash = item.ContentHash,
        AudioHash = item.AudioHash,
        CloudTrackId = item.CloudTrackId,
        Path = item.Path,
        MatchKey = item.MatchKey,
        Title = item.Title,
        State = (CloudPairingState)(int)item.Status,
        Reason = item.Reason,
    };
}

/// <summary>Users' uploads to their own Ultimate Cloud, as the server keeps them (<see cref="IMyCloudUploadQueue"/>).</summary>
internal sealed class CoreMyCloudUploadQueue(IArcanistDbContextFactory databases, DatabaseMaintenance maintenance, TimeProvider time)
    : CoreStore(databases, maintenance, time), IMyCloudUploadQueue
{
    public Task RequeueInterruptedAsync(CancellationToken cancellationToken) =>
        WriteAsync(db => db.UserUploads.Where(u => u.Kind == UserUploadKind.UltimateCloud && u.Status == UserUploadStatus.Uploading)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.Status, UserUploadStatus.Queued), cancellationToken), cancellationToken);

    public Task<MyCloudUpload?> NextAsync(CancellationToken cancellationToken) =>
        ReadAsync(db => db.UserUploads.AsNoTracking()
            .Where(u => u.Kind == UserUploadKind.UltimateCloud && u.Status == UserUploadStatus.Queued)
            .OrderBy(u => u.CreatedAt).ThenBy(u => u.Id)
            .Select(u => new MyCloudUpload(u.Id, u.UserId, u.FileId, u.Name))
            .FirstOrDefaultAsync(cancellationToken));

    public Task SetAsync(Guid uploadId, MyCloudUploadState state, string? errorCode, string? cloudTrackId, CancellationToken cancellationToken)
    {
        var now = Now();
        var status = state switch
        {
            MyCloudUploadState.Queued => UserUploadStatus.Queued,
            MyCloudUploadState.Uploading => UserUploadStatus.Uploading,
            MyCloudUploadState.Uploaded => UserUploadStatus.Uploaded,
            MyCloudUploadState.AlreadyInCloud => UserUploadStatus.AlreadyInCloud,
            _ => UserUploadStatus.Failed,
        };
        return WriteAsync(db => db.UserUploads.Where(u => u.Id == uploadId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(u => u.Status, status)
                .SetProperty(u => u.ErrorCode, errorCode)
                .SetProperty(u => u.CloudTrackId, u => cloudTrackId ?? u.CloudTrackId)
                .SetProperty(u => u.UpdatedAt, now), cancellationToken), cancellationToken);
    }

    public Task FailQueuedAsync(Guid userId, string errorCode, CancellationToken cancellationToken)
    {
        var now = Now();
        return WriteAsync(db => db.UserUploads.Where(u => u.UserId == userId && u.Kind == UserUploadKind.UltimateCloud && u.Status == UserUploadStatus.Queued)
            .ExecuteUpdateAsync(s => s
                .SetProperty(u => u.Status, UserUploadStatus.Failed)
                .SetProperty(u => u.ErrorCode, errorCode)
                .SetProperty(u => u.UpdatedAt, now), cancellationToken), cancellationToken);
    }
}

/// <summary>Cloud Drop's songs waiting to go to the account's library (<see cref="ICloudDropUploads"/>).</summary>
internal sealed class CoreCloudDropUploads(IArcanistDbContextFactory databases, DatabaseMaintenance maintenance, TimeProvider time)
    : CoreStore(databases, maintenance, time), ICloudDropUploads
{
    public Task<IReadOnlyList<CloudDropUploadCandidate>> WaitingAsync(int max, CancellationToken cancellationToken) =>
        ReadAsync<IReadOnlyList<CloudDropUploadCandidate>>(async db =>
        {
            var waiting = await db.CloudDropFetches.AsNoTracking()
                .Where(f => f.DropFolder.UploadToCloud && f.DropFolder.Enabled
                            && (f.Upload == CloudDropUpload.Waiting || f.Upload == CloudDropUpload.Failed))
                .OrderBy(f => f.UpdatedAt)
                .Take(max)
                .Select(f => new { f.Id, f.DropFolderId, f.DropFolder.UploadAccount, f.Title, f.TrackId, f.LibrarySourceId, f.LibraryPath })
                .ToListAsync(cancellationToken);
            var candidates = new List<CloudDropUploadCandidate>(waiting.Count);
            foreach (var fetch in waiting)
            {
                var trackId = fetch.TrackId ?? await db.MediaFiles
                    .Where(f => f.LibrarySourceId == fetch.LibrarySourceId && f.RelativePath == fetch.LibraryPath && f.MissingSince == null)
                    .Select(f => (Guid?)f.TrackId)
                    .FirstOrDefaultAsync(cancellationToken);
                var identified = trackId is not null && await db.Tracks
                    .AnyAsync(t => t.Id == trackId && (t.Recording.AcoustId != null || t.Recording.MusicBrainzId != null), cancellationToken);
                candidates.Add(new CloudDropUploadCandidate(fetch.Id, fetch.DropFolderId, fetch.UploadAccount, fetch.Title ?? "", trackId, identified));
            }
            return candidates;
        });

    public Task SetAsync(Guid id, Guid? trackId, string? cloudTrackId, string? errorCode, CancellationToken cancellationToken) =>
        WriteAsync(async db =>
        {
            var row = await db.CloudDropFetches.SingleOrDefaultAsync(f => f.Id == id, cancellationToken);
            if (row is null)
                return 0;
            row.TrackId = trackId ?? row.TrackId;
            row.Upload = cloudTrackId is not null ? CloudDropUpload.Uploaded : CloudDropUpload.Failed;
            row.UploadedTrackId = cloudTrackId;
            row.ErrorCode = errorCode;
            row.UpdatedAt = Now();
            return await db.SaveChangesAsync(cancellationToken);
        }, cancellationToken);
}

/// <summary>This server's P2P node, and the devices Ultimate Cloud vouches for (<see cref="IP2PDevices"/>).</summary>
internal sealed class CoreP2PDevices(
    IArcanistDbContextFactory databases,
    DatabaseMaintenance maintenance,
    P2PSettings settings,
    P2PNode node,
    P2PPeerDirectory directory,
    TimeProvider time,
    ILogger<CoreP2PDevices> logger)
    : CoreStore(databases, maintenance, time), IP2PDevices
{
    public async Task<(string NodeId, string Ticket, string Name)?> NodeAsync(CancellationToken cancellationToken)
    {
        var config = await settings.GetAsync(cancellationToken);
        if (!config.Enabled || node.State is not { Running: true, NodeId: { } nodeId, Ticket: { } ticket })
            return null;
        return (nodeId, Application.P2P.P2PService.Base64Url(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(ticket, NativeP2P.Json))), config.DisplayName);
    }

    public Task<IReadOnlyList<CloudDevice>> CloudDevicesAsync(Guid? ownerUserId, CancellationToken cancellationToken) =>
        ReadAsync<IReadOnlyList<CloudDevice>>(async db => await db.P2PPeers.AsNoTracking()
            .Where(p => p.Source == P2PPeerSource.UltimateCloud && (ownerUserId == null || p.UserId == ownerUserId))
            .Select(p => new CloudDevice(p.NodeId, p.Name, p.UserId))
            .ToListAsync(cancellationToken));

    public async Task ReplaceCloudDevicesAsync(IReadOnlyList<CloudDevice> devices, CancellationToken cancellationToken)
    {
        var changed = await WriteAsync(async db =>
        {
            var current = await db.P2PPeers.Where(p => p.Source == P2PPeerSource.UltimateCloud).ToListAsync(cancellationToken);
            var wanted = devices.DistinctBy(d => d.NodeId).ToDictionary(d => d.NodeId, StringComparer.Ordinal);
            var changed = false;

            foreach (var peer in current.Where(p => !wanted.ContainsKey(p.NodeId)))
            {
                db.P2PPeers.Remove(peer);
                changed = true;
            }

            var newIds = wanted.Keys.Except(current.Select(p => p.NodeId)).ToList();
            if (newIds.Count > 0)
            {
                // Nodes paired by hand stay as they are.
                var paired = await db.P2PPeers.Where(p => newIds.Contains(p.NodeId)).Select(p => p.NodeId).ToListAsync(cancellationToken);
                foreach (var id in newIds.Except(paired))
                {
                    db.P2PPeers.Add(new P2PPeer
                    {
                        Kind = P2PPeerKind.Device,
                        NodeId = id,
                        Name = Cut(wanted[id].Name),
                        UserId = wanted[id].OwnerUserId,
                        Source = P2PPeerSource.UltimateCloud,
                        Rules = new P2PRules { AllowDownload = true },
                        CreatedAt = Now(),
                    });
                    changed = true;
                }
            }

            // A device keeps the owner it came with (one in several accounts belongs to the first).
            foreach (var peer in current.Where(p => wanted.TryGetValue(p.NodeId, out var device) && p.Name != Cut(device.Name)))
            {
                peer.Name = Cut(wanted[peer.NodeId].Name);
                changed = true;
            }

            if (changed)
                await db.SaveChangesAsync(cancellationToken);
            return changed;
        }, cancellationToken);

        if (!changed)
            return;
        await directory.InvalidateAsync(cancellationToken);
        logger.LogInformation("Devices of Ultimate Cloud accounts trusted through the P2P network: {Count}", devices.Count);
    }

    private static string Cut(string name) => name.Length > 120 ? name[..120] : name;
}
