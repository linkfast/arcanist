using System.Collections.Concurrent;
using Arcanist.Core.Application.Backups;
using Arcanist.Core.Application.Scanning;

namespace Arcanist.Core.Application.Jams;

/// <summary>
/// Changes of Jukebox Jams, for the <c>jukeboxJamChanged</c> subscription and for <c>JukeboxJamPlayer</c>; and a lock
/// per Jam, so votes and additions are counted one at a time.
/// </summary>
public sealed class JukeboxJamSignals
{
    private readonly LiveFeed<(Guid JamId, JukeboxJamChange Change)> _feed = new();
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _locks = new();
    private readonly ConcurrentDictionary<Guid, ConcurrentDictionary<Guid, byte>> _rejected = new();

    /// <summary>A Jam changed (its id and the change).</summary>
    public event Action<Guid, JukeboxJamChange>? Changed;

    internal void Publish(Guid jamId, JukeboxJamChange change)
    {
        _feed.Publish((jamId, change));
        Changed?.Invoke(jamId, change);
    }

    /// <summary>The changes of the Jam with this code (its code is what lets anyone follow it).</summary>
    internal IObservable<JukeboxJamChange> For(string code) =>
        new Changes(new FilteredObservable<(Guid JamId, JukeboxJamChange Change)>(_feed, c => c.Change.Code == code));

    /// <summary>Someone took a suggestion out: it is not suggested again in this Jam (in memory: a restart forgets).</summary>
    internal void RejectSuggestion(Guid jamId, Guid trackId) => _rejected.GetOrAdd(jamId, _ => new())[trackId] = 0;

    internal IReadOnlyCollection<Guid> RejectedSuggestions(Guid jamId) =>
        _rejected.TryGetValue(jamId, out var rejected) ? rejected.Keys.ToList() : [];

    internal async Task<IDisposable> LockAsync(Guid jamId, CancellationToken cancellationToken)
    {
        var gate = _locks.GetOrAdd(jamId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        return new Release(gate);
    }

    private sealed class Release(SemaphoreSlim gate) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
                gate.Release();
        }
    }

    private sealed class Changes(IObservable<(Guid JamId, JukeboxJamChange Change)> source) : IObservable<JukeboxJamChange>
    {
        public IDisposable Subscribe(IObserver<JukeboxJamChange> observer) => source.Subscribe(new Observer(observer));

        private sealed class Observer(IObserver<JukeboxJamChange> inner) : IObserver<(Guid JamId, JukeboxJamChange Change)>
        {
            public void OnNext((Guid JamId, JukeboxJamChange Change) value) => inner.OnNext(value.Change);

            public void OnError(Exception error) => inner.OnError(error);

            public void OnCompleted() => inner.OnCompleted();
        }
    }
}
