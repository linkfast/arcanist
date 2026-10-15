using Arcanist.Core.Application.Libraries;
using Arcanist.Core.Auth;
using Arcanist.Core.Capabilities;
using Arcanist.Core.Cloud;
using Arcanist.Core.Data;
using Arcanist.Core.Data.Entities;
using Arcanist.Core.Scanning;
using Microsoft.EntityFrameworkCore;
using LibraryEntity = Arcanist.Core.Data.Entities.Library;
using LibrarySourceEntity = Arcanist.Core.Data.Entities.LibrarySource;

namespace Arcanist.Core.Application.Cloud;

// Types in this file are part of the public API contracts (GraphQL schema and OpenAPI document).

public enum UltimateCloudLinkState
{
    /// <summary>This server has no Ultimate Cloud address (<c>Arcanist:UltimateCloud:ApiUrl</c>).</summary>
    NotConfigured,

    Unlinked,

    /// <summary>Waiting for the account to approve the code.</summary>
    Pending,

    Linked,
}

/// <summary>The code to approve on Ultimate Cloud's site.</summary>
/// <param name="VerificationUriComplete">The same page with the code filled in, for a link or a QR code.</param>
public sealed record UltimateCloudPendingLink(string UserCode, string VerificationUri, string? VerificationUriComplete, DateTime ExpiresAt);

/// <summary>The account's plan as Ultimate Cloud last reported it.</summary>
/// <param name="Name"><c>Standard</c>, <c>Pro</c> or <c>Cloud</c>; only <c>Cloud</c> unlocks the paid features.</param>
/// <param name="CloudStatus">The Cloud subscription: <c>None</c>, <c>Active</c>, <c>PastDue</c> or <c>PendingCancellation</c>.</param>
/// <param name="ReadOnly">The storage is full or the subscription lapsed: nothing new can be uploaded.</param>
/// <param name="CheckedAt">When it was last read; it holds for a week while Ultimate Cloud cannot be reached.</param>
public sealed record UltimateCloudPlan(
    string Name,
    string CloudStatus,
    long UsedBytes,
    long LimitBytes,
    bool ReadOnly,
    DateTime? RenewsAt,
    DateTime CheckedAt);

/// <summary>A library of this server that shows a collection of the account.</summary>
public sealed record UltimateCloudLibrary(Guid LibraryId, string Name, UltimateCloudCollection Collection);

/// <param name="Name">The library's name; by default, "Ultimate Cloud" (or "Cloud Drop").</param>
public sealed record AddUltimateCloudLibraryInput(UltimateCloudCollection Collection, string? Name);

/// <summary>ultimate-audio, the tool Ultimate Cloud serves for syncing (audio hashes and tags as Ultimate clients handle them).</summary>
/// <param name="Version">The installed version; null while it is not installed.</param>
/// <param name="ErrorCode">Why the last install or update failed.</param>
public sealed record UltimateCloudTool(string? Version, string? ErrorCode);

/// <summary>The Ultimate Cloud account linked to this server, and what it enables.</summary>
/// <param name="Account">The account's user name or email.</param>
/// <param name="ErrorCode">The last problem: <c>cloud_unreachable</c>, <c>cloud_link_expired</c> (link again), <c>cloud_link_denied</c>…</param>
public sealed record UltimateCloudStatus(
    UltimateCloudLinkState State,
    string? Account,
    DateTime? LinkedAt,
    UltimateCloudPendingLink? Pending,
    UltimateCloudPlan? Plan,
    CloudTier Tier,
    IReadOnlyList<Capability> Capabilities,
    string? ErrorCode,
    IReadOnlyList<UltimateCloudLibrary> Libraries,
    UltimateCloudTool AudioTool);

