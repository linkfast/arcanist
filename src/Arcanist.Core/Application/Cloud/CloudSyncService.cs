using Arcanist.Core.Capabilities;
using Arcanist.Core.Cloud;
using Arcanist.Core.Data;
using Arcanist.Core.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Arcanist.Core.Application.Cloud;

// Types in this file are part of the public API contracts (GraphQL schema and OpenAPI document).

/// <summary>A song a sync could not handle.</summary>
/// <param name="Reason"><c>upload_failed</c>, <c>download_failed</c>, <c>write_failed</c>, <c>not_found</c>, <c>tool:hash_changed</c> (it cannot be uploaded as it is)…</param>
public sealed record CloudSyncFailure(string Title, string? Path, string Reason);

/// <param name="Phase"><c>listing</c>, <c>matching</c>, <c>uploading</c> or <c>downloading</c>.</param>
public sealed record CloudSyncRun(string Phase, int Done, int Total);

/// <summary>How a library syncs with the Ultimate Cloud account, and how far it got.</summary>
/// <param name="Matched">Songs on both sides.</param>
/// <param name="LocalOnly">Songs only here (not uploaded: the library does not upload, or the account is full).</param>
/// <param name="Running">The run in progress; null between runs.</param>
/// <param name="ErrorCode">Why the last run stopped early: <c>cloud_sync_unavailable</c> (no Cloud plan), <c>cloud_quota_exceeded</c>, <c>cloud_tool_missing</c>…</param>
/// <param name="Failures">The first 50 songs that failed; they are tried again on each run.</param>
/// <param name="AccountUserId">Whose account it syncs with: an administrator's own; the server's when null.</param>
public sealed record LibraryCloudSyncState(
    Guid LibraryId,
    CloudSyncMode Mode,
    Guid? AccountUserId,
    Guid? DownloadSourceId,
    DateTime? LastRunAt,
    bool Requested,
    CloudSyncRun? Running,
    int Matched,
    int Uploaded,
    int Downloaded,
    int LocalOnly,
    int Failed,
    string? ErrorCode,
    IReadOnlyList<CloudSyncFailure> Failures);

/// <param name="DownloadSourceId">The library folder downloads go to; its first folder by default.</param>
/// <param name="AccountUserId">Sync with the caller's own account (their id; it needs the Cloud plan); with the server's when null.</param>
public sealed record SetLibraryCloudSyncInput(CloudSyncMode Mode, Guid? DownloadSourceId, Guid? AccountUserId = null);

/// <summary>
/// Syncing libraries with the linked Ultimate Cloud account (Cloud plan). Administrators only: it sends the
/// server's music to the account; turning it on needs a recent authentication. The server keeps how each library syncs;
/// Ultimate Cloud Plus (<see cref="CloudPlus"/>) does the syncing.
/// </summary>
public sealed class CloudSyncService(ArcanistDbContext db, AccessGuard guard, ICapabilityService capabilities, UserCloudAccounts accounts, CloudPlus plus)
{

    /// <summary>The sync of every library with folders on this server.</summary>
    public async Task<IReadOnlyList<LibraryCloudSyncState>> ListAsync(CancellationToken cancellationToken)
    {
        guard.Admin();
        var libraries = await db.Libraries.AsNoTracking()
            .Where(l => l.Sources.Any() && l.Sources.All(s => s.Kind == LibrarySourceKind.Local))
            .Select(l => l.Id)
            .ToListAsync(cancellationToken);
        return await StatesAsync(libraries, cancellationToken);
    }

