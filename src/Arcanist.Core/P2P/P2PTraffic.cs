using System.Collections.Concurrent;
using Arcanist.Core.Data;
using Arcanist.Core.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Arcanist.Core.P2P;

/// <summary>
/// Limits a rate in bytes per second, allowing a burst of one second. A rate of zero or less means no limit; the
/// rate is read on every use, so changed limits apply at once.
/// </summary>
internal sealed class TokenBucket(Func<long> rate, TimeProvider time)
{
    private readonly Lock _lock = new();
    private double _tokens;
    private long _last = time.GetTimestamp();

    /// <summary>Takes <paramref name="bytes"/> and returns how long to wait before sending them.</summary>
    public TimeSpan Take(int bytes)
    {
        var limit = rate();
        if (limit <= 0)
            return TimeSpan.Zero;
        lock (_lock)
        {
            var now = time.GetTimestamp();
            _tokens = Math.Min(limit, _tokens + time.GetElapsedTime(_last, now).TotalSeconds * limit);
            _last = now;
            _tokens -= bytes;
            return _tokens >= 0 ? TimeSpan.Zero : TimeSpan.FromSeconds(-_tokens / limit);
        }
    }
}

/// <summary>Live counters of one peer, kept while the server runs and added to its row every minute.</summary>
public sealed class PeerTraffic
{
    internal long Sent;
    internal long Received;
    internal int Connections;
    internal int Streams;
    internal long StreamsStarted;
    internal long ConnectedSeconds;
    internal long Peak;
    internal long LastSecondSent;
    internal long RateSent;
    internal DateTime? DisconnectedAt;

    // Already added to the database.
    internal long SavedSent;
    internal long SavedReceived;
    internal long SavedSeconds;
    internal long SavedStreams;

    internal TokenBucket Bucket = null!;

    public long BytesSent => Interlocked.Read(ref Sent);

    public long BytesReceived => Interlocked.Read(ref Received);

    public int OpenConnections => Volatile.Read(ref Connections);

    public int ActiveStreams => Volatile.Read(ref Streams);

    /// <summary>Bytes per second sent during the last second.</summary>
    public long CurrentRate => Interlocked.Read(ref RateSent);
}

