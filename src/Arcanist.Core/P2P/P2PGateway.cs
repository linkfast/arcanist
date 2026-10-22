using System.Buffers;
using System.IO.Pipelines;
using System.Net;
using System.Security.Cryptography;
using Arcanist.Core.Api;
using Arcanist.Core.Application;
using Arcanist.Core.Data;
using Arcanist.Core.Data.Entities;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.EntityFrameworkCore;

namespace Arcanist.Core.P2P;

/// <summary>Set on connections that come through the P2P network: the remote node.</summary>
public interface IP2PConnectionFeature
{
    /// <summary>The remote node's id, as lowercase hex.</summary>
    string NodeId { get; }

    /// <summary>It came through the mStream apps' tunnel (quick connect), not Arcanist's.</summary>
    bool Mstream { get; }
}

internal sealed record P2PConnectionFeature(string NodeId, bool Mstream = false) : IP2PConnectionFeature;

/// <summary>
/// Recognizes connections the P2P node opens to the server: on loopback, they start with a preamble holding this
/// process's secret and the remote node's id. The preamble is consumed, the connection is marked with
/// <see cref="IP2PConnectionFeature"/>, and its traffic is counted and limited. Every other connection passes
/// through untouched (nothing is read from it unless it comes from loopback, and then only what it already sent).
/// </summary>
internal static class P2PConnections
{
    public static void Use(ListenOptions listen) => listen.Use(next => context => HandleAsync(context, next, listen.ApplicationServices));

    private static async Task HandleAsync(ConnectionContext context, ConnectionDelegate next, IServiceProvider services)
    {
        if (context.RemoteEndPoint is not IPEndPoint remote || !IPAddress.IsLoopback(remote.Address))
        {
            await next(context);
            return;
        }

        var keys = services.GetRequiredService<P2PKeys>();
        string? nodeId;
        bool mstream;
        try
        {
            (nodeId, mstream) = await ReadPreambleAsync(context.Transport.Input, keys.Preamble, context.ConnectionClosed);
        }
        catch (OperationCanceledException) when (context.ConnectionClosed.IsCancellationRequested)
        {
            // Closed before sending anything (browsers open spare connections and drop them).
            return;
        }
        switch (nodeId)
        {
            case null:
                await next(context);
                return;
            case "":
                // The magic, but not this process's secret: refuse rather than let it look local.
                context.Abort();
                return;
        }

        var traffic = services.GetRequiredService<P2PTraffic>();
        var peer = traffic.For(nodeId);
        context.Features.Set<IP2PConnectionFeature>(new P2PConnectionFeature(nodeId, mstream));
        var original = context.Transport;
        var stream = new MeteredStream(new DuplexPipeStream(original), traffic, peer);
        context.Transport = new DuplexPipe(PipeReader.Create(stream), PipeWriter.Create(stream));
        traffic.Opened(nodeId);
        try
        {
            await next(context);
        }
        finally
        {
            traffic.Closed(nodeId);
            context.Transport = original;
        }
    }

    /// <summary>
    /// The node id of a tunnelled connection, and whether it came through mStream's tunnel; a null id for any other
    /// connection (left unread); "" for a preamble with the wrong secret.
    /// </summary>
    internal static async Task<(string? NodeId, bool Mstream)> ReadPreambleAsync(PipeReader input, byte[] secret, CancellationToken cancellationToken)
    {
        while (true)
        {
            var result = await input.ReadAsync(cancellationToken);
            var buffer = result.Buffer;
            var compare = (int)Math.Min(buffer.Length, PreambleMagic.Length);
            var start = new byte[compare];
            buffer.Slice(0, compare).CopyTo(start);
            var arcanist = PreambleMagic.AsSpan(0, compare).SequenceEqual(start);
            var mstream = MstreamPreambleMagic.AsSpan(0, compare).SequenceEqual(start);
            if (!arcanist && !mstream)
            {
                input.AdvanceTo(buffer.Start);
                return (null, false);
            }

            if (buffer.Length >= PreambleLength)
            {
                var preamble = buffer.Slice(0, PreambleLength).ToArray();
                input.AdvanceTo(buffer.GetPosition(PreambleLength));
                var isMstream = MstreamPreambleMagic.AsSpan().SequenceEqual(preamble.AsSpan(0, 16));
                return CryptographicOperations.FixedTimeEquals(preamble.AsSpan(16, 32), secret)
                    ? (Convert.ToHexStringLower(preamble.AsSpan(48, 32)), isMstream)
                    : ("", false);
            }

            if (result.IsCompleted)
            {
                input.AdvanceTo(buffer.Start);
                return (null, false);
            }

            input.AdvanceTo(buffer.Start, buffer.End);
        }
    }

