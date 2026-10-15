using System.Threading.Channels;
using Arcanist.Core.Data;
using Arcanist.Core.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Arcanist.Core.P2P;

/// <summary>A peer as tunnelled requests see it.</summary>
public sealed record P2PPeerSnapshot(
    Guid Id,
    P2PPeerKind Kind,
    string NodeId,
    string Name,
    Guid? UserId,
    P2PRules Rules,
    DateTime? ExpiresAt,
    DateTime? RevokedAt,
    IReadOnlySet<Guid> LibraryIds)
{
    /// <summary>Why the peer may not come in now, as an error code; null when it may.</summary>
    public string? Refusal(DateTime now) =>
        RevokedAt is not null ? Application.ErrorCodes.PeerRevoked
        : ExpiresAt is { } expires && expires <= now ? Application.ErrorCodes.PeerExpired
        : null;
}

/// <summary>
/// Every peer in memory, by node id: each tunnelled request needs its peer, and there are few of them. Reloaded
/// every 30 seconds and at once after any change (<see cref="InvalidateAsync"/>), so a revocation applies
/// immediately.
/// </summary>
public sealed class P2PPeerDirectory(IArcanistDbContextFactory databases, TimeProvider time)
{
    private static readonly TimeSpan MaxAge = TimeSpan.FromSeconds(30);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private volatile IReadOnlyDictionary<string, P2PPeerSnapshot> _peers = new Dictionary<string, P2PPeerSnapshot>();
    private long _loadedAt = long.MinValue;

    /// <summary>The peer as last loaded, without waiting (for the bandwidth limits).</summary>
    public P2PPeerSnapshot? Find(string nodeId) => _peers.GetValueOrDefault(nodeId);

    public async Task<P2PPeerSnapshot?> FindAsync(string nodeId, CancellationToken cancellationToken)
    {
        if (_loadedAt == long.MinValue || time.GetElapsedTime(_loadedAt) > MaxAge)
            await LoadAsync(cancellationToken);
        return Find(nodeId);
    }

    public Task InvalidateAsync(CancellationToken cancellationToken) => LoadAsync(cancellationToken);

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!databases.IsConfigured)
                return;
            await using var db = databases.CreateDbContext();
            var peers = await db.P2PPeers.AsNoTracking().Include(p => p.Libraries).ToListAsync(cancellationToken);
            _peers = peers.ToDictionary(p => p.NodeId, p => new P2PPeerSnapshot(
                p.Id, p.Kind, p.NodeId, p.Name, p.UserId, p.Rules, p.ExpiresAt, p.RevokedAt,
                p.Libraries.Select(l => l.LibraryId).ToHashSet()), StringComparer.Ordinal);
            _loadedAt = time.GetTimestamp();
        }
        finally
        {
            _gate.Release();
        }
    }
}

/// <summary>
/// The activity log: entries are queued and written in the background every second, so logging never slows a
/// request. Entries older than 90 days, or beyond the newest 20,000, are removed every hour.
/// </summary>
public sealed class P2PActivityLog(IArcanistDbContextFactory databases, DatabaseMaintenance maintenance, TimeProvider time, ILogger<P2PActivityLog> logger)
    : BackgroundService
{
    private const int Keep = 20_000;
    private static readonly TimeSpan Retention = TimeSpan.FromDays(90);

    private readonly Channel<P2PActivity> _queue = Channel.CreateBounded<P2PActivity>(
        new BoundedChannelOptions(10_000) { FullMode = BoundedChannelFullMode.DropOldest });

    public void Add(P2PActivityKind kind, Guid? peerId, string peerName, string? detail) =>
        _queue.Writer.TryWrite(new P2PActivity
        {
            At = time.GetUtcNow().UtcDateTime,
            Kind = kind,
            PeerId = peerId,
            PeerName = peerName,
            Detail = detail is { Length: > 500 } ? detail[..500] : detail,
        });

    private readonly SemaphoreSlim _flushing = new(1, 1);

    /// <summary>Writes what is queued now (before reading the log), after any write in progress.</summary>
    public async Task FlushAsync(CancellationToken cancellationToken)
    {
        await _flushing.WaitAsync(cancellationToken);
        try
        {
            var batch = new List<P2PActivity>();
            while (_queue.Reader.TryRead(out var entry))
                batch.Add(entry);
            if (batch.Count == 0 || !databases.IsConfigured)
                return;
            await using var db = databases.CreateDbContext();
            db.P2PActivities.AddRange(batch);
            await db.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            _flushing.Release();
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1), time);
        var tick = 0;
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    using var work = await maintenance.WorkAsync(stoppingToken);
                    await FlushAsync(stoppingToken);
                    if (++tick % 3600 == 0)
                        await PurgeAsync(stoppingToken);
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    logger.LogWarning(e, "The P2P activity log could not be written");
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }

        await FlushAsync(CancellationToken.None);
    }

    private async Task PurgeAsync(CancellationToken cancellationToken)
    {
        if (!databases.IsConfigured)
            return;
        await using var db = databases.CreateDbContext();
        var cutoff = time.GetUtcNow().UtcDateTime - Retention;
        await db.P2PActivities.Where(a => a.At < cutoff).ExecuteDeleteAsync(cancellationToken);
        var oldestKept = await db.P2PActivities.OrderByDescending(a => a.At).Skip(Keep).Select(a => (DateTime?)a.At).FirstOrDefaultAsync(cancellationToken);
        if (oldestKept is { } at)
            await db.P2PActivities.Where(a => a.At <= at).ExecuteDeleteAsync(cancellationToken);
    }
}
