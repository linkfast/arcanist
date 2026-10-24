using Arcanist.Core.Auth;

namespace Arcanist.Core.Application.Catalog;

/// <summary>
/// Something changed in the catalog: tracks edited or deleted through the API, or a scan that added, updated or lost
/// files. Part of the API contract (the <c>catalogChanged</c> subscription). Clients refresh with
/// <c>tracks(changedSince:)</c> and <c>trackIds</c>; the ids here say what to look at first.
/// </summary>
/// <param name="LibraryIds">The libraries it happened in (only those the user can access are told).</param>
/// <param name="UpdatedTrackIds">Tracks whose tags, covers or copies changed (empty after a scan: it can be many).</param>
/// <param name="RemovedTrackIds">Tracks deleted through the API.</param>
/// <param name="Scanned">A scan changed the library: refresh with <c>changedSince</c> and <c>trackIds</c>.</param>
public sealed record CatalogChange(
    IReadOnlyList<Guid> LibraryIds, IReadOnlyList<Guid> UpdatedTrackIds, IReadOnlyList<Guid> RemovedTrackIds, bool Scanned, DateTime ChangedAt);

/// <summary>Tells <c>catalogChanged</c> subscribers what the catalog just did.</summary>
public sealed class CatalogSignals(TimeProvider time)
{
    private event Action<CatalogChange>? Changed;

    internal void Publish(IEnumerable<Guid> libraryIds, IEnumerable<Guid> updated, IEnumerable<Guid> removed, bool scanned = false)
    {
        var libraries = libraryIds.Distinct().ToList();
        if (libraries.Count == 0)
            return;
        Changed?.Invoke(new CatalogChange(libraries, updated.Distinct().ToList(), removed.Distinct().ToList(), scanned, time.GetUtcNow().UtcDateTime));
    }

    /// <summary>The changes in libraries <paramref name="access"/> can use, with only those libraries.</summary>
    internal IObservable<CatalogChange> For(UserAccess access) => new Feed(this, access);

    private sealed class Feed(CatalogSignals signals, UserAccess access) : IObservable<CatalogChange>
    {
        public IDisposable Subscribe(IObserver<CatalogChange> observer)
        {
            void Handle(CatalogChange change)
            {
                var visible = change.LibraryIds.Where(access.CanAccessLibrary).ToList();
                if (visible.Count > 0)
                    observer.OnNext(change with { LibraryIds = visible });
            }

            signals.Changed += Handle;
            return new Unsubscriber(() => signals.Changed -= Handle);
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
