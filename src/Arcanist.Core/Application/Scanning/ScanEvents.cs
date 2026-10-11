using System.Collections.Concurrent;
using Arcanist.Core.Data.Entities;
using Arcanist.Core.Scanning;
using ScanJobEntity = Arcanist.Core.Data.Entities.ScanJob;

namespace Arcanist.Core.Application.Scanning;

/// <summary>
/// Live feed of scan job changes for subscriptions: status changes as they happen, and progress at most every
/// <see cref="ProgressInterval"/> per job so clients are not flooded during large scans.
/// </summary>
public sealed class ScanEvents : IObservable<ScanJob>
{
    public static readonly TimeSpan ProgressInterval = TimeSpan.FromMilliseconds(500);

    private readonly ConcurrentDictionary<Guid, IObserver<ScanJob>> _observers = new();
    private readonly ConcurrentDictionary<Guid, ScanJob> _active = new();
    private readonly ConcurrentDictionary<Guid, long> _lastProgress = new();
    private readonly ScanProgressTracker _progress;
    private readonly TimeProvider _time;

    public ScanEvents(ScanProgressTracker progress, TimeProvider time)
    {
        _progress = progress;
        _time = time;
        progress.Changed += OnProgress;
    }

    /// <summary>Publishes the current state of a job (queued, started, counters changed, finished).</summary>
    public void Publish(ScanJobEntity job)
    {
        var model = ScanJobMapping.ToModel(job, job.Status == ScanStatus.Running ? _progress.Get(job.Id) : null);

        if (job.Status is ScanStatus.Queued or ScanStatus.Running)
            _active[job.Id] = model;
        else
        {
            _active.TryRemove(job.Id, out _);
            _lastProgress.TryRemove(job.Id, out _);
        }

        Broadcast(model);
    }

    internal int SubscriberCount => _observers.Count;

    public IDisposable Subscribe(IObserver<ScanJob> observer)
    {
        var id = Guid.NewGuid();
        _observers[id] = observer;
        return new Unsubscriber(() => _observers.TryRemove(id, out _));
    }

    private void OnProgress(Guid jobId, ScanProgress progress)
    {
        if (!_active.TryGetValue(jobId, out var job))
            return;

        var now = _time.GetTimestamp();
        var last = _lastProgress.GetOrAdd(jobId, 0);
        if (last != 0 && _time.GetElapsedTime(last, now) < ProgressInterval)
            return;

        _lastProgress[jobId] = now;
        Broadcast(job with { Progress = progress });
    }

    private void Broadcast(ScanJob job)
    {
        foreach (var observer in _observers.Values)
            observer.OnNext(job);
    }

    private sealed class Unsubscriber(Action dispose) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                dispose();
        }
    }
}

internal static class ScanJobMapping
{
    public static ScanJob ToModel(ScanJobEntity job, ScanProgress? progress) => new(
        job.Id, job.LibraryId, job.Kind, job.Status, job.RequestedAt, job.StartedAt, job.FinishedAt,
        job.FilesSeen, job.FilesAdded, job.FilesUpdated, job.FilesMissing, job.Errors, job.ErrorCode, progress);
}

/// <summary>Passes on only the events a predicate accepts.</summary>
internal sealed class FilteredObservable<T>(IObservable<T> source, Func<T, bool> predicate) : IObservable<T>
{
    public IDisposable Subscribe(IObserver<T> observer) => source.Subscribe(new Filter(observer, predicate));

    private sealed class Filter(IObserver<T> inner, Func<T, bool> predicate) : IObserver<T>
    {
        public void OnNext(T value)
        {
            if (predicate(value))
                inner.OnNext(value);
        }

        public void OnError(Exception error) => inner.OnError(error);

        public void OnCompleted() => inner.OnCompleted();
    }
}
