using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Arcanist.Core.Application.Libraries;
using Arcanist.Core.Auth;
using Arcanist.Core.Data;
using Arcanist.Core.Data.Entities;
using Arcanist.Core.P2P;
using Arcanist.Core.Scanning;
using Microsoft.EntityFrameworkCore;
using LibraryEntity = Arcanist.Core.Data.Entities.Library;
using LibrarySourceEntity = Arcanist.Core.Data.Entities.LibrarySource;

namespace Arcanist.Core.Application.P2P;

// Types in this file are part of the public API contracts (GraphQL schema and OpenAPI document).

public enum P2PRelayMode
{
    /// <summary>n0's public relays (the default): connects behind any router; traffic stays end-to-end encrypted.</summary>
    N0,

    /// <summary>The administrator's own iroh relay, at <see cref="P2PSettingsInput.RelayUrl"/>.</summary>
    Custom,

    /// <summary>Direct connections only. Some networks will not connect.</summary>
    Direct,

    /// <summary>Ultimate's relay, with the Cloud plan of the linked Ultimate Cloud account (n0's while it is not available).</summary>
    Ultimate,
}

/// <summary>The P2P network: whether it runs, how, and what goes through it now.</summary>
/// <param name="Available">The P2P library exists for this platform.</param>
/// <param name="Running">The node is up (it may still be looking for its relay).</param>
/// <param name="ErrorCode">Why it is not running although enabled.</param>
/// <param name="NodeId">This server's node id: what devices and other servers know it by.</param>
/// <param name="Name">The name other servers and devices see.</param>
/// <param name="CustomName">The name chosen by an administrator; null for the default.</param>
/// <param name="HomeRelay">The relay the node uses now.</param>
/// <param name="CurrentBytesPerSecond">What all peers receive now.</param>
public sealed record P2PStatus(
    bool Enabled,
    bool Available,
    bool Running,
    string? ErrorCode,
    string? NodeId,
    string Name,
    string? CustomName,
    P2PRelayMode Relay,
    string? RelayUrl,
    string? HomeRelay,
    bool RelayConnected,
    int? BandwidthKbps,
    int? MaxStreams,
    int ConnectedPeers,
    int ActiveStreams,
    long CurrentBytesPerSecond,
    long PeakBytesPerSecond);

/// <param name="BandwidthKbps">kbit/s for all peers together; null for no limit.</param>
/// <param name="MaxStreams">Audio streams all peers may receive at once; null for no limit.</param>
public sealed record P2PSettingsInput(bool Enabled, P2PRelayMode Relay, string? RelayUrl, string? Name, int? BandwidthKbps, int? MaxStreams);

/// <summary>What a peer may do and use. Null limits mean none (the global ones still apply).</summary>
/// <param name="AllowDownload">Downloads of originals, and copies into the other server's libraries.</param>
/// <param name="MaxBitRate">kbit/s: streams are converted to at most this ("compressed only").</param>
/// <param name="BandwidthKbps">kbit/s for everything the peer receives.</param>
/// <param name="MaxStreams">Audio streams it may receive at once.</param>
public sealed record PeerRules(bool AllowDownload, int? MaxBitRate, int? BandwidthKbps, int? MaxStreams);

public sealed record PeerRulesInput(bool AllowDownload, int? MaxBitRate, int? BandwidthKbps, int? MaxStreams);

/// <summary>A node allowed through the P2P network: a user's device, or a server libraries are shared with.</summary>
/// <param name="UserId">The user a device belongs to.</param>
/// <param name="LibraryIds">Libraries shared with a server.</param>
/// <param name="Online">It has a connection open now.</param>
/// <param name="AverageBytesPerSecond">What it received on average while connected.</param>
/// <param name="Streams">Audio streams it started.</param>
/// <param name="Source">Paired with an invitation, or a device of the linked Ultimate Cloud account (Cloud plan).</param>
public sealed record NetworkPeer(
    Guid Id,
    P2PPeerKind Kind,
    P2PPeerSource Source,
    string Name,
    string NodeId,
    Guid? UserId,
    string? UserName,
    PeerRules Rules,
    IReadOnlyList<Guid> LibraryIds,
    DateTime CreatedAt,
    DateTime? ExpiresAt,
    DateTime? RevokedAt,
    DateTime? LastSeenAt,
    bool Online,
    int ActiveStreams,
    long BytesSent,
    long BytesReceived,
    long CurrentBytesPerSecond,
    long AverageBytesPerSecond,
    long PeakBytesPerSecond,
    long Streams);

