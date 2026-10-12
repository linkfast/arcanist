namespace Arcanist.Core.Online;

/// <summary>
/// Lets one request through per interval, in order. Online services ask clients to stay under a request rate
/// (MusicBrainz: one per second per application), so each service gets its own gate.
/// </summary>
public sealed class RateGate(TimeSpan interval, TimeProvider time)
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private long _last;

    public async Task WaitAsync(CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (_last != 0)
            {
                var remaining = interval - time.GetElapsedTime(_last);
                if (remaining > TimeSpan.Zero)
                    await Task.Delay(remaining, time, cancellationToken);
            }

            _last = time.GetTimestamp();
        }
        finally
        {
            _lock.Release();
        }
    }
}
