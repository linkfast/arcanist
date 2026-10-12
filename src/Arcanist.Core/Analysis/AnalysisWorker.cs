using System.Collections.Concurrent;
using Arcanist.Core.Application.Analysis;
using Arcanist.Core.Application.Scanning;
using Arcanist.Core.Data;
using Arcanist.Core.Data.Entities;
using Arcanist.Core.Media;
using Arcanist.Core.Setup;
using Arcanist.Core.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Arcanist.Core.Analysis;

/// <summary>Bound from <c>Arcanist:Analysis</c>.</summary>
public sealed class AnalysisOptions
{
    public const string Section = "Arcanist:Analysis";

    /// <summary>Master switch for this process, for tests and constrained machines; the admin panel has its own.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Files analyzed at once. Each takes a core for about a second per four minutes of audio.</summary>
    public int Parallelism { get; set; } = Math.Clamp(Environment.ProcessorCount / 2, 1, 4);

    public int BatchSize { get; set; } = 32;
}

/// <summary>
/// Analyzes the catalog's audio in the background: each distinct piece of audio (by content hash) once per
/// analyzer version, on low-priority threads, pausing while scans run. Results feed loudness normalization,
/// duplicate detection, waveforms, tempo and key.
/// </summary>
internal sealed class AnalysisWorker(
    IArcanistDbContextFactory databases,
    ILibraryStorageResolver storages,
    AnalysisMonitor monitor,
    ScanEvents scans,
    SettingsStore settings,
    FfmpegLocator ffmpeg,
    DuplicateDetector duplicates,
    IOptionsMonitor<SetupOptions> setup,
    IOptionsMonitor<AnalysisOptions> options,
    TimeProvider time,
    ILogger<AnalysisWorker> logger) : BackgroundService, IObserver<Application.Scanning.ScanJob>
{
    private static readonly TimeSpan IdleDelay = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan RetryUnreadable = TimeSpan.FromHours(1);

    private readonly SemaphoreSlim _wake = new(0);

    /// <summary>Audio whose file could not be opened (an offline share), skipped for a while instead of failed.</summary>
    private readonly ConcurrentDictionary<string, DateTime> _unreadable = new();

    /// <summary>Asks the worker to look for work now (after a scan, or when re-enabled).</summary>
    public void Wake()
    {
        if (_wake.CurrentCount == 0)
            _wake.Release();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();
        using var subscription = scans.Subscribe(this);
        monitor.RunRequested += Wake;

        while (!stoppingToken.IsCancellationRequested)
        {
            TimeSpan delay;
            try
            {
                delay = await RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception e)
            {
                logger.LogError(e, "Audio analysis failed");
                monitor.Update(p => p with { Error = e.Message });
                delay = TimeSpan.FromMinutes(1);
            }

            if (delay > TimeSpan.Zero)
            {
                try
                {
                    await _wake.WaitAsync(delay, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
    }

    /// <summary>Does one step of work and says how long to wait before the next.</summary>
    internal async Task<TimeSpan> RunOnceAsync(CancellationToken cancellationToken)
    {
        if (!options.CurrentValue.Enabled || !setup.CurrentValue.Completed || !databases.IsConfigured)
        {
            monitor.Update(p => p with { Activity = AnalysisActivity.Disabled });
            return TimeSpan.FromMinutes(1);
        }

        if (!AudioAnalyzer.IsAvailable)
        {
            monitor.Update(p => p with { Activity = AnalysisActivity.Unavailable, Error = NativeAudio.LoadError });
            return IdleDelay;
        }

        var enabled = await settings.GetAsync(SettingKeys.AnalysisEnabled, true, cancellationToken);
        if (!enabled)
        {
            monitor.Update(p => p with { Activity = AnalysisActivity.Disabled, Enabled = false });
            return IdleDelay;
        }

        if (scans.HasActiveJobs)
        {
            monitor.Update(p => p with { Activity = AnalysisActivity.WaitingForScan, Enabled = true });
            return TimeSpan.FromSeconds(30);
        }

        await using var db = databases.CreateDbContext();
        var ffmpegPath = (await ffmpeg.GetAsync(cancellationToken))?.Path;
        var batch = await PendingAsync(db, options.CurrentValue.BatchSize, ffmpegPath is not null, cancellationToken);
        if (batch.Count == 0)
        {
            await RefreshCountsAsync(db, AnalysisActivity.Idle, ffmpegPath is not null, cancellationToken);
            return IdleDelay;
        }

        monitor.Update(p => p with { Activity = AnalysisActivity.Running, Enabled = true, Error = null });
        var results = await AnalyzeAsync(batch, ffmpegPath, cancellationToken);
        await SaveAsync(db, results, cancellationToken);
        monitor.Analyzed(results.Count);
        await RefreshCountsAsync(db, AnalysisActivity.Running, ffmpegPath is not null, cancellationToken);
        return TimeSpan.Zero;
    }

    /// <param name="StartMs">With <paramref name="EndMs"/>, the part of the file that is this audio (a cue track).</param>
    private sealed record WorkItem(string ContentHash, string RelativePath, StorageLocation Location, int? StartMs, int? EndMs);

    private async Task<List<WorkItem>> PendingAsync(ArcanistDbContext db, int size, bool withFfmpeg, CancellationToken cancellationToken)
    {
        var version = AudioAnalyzer.Version;
        var now = time.GetUtcNow().UtcDateTime;

        // Oldest files first (UUIDv7 ids). Copies of the same audio are rare, so a few extra rows cover them.
        var candidates = await Pending(db, version, withFfmpeg)
            .OrderBy(f => f.Id)
            .Select(f => new { f.ContentHash, f.RelativePath, f.LibrarySource, f.StartMs, f.EndMs })
            .Take(size * 4)
            .ToListAsync(cancellationToken);

        return candidates
            .Where(c => !_unreadable.TryGetValue(c.ContentHash, out var until) || until < now)
            .DistinctBy(c => c.ContentHash)
            .Take(size)
            .Select(c => new WorkItem(c.ContentHash, c.RelativePath, StorageLocation.From(c.LibrarySource), c.StartMs, c.EndMs))
            .ToList();
    }

    /// <summary>Present files whose audio has no result from this analyzer version; with ffmpeg, also those that were waiting for it.</summary>
    private static IQueryable<MediaFile> Pending(ArcanistDbContext db, int version, bool withFfmpeg) =>
        db.MediaFiles.AsNoTracking()
            .Where(f => f.MissingSince == null)
            .Where(f => !db.AudioAnalyses.Any(a => a.ContentHash == f.ContentHash && a.AnalyzerVersion == version
                                                   && !(withFfmpeg && a.State == AnalysisState.NeedsFfmpeg)));

    private async Task<List<(string ContentHash, AnalysisOutcome Outcome)>> AnalyzeAsync(List<WorkItem> batch, string? ffmpegPath, CancellationToken cancellationToken)
    {
        var queue = new ConcurrentQueue<WorkItem>(batch);
        var results = new ConcurrentBag<(string, AnalysisOutcome)>();

        // Dedicated below-normal threads: analysis must never slow down streaming or the API.
        var workers = Enumerable.Range(0, Math.Min(options.CurrentValue.Parallelism, batch.Count)).Select(_ => Task.Factory.StartNew(() =>
        {
            Thread.CurrentThread.Priority = ThreadPriority.BelowNormal;
            while (!cancellationToken.IsCancellationRequested && queue.TryDequeue(out var item))
            {
                if (Analyze(item, ffmpegPath, cancellationToken) is { } outcome)
                    results.Add((item.ContentHash, outcome));
            }
        }, cancellationToken, TaskCreationOptions.LongRunning, TaskScheduler.Default));

        await Task.WhenAll(workers);
        cancellationToken.ThrowIfCancellationRequested();
        return results.ToList();
    }

    private AnalysisOutcome? Analyze(WorkItem item, string? ffmpegPath, CancellationToken cancellationToken)
    {
        var outcome = WithFile(item, stream => AudioAnalyzer.Analyze(stream, Path.GetExtension(item.RelativePath), startMs: item.StartMs, endMs: item.EndMs), cancellationToken);

        // Formats the built-in decoder does not read go through ffmpeg when there is one.
        if (outcome?.Status == AnalyzerStatus.Unsupported && ffmpegPath is not null)
            outcome = WithFile(item, stream => FfmpegDecoder.Analyze(ffmpegPath, stream, cancellationToken, item.StartMs, item.EndMs), cancellationToken);

        if (outcome?.Status == AnalyzerStatus.Invalid)
            logger.LogInformation("Could not analyze {Path}: {Error}", item.RelativePath, outcome.Error);
        return outcome;
    }

    private AnalysisOutcome? WithFile(WorkItem item, Func<Stream, AnalysisOutcome> analyze, CancellationToken cancellationToken)
    {
        Stream stream;
        try
        {
            stream = storages.For(item.Location.Kind).OpenReadAsync(item.Location, item.RelativePath, cancellationToken).GetAwaiter().GetResult();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // The source may be offline; the scanner decides when a file is gone. Try again later.
            logger.LogDebug(e, "Cannot open {Path} for analysis", item.RelativePath);
            _unreadable[item.ContentHash] = time.GetUtcNow().UtcDateTime + RetryUnreadable;
            return null;
        }

        using (stream)
            return analyze(stream);
    }

    private async Task SaveAsync(ArcanistDbContext db, List<(string ContentHash, AnalysisOutcome Outcome)> results, CancellationToken cancellationToken)
    {
        if (results.Count == 0)
            return;

        var hashes = results.Select(r => r.ContentHash).ToList();
        var existing = await db.AudioAnalyses.Where(a => hashes.Contains(a.ContentHash)).ToDictionaryAsync(a => a.ContentHash, cancellationToken);
        var now = time.GetUtcNow().UtcDateTime;
        var version = AudioAnalyzer.Version;

        foreach (var (hash, outcome) in results)
        {
            if (!existing.TryGetValue(hash, out var analysis))
            {
                analysis = new AudioAnalysis { ContentHash = hash };
                db.AudioAnalyses.Add(analysis);
            }

            Apply(analysis, outcome, version, now);
        }

        await db.SaveChangesAsync(cancellationToken);

        var fingerprinted = results.Where(r => r.Outcome.Analysis?.Fingerprint is not null).Select(r => r.ContentHash).ToList();
        var found = await duplicates.ProcessAsync(db, fingerprinted, cancellationToken);
        if (found != new DuplicateDetector.Outcome(0, 0, 0))
            logger.LogInformation("Duplicates: {Merged} merged, {Linked} linked, {Proposed} proposed", found.Merged, found.Linked, found.Proposed);

        // After merges, which can change albums' track lists.
        var albums = await db.MediaFiles.Where(f => hashes.Contains(f.ContentHash)).Select(f => f.Track.AlbumId).Distinct().ToListAsync(cancellationToken);
        await AlbumLoudness.UpdateAsync(db, albums, cancellationToken);
    }

    internal static void Apply(AudioAnalysis analysis, AnalysisOutcome outcome, int version, DateTime now)
    {
        analysis.AnalyzerVersion = version;
        analysis.AnalyzedAt = now;
        analysis.State = outcome.Status switch
        {
            AnalyzerStatus.Ok => AnalysisState.Analyzed,
            AnalyzerStatus.Unsupported => AnalysisState.NeedsFfmpeg,
            _ => AnalysisState.Failed,
        };
        analysis.Error = outcome.Error is { } error ? error[..Math.Min(error.Length, 500)] : null;

        var result = outcome.Analysis;
        analysis.DurationMs = (int)(result?.DurationMs ?? 0);
        analysis.Loudness = result?.Loudness?.Integrated;
        analysis.LoudnessRange = result?.Loudness?.Range;
        analysis.TruePeak = result?.Loudness?.TruePeak;
        analysis.LoudnessHistogram = result?.Loudness?.Histogram;
        analysis.Bpm = result?.Tempo?.Bpm;
        analysis.BpmConfidence = result?.Tempo?.Confidence;
        analysis.KeyTonic = result?.Key?.Tonic;
        analysis.KeyMinor = result?.Key?.Minor;
        analysis.KeyConfidence = result?.Key?.Confidence;
        analysis.AcoustIdFingerprint = result?.Fingerprint?.Compressed;
        analysis.Fingerprint = result?.Fingerprint is { } fingerprint ? Convert.FromBase64String(fingerprint.Raw) : null;
        analysis.FingerprintDurationMs = (int?)result?.Fingerprint?.DurationMs;
        analysis.Waveform = result?.Waveform is { } waveform ? Convert.FromBase64String(waveform) : null;
    }

    private async Task RefreshCountsAsync(ArcanistDbContext db, AnalysisActivity activity, bool withFfmpeg, CancellationToken cancellationToken)
    {
        var version = AudioAnalyzer.Version;
        var pending = await Pending(db, version, withFfmpeg).Select(f => f.ContentHash).Distinct().CountAsync(cancellationToken);
        var states = await db.AudioAnalyses.AsNoTracking()
            .Where(a => a.AnalyzerVersion == version)
            .GroupBy(a => a.State)
            .Select(g => new { State = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.State, g => g.Count, cancellationToken);

        monitor.Update(p => p with
        {
            Activity = activity,
            Enabled = true,
            Pending = pending,
            Analyzed = states.GetValueOrDefault(AnalysisState.Analyzed),
            NeedsFfmpeg = states.GetValueOrDefault(AnalysisState.NeedsFfmpeg),
            Failed = states.GetValueOrDefault(AnalysisState.Failed),
        });
    }

    // A finished scan may have added files to analyze.
    void IObserver<Application.Scanning.ScanJob>.OnNext(Application.Scanning.ScanJob job)
    {
        if (job.Status is not (ScanStatus.Queued or ScanStatus.Running))
            Wake();
    }

    void IObserver<Application.Scanning.ScanJob>.OnCompleted()
    {
    }

    void IObserver<Application.Scanning.ScanJob>.OnError(Exception error)
    {
    }

    public override void Dispose()
    {
        _wake.Dispose();
        base.Dispose();
    }
}

public static class AnalysisServiceCollectionExtensions
{
    public static IServiceCollection AddAnalysis(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<AnalysisOptions>(configuration.GetSection(AnalysisOptions.Section));
        services.AddSingleton<SettingsStore>();
        services.AddSingleton<AnalysisMonitor>();
        services.AddSingleton<DuplicateDetector>();
        services.AddSingleton<AnalysisWorker>();
        services.AddHostedService(provider => provider.GetRequiredService<AnalysisWorker>());
        return services;
    }
}
