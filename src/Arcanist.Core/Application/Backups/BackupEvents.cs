using System.Collections.Concurrent;

namespace Arcanist.Core.Application.Backups;

/// <summary>A live feed for subscriptions: whatever is published reaches every subscriber.</summary>
public class LiveFeed<T> : IObservable<T>
{
    private readonly ConcurrentDictionary<Guid, IObserver<T>> _observers = new();

    public void Publish(T value)
    {
        foreach (var observer in _observers.Values)
            observer.OnNext(value);
    }

    public IDisposable Subscribe(IObserver<T> observer)
    {
        var id = Guid.NewGuid();
        _observers[id] = observer;
        return new Unsubscriber(() => _observers.TryRemove(id, out _));
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

/// <summary>Backup runs as they change: their state at once, their progress about once a second.</summary>
public sealed class BackupEvents : LiveFeed<BackupJob>;

/// <summary>Restores as they change: their state at once, their progress about once a second.</summary>
public sealed class RestoreEvents : LiveFeed<RestoreJobInfo>;
