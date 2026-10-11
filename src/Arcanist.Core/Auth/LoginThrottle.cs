using System.Collections.Concurrent;
using Microsoft.Extensions.Options;

namespace Arcanist.Core.Auth;

/// <summary>
/// Per-account lockout after repeated failed logins, growing exponentially (30 s, 1 min, 2 min… up to 15 min).
/// Complements the per-IP rate limiter against distributed guessing of one account. In-memory: an Arcanist
/// server is a single process.
/// </summary>
public sealed class LoginThrottle(IOptions<AuthOptions> options, TimeProvider time)
{
    private static readonly TimeSpan BaseLockout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan MaxLockout = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan FailureMemory = TimeSpan.FromMinutes(15);

    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);

    /// <summary>Remaining lockout for the account, or null when a login attempt is allowed.</summary>
    public TimeSpan? GetLockout(string normalizedUserName)
    {
        if (!_entries.TryGetValue(normalizedUserName, out var entry) || entry.LockedUntil is not { } until)
            return null;

        var remaining = until - time.GetUtcNow();
        return remaining > TimeSpan.Zero ? remaining : null;
    }

    public void RecordFailure(string normalizedUserName)
    {
        var now = time.GetUtcNow();
        _entries.AddOrUpdate(normalizedUserName,
            _ => Next(new Entry(0, now, null), now),
            (_, entry) => Next(now - entry.LastFailure > FailureMemory ? new Entry(0, now, null) : entry, now));
    }

    public void RecordSuccess(string normalizedUserName) => _entries.TryRemove(normalizedUserName, out _);

    private Entry Next(Entry entry, DateTimeOffset now)
    {
        var failures = entry.Failures + 1;
        var excess = failures - options.Value.MaxFailedLogins;
        if (excess < 0)
            return new Entry(failures, now, null);

        var lockout = TimeSpan.FromTicks(Math.Min(MaxLockout.Ticks, BaseLockout.Ticks << Math.Min(excess, 10)));
        return new Entry(failures, now, now + lockout);
    }

    private sealed record Entry(int Failures, DateTimeOffset LastFailure, DateTimeOffset? LockedUntil);
}
