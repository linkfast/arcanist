namespace Arcanist.Core.Media;

/// <summary>How many audio streams are being served right now (players, apps, DLNA), so background work can wait.</summary>
public sealed class StreamActivity
{
    private int _active;

    public int Active => Volatile.Read(ref _active);

    /// <summary>Counts a stream until the returned handle is disposed.</summary>
    public IDisposable Begin()
    {
        Interlocked.Increment(ref _active);
        return new Handle(this);
    }

    private sealed class Handle(StreamActivity owner) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                Interlocked.Decrement(ref owner._active);
        }
    }
}