    /// <summary>Mirrors <c>PREAMBLE_MAGIC</c> in <c>lib.rs</c>.</summary>
    internal static readonly byte[] PreambleMagic = "ARCANIST-P2P/1\r\n"u8.ToArray();

    /// <summary>Mirrors <c>MSTREAM_PREAMBLE_MAGIC</c> in <c>lib.rs</c>.</summary>
    internal static readonly byte[] MstreamPreambleMagic = "ARCANIST-MST/1\r\n"u8.ToArray();

    internal const int PreambleLength = 16 + 32 + 32;

    private sealed class DuplexPipe(PipeReader input, PipeWriter output) : IDuplexPipe
    {
        public PipeReader Input { get; } = input;
        public PipeWriter Output { get; } = output;
    }

    /// <summary>The connection's pipes as a stream, so they can be wrapped.</summary>
    private sealed class DuplexPipeStream(IDuplexPipe pipe) : Stream
    {
        private readonly Stream _input = pipe.Input.AsStream();
        private readonly Stream _output = pipe.Output.AsStream();

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => _input.ReadAsync(buffer, cancellationToken);

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) => _output.WriteAsync(buffer, cancellationToken);

        public override Task FlushAsync(CancellationToken cancellationToken) => _output.FlushAsync(cancellationToken);

        public override int Read(byte[] buffer, int offset, int count) => _input.Read(buffer, offset, count);

        public override void Write(byte[] buffer, int offset, int count) => _output.Write(buffer, offset, count);

        public override void Flush() => _output.Flush();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();
    }
}

/// <summary>
/// What tunnelled requests may do. Runs before the forwarded headers: a tunnelled request comes from loopback,
/// but it is anyone on the internet, so it gets an address that is neither local nor trusted (one per node, so
/// rate limits apply per node) and loses any forwarded headers.
/// <list type="bullet">
/// <item>The handshakes (pairing a device, joining a share) and <c>/health</c> are open to any node.</item>
/// <item>Other nodes must be peers, neither revoked nor expired.</item>
/// <item>Devices use the API like any client (they sign in); servers only reach the peer API.</item>
/// <item>Audio streams count against the peer's and the global limit of simultaneous streams.</item>
/// </list>
/// </summary>
internal sealed class P2PGateMiddleware(RequestDelegate next)
{
    public const string PairPath = "/rest/v1/p2p/pair";
    public const string JoinPath = "/rest/v1/p2p/join";
    public const string PeerApiPrefix = "/rest/v1/peer";

    /// <summary>What the mStream apps reach through their tunnel: the mStream extension's routes.</summary>
    private static readonly string[] MstreamPrefixes = ["/api", "/media", "/album-art", "/transcode"];

    public async Task InvokeAsync(HttpContext context, P2PPeerDirectory directory, P2PSettings settings, P2PTraffic traffic, P2PActivityLog activity,
        IArcanistDbContextFactory databases, TimeProvider time)
    {
        if (context.Features.Get<IP2PConnectionFeature>() is not { } connection)
        {
            await next(context);
            return;
        }

        context.Connection.RemoteIpAddress = AddressOf(connection.NodeId);
        foreach (var header in context.Request.Headers.Keys.Where(h => h.StartsWith("X-Forwarded-", StringComparison.OrdinalIgnoreCase)).ToList())
            context.Request.Headers.Remove(header);
        context.Request.Headers.Remove("Forwarded");

        var path = context.Request.Path;
        if (connection.Mstream)
        {
            // mStream's quick connect: the secret opened the tunnel; behind it, only mStream's routes, with their
            // own sign-in, and only while the quick connect is on.
            var tunnels = context.RequestServices.GetService<IAppTunnels>();
            if (tunnels is null || !await tunnels.MstreamAllowedAsync(context.RequestAborted)
                                || !MstreamPrefixes.Any(p => path.StartsWithSegments(p, StringComparison.OrdinalIgnoreCase)))
            {
                await Problems.Create(StatusCodes.Status404NotFound, ErrorCodes.NotFound).ExecuteAsync(context);
                return;
            }

            await next(context);
            return;
        }

        // The export API checks its own migration code: any node that has one may migrate this server.
        if (path.Equals(PairPath, StringComparison.OrdinalIgnoreCase) || path.Equals(JoinPath, StringComparison.OrdinalIgnoreCase)
                                                                      || path.Equals("/health", StringComparison.OrdinalIgnoreCase)
                                                                      || path.StartsWithSegments(ServerMigration.ExportApi.Prefix, StringComparison.OrdinalIgnoreCase))
        {
            await next(context);
            return;
        }

        var peer = await directory.FindAsync(connection.NodeId, context.RequestAborted);
        if (peer is null)
        {
            await Problems.Create(StatusCodes.Status403Forbidden, ErrorCodes.PeerUnknown).ExecuteAsync(context);
            return;
        }

        if (peer.Refusal(time.GetUtcNow().UtcDateTime) is { } refusal)
        {
            await Problems.Create(StatusCodes.Status403Forbidden, refusal).ExecuteAsync(context);
            return;
        }

        var isPeerApi = path.StartsWithSegments(PeerApiPrefix, StringComparison.OrdinalIgnoreCase);
        if (isPeerApi != (peer.Kind == P2PPeerKind.Server))
        {
            await Problems.Create(StatusCodes.Status404NotFound, ErrorCodes.NotFound).ExecuteAsync(context);
            return;
        }

        context.Items[typeof(P2PPeerSnapshot)] = peer;
        if (!IsStream(context.Request))
        {
            await next(context);
            return;
        }

        var peerTraffic = traffic.For(connection.NodeId);
        var global = settings.Current.MaxStreams;
        if ((peer.Rules.MaxStreams is { } own && peerTraffic.ActiveStreams >= own) || (global is { } all && traffic.ActiveStreams >= all))
        {
            activity.Add(P2PActivityKind.Refused, peer.Id, peer.Name, ErrorCodes.TooManyStreams);
            await Problems.Create(StatusCodes.Status429TooManyRequests, ErrorCodes.TooManyStreams).ExecuteAsync(context);
            return;
        }

        Interlocked.Increment(ref peerTraffic.Streams);
        if (StartsPlayback(context.Request))
        {
            Interlocked.Increment(ref peerTraffic.StreamsStarted);
            activity.Add(P2PActivityKind.Streamed, peer.Id, peer.Name, await PlayedAsync(context.Request, databases, context.RequestAborted));
        }
        try
        {
            await next(context);
        }
        finally
        {
            Interlocked.Decrement(ref peerTraffic.Streams);
        }
    }