/// <summary>An invitation to show as a QR code or send: an <c>arcanist://p2p</c> link, usable once.</summary>
public sealed record P2PInvitationLink(string Link, DateTime ExpiresAt);

/// <param name="Name">What to call the device; it can name itself when pairing.</param>
/// <param name="UserId">The user it belongs to (administrators only); the caller when null.</param>
public sealed record CreateDeviceInvitationInput(string? Name, Guid? UserId);

/// <param name="Name">What to call the other server.</param>
/// <param name="AccessExpiresAt">When its access ends; null for no end.</param>
public sealed record CreateShareInvitationInput(string Name, IReadOnlyList<Guid> LibraryIds, PeerRulesInput Rules, DateTime? AccessExpiresAt);

/// <param name="LibraryIds">For a server; ignored for devices.</param>
public sealed record UpdateNetworkPeerInput(Guid Id, string Name, PeerRulesInput Rules, IReadOnlyList<Guid>? LibraryIds, DateTime? ExpiresAt);

/// <summary>Libraries another server shares with this one, which appear here as the library <see cref="LibraryId"/>.</summary>
/// <param name="Rules">What the other server allows.</param>
/// <param name="Active">False once the other server revoked the share or it expired.</param>
/// <param name="Online">There is a connection to the other server now.</param>
/// <param name="Copy">The last copy into a library of this server, since the server started.</param>
public sealed record ReceivedShare(
    Guid Id, string Name, string NodeId, Guid LibraryId, PeerRules Rules, bool Active, bool Online, DateTime CreatedAt, DateTime? LastContactAt,
    ShareCopyProgress? Copy);

/// <param name="TargetSourceId">The library folder the files are copied into, keeping their paths.</param>
public sealed record CopyReceivedShareInput(Guid ShareId, Guid TargetSourceId);

public sealed record P2PActivityEntry(Guid Id, DateTime At, P2PActivityKind Kind, Guid? PeerId, string PeerName, string? Detail);

/// <summary>Sent by a device through the P2P network to pair, with the token of its invitation.</summary>
/// <param name="Name">The device's own name (its model, for example); the invitation's when null.</param>
public sealed record PairDeviceRequest(string Token, string? Name);

/// <param name="PeerId">The device's id here, which a user sees among their devices.</param>
public sealed record PairDeviceResult(string ServerName, Guid PeerId);

/// <summary>Sent by another server through the P2P network to accept a share.</summary>
/// <param name="Name">The other server's name.</param>
public sealed record JoinShareRequest(string Token, string Name);

