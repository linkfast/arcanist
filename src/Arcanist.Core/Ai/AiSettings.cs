using System.Collections.Concurrent;
using Arcanist.Core.Application.Ai;
using Arcanist.Core.Data;

namespace Arcanist.Core.Ai;

/// <summary>Bound from <c>Arcanist:Ai</c>.</summary>
public sealed class AiOptions
{
    public const string Section = "Arcanist:Ai";

    /// <summary>Master switch of the background analysis for this process (tests, constrained machines).</summary>
    public bool Enabled { get; set; } = true;
}

/// <summary>The AI extension's settings, kept in <c>ai.settings</c>.</summary>
public sealed record AiConfig(int Threads, bool PauseWhileScanning, bool PauseWhileStreaming, bool OnDemandSources, bool ShareServerStats)
{
    public static AiConfig Default { get; } = new(1, true, false, true, true);

    /// <summary>Threads the server may give the analysis: half its cores, at most four.</summary>
    public static int MaxThreads => Math.Clamp(Environment.ProcessorCount / 2, 1, 4);

    public AiSettingsModel ToModel() => new(Threads, PauseWhileScanning, PauseWhileStreaming, OnDemandSources, ShareServerStats);
}

/// <summary>Reads (and caches) the AI extension's settings.</summary>
public sealed class AiSettings(SettingsStore store)
{
    private AiConfig? _cached;

    public async Task<AiConfig> GetAsync(CancellationToken cancellationToken) =>
        _cached ??= Normalize(await store.GetAsync(SettingKeys.Ai, AiConfig.Default, cancellationToken));

    public async Task SetAsync(AiConfig config, CancellationToken cancellationToken)
    {
        config = Normalize(config);
        await store.SetAsync(SettingKeys.Ai, config, cancellationToken);
        _cached = config;
    }

    /// <summary>Forgets the cached settings (after a reset of all settings).</summary>
    public void Invalidate() => _cached = null;

    private static AiConfig Normalize(AiConfig config) => config with { Threads = Math.Clamp(config.Threads, 1, AiConfig.MaxThreads) };
}

/// <summary>What the analysis is doing, for the panel's AI page (broadcast at most once a second, activity changes at once).</summary>
public sealed class AiMonitor(TimeProvider time) : IObservable<AiMonitor.Snapshot>
{
    public sealed record Snapshot(AiActivity Activity, TrackAnalysisProgress Progress, double SongsPerMinute, DateTime? LastAnalyzedAt, string? Error);

    private static readonly TimeSpan BroadcastInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan SpeedWindow = TimeSpan.FromMinutes(5);

    private readonly ConcurrentDictionary<Guid, IObserver<Snapshot>> _observers = new();
    private readonly Queue<DateTime> _recent = new();
    private readonly Lock _lock = new();
    private long _lastBroadcast;

    public Snapshot Current { get; private set; } = new(AiActivity.Disabled, new TrackAnalysisProgress(0, 0, 0, 0), 0, null, null);

    internal void Update(Func<Snapshot, Snapshot> change)
    {
        Snapshot previous, next;
        lock (_lock)
        {
            previous = Current;
            next = change(previous) with { SongsPerMinute = Speed() };
            Current = next;
        }

        var now = time.GetTimestamp();
        if (next.Activity != previous.Activity || time.GetElapsedTime(Interlocked.Read(ref _lastBroadcast), now) >= BroadcastInterval)
        {
            Interlocked.Exchange(ref _lastBroadcast, now);
            foreach (var observer in _observers.Values)
                observer.OnNext(next);
        }
    }

    internal void Analyzed(int count)
    {
        var now = time.GetUtcNow().UtcDateTime;
        lock (_lock)
        {
            for (var i = 0; i < count; i++)
                _recent.Enqueue(now);
        }

        Update(s => s with { LastAnalyzedAt = now });
    }

    private double Speed()
    {
        var now = time.GetUtcNow().UtcDateTime;
        while (_recent.Count > 0 && now - _recent.Peek() > SpeedWindow)
            _recent.Dequeue();
        if (_recent.Count == 0)
            return 0;
        var span = Math.Max((now - _recent.Peek()).TotalMinutes, 1.0 / 60);
        return Math.Round(_recent.Count / span, 1);
    }

    /// <summary>Raised when something asks the worker to look for work now.</summary>
    internal event Action? RunRequested;

    public void RequestRun() => RunRequested?.Invoke();

    public IDisposable Subscribe(IObserver<Snapshot> observer)
    {
        var id = Guid.NewGuid();
        _observers[id] = observer;
        return new Subscription(() => _observers.TryRemove(id, out _));
    }

    private sealed class Subscription(Action dispose) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                dispose();
        }
    }
}

/// <summary>Audio whose analysis just finished (by content hash), for <c>trackAnalysisReady</c>.</summary>
public sealed class TrackAnalysisEvents : IObservable<(string ContentHash, int Version)>
{
    private readonly ConcurrentDictionary<Guid, IObserver<(string, int)>> _observers = new();

    internal int SubscriberCount => _observers.Count;

    internal void Publish(string contentHash, int version)
    {
        foreach (var observer in _observers.Values)
            observer.OnNext((contentHash, version));
    }

    public IDisposable Subscribe(IObserver<(string ContentHash, int Version)> observer)
    {
        var id = Guid.NewGuid();
        _observers[id] = observer;
        return new Unsubscriber(() => _observers.TryRemove(id, out _));
    }

    private sealed class Unsubscriber(Action dispose) : IDisposable
    {
        public void Dispose() => dispose();
    }
}
