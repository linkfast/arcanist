using Arcanist.Core.Application;
using Arcanist.Core.Application.P2P;
using Arcanist.Core.Auth;
using Arcanist.Core.Data.Entities;
using Arcanist.Core.Media;
using Arcanist.Core.P2P;
using Arcanist.Core.Scanning;

namespace Arcanist.Core.Api.Rest;

public sealed record AcceptShareInput(string Link);

public static class P2PEndpoints
{
    public static IEndpointRouteBuilder MapP2PEndpoints(this IEndpointRouteBuilder rest)
    {
        var p2p = rest.MapGroup("/p2p").WithTags("P2P");

        p2p.MapGet("", (P2PService service, CancellationToken ct) => service.StatusAsync(ct))
            .WithName("P2pStatus")
            .WithSummary("The P2P network: whether it runs, this server's node id, its relay and what goes through it now. Administrators only.");

        p2p.MapPut("/settings", (P2PSettingsInput input, P2PService service, CancellationToken ct) => service.SetSettingsAsync(input, ct))
            .WithName("SetP2pSettings")
            .WithSummary("Turns the P2P network on or off and sets its relay, the name peers see and the global limits. " +
                         "Administrators only, with a recent authentication.");

        p2p.MapGet("/peers", (string? kind, P2PService service, CancellationToken ct) =>
                service.PeersAsync(kind is null ? null : Enum.TryParse<P2PPeerKind>(kind, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed)
                    ? parsed
                    : throw ArcanistException.Field("kind", ErrorCodes.InvalidFormat), ct))
            .WithName("NetworkPeers")
            .WithSummary("Nodes allowed through the P2P network, with their rules and traffic: the caller's devices, or every peer for administrators.");

        p2p.MapPost("/invitations/device", (CreateDeviceInvitationInput input, P2PService service, CancellationToken ct) =>
                service.CreateDeviceInvitationAsync(input, ct))
            .WithName("CreateDeviceInvitation")
            .WithSummary("An invitation to pair a device with the caller (or, for administrators, another user), valid 30 minutes. " +
                         "The app reads it (as a QR code) and pairs through the P2P network.");

        p2p.MapPost("/invitations/share", (CreateShareInvitationInput input, P2PService service, CancellationToken ct) =>
                service.CreateShareInvitationAsync(input, ct))
            .WithName("CreateShareInvitation")
            .WithSummary("An invitation for another Arcanist server to receive libraries, with rules and an optional end, valid 7 days. " +
                         "Administrators only, with a recent authentication.");

        p2p.MapPut("/peers/{id:guid}", (Guid id, UpdateNetworkPeerInput input, P2PService service, CancellationToken ct) =>
                service.UpdatePeerAsync(input with { Id = id }, ct))
            .WithName("UpdateNetworkPeer")
            .WithSummary("Changes a peer's name, rules, end and (for a server) shared libraries. Administrators only, with a recent authentication.");

        p2p.MapPost("/peers/{id:guid}/revoke", (Guid id, P2PService service, CancellationToken ct) => service.RevokePeerAsync(id, ct))
            .WithName("RevokeNetworkPeer")
            .WithSummary("Ends a peer's access at once. Users revoke their own devices; administrators, any peer.");

        p2p.MapGet("/shares", (P2PService service, CancellationToken ct) => service.ReceivedSharesAsync(ct))
            .WithName("ReceivedShares")
            .WithSummary("Libraries other servers share with this one, each shown here as a library. Administrators only.");

        p2p.MapPost("/shares", (AcceptShareInput input, P2PService service, CancellationToken ct) => service.AcceptShareAsync(input.Link, ct))
            .WithName("AcceptShare")
            .WithSummary("Accepts another server's share with its invitation link: its libraries become one library here, scanned at once " +
                         "(nothing is downloaded: the music streams from that server). Administrators only, with a recent authentication.");

        p2p.MapGet("/activity", (Guid? peerId, int? limit, P2PService service, CancellationToken ct) => service.ActivityAsync(peerId, limit, ct))
            .WithName("P2pActivity")
            .WithSummary("What happened in the P2P network, newest first: pairings, connections, streams, refusals and revocations. Administrators only.");

        // Handshakes: nodes reach them through the P2P network, before they are peers.
        var handshakes = p2p.MapGroup("").RequireRateLimiting(AuthServiceCollectionExtensions.AuthRateLimit);

        handshakes.MapPost("/pair", (HttpContext context, PairDeviceRequest request, P2PService service, CancellationToken ct) =>
                P2PRequest.NodeId(context) is { } node ? service.PairAsync(node, request, ct) : throw new ArcanistException.NotFound())
            .WithName("PairDevice")
            .WithSummary("Pairs the calling device with the user of a device invitation. Only through the P2P network.");

        handshakes.MapPost("/join", (HttpContext context, JoinShareRequest request, P2PService service, CancellationToken ct) =>
                P2PRequest.NodeId(context) is { } node ? service.JoinAsync(node, request, ct) : throw new ArcanistException.NotFound())
            .WithName("JoinShare")
            .WithSummary("Accepts a share invitation for the calling server. Only through the P2P network, server to server.");

        MapPeerApi(rest.MapGroup("/peer").WithTags("Peer"));
        return rest;
    }

