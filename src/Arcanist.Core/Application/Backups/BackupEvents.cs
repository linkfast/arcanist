using System.Collections.Concurrent;

namespace Arcanist.Core.Application.Backups;

/// <summary>Live feed of backup runs for subscriptions: changes of state at once, progress about once a second.</summary>
public sealed class BackupEvents : IObservable<BackupJob>
{
    private readonly ConcurrentDictionary<Guid, IObserver<BackupJob>> _observers = new();

    public void Publish(BackupJob job)
    {
        foreach (var observer in _observers.Values)
            observer.OnNext(job);
    }

    public IDisposable Subscribe(IObserver<BackupJob> observer)
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
