using System.Collections.Concurrent;
using Arcanist.Core.Application.Analysis;

namespace Arcanist.Core.Analysis;

/// <summary>
/// Current <see cref="AnalysisProgress"/>, kept by the worker and observed by the admin panel. Changes are
/// broadcast at most once a second, except changes of activity, which go out at once.
/// </summary>
public sealed class AnalysisMonitor(TimeProvider time) : IObservable<AnalysisProgress>
{
    private static readonly TimeSpan BroadcastInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan SpeedWindow = TimeSpan.FromMinutes(5);

    private readonly ConcurrentDictionary<Guid, IObserver<AnalysisProgress>> _observers = new();
    private readonly Queue<DateTime> _recent = new();
    private readonly Lock _lock = new();
    private long _lastBroadcast;

    public AnalysisProgress Current { get; private set; } = new(AnalysisActivity.Disabled, true, 0, 0, 0, 0, 0, null, null);

    internal void Update(Func<AnalysisProgress, AnalysisProgress> change)
    {
        AnalysisProgress previous, next;
        lock (_lock)
        {
            previous = Current;
            next = change(previous) with { FilesPerMinute = Speed() };
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

    /// <summary>Records analyzed files, for <see cref="AnalysisProgress.FilesPerMinute"/>.</summary>
    internal void Analyzed(int count)
    {
        var now = time.GetUtcNow().UtcDateTime;
        lock (_lock)
        {
            for (var i = 0; i < count; i++)
                _recent.Enqueue(now);
        }

        Update(p => p with { LastAnalyzedAt = now });
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

    /// <summary>Asks the background worker to look for work now instead of at its next check.</summary>
    public void RequestRun() => RunRequested?.Invoke();

    internal int SubscriberCount => _observers.Count;

    public IDisposable Subscribe(IObserver<AnalysisProgress> observer)
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
