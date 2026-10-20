using System.Collections.Concurrent;
using System.Threading.Channels;
using Arcanist.Core.Application.P2P;
using Arcanist.Core.Application.Streaming;
using Arcanist.Core.Caching;
using Arcanist.Core.Data;
using Arcanist.Core.Data.Entities;
using Arcanist.Core.Hosting;
using Arcanist.Core.Storage;

namespace Arcanist.Core.P2P;

/// <summary>How libraries shared by other servers are played here (the setting <see cref="SettingKeys.RemoteLibraries"/>).</summary>
public sealed record RemoteLibrariesConfig(bool CacheEnabled, long CacheMaxBytes, int CacheMaxAgeDays, RemoteTranscoding Transcoding)
{
    public static readonly RemoteLibrariesConfig Default = new(true, 10_000_000_000, 60, RemoteTranscoding.Auto);
}

/// <summary>The setting, cached: every stream of a shared library reads it.</summary>
public sealed class RemoteLibrariesSettings(SettingsStore store)
{
    private volatile RemoteLibrariesConfig? _current;

    public RemoteLibrariesConfig Current => _current ?? RemoteLibrariesConfig.Default;

    public async Task<RemoteLibrariesConfig> GetAsync(CancellationToken cancellationToken) =>
        _current ??= await store.GetAsync(SettingKeys.RemoteLibraries, RemoteLibrariesConfig.Default, cancellationToken);

    public async Task SetAsync(RemoteLibrariesConfig config, CancellationToken cancellationToken)
    {
        await store.SetAsync(SettingKeys.RemoteLibraries, config, cancellationToken);
        _current = config;
    }
}

/// <summary>
/// Songs of libraries other servers share, kept here (<c>cache/peers</c>, by content hash; the least recently
/// played go first) when their owner allows it (<see cref="P2PRules.AllowCache"/>) and sends originals (no bit-rate
/// cap). Playing a song not cached yet fetches the whole original in the background, one at a time; from then on
/// it plays from here, with ranges, converted here if need be, and without the other server.
/// Never a download nor a copy in a library: only for playing.
/// </summary>
public sealed class PeerCache : BackgroundService
{
    private readonly RemoteLibrariesSettings _settings;
    private readonly IServiceProvider _services;
    private readonly ILogger<PeerCache> _logger;
    private readonly Channel<StreamTarget> _queue = Channel.CreateBounded<StreamTarget>(
        new BoundedChannelOptions(100) { FullMode = BoundedChannelFullMode.DropWrite, SingleReader = true });
    private readonly ConcurrentDictionary<string, byte> _queued = new(StringComparer.Ordinal);
    private DateTime _purgedAt;

    public PeerCache(ArcanistPaths paths, RemoteLibrariesSettings settings, IServiceProvider services, TimeProvider time, ILogger<PeerCache> logger)
    {
        _settings = settings;
        _services = services;
        _logger = logger;
        Cache = new DiskCache(Path.Combine(paths.Cache, "peers"), () => settings.Current.CacheMaxBytes, time, logger);
    }

    public DiskCache Cache { get; }

    /// <summary>Whether songs of a share can be kept: its owner allows it and sends originals.</summary>
    public static bool Allowed(P2PRules rules) => rules is { AllowCache: true, MaxBitRate: null };

    /// <summary>The cached original of a song of a shared library, if any. Parts of a file split by a cue sheet are not cached.</summary>
    public string? Find(StreamTarget target) =>
        target.StartMs is null && _settings.Current.CacheEnabled ? Cache.TryGet(target.ContentHash) : null;

    /// <summary>Fetches the whole original in the background, unless it is cached, queued, or not to be kept.</summary>
    public void Prefetch(StreamTarget target, P2PRules rules)
    {
        if (target.StartMs is not null || !_settings.Current.CacheEnabled || !Allowed(rules) || Cache.Contains(target.ContentHash))
            return;
        if (_queued.TryAdd(target.ContentHash, 0) && !_queue.Writer.TryWrite(target))
            _queued.TryRemove(target.ContentHash, out _);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var target in _queue.Reader.ReadAllAsync(stoppingToken))
            {
                try
                {
                    await FetchAsync(target, stoppingToken);
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    _logger.LogDebug("Could not keep {Path} of a shared library: {Error}", target.RelativePath, e.Message);
                }
                finally
                {
                    _queued.TryRemove(target.ContentHash, out _);
                }

                if (DateTime.UtcNow - _purgedAt > TimeSpan.FromHours(1))
                {
                    Cache.Purge(TimeSpan.FromDays(_settings.Current.CacheMaxAgeDays));
                    _purgedAt = DateTime.UtcNow;
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    private async Task FetchAsync(StreamTarget target, CancellationToken cancellationToken)
    {
        if (Cache.Contains(target.ContentHash))
            return;
        var storage = (PeerStorage)_services.GetRequiredService<ILibraryStorageResolver>().For(LibrarySourceKind.Peer);
        var partial = Cache.NewPartialPath();
        try
        {
            await using (var input = await storage.OpenStreamAsync(target.Location, target.RelativePath, cancellationToken))
            await using (var output = new FileStream(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, true))
                await input.CopyToAsync(output, cancellationToken);

            // Only the whole original: a converted stream (the owner's rules changed) or a cut one is not it.
            if (new FileInfo(partial).Length != target.Size)
                return;
            Cache.Commit(partial, target.ContentHash);
        }
        finally
        {
            DiskCache.Discard(partial);
        }
    }
}
