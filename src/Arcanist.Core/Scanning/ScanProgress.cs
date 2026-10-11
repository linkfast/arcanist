using System.Collections.Concurrent;

namespace Arcanist.Core.Scanning;

/// <summary>Bound from <c>Arcanist:Scanner</c>.</summary>
public sealed class ScannerOptions
{
    public const string Section = "Arcanist:Scanner";

    /// <summary>Files read at the same time. Lower it for slow network shares.</summary>
    public int Parallelism { get; set; } = 3;

    /// <summary>Files written to the database per transaction.</summary>
    public int BatchSize { get; set; } = 500;

    /// <summary>Days a file may stay missing before it leaves the catalog (to survive a disk briefly unplugged).</summary>
    public int MissingFileRetentionDays { get; set; } = 7;
}

public enum ScanPhase
{
    /// <summary>Listing the files of the library's sources.</summary>
    Discovering,

    /// <summary>Reading tags of new and changed files.</summary>
    Reading,

    /// <summary>Removing what no longer exists from the catalog.</summary>
    CleaningUp,
}

/// <summary>Live progress of a running scan. Part of the API contract.</summary>
public sealed record ScanProgress(ScanPhase Phase, int FilesFound, int FilesToRead, int FilesRead);

/// <summary>In-memory progress of running scans; the database only stores totals when a scan ends.</summary>
public sealed class ScanProgressTracker
{
    private readonly ConcurrentDictionary<Guid, Counter> _running = new();

    public event Action<Guid, ScanProgress>? Changed;

    public ScanProgress? Get(Guid jobId) => _running.TryGetValue(jobId, out var counter) ? counter.Snapshot() : null;

    internal Counter Start(Guid jobId) => _running[jobId] = new Counter(this, jobId);

    internal void Stop(Guid jobId) => _running.TryRemove(jobId, out _);

    internal sealed class Counter(ScanProgressTracker owner, Guid jobId)
    {
        private int _phase;
        private int _found;
        private int _toRead;
        private int _read;

        public void Phase(ScanPhase phase)
        {
            Interlocked.Exchange(ref _phase, (int)phase);
            Notify();
        }

        public void Found(int count) => Interlocked.Add(ref _found, count);

        public void ToRead(int count) => Interlocked.Add(ref _toRead, count);

        public void Read(int count)
        {
            Interlocked.Add(ref _read, count);
            Notify();
        }

        public ScanProgress Snapshot() =>
            new((ScanPhase)Volatile.Read(ref _phase), Volatile.Read(ref _found), Volatile.Read(ref _toRead), Volatile.Read(ref _read));

        private void Notify() => owner.Changed?.Invoke(jobId, Snapshot());
    }
}
