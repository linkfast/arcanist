using System.Collections.Concurrent;
using System.Text.Json;
using Arcanist.Core.Analysis;
using Arcanist.Core.Application.Ai;
using Arcanist.Core.Application.Scanning;
using ScanJob = Arcanist.Core.Application.Scanning.ScanJob;
using Arcanist.Core.Data;
using Arcanist.Core.Data.Entities;
using Arcanist.Core.Extensions;
using Arcanist.Core.Extensions.Ai;
using Arcanist.Core.Hosting;
using Arcanist.Core.Media;
using Arcanist.Core.Setup;
using Arcanist.Core.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Arcanist.Core.Ai;

/// <summary>
/// The DJ analysis of the library, in the background while the AI extension is on: first the songs clients asked
/// for (by priority, then in order: those of S3, other servers and Ultimate Cloud only this way), then each distinct
/// piece of local audio once per analyzer version, oldest first. Low-priority threads, as many as the settings say,
/// waiting while scans (and, if asked, streams) run. Database work goes in short maintenance units; reading and
/// analyzing happen outside them.
/// </summary>
internal sealed class TrackAnalysisWorker(
    IArcanistDbContextFactory databases,
    ILibraryStorageResolver storages,
    ExtensionHost extensions,
    AiSettings settings,
    AiMonitor monitor,
    TrackAnalysisEvents events,
    ScanEvents scans,
    StreamActivity streams,
    FfmpegLocator ffmpeg,
    IOptionsMonitor<SetupOptions> setup,
    IOptionsMonitor<AiOptions> options,
    ArcanistPaths paths,
    TimeProvider time,
    DatabaseMaintenance maintenance,
    ILogger<TrackAnalysisWorker> logger) : BackgroundService, IObserver<ScanJob>
{
    private static readonly TimeSpan IdleDelay = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan RetryUnreadable = TimeSpan.FromHours(1);

    private readonly SemaphoreSlim _wake = new(0);
    private readonly ConcurrentDictionary<string, DateTime> _unreadable = new();

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
        extensions.EnabledChanged += OnExtensionChanged;
        try
        {
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
                    logger.LogError(e, "Track analysis failed");
                    monitor.Update(s => s with { Error = e.Message });
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
        finally
        {
            extensions.EnabledChanged -= OnExtensionChanged;
            monitor.RunRequested -= Wake;
        }
    }

    private void OnExtensionChanged(string id)
    {
        if (id == AiExtension.Id)
            Wake();
    }

    /// <summary>Does one batch of work and says how long to wait before the next.</summary>
    internal async Task<TimeSpan> RunOnceAsync(CancellationToken cancellationToken)
    {
        if (!extensions.IsEnabled(AiExtension.Id) || !options.CurrentValue.Enabled || !setup.CurrentValue.Completed || !databases.IsConfigured)
        {
            monitor.Update(s => s with { Activity = AiActivity.Disabled });
            return TimeSpan.FromMinutes(1);
        }

        if (!DjAudioAnalyzer.IsAvailable)
        {
            monitor.Update(s => s with { Activity = AiActivity.Unavailable, Error = NativeAudio.LoadError ?? "The native analyzer has no DJ analysis." });
            return IdleDelay;
        }

        var config = await settings.GetAsync(cancellationToken);
        if ((config.PauseWhileScanning && scans.HasActiveJobs) || (config.PauseWhileStreaming && streams.Active > 0))
        {
            monitor.Update(s => s with { Activity = AiActivity.Paused });
            return TimeSpan.FromSeconds(15);
        }

        var ffmpegPath = (await ffmpeg.GetAsync(cancellationToken))?.Path;
        var batch = await DbAsync(db => NextAsync(db, config.Threads * 2, ffmpegPath is not null, config.OnDemandSources, cancellationToken), cancellationToken);
        if (batch.Count == 0)
        {
            await RefreshAsync(AiActivity.Idle, ffmpegPath is not null, cancellationToken);
            return IdleDelay;
        }

        monitor.Update(s => s with { Activity = AiActivity.Running, Error = null });
        var results = await AnalyzeAsync(batch, config.Threads, ffmpegPath, cancellationToken);
        await DbAsync(db => SaveAsync(db, results, cancellationToken), cancellationToken);
        foreach (var (hash, outcome) in results)
        {
            if (outcome.Status == AnalyzerStatus.Ok)
                events.Publish(hash, DjAudioAnalyzer.Version);
        }

        monitor.Analyzed(results.Count);
        await RefreshAsync(AiActivity.Running, ffmpegPath is not null, cancellationToken);
        return TimeSpan.Zero;
    }

    /// <param name="Local">On this server's disks: read in place (the others are copied to a temporary file first).</param>
    private sealed record WorkItem(string ContentHash, string RelativePath, StorageLocation Location, int? StartMs, int? EndMs, bool Local);

    private async Task<List<WorkItem>> NextAsync(ArcanistDbContext db, int size, bool withFfmpeg, bool onDemand, CancellationToken cancellationToken)
    {
        var version = DjAudioAnalyzer.Version;
        var now = time.GetUtcNow().UtcDateTime;
        var items = new List<WorkItem>();

        // What clients asked for, first.
        var requests = await db.TrackAnalysisRequests.AsNoTracking()
            .OrderByDescending(r => r.Priority).ThenBy(r => r.RequestedAt).ThenBy(r => r.Id)
            .Take(size * 2)
            .ToListAsync(cancellationToken);
        var done = new List<Guid>();
        foreach (var request in requests)
        {
            if (items.Count >= size)
                break;
            if (_unreadable.TryGetValue(request.ContentHash, out var until) && until > now)
                continue;
            var current = await db.TrackAnalyses.AsNoTracking().AnyAsync(a => a.ContentHash == request.ContentHash && a.AnalysisVersion == version
                && !(withFfmpeg && a.State == TrackAnalysisState.NeedsFfmpeg), cancellationToken);
            // A present copy, preferably a local one (any copy has the same audio).
            var file = await db.MediaFiles.AsNoTracking()
                .Where(f => f.ContentHash == request.ContentHash && f.MissingSince == null)
                .OrderBy(f => f.LibrarySource.Kind == LibrarySourceKind.Local ? 0 : 1).ThenBy(f => f.Id == request.FileId ? 0 : 1)
                .Select(f => new { f.RelativePath, f.LibrarySource, f.StartMs, f.EndMs })
                .FirstOrDefaultAsync(cancellationToken);
            var local = file?.LibrarySource.Kind == LibrarySourceKind.Local;
            if (current || file is null || (!local && !onDemand))
            {
                done.Add(request.Id);
                continue;
            }

            items.Add(new WorkItem(request.ContentHash, file.RelativePath, StorageLocation.From(file.LibrarySource), file.StartMs, file.EndMs, local));
        }

        if (done.Count > 0)
            await db.TrackAnalysisRequests.Where(r => done.Contains(r.Id)).ExecuteDeleteAsync(cancellationToken);

        if (items.Count < size)
        {
            var taken = items.Select(i => i.ContentHash).ToHashSet();
            var candidates = await Pending(db, version, withFfmpeg)
                .OrderBy(f => f.Id)
                .Select(f => new { f.ContentHash, f.RelativePath, f.LibrarySource, f.StartMs, f.EndMs })
                .Take(size * 4)
                .ToListAsync(cancellationToken);
            items.AddRange(candidates
                .Where(c => !taken.Contains(c.ContentHash) && (!_unreadable.TryGetValue(c.ContentHash, out var until) || until < now))
                .DistinctBy(c => c.ContentHash)
                .Take(size - items.Count)
                .Select(c => new WorkItem(c.ContentHash, c.RelativePath, StorageLocation.From(c.LibrarySource), c.StartMs, c.EndMs, true)));
        }

        return items;
    }

    /// <summary>Present local files whose audio has no analysis of this version (with ffmpeg, also those waiting for it).</summary>
    internal static IQueryable<MediaFile> Pending(ArcanistDbContext db, int version, bool withFfmpeg) =>
        db.MediaFiles.AsNoTracking()
            .Where(f => f.MissingSince == null && f.LibrarySource.Kind == LibrarySourceKind.Local)
            .Where(f => !db.TrackAnalyses.Any(a => a.ContentHash == f.ContentHash && a.AnalysisVersion == version
                                                   && !(withFfmpeg && a.State == TrackAnalysisState.NeedsFfmpeg)));

    private async Task<List<(string ContentHash, DjOutcome Outcome)>> AnalyzeAsync(List<WorkItem> batch, int threads, string? ffmpegPath, CancellationToken cancellationToken)
    {
        var queue = new ConcurrentQueue<WorkItem>(batch);
        var results = new ConcurrentBag<(string, DjOutcome)>();

        // Dedicated below-normal threads: the analysis must never slow down streaming or the API.
        var workers = Enumerable.Range(0, Math.Min(threads, batch.Count)).Select(_ => Task.Factory.StartNew(() =>
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

    private DjOutcome? Analyze(WorkItem item, string? ffmpegPath, CancellationToken cancellationToken)
    {
        var extension = Path.GetExtension(item.RelativePath);
        string? copy = null;
        try
        {
            // Songs elsewhere (S3, another server, Ultimate Cloud) are copied first: the decoder seeks.
            if (!item.Local)
            {
                copy = Path.Combine(paths.Cache, "ai", $"{Guid.NewGuid():N}{extension}");
                Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
                using var input = Open(item, cancellationToken);
                if (input is null)
                    return null;
                using var output = File.Create(copy);
                input.CopyTo(output);
            }

            var outcome = WithStream(item, copy, stream => DjAudioAnalyzer.Analyze(stream, extension, item.StartMs, item.EndMs), cancellationToken);
            if (outcome?.Status == AnalyzerStatus.Unsupported && ffmpegPath is not null)
                outcome = WithStream(item, copy, stream => FfmpegDecoder.AnalyzeDj(ffmpegPath, stream, cancellationToken, item.StartMs, item.EndMs), cancellationToken);
            if (outcome?.Status == AnalyzerStatus.Invalid)
                logger.LogInformation("Could not analyze {Path}: {Error}", item.RelativePath, outcome.Error);
            return outcome;
        }
        catch (Exception e) when (e is IOException or HttpRequestException or UnauthorizedAccessException)
        {
            logger.LogDebug(e, "Cannot read {Path} for its analysis", item.RelativePath);
            _unreadable[item.ContentHash] = time.GetUtcNow().UtcDateTime + RetryUnreadable;
            return null;
        }
        finally
        {
            if (copy is not null)
                File.Delete(copy);
        }
    }

    private DjOutcome? WithStream(WorkItem item, string? copy, Func<Stream, DjOutcome> analyze, CancellationToken cancellationToken)
    {
        using var stream = copy is not null ? File.OpenRead(copy) : Open(item, cancellationToken);
        return stream is null ? null : analyze(stream);
    }

    /// <summary>The song's bytes; another server's as it streams them (analyzing needs no permission to download).</summary>
    private Stream? Open(WorkItem item, CancellationToken cancellationToken)
    {
        try
        {
            var storage = storages.For(item.Location.Kind);
            return storage is P2P.PeerStorage peer
                ? peer.OpenStreamAsync(item.Location, item.RelativePath, cancellationToken).GetAwaiter().GetResult()
                : storage.OpenReadAsync(item.Location, item.RelativePath, cancellationToken).GetAwaiter().GetResult();
        }
        catch (Exception e) when (e is IOException or HttpRequestException or UnauthorizedAccessException)
        {
            logger.LogDebug(e, "Cannot open {Path} for its analysis", item.RelativePath);
            _unreadable[item.ContentHash] = time.GetUtcNow().UtcDateTime + RetryUnreadable;
            return null;
        }
    }

    private async Task<int> SaveAsync(ArcanistDbContext db, List<(string ContentHash, DjOutcome Outcome)> results, CancellationToken cancellationToken)
    {
        if (results.Count == 0)
            return 0;
        var hashes = results.Select(r => r.ContentHash).ToList();
        var existing = await db.TrackAnalyses.Where(a => hashes.Contains(a.ContentHash)).ToDictionaryAsync(a => a.ContentHash, cancellationToken);
        var now = time.GetUtcNow().UtcDateTime;
        var version = DjAudioAnalyzer.Version;
        foreach (var (hash, outcome) in results)
        {
            if (!existing.TryGetValue(hash, out var analysis))
            {
                analysis = new TrackAnalysis { ContentHash = hash };
                db.TrackAnalyses.Add(analysis);
            }

            analysis.AnalysisVersion = version;
            analysis.AnalyzedAt = now;
            analysis.State = outcome.Status switch
            {
                AnalyzerStatus.Ok => TrackAnalysisState.Analyzed,
                AnalyzerStatus.Unsupported => TrackAnalysisState.NeedsFfmpeg,
                _ => TrackAnalysisState.Failed,
            };
            analysis.Error = outcome.Error is { } error ? error[..Math.Min(error.Length, 500)] : null;
            // A failure keeps the previous analysis (an older version), still served as stale.
            if (outcome.Json is { } json)
            {
                analysis.Json = json;
                analysis.DurationMs = DurationMs(json);
            }
        }

        // Together: a client never sees the analysis ready while its request is still in line.
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        var done = await db.TrackAnalysisRequests.Where(r => hashes.Contains(r.ContentHash)).ExecuteDeleteAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return done;
    }

    private static int? DurationMs(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return (int)Math.Round(document.RootElement.GetProperty("durationSeconds").GetDouble() * 1000);
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return null;
        }
    }

    private Task RefreshAsync(AiActivity activity, bool withFfmpeg, CancellationToken cancellationToken) =>
        DbAsync(async db =>
        {
            var progress = await ProgressAsync(db, withFfmpeg, cancellationToken);
            monitor.Update(s => s with { Activity = activity, Progress = progress });
            return 0;
        }, cancellationToken);

    /// <summary>The library's progress: analyzed and failed audio of this version, local audio waiting and requests.</summary>
    internal static async Task<TrackAnalysisProgress> ProgressAsync(ArcanistDbContext db, bool withFfmpeg, CancellationToken cancellationToken)
    {
        var version = DjAudioAnalyzer.Version;
        var pending = await Pending(db, version, withFfmpeg).Select(f => f.ContentHash).Distinct().CountAsync(cancellationToken);
        var requested = await db.TrackAnalysisRequests.CountAsync(r => !db.MediaFiles.Any(f => f.ContentHash == r.ContentHash && f.MissingSince == null
            && f.LibrarySource.Kind == LibrarySourceKind.Local), cancellationToken);
        var states = await db.TrackAnalyses.AsNoTracking()
            .Where(a => a.AnalysisVersion == version)
            .GroupBy(a => a.State)
            .Select(g => new { State = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.State, g => g.Count, cancellationToken);
        return new TrackAnalysisProgress(version, states.GetValueOrDefault(TrackAnalysisState.Analyzed), pending + requested,
            states.GetValueOrDefault(TrackAnalysisState.Failed));
    }

    private async Task<T> DbAsync<T>(Func<ArcanistDbContext, Task<T>> work, CancellationToken cancellationToken)
    {
        using (await maintenance.WorkAsync(cancellationToken))
        {
            await using var db = databases.CreateDbContext();
            return await work(db);
        }
    }

    void IObserver<ScanJob>.OnNext(ScanJob job)
    {
        if (job.Status is not (ScanStatus.Queued or ScanStatus.Running))
            Wake();
    }

    void IObserver<ScanJob>.OnCompleted()
    {
    }

    void IObserver<ScanJob>.OnError(Exception error)
    {
    }

    public override void Dispose()
    {
        _wake.Dispose();
        base.Dispose();
    }
}