/// <summary>
/// The P2P network: settings and status (administrators), devices (each user pairs their own; administrators see
/// all), libraries shared with other servers and shares received from them (administrators, with a recent password
/// for what grants access), and the handshakes peers make through the network.
/// </summary>
public sealed class P2PService(
    ArcanistDbContext db,
    AccessGuard guard,
    UserAccessCache accessCache,
    TimeProvider time,
    IServiceProvider services)
{
    public const int NameMaxLength = 64;
    private static readonly TimeSpan DeviceInvitationLifetime = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan ShareInvitationLifetime = TimeSpan.FromDays(7);

    private P2PSettings Settings => services.GetRequiredService<P2PSettings>();
    private P2PNode Node => services.GetRequiredService<P2PNode>();
    private P2PTraffic Traffic => services.GetRequiredService<P2PTraffic>();
    private P2PPeerDirectory Directory => services.GetRequiredService<P2PPeerDirectory>();
    private P2PActivityLog Activity => services.GetRequiredService<P2PActivityLog>();
    private PeerClient Client => services.GetRequiredService<PeerClient>();
    private PeerApi Api => services.GetRequiredService<PeerApi>();

    public async Task<P2PStatus> StatusAsync(CancellationToken cancellationToken)
    {
        guard.Admin();
        var config = await Settings.GetAsync(cancellationToken);
        var state = Node.State;
        var traffic = Traffic;
        return new P2PStatus(config.Enabled, NativeP2P.IsAvailable, state.Running, config.Enabled ? state.ErrorCode : null, state.NodeId,
            config.DisplayName, config.Name, ToMode(config.Relay), config.RelayUrl, state.Relay, state.RelayConnected, config.BandwidthKbps,
            config.MaxStreams, traffic.Snapshot.Values.Count(t => t.OpenConnections > 0), traffic.ActiveStreams, traffic.GlobalRate,
            traffic.GlobalPeak);
    }

    public async Task<P2PStatus> SetSettingsAsync(P2PSettingsInput input, CancellationToken cancellationToken)
    {
        guard.Admin();
        guard.RecentAuthentication();
        var errors = new ValidationErrors();
        var name = string.IsNullOrWhiteSpace(input.Name) ? null : input.Name.Trim();
        if (name is { Length: > NameMaxLength })
            errors.Add("name", ErrorCodes.InvalidFormat);
        var relayUrl = string.IsNullOrWhiteSpace(input.RelayUrl) ? null : input.RelayUrl.Trim();
        if (input.Relay == P2PRelayMode.Ultimate && !services.GetRequiredService<Capabilities.ICapabilityService>().IsEnabled(Capabilities.Capability.CloudConnect))
            errors.Add("relay", ErrorCodes.CloudConnectUnavailable);
        if (input.Relay == P2PRelayMode.Custom
            && !(Uri.TryCreate(relayUrl, UriKind.Absolute, out var relay) && relay.Scheme is "https" or "http"))
        {
            errors.Add("relayUrl", relayUrl is null ? ErrorCodes.Required : ErrorCodes.InvalidFormat);
        }

        ValidateLimit(input.BandwidthKbps, "bandwidthKbps", errors);
        ValidateLimit(input.MaxStreams, "maxStreams", errors);
        errors.ThrowIfAny();

        await Settings.SetAsync(new P2PConfig(input.Enabled, FromMode(input.Relay), relayUrl, name, input.BandwidthKbps, input.MaxStreams), cancellationToken);
        Node.Changed();
        // Registers the node in Ultimate Cloud, or forgets the account's devices, as the network turns on or off.
        services.GetRequiredService<Core.Cloud.UltimateCloudAccount>().Wake();
        return await StatusAsync(cancellationToken);
    }

    /// <summary>Devices of the caller; for administrators, every peer (of <paramref name="kind"/> when given).</summary>
    public async Task<IReadOnlyList<NetworkPeer>> PeersAsync(P2PPeerKind? kind, CancellationToken cancellationToken)
    {
        var actor = guard.Authenticated();
        var query = db.P2PPeers.AsNoTracking().Include(p => p.Libraries).Include(p => p.User).AsQueryable();
        query = actor.IsAdmin ? query : query.Where(p => p.Kind == P2PPeerKind.Device && p.UserId == actor.UserId);
        if (kind is { } only)
            query = query.Where(p => p.Kind == only);
        var peers = await query.OrderByDescending(p => p.CreatedAt).ToListAsync(cancellationToken);
        return peers.Select(ToModel).ToList();
    }

    public async Task<P2PInvitationLink> CreateDeviceInvitationAsync(CreateDeviceInvitationInput input, CancellationToken cancellationToken)
    {
        var actor = guard.Authenticated();
        var userId = input.UserId ?? actor.UserId;
        if (userId != actor.UserId)
        {
            guard.Admin();
            if (!await db.Users.AnyAsync(u => u.Id == userId, cancellationToken))
                throw ArcanistException.Field("userId", ErrorCodes.NotFound);
        }

        var name = CleanName(input.Name, "name") ?? "";
        return await InviteAsync(new P2PInvitation
        {
            Kind = P2PPeerKind.Device,
            Name = name,
            TokenHash = "",
            CreatedByUserId = actor.UserId,
            UserId = userId,
            CreatedAt = Now(),
            ExpiresAt = Now() + DeviceInvitationLifetime,
            Rules = new P2PRules { AllowDownload = true },
        }, cancellationToken);
    }

    public async Task<P2PInvitationLink> CreateShareInvitationAsync(CreateShareInvitationInput input, CancellationToken cancellationToken)
    {
        var actor = guard.Admin();
        guard.RecentAuthentication();
        var errors = new ValidationErrors();
        var name = CleanName(input.Name, "name", errors, required: true);
        var libraries = await ValidLibrariesAsync(input.LibraryIds, errors, cancellationToken);
        var rules = ValidRules(input.Rules, errors);
        if (input.AccessExpiresAt is { } end && end <= Now())
            errors.Add("accessExpiresAt", ErrorCodes.InvalidFormat);
        errors.ThrowIfAny();

        return await InviteAsync(new P2PInvitation
        {
            Kind = P2PPeerKind.Server,
            Name = name!,
            TokenHash = "",
            CreatedByUserId = actor.UserId,
            CreatedAt = Now(),
            ExpiresAt = Now() + ShareInvitationLifetime,
            Rules = rules,
            AccessExpiresAt = input.AccessExpiresAt?.ToUniversalTime(),
            LibraryIds = JsonSerializer.Serialize(libraries),
        }, cancellationToken);
    }

    public async Task<NetworkPeer> UpdatePeerAsync(UpdateNetworkPeerInput input, CancellationToken cancellationToken)
    {
        guard.Admin();
        guard.RecentAuthentication();
        var peer = await db.P2PPeers.Include(p => p.Libraries).Include(p => p.User).SingleOrDefaultAsync(p => p.Id == input.Id, cancellationToken)
                   ?? throw new ArcanistException.NotFound();
        var errors = new ValidationErrors();
        var name = CleanName(input.Name, "name", errors, required: true);
        var rules = ValidRules(input.Rules, errors);
        var libraries = peer.Kind == P2PPeerKind.Server && input.LibraryIds is not null
            ? await ValidLibrariesAsync(input.LibraryIds, errors, cancellationToken)
            : null;
        errors.ThrowIfAny();

        peer.Name = name!;
        peer.Rules = rules;
        peer.ExpiresAt = input.ExpiresAt?.ToUniversalTime();
        if (libraries is not null)
        {
            peer.Libraries.RemoveAll(l => !libraries.Contains(l.LibraryId));
            foreach (var id in libraries.Where(id => peer.Libraries.All(l => l.LibraryId != id)))
                peer.Libraries.Add(new P2PPeerLibrary { PeerId = peer.Id, LibraryId = id });
        }

        await db.SaveChangesAsync(cancellationToken);
        await Directory.InvalidateAsync(cancellationToken);
        Activity.Add(P2PActivityKind.Changed, peer.Id, peer.Name, null);
        return ToModel(peer);
    }

    /// <summary>Ends a peer's access at once. Administrators revoke any; users, their own devices.</summary>
    public async Task<NetworkPeer> RevokePeerAsync(Guid id, CancellationToken cancellationToken)
    {
        var actor = guard.Authenticated();
        var peer = await db.P2PPeers.Include(p => p.Libraries).Include(p => p.User).SingleOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (peer is null || (!actor.IsAdmin && (peer.Kind != P2PPeerKind.Device || peer.UserId != actor.UserId)))
            throw new ArcanistException.NotFound();
        peer.RevokedAt ??= Now();
        await db.SaveChangesAsync(cancellationToken);
        await Directory.InvalidateAsync(cancellationToken);
        Activity.Add(P2PActivityKind.Revoked, peer.Id, peer.Name, null);
        return ToModel(peer);
    }

    public async Task<IReadOnlyList<ReceivedShare>> ReceivedSharesAsync(CancellationToken cancellationToken)
    {
        guard.Admin();
        var remotes = await db.P2PRemotes.AsNoTracking().OrderBy(r => r.CreatedAt).ToListAsync(cancellationToken);
        var connected = Node.State.Connected.ToHashSet(StringComparer.Ordinal);
        return remotes.Select(r => ToModel(r, connected)).ToList();
    }

    /// <summary>
    /// Accepts a share from another server with its invitation link: its libraries become one library here, which
    /// is scanned (described by that server; nothing is downloaded) and visible to administrators until shared with
    /// users.
    /// </summary>
    public async Task<ReceivedShare> AcceptShareAsync(string link, CancellationToken cancellationToken)
    {
        var actor = guard.Admin();
        guard.RecentAuthentication();
        var invitation = P2PLinks.Parse(link);
        if (invitation is not { Kind: P2PPeerKind.Server })
            throw ArcanistException.Field("link", ErrorCodes.InvitationInvalid);

        var config = await Settings.GetAsync(cancellationToken);
        if (!config.Enabled || !Node.IsRunning)
            throw new ArcanistException.Conflict(ErrorCodes.P2PDisabled);

        PeerShareInfo share;
        try
        {
            share = await Client.PostAsync<PeerShareInfo>(invitation.Ticket, P2PGateMiddleware.JoinPath,
                new JoinShareRequest(invitation.Token, config.DisplayName), cancellationToken);
        }
        catch (ArcanistException.Forbidden)
        {
            throw ArcanistException.Field("link", ErrorCodes.InvitationInvalid);
        }

        var ticket = JsonSerializer.Serialize(invitation.Ticket, NativeP2P.Json);
        var remote = await db.P2PRemotes.Include(r => r.Library).SingleOrDefaultAsync(r => r.NodeId == invitation.Ticket.Id, cancellationToken);
        if (remote is null)
        {
            var library = new LibraryEntity
            {
                Number = await LibraryService.NextNumberAsync(db, cancellationToken),
                Name = await LibraryNameAsync(share.Name, cancellationToken),
                VisibleToAll = false,
                ScanIntervalMinutes = 6 * 60,
            };
            remote = new P2PRemote
            {
                NodeId = invitation.Ticket.Id,
                Ticket = ticket,
                Name = share.Name,
                CreatedAt = Now(),
                Rules = share.Rules,
                Status = P2PRemoteStatus.Active,
                LibraryId = library.Id,
                LastContactAt = Now(),
            };
            library.Sources.Add(new LibrarySourceEntity { Library = library, Kind = LibrarySourceKind.Peer, Location = remote.Id.ToString() });
            db.Libraries.Add(library);
            db.P2PRemotes.Add(remote);
        }
        else
        {
            (remote.Ticket, remote.Name, remote.Rules, remote.Status, remote.LastContactAt) = (ticket, share.Name, share.Rules, P2PRemoteStatus.Active, Now());
        }

        await db.SaveChangesAsync(cancellationToken);
        accessCache.InvalidateAll();
        Node.CloseTunnel(remote.NodeId);
        await services.GetRequiredService<ScanCoordinator>().EnqueueAsync(remote.LibraryId, ScanKind.Full, actor.UserId, cancellationToken);
        return ToModel(remote, Node.State.Connected.ToHashSet(StringComparer.Ordinal));
    }

    public async Task<ReceivedShare> CopyShareAsync(CopyReceivedShareInput input, CancellationToken cancellationToken)
    {
        guard.Admin();
        guard.RecentAuthentication();
        var remote = await db.P2PRemotes.AsNoTracking().SingleOrDefaultAsync(r => r.Id == input.ShareId, cancellationToken)
                     ?? throw new ArcanistException.NotFound();
        if (!remote.Rules.AllowDownload)
            throw new ArcanistException.Forbidden(ErrorCodes.DownloadNotAllowed);
        var target = await db.LibrarySources.AsNoTracking().SingleOrDefaultAsync(s => s.Id == input.TargetSourceId, cancellationToken);
        var storages = services.GetRequiredService<Storage.ILibraryStorageResolver>();
        if (target is null || target.Kind != LibrarySourceKind.Local)
            throw ArcanistException.Field("targetSourceId", ErrorCodes.NotFound);
        if (!await storages.For(target.Kind).CanWriteAsync(Storage.StorageLocation.From(target), cancellationToken))
            throw ArcanistException.Field("targetSourceId", ErrorCodes.AccessDenied);

        services.GetRequiredService<ShareCopier>().Enqueue(remote.Id, target.Id);
        return ToModel(remote, Node.State.Connected.ToHashSet(StringComparer.Ordinal));
    }

    public async Task<IReadOnlyList<P2PActivityEntry>> ActivityAsync(Guid? peerId, int? limit, CancellationToken cancellationToken)
    {
        guard.Admin();
        await Activity.FlushAsync(cancellationToken);
        var query = db.P2PActivities.AsNoTracking();
        if (peerId is { } id)
            query = query.Where(a => a.PeerId == id);
        var entries = await query.OrderByDescending(a => a.At).ThenByDescending(a => a.Id).Take(Math.Clamp(limit ?? 100, 1, 500)).ToListAsync(cancellationToken);
        return entries.Select(a => new P2PActivityEntry(a.Id, a.At, a.Kind, a.PeerId, a.PeerName, a.Detail)).ToList();
    }

    // Handshakes, made by nodes through the P2P network without signing in.

    public async Task<PairDeviceResult> PairAsync(string nodeId, PairDeviceRequest request, CancellationToken cancellationToken)
    {
        var invitation = await RedeemAsync(P2PPeerKind.Device, request.Token, cancellationToken);
        var name = CleanName(request.Name, "name") is { Length: > 0 } own ? own : invitation.Name is { Length: > 0 } given ? given : "Device";
        var peer = await JoinedAsync(invitation, nodeId, name, cancellationToken);
        var config = await Settings.GetAsync(cancellationToken);
        return new PairDeviceResult(config.DisplayName, peer.Id);
    }

    public async Task<PeerShareInfo> JoinAsync(string nodeId, JoinShareRequest request, CancellationToken cancellationToken)
    {
        var invitation = await RedeemAsync(P2PPeerKind.Server, request.Token, cancellationToken);
        await JoinedAsync(invitation, nodeId, invitation.Name, cancellationToken);
        var snapshot = await Directory.FindAsync(nodeId, cancellationToken) ?? throw new ArcanistException.NotFound();
        return await Api.InfoAsync(snapshot, cancellationToken);
    }

    private async Task<P2PInvitation> RedeemAsync(P2PPeerKind kind, string token, CancellationToken cancellationToken)
    {
        var hash = Hash(token ?? "");
        var invitation = await db.P2PInvitations.SingleOrDefaultAsync(i => i.TokenHash == hash, cancellationToken);
        if (invitation is null || invitation.Kind != kind || invitation.UsedAt is not null || invitation.ExpiresAt <= Now())
            throw new ArcanistException.Forbidden(ErrorCodes.InvitationInvalid);
        return invitation;
    }

    /// <summary>Creates the peer, or renews it when the node was a peer already (paired again, or a share accepted again).</summary>
    private async Task<P2PPeer> JoinedAsync(P2PInvitation invitation, string nodeId, string name, CancellationToken cancellationToken)
    {
        var peer = await db.P2PPeers.Include(p => p.Libraries).SingleOrDefaultAsync(p => p.NodeId == nodeId, cancellationToken);
        if (peer is not null && (peer.Kind != invitation.Kind || peer.UserId != invitation.UserId))
        {
            db.P2PPeers.Remove(peer);
            peer = null;
        }

        if (peer is null)
        {
            peer = new P2PPeer { Kind = invitation.Kind, NodeId = nodeId, Name = name, UserId = invitation.UserId, CreatedAt = Now() };
            db.P2PPeers.Add(peer);
        }

        peer.Name = name;
        peer.Source = P2PPeerSource.Pairing;
        peer.Rules = invitation.Rules;
        peer.ExpiresAt = invitation.AccessExpiresAt;
        peer.RevokedAt = null;
        var libraries = JsonSerializer.Deserialize<List<Guid>>(invitation.LibraryIds) ?? [];
        peer.Libraries.RemoveAll(l => !libraries.Contains(l.LibraryId));
        foreach (var id in libraries.Where(id => peer.Libraries.All(l => l.LibraryId != id)))
            peer.Libraries.Add(new P2PPeerLibrary { PeerId = peer.Id, LibraryId = id });

        invitation.UsedAt = Now();
        invitation.PeerId = peer.Id;
        await db.SaveChangesAsync(cancellationToken);
        await Directory.InvalidateAsync(cancellationToken);
        Activity.Add(P2PActivityKind.Joined, peer.Id, peer.Name, invitation.Kind == P2PPeerKind.Device ? null : string.Join(", ", libraries));
        return peer;
    }

    private async Task<P2PInvitationLink> InviteAsync(P2PInvitation draft, CancellationToken cancellationToken)
    {
        var config = await Settings.GetAsync(cancellationToken);
        var state = Node.State;
        if (!config.Enabled || state.Ticket is null)
            throw new ArcanistException.Conflict(ErrorCodes.P2PDisabled);

        var token = Base64Url(RandomNumberGenerator.GetBytes(24));
        var invitation = new P2PInvitation
        {
            Kind = draft.Kind,
            Name = draft.Name,
            TokenHash = Hash(token),
            CreatedByUserId = draft.CreatedByUserId,
            UserId = draft.UserId,
            CreatedAt = draft.CreatedAt,
            ExpiresAt = draft.ExpiresAt,
            Rules = draft.Rules,
            AccessExpiresAt = draft.AccessExpiresAt,
            LibraryIds = draft.LibraryIds,
        };
        db.P2PInvitations.Add(invitation);
        // Invitations nobody used are of no use a day after they expire.
        var stale = Now() - TimeSpan.FromDays(1);
        await db.P2PInvitations.Where(i => i.UsedAt == null && i.ExpiresAt < stale).ExecuteDeleteAsync(cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return new P2PInvitationLink(P2PLinks.Build(draft.Kind, state.Ticket, token, config.DisplayName), invitation.ExpiresAt);
    }

    private async Task<List<Guid>> ValidLibrariesAsync(IReadOnlyList<Guid>? ids, ValidationErrors errors, CancellationToken cancellationToken)
    {
        var libraries = (ids ?? []).Distinct().ToList();
        // A share received from another server cannot be shared on, nor an Ultimate Cloud library (the account is personal).
        var existing = await db.Libraries.Where(l => libraries.Contains(l.Id) && l.Sources.All(s => s.Kind == LibrarySourceKind.Local))
            .CountAsync(cancellationToken);
        if (libraries.Count == 0)
            errors.Add("libraryIds", ErrorCodes.Required);
        else if (existing != libraries.Count)
            errors.Add("libraryIds", ErrorCodes.NotFound);
        return libraries;
    }

    private static P2PRules ValidRules(PeerRulesInput? input, ValidationErrors errors)
    {
        input ??= new PeerRulesInput(false, null, null, null);
        ValidateLimit(input.MaxBitRate, "rules.maxBitRate", errors);
        ValidateLimit(input.BandwidthKbps, "rules.bandwidthKbps", errors);
        ValidateLimit(input.MaxStreams, "rules.maxStreams", errors);
        return new P2PRules
        {
            AllowDownload = input.AllowDownload,
            MaxBitRate = input.MaxBitRate,
            BandwidthKbps = input.BandwidthKbps,
            MaxStreams = input.MaxStreams,
        };
    }

    private static void ValidateLimit(int? value, string field, ValidationErrors errors)
    {
        if (value is <= 0 or > 10_000_000)
            errors.Add(field, ErrorCodes.InvalidFormat);
    }

    private static string? CleanName(string? name, string field, ValidationErrors? errors = null, bool required = false)
    {
        var clean = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
        if (clean is null && required)
            errors?.Add(field, ErrorCodes.Required);
        if (clean is { Length: > NameMaxLength })
        {
            if (errors is null)
                return clean[..NameMaxLength];
            errors.Add(field, ErrorCodes.InvalidFormat);
        }

        return clean;
    }

    /// <summary>"Biblioteca de X" or "X's library" in the server's language, made unique.</summary>
    private async Task<string> LibraryNameAsync(string server, CancellationToken cancellationToken)
    {
        var language = await services.GetRequiredService<SettingsStore>().GetAsync<string?>(SettingKeys.DefaultLanguage, null, cancellationToken);
        var name = language?.StartsWith("es", StringComparison.OrdinalIgnoreCase) == true ? $"Biblioteca de {server}" : $"{server}'s library";
        var taken = (await db.Libraries.Select(l => l.Name).ToListAsync(cancellationToken)).Select(TextNormalizer.Normalize).ToHashSet();
        var candidate = name;
        for (var i = 2; taken.Contains(TextNormalizer.Normalize(candidate)); i++)
            candidate = $"{name} ({i})";
        return candidate;
    }

    private NetworkPeer ToModel(P2PPeer peer)
    {
        var traffic = Traffic.Snapshot.GetValueOrDefault(peer.NodeId);
        // What was not saved yet (the totals are added to the row every minute).
        var sent = peer.BytesSent + (traffic is null ? 0 : traffic.BytesSent - traffic.SavedSent);
        var received = peer.BytesReceived + (traffic is null ? 0 : traffic.BytesReceived - traffic.SavedReceived);
        var seconds = peer.ConnectedSeconds + (traffic is null ? 0 : Interlocked.Read(ref traffic.ConnectedSeconds) - traffic.SavedSeconds);
        var streams = peer.Streams + (traffic is null ? 0 : Interlocked.Read(ref traffic.StreamsStarted) - traffic.SavedStreams);
        return new NetworkPeer(peer.Id, peer.Kind, peer.Source, peer.Name, peer.NodeId, peer.UserId, peer.User?.UserName,
            new PeerRules(peer.Rules.AllowDownload, peer.Rules.MaxBitRate, peer.Rules.BandwidthKbps, peer.Rules.MaxStreams),
            peer.Libraries.Select(l => l.LibraryId).ToList(), peer.CreatedAt, peer.ExpiresAt, peer.RevokedAt, peer.LastSeenAt,
            traffic?.OpenConnections > 0, traffic?.ActiveStreams ?? 0, sent, received, traffic?.CurrentRate ?? 0,
            seconds > 0 ? sent / seconds : 0, Math.Max(peer.PeakBytesPerSecond, traffic?.Peak ?? 0), streams);
    }

    private ReceivedShare ToModel(P2PRemote remote, IReadOnlySet<string> connected) => new(
        remote.Id, remote.Name, remote.NodeId, remote.LibraryId,
        new PeerRules(remote.Rules.AllowDownload, remote.Rules.MaxBitRate, remote.Rules.BandwidthKbps, remote.Rules.MaxStreams),
        remote.Status == P2PRemoteStatus.Active, connected.Contains(remote.NodeId), remote.CreatedAt, remote.LastContactAt,
        services.GetRequiredService<ShareCopier>().Progress(remote.Id));

    private static P2PRelayMode ToMode(P2PRelay relay) => relay switch
    {
        P2PRelay.Custom => P2PRelayMode.Custom,
        P2PRelay.Direct or P2PRelay.Local => P2PRelayMode.Direct,
        P2PRelay.Ultimate => P2PRelayMode.Ultimate,
        _ => P2PRelayMode.N0,
    };

    private static P2PRelay FromMode(P2PRelayMode mode) => mode switch
    {
        P2PRelayMode.Custom => P2PRelay.Custom,
        P2PRelayMode.Direct => P2PRelay.Direct,
        P2PRelayMode.Ultimate => P2PRelay.Ultimate,
        _ => P2PRelay.N0,
    };

    internal static string Hash(string token) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    internal static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private DateTime Now() => time.GetUtcNow().UtcDateTime;
}

/// <summary>What an invitation link holds.</summary>
public sealed record P2PInvitationData(P2PPeerKind Kind, NodeTicket Ticket, string Token, string Name);

/// <summary>
/// Invitation links: <c>arcanist://p2p?kind=device|share&amp;node=…&amp;token=…&amp;name=…</c>, with the node's ticket
/// as base64url JSON. The apps read them with <c>parse_invitation</c> in the P2P library.
/// </summary>
public static class P2PLinks
{
    private const string Prefix = "arcanist://p2p?";

    public static string Build(P2PPeerKind kind, NodeTicket ticket, string token, string name) =>
        Prefix + string.Join('&',
            "kind=" + (kind == P2PPeerKind.Device ? "device" : "share"),
            "node=" + P2PService.Base64Url(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(ticket, NativeP2P.Json))),
            "token=" + Uri.EscapeDataString(token),
            "name=" + Uri.EscapeDataString(name));

    public static P2PInvitationData? Parse(string? link)
    {
        if (link?.Trim() is not { } text || !text.StartsWith(Prefix, StringComparison.Ordinal))
            return null;
        var values = text[Prefix.Length..].Split('&')
            .Select(p => p.Split('=', 2))
            .Where(p => p.Length == 2)
            .GroupBy(p => p[0])
            .ToDictionary(g => g.Key, g => Uri.UnescapeDataString(g.First()[1].Replace('+', ' ')));
        if (!values.TryGetValue("kind", out var kind) || !values.TryGetValue("node", out var node) || !values.TryGetValue("token", out var token))
            return null;
        try
        {
            var padded = node.Replace('-', '+').Replace('_', '/');
            padded += new string('=', (4 - padded.Length % 4) % 4);
            var ticket = JsonSerializer.Deserialize<NodeTicket>(Convert.FromBase64String(padded), NativeP2P.Json);
            if (ticket is not { Id.Length: 64 })
                return null;
            return new P2PInvitationData(kind == "device" ? P2PPeerKind.Device : P2PPeerKind.Server, ticket, token, values.GetValueOrDefault("name") ?? "");
        }
        catch (Exception e) when (e is FormatException or JsonException)
        {
            return null;
        }
    }
}