/// <summary>Linking the server to an Ultimate Cloud account. Administrators only; linking and unlinking need a recent authentication.</summary>
public sealed class UltimateCloudService(
    ArcanistDbContext db,
    AccessGuard guard,
    UltimateCloudAccount account,
    UltimateCloudAuth auth,
    ICapabilityService capabilities,
    ScanCoordinator scans,
    UserAccessCache accessCache,
    UltimateAudioTool tool)
{
    public async Task<UltimateCloudStatus> StatusAsync(CancellationToken cancellationToken)
    {
        guard.Admin();
        return await CurrentAsync(cancellationToken);
    }

    /// <summary>Asks Ultimate Cloud for a code; the server waits for its approval in the background.</summary>
    public async Task<UltimateCloudStatus> StartLinkAsync(CancellationToken cancellationToken)
    {
        guard.Admin();
        guard.RecentAuthentication();
        await account.StartLinkAsync(guard.Authenticated().UserId, cancellationToken);
        return await CurrentAsync(cancellationToken);
    }

    public async Task<UltimateCloudStatus> CancelLinkAsync(CancellationToken cancellationToken)
    {
        guard.Admin();
        account.CancelLink();
        return await CurrentAsync(cancellationToken);
    }

    /// <summary>Forgets the account: everything local keeps working, only the Ultimate Cloud features turn off.</summary>
    public async Task<UltimateCloudStatus> UnlinkAsync(CancellationToken cancellationToken)
    {
        guard.Admin();
        guard.RecentAuthentication();
        await account.UnlinkAsync(cancellationToken);
        return await CurrentAsync(cancellationToken);
    }

    /// <summary>Reads the plan again now (after subscribing or changing plans).</summary>
    public async Task<UltimateCloudStatus> RefreshAsync(CancellationToken cancellationToken)
    {
        guard.Admin();
        await account.RefreshAsync(cancellationToken);
        return await CurrentAsync(cancellationToken);
    }

    /// <summary>
    /// Shows a collection of the account as a library of this server (one library per collection): visible to
    /// administrators until it is given to users, scanned now and every 6 hours. Nothing is downloaded.
    /// </summary>
    public async Task<UltimateCloudStatus> AddLibraryAsync(AddUltimateCloudLibraryInput input, CancellationToken cancellationToken)
    {
        var actor = guard.Admin();
        guard.RecentAuthentication();
        if (!capabilities.IsEnabled(Capability.UltimateCloudSource) || await auth.LinkAsync(cancellationToken) is null)
            throw new ArcanistException.Conflict(ErrorCodes.CloudNotLinked);

        var location = input.Collection.ToString();
        if (await db.LibrarySources.AnyAsync(s => s.Kind == LibrarySourceKind.UltimateCloud && s.Location == location, cancellationToken))
            throw ArcanistException.Field("collection", ErrorCodes.Duplicate);

        var name = string.IsNullOrWhiteSpace(input.Name)
            ? input.Collection == UltimateCloudCollection.CloudDrop ? "Cloud Drop" : "Ultimate Cloud"
            : input.Name.Trim();
        var normalized = TextNormalizer.Normalize(name);
        if ((await db.Libraries.Select(l => l.Name).ToListAsync(cancellationToken)).Any(n => TextNormalizer.Normalize(n) == normalized))
            throw ArcanistException.Field("name", ErrorCodes.Duplicate);

        var library = new LibraryEntity
        {
            Number = await LibraryService.NextNumberAsync(db, cancellationToken),
            Name = name,
            VisibleToAll = false,
            ScanIntervalHours = 6,
        };
        library.Sources.Add(new LibrarySourceEntity { Library = library, Kind = LibrarySourceKind.UltimateCloud, Location = location });
        db.Libraries.Add(library);
        await db.SaveChangesAsync(cancellationToken);
        accessCache.InvalidateAll();
        await scans.EnqueueAsync(library.Id, ScanKind.Full, actor.UserId, cancellationToken);
        return await CurrentAsync(cancellationToken);
    }

    private async Task<UltimateCloudStatus> CurrentAsync(CancellationToken cancellationToken)
    {
        var link = account.IsConfigured ? await auth.LinkAsync(cancellationToken) : null;
        var pending = account.Pending;
        var state = !account.IsConfigured ? UltimateCloudLinkState.NotConfigured
            : link is not null ? UltimateCloudLinkState.Linked
            : pending is not null ? UltimateCloudLinkState.Pending
            : UltimateCloudLinkState.Unlinked;
        var plan = link is null ? null : account.Plan;
        return new UltimateCloudStatus(
            state,
            link?.UserName ?? link?.Email,
            link?.LinkedAt,
            state == UltimateCloudLinkState.Pending
                ? new UltimateCloudPendingLink(pending!.UserCode, pending.VerificationUri, pending.VerificationUriComplete, pending.ExpiresAt)
                : null,
            plan is null ? null : new UltimateCloudPlan(plan.Plan, plan.CloudStatus, plan.UsedBytes, plan.LimitBytes, plan.ReadOnly, plan.RenewsAt, plan.CheckedAt),
            account.Tier,
            Enum.GetValues<Capability>().Where(capabilities.IsEnabled).ToList(),
            account.ErrorCode,
            (await db.LibrarySources.AsNoTracking()
                .Where(s => s.Kind == LibrarySourceKind.UltimateCloud)
                .Select(s => new { s.LibraryId, s.Library.Name, s.Location })
                .ToListAsync(cancellationToken))
            .Select(s => new UltimateCloudLibrary(s.LibraryId, s.Name,
                Enum.TryParse<UltimateCloudCollection>(s.Location, out var collection) ? collection : UltimateCloudCollection.Library))
            .OrderBy(l => l.Name)
            .ToList(),
            new UltimateCloudTool(tool.Version, link is null ? null : tool.ErrorCode));
    }
}
