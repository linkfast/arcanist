using System.Collections.Concurrent;

namespace Arcanist.Core.Organization;

/// <summary>
/// Keeps scans, organizing and imports of one library from overlapping: a scan must not list a folder while files
/// move through it, or it would report them missing. Also tells the library watcher which changes are Arcanist's own.
/// </summary>
public sealed class LibraryLocks(TimeProvider time)
{
    /// <summary>Changes seen this long after Arcanist wrote to a library are taken as its own (watchers lag).</summary>
    private static readonly TimeSpan EchoWindow = TimeSpan.FromSeconds(5);

    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _locks = new();
    private readonly ConcurrentDictionary<Guid, int> _writers = new();
    private readonly ConcurrentDictionary<Guid, DateTimeOffset> _lastWrite = new();

    /// <summary>Waits until no one else works on the library. Dispose the result to let others in.</summary>
    public async Task<IDisposable> AcquireAsync(Guid libraryId, CancellationToken cancellationToken)
    {
        var gate = _locks.GetOrAdd(libraryId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        return new Releaser(() => gate.Release());
    }

    /// <summary>Like <see cref="AcquireAsync"/>, for work that changes files: their changes are not reported as the user's.</summary>
    public async Task<IDisposable> AcquireForWritingAsync(Guid libraryId, CancellationToken cancellationToken)
    {
        var held = await AcquireAsync(libraryId, cancellationToken);
        _writers.AddOrUpdate(libraryId, 1, (_, count) => count + 1);
        return new Releaser(() =>
        {
            _lastWrite[libraryId] = time.GetUtcNow();
            _writers.AddOrUpdate(libraryId, 0, (_, count) => count - 1);
            held.Dispose();
        });
    }

    /// <summary>Whether Arcanist is changing the library's files, or just did.</summary>
    public bool IsWriting(Guid libraryId) =>
        _writers.TryGetValue(libraryId, out var count) && count > 0
        || _lastWrite.TryGetValue(libraryId, out var last) && time.GetUtcNow() - last < EchoWindow;

    private sealed class Releaser(Action release) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
                release();
        }
    }
}