    /// <summary>The request asks for audio: the REST API's, the peer API's, Subsonic's or mStream's.</summary>
    internal static bool IsStream(HttpRequest request)
    {
        var path = request.Path.Value ?? "";
        return path.StartsWith("/rest/v1/stream/", StringComparison.OrdinalIgnoreCase)
               || path.StartsWith("/rest/v1/peer/stream", StringComparison.OrdinalIgnoreCase)
               || path.StartsWith("/rest/stream", StringComparison.OrdinalIgnoreCase)
               || path.StartsWith("/rest/download", StringComparison.OrdinalIgnoreCase)
               || path.StartsWith("/media/", StringComparison.OrdinalIgnoreCase)
               || path.StartsWith("/transcode/", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>What is being played, for the activity log: the track's title, or the file's name.</summary>
    private static async Task<string?> PlayedAsync(HttpRequest request, IArcanistDbContextFactory databases, CancellationToken cancellationToken)
    {
        var path = request.Path.Value ?? "";
        if (path.StartsWith("/rest/v1/stream/", StringComparison.OrdinalIgnoreCase) && Guid.TryParse(path["/rest/v1/stream/".Length..], out var trackId))
        {
            await using var db = databases.CreateDbContext();
            return await db.Tracks.Where(t => t.Id == trackId).Select(t => t.Title).FirstOrDefaultAsync(cancellationToken);
        }

        var file = request.Query["path"].ToString();
        return file.Length > 0 ? file[(file.LastIndexOf('/') + 1)..] : null;
    }

    /// <summary>A first request for the audio, not one of the range requests that follow while it plays.</summary>
    private static bool StartsPlayback(HttpRequest request) =>
        HttpMethods.IsGet(request.Method)
        && (request.Headers.Range.Count == 0 || request.Headers.Range.ToString().StartsWith("bytes=0-", StringComparison.Ordinal));

    /// <summary>
    /// An address in 100::/64, the IPv6 discard prefix: never local, never a trusted proxy, and different for each
    /// node, so rate limits by address apply per node.
    /// </summary>
    internal static IPAddress AddressOf(string nodeId)
    {
        var bytes = new byte[16];
        bytes[0] = 0x01;
        Convert.FromHexString(nodeId.AsSpan(0, 16)).CopyTo(bytes, 8);
        return new IPAddress(bytes);
    }
}

public static class P2PRequest
{
    /// <summary>The peer behind a tunnelled request; null for any other request.</summary>
    public static P2PPeerSnapshot? Peer(HttpContext context) => context.Items.TryGetValue(typeof(P2PPeerSnapshot), out var peer) ? peer as P2PPeerSnapshot : null;

    /// <summary>The node behind a tunnelled request, peer or not (the handshakes).</summary>
    public static string? NodeId(HttpContext context) => context.Features.Get<IP2PConnectionFeature>()?.NodeId;
}