    public async Task<LibraryCloudSyncState> SetAsync(Guid libraryId, SetLibraryCloudSyncInput input, CancellationToken cancellationToken)
    {
        var actor = guard.Admin();
        var library = await db.Libraries.Include(l => l.Sources).SingleOrDefaultAsync(l => l.Id == libraryId, cancellationToken)
                      ?? throw new ArcanistException.NotFound();
        var previous = await db.LibraryCloudSyncs.AsNoTracking().Where(s => s.LibraryId == libraryId).Select(s => s.AccountUserId).FirstOrDefaultAsync(cancellationToken);
        if (input.Mode != CloudSyncMode.Off)
        {
            guard.RecentAuthentication();
            // Syncing is Ultimate Cloud Plus's (closed source): without it, nothing would sync.
            if (plus.Sync is null)
                throw new ArcanistException.Conflict(ErrorCodes.CloudPlusRequired);
            // Another user's account is theirs: only the server's, or the caller's own (unless it was chosen already).
            if (input.AccountUserId is { } user && user != actor.UserId && user != previous)
                throw ArcanistException.Field("accountUserId", ErrorCodes.NotFound);
            if (!capabilities.IsEnabled(Capability.CloudSync) || accounts.TierOf(input.AccountUserId) != CloudTier.Paid)
                throw new ArcanistException.Conflict(ErrorCodes.CloudSyncUnavailable);
            if (library.Sources.Count == 0 || library.Sources.Any(s => s.Kind != LibrarySourceKind.Local))
                throw ArcanistException.Field("mode", ErrorCodes.ReadOnly);
            if (input.DownloadSourceId is { } sourceId && library.Sources.All(s => s.Id != sourceId))
                throw ArcanistException.Field("downloadSourceId", ErrorCodes.NotFound);
            if (input.Mode is CloudSyncMode.Download or CloudSyncMode.Both
                && await db.LibraryCloudSyncs.AnyAsync(s => s.LibraryId != libraryId && s.AccountUserId == input.AccountUserId
                                                            && (s.Mode == CloudSyncMode.Download || s.Mode == CloudSyncMode.Both), cancellationToken))
                throw ArcanistException.Field("mode", ErrorCodes.CloudSyncDownloadTaken);
        }

        var sync = await db.LibraryCloudSyncs.SingleOrDefaultAsync(s => s.LibraryId == libraryId, cancellationToken);
        if (sync is null)
        {
            sync = new LibraryCloudSync { LibraryId = libraryId };
            db.LibraryCloudSyncs.Add(sync);
        }

        if (input.Mode != CloudSyncMode.Off && previous != input.AccountUserId)
        {
            // What was paired was with another account: everything is looked at again with this one.
            await db.CloudSyncItems.Where(i => i.LibraryId == libraryId).ExecuteDeleteAsync(cancellationToken);
            sync.AccountUserId = input.AccountUserId;
        }

        sync.Mode = input.Mode;
        sync.DownloadSourceId = input.DownloadSourceId;
        sync.Requested = input.Mode != CloudSyncMode.Off;
        await db.SaveChangesAsync(cancellationToken);
        plus.Sync?.Wake();
        return (await StatesAsync([libraryId], cancellationToken))[0];
    }

    /// <summary>Asks for a run now (otherwise libraries sync every 6 hours).</summary>
    public async Task<LibraryCloudSyncState> SyncNowAsync(Guid libraryId, CancellationToken cancellationToken)
    {
        guard.Admin();
        var sync = await db.LibraryCloudSyncs.SingleOrDefaultAsync(s => s.LibraryId == libraryId && s.Mode != CloudSyncMode.Off, cancellationToken)
                   ?? throw new ArcanistException.NotFound();
        sync.Requested = true;
        await db.SaveChangesAsync(cancellationToken);
        plus.Sync?.Wake();
        return (await StatesAsync([libraryId], cancellationToken))[0];
    }

    private async Task<IReadOnlyList<LibraryCloudSyncState>> StatesAsync(IReadOnlyCollection<Guid> libraries, CancellationToken cancellationToken)
    {
        var syncs = await db.LibraryCloudSyncs.AsNoTracking().Where(s => libraries.Contains(s.LibraryId)).ToDictionaryAsync(s => s.LibraryId, cancellationToken);
        var counts = await db.CloudSyncItems.AsNoTracking()
            .Where(i => libraries.Contains(i.LibraryId))
            .GroupBy(i => new { i.LibraryId, i.Status })
            .Select(g => new { g.Key.LibraryId, g.Key.Status, Count = g.Count() })
            .ToListAsync(cancellationToken);
        var failures = await db.CloudSyncItems.AsNoTracking()
            .Where(i => libraries.Contains(i.LibraryId) && i.Status == CloudSyncItemStatus.Failed)
            .OrderBy(i => i.Title)
            .Select(i => new { i.LibraryId, i.Title, i.Path, i.Reason })
            .ToListAsync(cancellationToken);
        var progress = plus.Sync?.Activity;

        int Count(Guid id, CloudSyncItemStatus status) => counts.Where(c => c.LibraryId == id && c.Status == status).Sum(c => c.Count);

        return libraries.Select(id =>
        {
            var sync = syncs.GetValueOrDefault(id);
            return new LibraryCloudSyncState(
                id,
                sync?.Mode ?? CloudSyncMode.Off,
                sync?.AccountUserId,
                sync?.DownloadSourceId,
                sync?.LastRunAt,
                sync?.Requested ?? false,
                progress?.LibraryId == id ? new CloudSyncRun(progress.Phase, progress.Done, progress.Total) : null,
                Count(id, CloudSyncItemStatus.Matched),
                Count(id, CloudSyncItemStatus.Uploaded),
                Count(id, CloudSyncItemStatus.Downloaded),
                Count(id, CloudSyncItemStatus.LocalOnly),
                Count(id, CloudSyncItemStatus.Failed),
                sync?.ErrorCode,
                failures.Where(f => f.LibraryId == id).Take(50).Select(f => new CloudSyncFailure(f.Title, f.Path, f.Reason ?? ErrorCodes.Interrupted)).ToList());
        }).ToList();
    }
}