    /// <summary>What a server that receives a share asks the sharing server. Only server peers reach it (see P2PGateMiddleware).</summary>
    private static void MapPeerApi(RouteGroupBuilder peer)
    {
        peer.MapGet("/info", (HttpContext context, PeerApi api, CancellationToken ct) => api.InfoAsync(Caller(context), ct))
            .WithName("PeerShareInfo")
            .WithSummary("Server to server: this server's name, the share's rules and libraries.");

        peer.MapGet("/files", (HttpContext context, Guid? after, PeerApi api, CancellationToken ct) => api.FilesAsync(Caller(context), after, ct))
            .WithName("PeerFiles")
            .WithSummary("Server to server: the shared files, described (tags, content hash, artwork), 500 at a time.");

        peer.MapMethods("/stream", ["GET", "HEAD"], async (HttpContext context, string path, int? cue, bool? download, string? format, int? maxBitRate,
                double? timeOffset, PeerApi api, P2PActivityLog activity, CancellationToken ct) =>
            {
                var caller = Caller(context);
                var file = await api.FindAsync(caller, path, cue ?? 0, ct);
                if (file is null)
                    return Problems.Create(StatusCodes.Status404NotFound, ErrorCodes.NotFound);
                TranscodeProfile? profile = null;
                if (format == "lpcm")
                    profile = TranscodeProfile.Lpcm;
                else if (format is not null)
                {
                    if (!Enum.TryParse<TranscodeFormat>(format, ignoreCase: true, out var parsed))
                        return Problems.Create(StatusCodes.Status400BadRequest, ErrorCodes.InvalidFormat);
                    profile = TranscodeProfile.For(parsed);
                }

                PeerApi.SignIn(context, caller);
                if (download == true && caller.Rules.AllowDownload)
                    activity.Add(P2PActivityKind.Downloaded, caller.Id, caller.Name, path[(path.LastIndexOf('/') + 1)..]);
                return await MediaResponses.StreamWithProfileAsync(context, file.TrackId, file.Id, download ?? false, profile, maxBitRate, timeOffset, ct);
            })
            .WithName("PeerStream")
            .WithSummary("Server to server: a shared file's audio, under the share's rules (conversion, bit-rate cap, downloads).");

        peer.MapGet("/artwork/{hash}", async (HttpContext context, string hash, PeerApi api, ArtworkStore store, CancellationToken ct) =>
                await api.ArtworkAsync(Caller(context), hash, ct) is { } artwork
                    ? Results.File(store.PathFor(artwork.Hash, artwork.MimeType), artwork.MimeType)
                    : Problems.Create(StatusCodes.Status404NotFound, ErrorCodes.NotFound))
            .WithName("PeerArtwork")
            .WithSummary("Server to server: the original image of a shared track's artwork.");
    }

    /// <summary>The server peer behind the request; the gate lets nobody else in.</summary>
    private static P2PPeerSnapshot Caller(HttpContext context) =>
        P2PRequest.Peer(context) is { Kind: P2PPeerKind.Server } peer ? peer : throw new ArcanistException.NotFound();
}
