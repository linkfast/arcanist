using Arcanist.Core.Application.Backups;
using Arcanist.Core.Application.Scanning;
using Arcanist.Core.Auth;

namespace Arcanist.Core.Application.Playlists;

/// <summary>A playlist changed, with who can see it, so each subscriber only hears of their own.</summary>
internal sealed record PlaylistSignal(PlaylistChange Change, Guid OwnerId, IReadOnlyCollection<Guid> MemberIds, bool IsPublic);

/// <summary>Changes of playlists (their tracks, name or sharing), for the <c>playlistChanged</c> subscription.</summary>
public sealed class PlaylistSignals
{
    private readonly LiveFeed<PlaylistSignal> _feed = new();

    internal void Publish(PlaylistSignal signal) => _feed.Publish(signal);

    /// <summary>The changes of the playlists <paramref name="access"/> sees, or of one of them.</summary>
    internal IObservable<PlaylistChange> For(UserAccess access, Guid? playlistId) =>
        new Mapped(new FilteredObservable<PlaylistSignal>(_feed, s =>
            (playlistId is null || s.Change.PlaylistId == playlistId)
            && (s.OwnerId == access.UserId || s.IsPublic || s.MemberIds.Contains(access.UserId))));

    private sealed class Mapped(IObservable<PlaylistSignal> source) : IObservable<PlaylistChange>
    {
        public IDisposable Subscribe(IObserver<PlaylistChange> observer) => source.Subscribe(new Observer(observer));

        private sealed class Observer(IObserver<PlaylistChange> inner) : IObserver<PlaylistSignal>
        {
            public void OnNext(PlaylistSignal value) => inner.OnNext(value.Change);

            public void OnError(Exception error) => inner.OnError(error);

            public void OnCompleted() => inner.OnCompleted();
        }
    }
}
