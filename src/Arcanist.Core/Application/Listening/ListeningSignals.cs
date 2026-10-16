namespace Arcanist.Core.Application.Listening;

/// <summary>Tells background work that users rated or played something (Ultimate Cloud's StarTracks and play reports).</summary>
public sealed class ListeningSignals
{
    /// <summary>A user's ratings changed, with the user's id.</summary>
    public event Action<Guid>? Rated;

    /// <summary>A play was recorded.</summary>
    public event Action? Played;

    internal void OnRated(Guid userId) => Rated?.Invoke(userId);

    internal void OnPlayed() => Played?.Invoke();

    /// <summary>The changes of a user's StarTracks, for the <c>starTracksChanged</c> subscription.</summary>
    internal IObservable<StarTracksChange> StarTracksOf(Guid userId, TimeProvider time) => new StarTracksFeed(this, userId, time);

    private sealed class StarTracksFeed(ListeningSignals signals, Guid userId, TimeProvider time) : IObservable<StarTracksChange>
    {
        public IDisposable Subscribe(IObserver<StarTracksChange> observer)
        {
            void Handle(Guid user)
            {
                if (user == userId)
                    observer.OnNext(new StarTracksChange(time.GetUtcNow().UtcDateTime));
            }

            signals.Rated += Handle;
            return new Unsubscriber(() => signals.Rated -= Handle);
        }
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