/// <summary>
/// Counts what goes through the P2P network, per peer and in total, and applies the bandwidth limits (the peer's
/// and the global one). Every second it measures the rates and peaks; every minute it saves the totals.
/// </summary>
public sealed class P2PTraffic(
    P2PSettings settings,
    P2PPeerDirectory peers,
    P2PActivityLog activity,
    IArcanistDbContextFactory databases,
    DatabaseMaintenance maintenance,
    TimeProvider time,
    ILogger<P2PTraffic> logger) : BackgroundService
{
    private readonly ConcurrentDictionary<string, PeerTraffic> _byNode = new(StringComparer.Ordinal);
    private TokenBucket? _global;
    private long _globalRate;
    private long _globalPeak;
    private long _lastGlobalSent;

    private TokenBucket Global => _global ??= new TokenBucket(() => KbpsToBytes(settings.Current.BandwidthKbps), time);

    public long GlobalRate => Interlocked.Read(ref _globalRate);

    public long GlobalPeak => Interlocked.Read(ref _globalPeak);

    public int ActiveStreams => _byNode.Values.Sum(t => t.ActiveStreams);

    public PeerTraffic For(string nodeId) => _byNode.GetOrAdd(nodeId, id => new PeerTraffic
    {
        Bucket = new TokenBucket(() => KbpsToBytes(peers.Find(id)?.Rules.BandwidthKbps), time),
    });

    public IReadOnlyDictionary<string, PeerTraffic> Snapshot => _byNode;

    private static long KbpsToBytes(int? kbps) => kbps is > 0 ? kbps.Value * 1000L / 8 : 0;

    internal void Opened(string nodeId)
    {
        var traffic = For(nodeId);
        if (Interlocked.Increment(ref traffic.Connections) == 1
            && (traffic.DisconnectedAt is not { } gone || time.GetUtcNow().UtcDateTime - gone > TimeSpan.FromMinutes(5))
            && peers.Find(nodeId) is { } peer)
        {
            activity.Add(P2PActivityKind.Connected, peer.Id, peer.Name, null);
        }
    }

    internal void Closed(string nodeId)
    {
        var traffic = For(nodeId);
        if (Interlocked.Decrement(ref traffic.Connections) == 0)
            traffic.DisconnectedAt = time.GetUtcNow().UtcDateTime;
    }

    /// <summary>How long to wait before sending <paramref name="bytes"/> to the peer.</summary>
    internal TimeSpan Sending(PeerTraffic traffic, int bytes)
    {
        Interlocked.Add(ref traffic.Sent, bytes);
        var own = traffic.Bucket.Take(bytes);
        var global = Global.Take(bytes);
        return own > global ? own : global;
    }

    internal static void Receiving(PeerTraffic traffic, int bytes) => Interlocked.Add(ref traffic.Received, bytes);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1), time);
        var tick = 0;
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                Measure();
                if (++tick % 60 == 0)
                    await SaveAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }

        await SaveAsync(CancellationToken.None);
    }

    private void Measure()
    {
        long total = 0;
        foreach (var traffic in _byNode.Values)
        {
            var sent = Interlocked.Read(ref traffic.Sent);
            var rate = sent - traffic.LastSecondSent;
            traffic.LastSecondSent = sent;
            Interlocked.Exchange(ref traffic.RateSent, rate);
            if (rate > traffic.Peak)
                traffic.Peak = rate;
            if (traffic.OpenConnections > 0)
                Interlocked.Increment(ref traffic.ConnectedSeconds);
            total += sent;
        }

        var globalRate = total - _lastGlobalSent;
        _lastGlobalSent = total;
        Interlocked.Exchange(ref _globalRate, globalRate);
        if (globalRate > _globalPeak)
            Interlocked.Exchange(ref _globalPeak, globalRate);
    }

    /// <summary>Adds what changed since the last save to each peer's row.</summary>
    internal async Task SaveAsync(CancellationToken cancellationToken)
    {
        if (!databases.IsConfigured || _byNode.IsEmpty)
            return;
        try
        {
            using var work = await maintenance.WorkAsync(cancellationToken);
            await using var db = databases.CreateDbContext();
            foreach (var (nodeId, traffic) in _byNode)
            {
                var sent = Interlocked.Read(ref traffic.Sent);
                var received = Interlocked.Read(ref traffic.Received);
                var seconds = Interlocked.Read(ref traffic.ConnectedSeconds);
                var streams = Interlocked.Read(ref traffic.StreamsStarted);
                var (dSent, dReceived, dSeconds, dStreams) =
                    (sent - traffic.SavedSent, received - traffic.SavedReceived, seconds - traffic.SavedSeconds, streams - traffic.SavedStreams);
                if (dSent == 0 && dReceived == 0 && dSeconds == 0 && dStreams == 0)
                    continue;

                var peak = traffic.Peak;
                var seen = time.GetUtcNow().UtcDateTime;
                await db.P2PPeers.Where(p => p.NodeId == nodeId).ExecuteUpdateAsync(s => s
                    .SetProperty(p => p.BytesSent, p => p.BytesSent + dSent)
                    .SetProperty(p => p.BytesReceived, p => p.BytesReceived + dReceived)
                    .SetProperty(p => p.ConnectedSeconds, p => p.ConnectedSeconds + dSeconds)
                    .SetProperty(p => p.Streams, p => p.Streams + dStreams)
                    .SetProperty(p => p.PeakBytesPerSecond, p => p.PeakBytesPerSecond > peak ? p.PeakBytesPerSecond : peak)
                    .SetProperty(p => p.LastSeenAt, seen), cancellationToken);
                (traffic.SavedSent, traffic.SavedReceived, traffic.SavedSeconds, traffic.SavedStreams) = (sent, received, seconds, streams);
            }
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            logger.LogWarning(e, "P2P traffic could not be saved");
        }
    }
}

/// <summary>
/// Counts the bytes of a tunnelled connection and slows its writes to the bandwidth limits. Writes go out in
/// pieces of at most 16 KB, so a limit is followed smoothly.
/// </summary>
internal sealed class MeteredStream(Stream inner, P2PTraffic traffic, PeerTraffic peer) : Stream
{
    private const int Piece = 16 * 1024;

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var read = await inner.ReadAsync(buffer, cancellationToken);
        P2PTraffic.Receiving(peer, read);
        return read;
    }

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        while (!buffer.IsEmpty)
        {
            var piece = buffer[..Math.Min(Piece, buffer.Length)];
            var wait = traffic.Sending(peer, piece.Length);
            if (wait > TimeSpan.Zero)
                await Task.Delay(wait, cancellationToken);
            await inner.WriteAsync(piece, cancellationToken);
            buffer = buffer[piece.Length..];
        }
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override Task FlushAsync(CancellationToken cancellationToken) => inner.FlushAsync(cancellationToken);

    public override int Read(byte[] buffer, int offset, int count) => ReadAsync(buffer, offset, count).GetAwaiter().GetResult();

    public override void Write(byte[] buffer, int offset, int count) => WriteAsync(buffer, offset, count).GetAwaiter().GetResult();

    public override void Flush() => inner.Flush();

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();
}
