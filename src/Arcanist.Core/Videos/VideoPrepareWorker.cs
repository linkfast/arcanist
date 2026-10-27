using System.Collections.Concurrent;
using Arcanist.Core.Data;
using Arcanist.Core.Data.Entities;
using Arcanist.Core.Hosting;
using Arcanist.Core.Media;
using Arcanist.Core.Setup;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Arcanist.Core.Videos;

/// <summary>
/// Prepares music videos for adaptive streaming, one at a time and below normal priority: those of the libraries the
/// settings name, and (when the settings say so) each video someone plays. For each, the picture at the heights the
/// settings ask for (never above its own), its own audio and, when aligned, the song's mixed audio. Prepared videos
/// that nobody wants any more (not in those libraries and unused for the cache's age) are removed, and so are mixes
/// made with an older alignment.
/// </summary>
public sealed class VideoPrepareWorker(
    IArcanistDbContextFactory databases,
    VideoSettings settings,
    VideoStore store,
    VideoPlans plans,
    VideoTranscoder transcoder,
    VideoSignals signals,
    FfmpegLocator ffmpeg,
    IOptionsMonitor<SetupOptions> setup,
    DatabaseMaintenance maintenance,
    TimeProvider time,
    ILogger<VideoPrepareWorker> logger) : BackgroundService
{
    private static readonly TimeSpan PassInterval = TimeSpan.FromMinutes(30);

    private readonly ConcurrentQueue<Guid> _requested = new();
    /// <summary>Videos just aligned, whose version with the song's audio is made ahead (so it seeks from the first play).</summary>
    private readonly ConcurrentQueue<Guid> _mixes = new();
    private readonly ConcurrentDictionary<Guid, byte> _queued = new();
    private readonly SemaphoreSlim _wake = new(0);
    private DateTime _lastPass = DateTime.MinValue;

    /// <summary>Prepares a video soon (someone played it).</summary>
    public void Request(Guid videoId)
    {
        if (_queued.TryAdd(videoId, 0))
        {
            _requested.Enqueue(videoId);
            Wake();
        }
    }

    public void Wake()
    {
        if (_wake.CurrentCount == 0)
            _wake.Release();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();
        settings.Changed += OnChanged;
        signals.AlignmentReady += OnAligned;
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await RunOnceAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception e)
                {
                    logger.LogError(e, "Preparing music videos failed");
                }

                try
                {
                    await _wake.WaitAsync(_requested.IsEmpty && _mixes.IsEmpty ? PassInterval : TimeSpan.Zero, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
        finally
        {
            signals.AlignmentReady -= OnAligned;
            settings.Changed -= OnChanged;
        }
    }

    private void OnChanged()
    {
        _lastPass = DateTime.MinValue;
        Wake();
    }

    // A newly aligned video gets its mix: the progressive one at once, the HLS one with the next pass if it is prepared.
    private void OnAligned(Guid videoId)
    {
        _mixes.Enqueue(videoId);
        _lastPass = DateTime.MinValue;
        Wake();
    }

    internal async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        if (!setup.CurrentValue.Completed || !databases.IsConfigured || await ffmpeg.GetAsync(cancellationToken) is not { } installation)
            return;
        var config = await settings.GetAsync(cancellationToken);

        if (_mixes.TryDequeue(out var aligned))
        {
            await MixAsync(aligned, installation.Encoders, cancellationToken);
            return;
        }

        if (_requested.TryDequeue(out var requested))
        {
            _queued.TryRemove(requested, out _);
            await PrepareAsync(requested, config, installation.Encoders, cancellationToken);
            return;
        }

        var now = time.GetUtcNow().UtcDateTime;
        if (now - _lastPass < PassInterval)
            return;
        _lastPass = now;

        var wanted = await DbAsync(db => db.MusicVideos.AsNoTracking()
            .Where(v => v.MissingSince == null && config.PrepareLibraryIds.Contains(v.LibrarySource.LibraryId))
            .Select(v => v.Id)
            .ToListAsync(cancellationToken), cancellationToken);
        foreach (var id in wanted)
        {
            if (!_requested.IsEmpty)
                break;
            await PrepareAsync(id, config, installation.Encoders, cancellationToken);
        }

        await CleanAsync(config, cancellationToken);
    }

    private async Task PrepareAsync(Guid videoId, VideoConfig config, IReadOnlySet<string> encoders, CancellationToken cancellationToken)
    {
        var (video, plan) = await DbAsync(async db =>
        {
            var row = await db.MusicVideos.AsNoTracking().Include(v => v.LibrarySource).SingleOrDefaultAsync(v => v.Id == videoId, cancellationToken);
            if (row is null || row.MissingSince is not null || row.LibrarySource.Kind is not (LibrarySourceKind.Local or LibrarySourceKind.S3))
                return (null, null);
            return (row, await plans.PlanAsync(db, row, VideoAudioMode.Track, db.MediaFiles, cancellationToken));
        }, cancellationToken);
        if (video is null || plan is null)
            return;

        var ladder = VideoLadder.For(video.Width, video.Height, video.VideoCodec, encoders);
        var targets = config.PrepareHeights
            .Select(h => ladder.Where(r => r.Height == h || (r.Copy && Math.Abs(r.Height - h) <= 16)).OrderByDescending(r => r.Copy).FirstOrDefault())
            .OfType<VideoRendition>()
            .DistinctBy(r => r.Name)
            .ToList();
        // A video smaller than every height asked for still gets its own size.
        if (targets.Count == 0 && ladder.Count > 0)
            targets.Add(ladder.OrderByDescending(r => r.Height).First());

        foreach (var rendition in targets)
        {
            var folder = store.PictureFolder(video.ContentHash, rendition);
            if (VideoStore.IsReady(folder))
                continue;
            await MakeAsync(folder, partial => transcoder.HlsAsync(plan.Input, rendition, audioOnly: false, partial, cancellationToken), rendition.Codec == "hevc");
        }

        var own = plan.Input with { TrackPath = null, Mix = null };
        if (own.VideoHasAudio && !VideoStore.IsReady(store.AudioFolder(video.ContentHash, "video")))
            await MakeAsync(store.AudioFolder(video.ContentHash, "video"), partial => transcoder.HlsAsync(own, null, audioOnly: true, partial, cancellationToken), false);
        if (plan.AudioKey != "video" && !VideoStore.IsReady(store.AudioFolder(video.ContentHash, plan.AudioKey)))
            await MakeAsync(store.AudioFolder(video.ContentHash, plan.AudioKey), partial => transcoder.HlsAsync(plan.Input, null, audioOnly: true, partial, cancellationToken), false);

        Directory.SetLastWriteTimeUtc(store.PreparedRoot(video.ContentHash), time.GetUtcNow().UtcDateTime);
    }

    /// <summary>
    /// The video's own picture with the song's audio, as one MP4 in the cache (index first, so it seeks): what players
    /// get without asking for a version, ready before anyone plays it. Copying the picture makes it cheap.
    /// </summary>
    private async Task MixAsync(Guid videoId, IReadOnlySet<string> encoders, CancellationToken cancellationToken)
    {
        var (video, plan) = await DbAsync(async db =>
        {
            var row = await db.MusicVideos.AsNoTracking().Include(v => v.LibrarySource).SingleOrDefaultAsync(v => v.Id == videoId, cancellationToken);
            if (row is null || row.MissingSince is not null || row.LibrarySource.Kind is not (LibrarySourceKind.Local or LibrarySourceKind.S3))
                return (null, null);
            return (row, await plans.PlanAsync(db, row, VideoAudioMode.Track, db.MediaFiles, cancellationToken));
        }, cancellationToken);
        if (video is null || plan is null || plan.AudioKey == "video")
            return;
        var rendition = VideoLadder.Pick(VideoLadder.For(video.Width, video.Height, video.VideoCodec, encoders), null, null);
        if (rendition is null)
            return;
        var key = VideoStore.CacheKey(video.ContentHash, plan.AudioKey, rendition);
        if (store.Cache.Contains(key))
            return;

        var fragmented = store.Cache.NewPartialPath();
        var seekable = store.Cache.NewPartialPath() + ".mp4";
        try
        {
            await using (var session = await transcoder.StartAsync(plan.Input, rendition, 0, cancellationToken))
            {
                if (session is null)
                    return;
                session.Lower();
                await using (var output = File.Create(fragmented))
                    await session.Output.CopyToAsync(output, cancellationToken);
                if (!await session.FinishedCleanlyAsync())
                    return;
            }
            if (await transcoder.MakeSeekableAsync(fragmented, seekable, cancellationToken))
            {
                var final = store.Cache.NewPartialPath();
                File.Move(seekable, final);
                store.Cache.Commit(final, key);
            }
        }
        finally
        {
            Caching.DiskCache.Discard(fragmented);
            Caching.DiskCache.Discard(seekable);
        }
    }

    private static async Task MakeAsync(string folder, Func<string, Task<bool>> make, bool hevc)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(folder)!);
        var partial = VideoStore.Partial(folder);
        try
        {
            if (await make(partial))
            {
                if (hevc)
                    await File.WriteAllTextAsync(Path.Combine(partial, "hevc"), "");
                VideoStore.Commit(partial, folder);
            }
        }
        finally
        {
            VideoStore.Discard(partial);
        }
    }

    /// <summary>Removes prepared videos nobody wants and mixes of older alignments.</summary>
    private async Task CleanAsync(VideoConfig config, CancellationToken cancellationToken)
    {
        var videos = await DbAsync(db => db.MusicVideos.AsNoTracking()
            .Where(v => v.MissingSince == null)
            .Select(v => new { v.ContentHash, Wanted = config.PrepareLibraryIds.Contains(v.LibrarySource.LibraryId) })
            .ToListAsync(cancellationToken), cancellationToken);
        var known = videos.GroupBy(v => VideoStore.Short(v.ContentHash)).ToDictionary(g => g.Key, g => g.Any(v => v.Wanted));
        var mixes = await DbAsync(db => db.VideoAlignments.AsNoTracking()
            .Where(a => a.State == VideoAlignmentState.Aligned && a.AlignerVersion == Analysis.AudioAligner.Version)
            .Select(a => new { a.VideoHash, a.AudioHash })
            .ToListAsync(cancellationToken), cancellationToken);
        var currentMixes = mixes.Select(m => $"a-{VideoStore.AudioKey(VideoAudioMode.Track, m.AudioHash, Analysis.AudioAligner.Version)}").ToHashSet();

        var oldest = time.GetUtcNow().UtcDateTime - TimeSpan.FromDays(config.CacheMaxAgeDays);
        foreach (var hash in store.PreparedVideos().ToList())
        {
            var root = store.PreparedRoot(hash);
            if (hash.StartsWith('.') || !known.TryGetValue(hash, out var wanted) || (!wanted && Directory.GetLastWriteTimeUtc(root) < oldest))
            {
                VideoStore.Discard(root);
                continue;
            }
            foreach (var mix in Directory.EnumerateDirectories(root, "a-track-*").ToList())
            {
                if (!currentMixes.Contains(Path.GetFileName(mix)))
                    VideoStore.Discard(mix);
            }
        }
        store.Cache.Purge(TimeSpan.FromDays(config.CacheMaxAgeDays));
    }

    private async Task<T> DbAsync<T>(Func<ArcanistDbContext, Task<T>> work, CancellationToken cancellationToken)
    {
        using (await maintenance.WorkAsync(cancellationToken))
        {
            await using var db = databases.CreateDbContext();
            return await work(db);
        }
    }

    public override void Dispose()
    {
        _wake.Dispose();
        base.Dispose();
    }
}
