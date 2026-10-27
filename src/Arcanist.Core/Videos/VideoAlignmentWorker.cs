using System.Collections.Concurrent;
using System.Text.Json;
using Arcanist.Core.Analysis;
using Arcanist.Core.Application.Scanning;
using ScanJob = Arcanist.Core.Application.Scanning.ScanJob;
using Arcanist.Core.Data;
using Arcanist.Core.Data.Entities;
using Arcanist.Core.Hosting;
using Arcanist.Core.Media;
using Arcanist.Core.Setup;
using Arcanist.Core.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Arcanist.Core.Videos;

/// <summary>
/// Aligns each linked music video with the audio of its song, in the background: first the videos someone asked
/// for, then every video whose alignment is missing for this aligner version. One at a time, on a low-priority
/// thread, waiting while scans and streams run; with the audio analysis switched off it does nothing. Database
/// work goes in short maintenance units; decoding and aligning happen outside them.
/// </summary>
public sealed class VideoAlignmentWorker(
    IArcanistDbContextFactory databases,
    ILibraryStorageResolver storages,
    VideoLinker linker,
    VideoSignals signals,
    SettingsStore settings,
    ScanEvents scans,
    StreamActivity streams,
    FfmpegLocator ffmpeg,
    IOptionsMonitor<SetupOptions> setup,
    IOptionsMonitor<AnalysisOptions> options,
    ArcanistPaths paths,
    TimeProvider time,
    DatabaseMaintenance maintenance,
    ILogger<VideoAlignmentWorker> logger) : BackgroundService, IObserver<ScanJob>
{
    private static readonly TimeSpan IdleDelay = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan RetryUnreadable = TimeSpan.FromHours(1);

    private readonly SemaphoreSlim _wake = new(0);
    private readonly ConcurrentDictionary<Guid, DateTime> _unreadable = new();

    public void Wake()
    {
        if (_wake.CurrentCount == 0)
            _wake.Release();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();
        using var subscription = scans.Subscribe(this);
        linker.Linked += Wake;
        signals.AlignmentRequested += Wake;
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
                    logger.LogError(e, "Music video alignment failed");
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
            signals.AlignmentRequested -= Wake;
            linker.Linked -= Wake;
        }
    }

    /// <summary>Aligns one video and says how long to wait before the next.</summary>
    internal async Task<TimeSpan> RunOnceAsync(CancellationToken cancellationToken)
    {
        if (!options.CurrentValue.Enabled || !setup.CurrentValue.Completed || !databases.IsConfigured || !AudioAligner.IsAvailable)
            return IdleDelay;
        if (!await settings.GetAsync(SettingKeys.AnalysisEnabled, true, cancellationToken))
            return IdleDelay;
        if (scans.HasActiveJobs || streams.Active > 0)
            return TimeSpan.FromSeconds(15);

        var ffmpegPath = (await ffmpeg.GetAsync(cancellationToken))?.Path;
        if (await DbAsync(db => NextAsync(db, ffmpegPath is not null, cancellationToken), cancellationToken) is not { } item)
            return IdleDelay;

        var outcome = await Task.Factory.StartNew(() =>
        {
            Thread.CurrentThread.Priority = ThreadPriority.BelowNormal;
            return Align(item, ffmpegPath, cancellationToken);
        }, cancellationToken, TaskCreationOptions.LongRunning, TaskScheduler.Default);

        if (outcome is null)
        {
            _unreadable[item.VideoId] = time.GetUtcNow().UtcDateTime + RetryUnreadable;
            await DbAsync(db => db.VideoAlignmentRequests.Where(r => r.VideoId == item.VideoId).ExecuteDeleteAsync(cancellationToken), cancellationToken);
            return TimeSpan.Zero;
        }

        await DbAsync(db => SaveAsync(db, item, outcome, cancellationToken), cancellationToken);
        signals.Aligned(item.VideoId);
        return TimeSpan.Zero;
    }

    internal sealed record WorkItem(Guid VideoId, string VideoHash, StorageLocation VideoLocation, string VideoPath, VideoSong Song);

    private async Task<WorkItem?> NextAsync(ArcanistDbContext db, bool withFfmpeg, CancellationToken cancellationToken)
    {
        var version = AudioAligner.Version;
        var now = time.GetUtcNow().UtcDateTime;

        var requested = await db.VideoAlignmentRequests.AsNoTracking()
            .OrderByDescending(r => r.Priority).ThenBy(r => r.RequestedAt)
            .Select(r => r.VideoId)
            .Take(20)
            .ToListAsync(cancellationToken);
        var pending = await Pending(db, version, withFfmpeg).OrderBy(v => v.AddedAt).Select(v => v.Id).Take(20).ToListAsync(cancellationToken);

        foreach (var id in requested.Concat(pending).Distinct())
        {
            if (_unreadable.TryGetValue(id, out var until) && until > now)
                continue;
            var video = await db.MusicVideos.AsNoTracking().Include(v => v.LibrarySource).SingleOrDefaultAsync(v => v.Id == id, cancellationToken);
            var song = video is null ? null : await VideoAudio.SongAsync(db, video.TrackId, video.RecordingId, db.MediaFiles, cancellationToken);
            var current = video is not null && song is not null && await db.VideoAlignments.AnyAsync(a => a.VideoHash == video.ContentHash
                && a.AudioHash == song.ContentHash && a.AlignerVersion == version && !(withFfmpeg && a.State == VideoAlignmentState.NeedsFfmpeg), cancellationToken);
            if (video is null || video.MissingSince is not null || song is null || current
                || video.LibrarySource.Kind is not (LibrarySourceKind.Local or LibrarySourceKind.S3))
            {
                await db.VideoAlignmentRequests.Where(r => r.VideoId == id).ExecuteDeleteAsync(cancellationToken);
                // Nothing to align now: not asked again until something changes (a scan wakes the worker).
                if (!requested.Contains(id))
                    _unreadable[id] = now + IdleDelay;
                continue;
            }
            return new WorkItem(video.Id, video.ContentHash, StorageLocation.From(video.LibrarySource), video.RelativePath, song);
        }
        return null;
    }

    /// <summary>Linked, present videos on storage this server reads, with no alignment of this version.</summary>
    internal static IQueryable<MusicVideo> Pending(ArcanistDbContext db, int version, bool withFfmpeg) =>
        db.MusicVideos.AsNoTracking()
            .Where(v => v.MissingSince == null && v.RecordingId != null
                        && (v.LibrarySource.Kind == LibrarySourceKind.Local || v.LibrarySource.Kind == LibrarySourceKind.S3))
            .Where(v => !db.VideoAlignments.Any(a => a.VideoHash == v.ContentHash && a.AlignerVersion == version
                                                     && !(withFfmpeg && a.State == VideoAlignmentState.NeedsFfmpeg)));

    private AlignmentOutcome? Align(WorkItem item, string? ffmpegPath, CancellationToken cancellationToken)
    {
        var copies = new List<string>();
        try
        {
            using var aligner = AudioAligner.Create();
            if (aligner is null)
                return AlignmentOutcome.Unavailable;

            var video = Side(aligner, AudioAligner.Video, item.VideoLocation, item.VideoPath, null, null, ffmpegPath, copies, cancellationToken);
            if (video != AnalyzerStatus.Ok)
                return Failure(video, "video");
            var track = Side(aligner, AudioAligner.Track, StorageLocation.From(item.Song.Source), item.Song.RelativePath, item.Song.StartMs, item.Song.EndMs,
                ffmpegPath, copies, cancellationToken);
            if (track != AnalyzerStatus.Ok)
                return Failure(track, "track");
            return aligner.Finish();
        }
        catch (Exception e) when (e is IOException or HttpRequestException or UnauthorizedAccessException)
        {
            logger.LogDebug(e, "Cannot read {Path} to align it", item.VideoPath);
            return null;
        }
        finally
        {
            foreach (var copy in copies)
                File.Delete(copy);
        }
    }

    private static AlignmentOutcome Failure(AnalyzerStatus status, string side) =>
        new(status, null, status == AnalyzerStatus.Unsupported ? $"the {side} needs ffmpeg" : $"the {side} could not be decoded");

    /// <summary>
    /// Feeds one side: the native decoder first, then ffmpeg (which opens the file itself when it is on this
    /// machine: an MP4 with its index at the end cannot be read from a pipe).
    /// </summary>
    private AnalyzerStatus Side(
        AudioAligner aligner, int side, StorageLocation location, string relativePath, int? startMs, int? endMs, string? ffmpegPath, List<string> copies,
        CancellationToken cancellationToken)
    {
        var storage = storages.For(location.Kind);
        var extension = Path.GetExtension(relativePath);
        var localPath = storage.TryGetLocalPath(location, relativePath);
        if (localPath is null)
        {
            localPath = Path.Combine(paths.Cache, "videos", "align", $"{Guid.NewGuid():N}{extension}");
            Directory.CreateDirectory(Path.GetDirectoryName(localPath)!);
            copies.Add(localPath);
            using var input = storage.OpenReadAsync(location, relativePath, cancellationToken).GetAwaiter().GetResult();
            using var output = File.Create(localPath);
            input.CopyTo(output);
        }

        AnalyzerStatus status;
        using (var stream = File.OpenRead(localPath))
            status = aligner.Decode(side, stream, extension, startMs, endMs);
        if (status != AnalyzerStatus.Unsupported || ffmpegPath is null)
            return status;
        var error = FfmpegDecoder.Align(ffmpegPath, aligner, side, localPath, null, cancellationToken, startMs, endMs);
        if (error is not null)
            logger.LogInformation("ffmpeg could not decode {Path} to align it: {Error}", relativePath, error);
        return error is null ? AnalyzerStatus.Ok : AnalyzerStatus.Invalid;
    }

    private async Task<int> SaveAsync(ArcanistDbContext db, WorkItem item, AlignmentOutcome outcome, CancellationToken cancellationToken)
    {
        var alignment = await db.VideoAlignments.SingleOrDefaultAsync(a => a.VideoHash == item.VideoHash && a.AudioHash == item.Song.ContentHash, cancellationToken);
        if (alignment is null)
        {
            alignment = new VideoAlignment { VideoHash = item.VideoHash, AudioHash = item.Song.ContentHash };
            db.VideoAlignments.Add(alignment);
        }

        alignment.AlignerVersion = AudioAligner.Version;
        alignment.AlignedAt = time.GetUtcNow().UtcDateTime;
        alignment.Error = outcome.Error is { } error ? error[..Math.Min(error.Length, 500)] : null;
        alignment.State = outcome.Status switch
        {
            AnalyzerStatus.Unsupported => VideoAlignmentState.NeedsFfmpeg,
            AnalyzerStatus.Ok => VideoAlignmentState.NoMatch,
            _ => VideoAlignmentState.Failed,
        };
        alignment.Coverage = 0;
        alignment.VideoGainDb = 0;
        alignment.Segments = null;
        if (outcome.Json is { } json)
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.GetProperty("matched").GetBoolean())
            {
                alignment.State = VideoAlignmentState.Aligned;
                alignment.Coverage = root.GetProperty("coverage").GetDouble();
                alignment.VideoGainDb = root.GetProperty("videoGainDb").GetDouble();
            }
            alignment.Segments = root.GetProperty("segments").GetRawText();
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await db.VideoAlignmentRequests.Where(r => r.VideoId == item.VideoId).ExecuteDeleteAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return 0;
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
        {
            _unreadable.Clear();
            Wake();
        }
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
