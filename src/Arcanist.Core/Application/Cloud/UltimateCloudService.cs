using Arcanist.Core.Capabilities;
using Arcanist.Core.Cloud;

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
    string? ErrorCode);

/// <summary>Linking the server to an Ultimate Cloud account. Administrators only; linking and unlinking need a recent authentication.</summary>
public sealed class UltimateCloudService(
    AccessGuard guard,
    UltimateCloudAccount account,
    UltimateCloudAuth auth,
    ICapabilityService capabilities)
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
        await account.StartLinkAsync(cancellationToken);
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
            account.ErrorCode);
    }
}
