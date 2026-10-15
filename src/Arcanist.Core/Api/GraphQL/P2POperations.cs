using System.ComponentModel;
using Arcanist.Core.Application.P2P;
using Arcanist.Core.Data.Entities;
using GraphQL;

namespace Arcanist.Core.Api.GraphQL;

public sealed partial class Query
{
    [Name("p2pStatus")]
    [Description("The P2P network: whether it runs, this server's node id, its relay and what goes through it now. Administrators only.")]
    public static Task<P2PStatus> P2PStatus([FromServices] P2PService p2p, CancellationToken cancellationToken) => p2p.StatusAsync(cancellationToken);

    [Name("networkPeers")]
    [Description("Nodes allowed through the P2P network, with their rules and traffic: the caller's devices, or every peer for administrators.")]
    public static Task<IReadOnlyList<NetworkPeer>> NetworkPeers(P2PPeerKind? kind, [FromServices] P2PService p2p, CancellationToken cancellationToken) =>
        p2p.PeersAsync(kind, cancellationToken);

    [Name("receivedShares")]
    [Description("Libraries other servers share with this one, each shown here as a library. Administrators only.")]
    public static Task<IReadOnlyList<ReceivedShare>> ReceivedShares([FromServices] P2PService p2p, CancellationToken cancellationToken) =>
        p2p.ReceivedSharesAsync(cancellationToken);

    [Name("p2pActivity")]
    [Description("What happened in the P2P network, newest first: pairings, connections, streams, refusals and revocations. Administrators only.")]
    public static Task<IReadOnlyList<P2PActivityEntry>> P2PActivity([Id] Guid? peerId, int? limit, [FromServices] P2PService p2p, CancellationToken cancellationToken) =>
        p2p.ActivityAsync(peerId, limit, cancellationToken);
}

public sealed partial class Mutation
{
    [Name("setP2pSettings")]
    [Description("Turns the P2P network on or off and sets its relay, the name peers see and the global limits. Administrators only, with a recent authentication.")]
    public static Task<P2PStatus> SetP2PSettings(P2PSettingsInput input, [FromServices] P2PService p2p, CancellationToken cancellationToken) =>
        p2p.SetSettingsAsync(input, cancellationToken);

    [Name("createDeviceInvitation")]
    [Description("An invitation to pair a device with the caller (or, for administrators, another user), valid 30 minutes. The app reads it (as a QR code) and pairs through the P2P network.")]
    public static Task<P2PInvitationLink> CreateDeviceInvitation(CreateDeviceInvitationInput input, [FromServices] P2PService p2p, CancellationToken cancellationToken) =>
        p2p.CreateDeviceInvitationAsync(input, cancellationToken);

    [Name("createShareInvitation")]
    [Description("An invitation for another Arcanist server to receive libraries, with rules and an optional end, valid 7 days. Administrators only, with a recent authentication.")]
    public static Task<P2PInvitationLink> CreateShareInvitation(CreateShareInvitationInput input, [FromServices] P2PService p2p, CancellationToken cancellationToken) =>
        p2p.CreateShareInvitationAsync(input, cancellationToken);

    [Name("updateNetworkPeer")]
    [Description("Changes a peer's name, rules, end and (for a server) shared libraries. Administrators only, with a recent authentication.")]
    public static Task<NetworkPeer> UpdateNetworkPeer(UpdateNetworkPeerInput input, [FromServices] P2PService p2p, CancellationToken cancellationToken) =>
        p2p.UpdatePeerAsync(input, cancellationToken);

    [Name("revokeNetworkPeer")]
    [Description("Ends a peer's access at once. Users revoke their own devices; administrators, any peer.")]
    public static Task<NetworkPeer> RevokeNetworkPeer([Id] Guid id, [FromServices] P2PService p2p, CancellationToken cancellationToken) =>
        p2p.RevokePeerAsync(id, cancellationToken);

    [Name("acceptShare")]
    [Description("Accepts another server's share with its invitation link: its libraries become one library here, scanned at once (nothing is downloaded: the music streams from that server). Administrators only, with a recent authentication.")]
    public static Task<ReceivedShare> AcceptShare(string link, [FromServices] P2PService p2p, CancellationToken cancellationToken) =>
        p2p.AcceptShareAsync(link, cancellationToken);
}
