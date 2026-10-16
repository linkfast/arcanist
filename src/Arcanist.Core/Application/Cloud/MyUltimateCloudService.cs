using Arcanist.Core.Capabilities;
using Arcanist.Core.Cloud;
using Arcanist.Core.Data;
using Arcanist.Core.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Arcanist.Core.Application.Cloud;

// Types in this file are part of the public API contracts (GraphQL schema and OpenAPI document).

/// <summary>The caller's own Ultimate Cloud account, and what it brings them.</summary>
/// <param name="Enabled">The Ultimate Cloud extension is on; while it is off nothing contacts Ultimate Cloud.</param>
/// <param name="Account">The account's user name or email.</param>
/// <param name="Tier">The account's: <c>FREE</c> with any plan, <c>PAID</c> with Cloud (an administrator's also unlocks the server's paid features).</param>
/// <param name="ErrorCode">The last problem: <c>cloud_unreachable</c>, <c>cloud_link_expired</c> (link again), <c>cloud_link_denied</c>…</param>
/// <param name="LibraryId">Their personal library with the account's music, which only they see.</param>
public sealed record MyUltimateCloudStatus(
    bool Enabled,
    UltimateCloudLinkState State,
    string? Account,
    string? OwnerFirstName,
    DateTime? LinkedAt,
    UltimateCloudPendingLink? Pending,
    UltimateCloudPlan? Plan,
    CloudTier Tier,
    string? ErrorCode,
    Guid? LibraryId,
    UltimateCloudServices Services);

/// <param name="LibraryName">The name of the personal library, in the user's language; "Ana's Ultimate Cloud" by default.</param>
public sealed record StartMyUltimateCloudLinkInput(string? LibraryName);

/// <summary>
/// Each user's own Ultimate Cloud account: any user links it from their account page and gets their music as a
/// personal library, their plays, statistics and StarTracks, and their devices through the P2P network. Linking and
/// unlinking need a recent authentication.
/// </summary>
public sealed class MyUltimateCloudService(
    ArcanistDbContext db,
    AccessGuard guard,
    UltimateCloudAccount server,
    UserCloudAccounts accounts,
    UltimateCloudAuth auth,
    UltimateCloudServicesSettings services,
    UltimateCloudClient client,
    UltimateCloudReporter reporter)
{
    public async Task<MyUltimateCloudStatus> StatusAsync(CancellationToken cancellationToken) =>
        await CurrentAsync(guard.Authenticated().UserId, cancellationToken);

    /// <summary>Asks Ultimate Cloud for a code; the server waits for its approval in the background.</summary>
    public async Task<MyUltimateCloudStatus> StartLinkAsync(StartMyUltimateCloudLinkInput input, CancellationToken cancellationToken)
    {
        var user = guard.Authenticated().UserId;
        guard.RecentAuthentication();
        if (input.LibraryName is { Length: > 100 })
            throw ArcanistException.Field("libraryName", ErrorCodes.InvalidFormat);
        await accounts.StartLinkAsync(user, input.LibraryName, cancellationToken);
        return await CurrentAsync(user, cancellationToken);
    }

    public async Task<MyUltimateCloudStatus> CancelLinkAsync(CancellationToken cancellationToken)
    {
        var user = guard.Authenticated().UserId;
        accounts.CancelLink(user);
        return await CurrentAsync(user, cancellationToken);
    }

    /// <summary>Forgets the account and removes the personal library; nothing is touched in Ultimate Cloud.</summary>
    public async Task<MyUltimateCloudStatus> UnlinkAsync(CancellationToken cancellationToken)
    {
        var user = guard.Authenticated().UserId;
        guard.RecentAuthentication();
        await accounts.UnlinkAsync(user, cancellationToken);
        return await CurrentAsync(user, cancellationToken);
    }

    /// <summary>Reads the plan again now.</summary>
    public async Task<MyUltimateCloudStatus> RefreshAsync(CancellationToken cancellationToken)
    {
        var user = guard.Authenticated().UserId;
        RequireEnabled();
        await accounts.RefreshAsync(user, cancellationToken);
        return await CurrentAsync(user, cancellationToken);
    }

    /// <summary>Chooses what the account gets from this server: plays, albums heard whole, StarTracks, statistics.</summary>
    public async Task<MyUltimateCloudStatus> SetServicesAsync(UltimateCloudServicesInput input, CancellationToken cancellationToken)
    {
        var user = guard.Authenticated().UserId;
        await services.SetAsync(new UltimateCloudServicesConfig(input.PersonalPlays, input.AlbumPlaythroughs, input.StarTracks, input.Metrics),
            cancellationToken, user);
        reporter.StarsChanged();
        return await CurrentAsync(user, cancellationToken);
    }

    /// <summary>The account's listening statistics, as Ultimate Cloud counts them.</summary>
    public async Task<UltimateCloudMetrics> MetricsAsync(CancellationToken cancellationToken)
    {
        var user = guard.Authenticated().UserId;
        RequireEnabled();
        if (!(await services.GetAsync(cancellationToken, user)).Metrics)
            throw new ArcanistException.Conflict(ErrorCodes.CloudServiceOff);
        if (await auth.LinkAsync(cancellationToken, user) is null)
            throw new ArcanistException.Conflict(ErrorCodes.CloudNotLinked);
        using (UltimateCloudClient.As(user))
            return await UltimateCloudService.FetchMetricsAsync(client, cancellationToken);
    }

    private async Task<MyUltimateCloudStatus> CurrentAsync(Guid user, CancellationToken cancellationToken)
    {
        var link = server.IsConfigured ? await auth.LinkAsync(cancellationToken, user) : null;
        var pending = accounts.PendingOf(user);
        var state = !server.IsConfigured ? UltimateCloudLinkState.NotConfigured
            : link is not null ? UltimateCloudLinkState.Linked
            : pending is not null ? UltimateCloudLinkState.Pending
            : UltimateCloudLinkState.Unlinked;
        var plan = link is null ? null : accounts.PlanOf(user);
        var config = await services.GetAsync(cancellationToken, user);
        return new MyUltimateCloudStatus(
            server.Enabled,
            state,
            link?.UserName ?? link?.Email,
            link?.DisplayFirstName,
            link?.LinkedAt,
            state == UltimateCloudLinkState.Pending
                ? new UltimateCloudPendingLink(pending!.UserCode, pending.VerificationUri, pending.VerificationUriComplete, pending.ExpiresAt)
                : null,
            plan is null ? null : new UltimateCloudPlan(plan.Plan, plan.CloudStatus, plan.UsedBytes, plan.LimitBytes, plan.ReadOnly, plan.RenewsAt, plan.CheckedAt),
            link is null ? CloudTier.None : accounts.TierOf(user),
            accounts.ErrorOf(user),
            await db.Libraries.AsNoTracking()
                .Where(l => l.OwnerUserId == user && l.Sources.Any(s => s.Kind == LibrarySourceKind.UltimateCloud))
                .Select(l => (Guid?)l.Id)
                .FirstOrDefaultAsync(cancellationToken),
            new UltimateCloudServices(config.PersonalPlays, config.AlbumPlaythroughs, config.StarTracks, config.Metrics));
    }

    private void RequireEnabled()
    {
        if (!server.Enabled)
            throw new ArcanistException.Conflict(ErrorCodes.CloudDisabled);
    }
}
