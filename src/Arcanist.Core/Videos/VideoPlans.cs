using Arcanist.Core.Analysis;
using Arcanist.Core.Data;
using Arcanist.Core.Data.Entities;
using Arcanist.Core.Hosting;
using Arcanist.Core.Storage;
using Microsoft.EntityFrameworkCore;

namespace Arcanist.Core.Videos;

/// <summary>What ffmpeg needs to make a version of a video with some audio, and the key it is kept under.</summary>
/// <param name="AudioKey">See <see cref="VideoStore.AudioKey"/>: "video" when the video keeps its own audio.</param>
public sealed record VideoPlan(VideoInput Input, string AudioKey);

/// <summary>
/// Prepares the inputs of a music video version: the files on this machine (those of S3 copied to the cache once)
/// and, with the track's audio, the song's copy and its alignment. Without a ready alignment that matched, the plan
/// keeps the video's own audio.
/// </summary>
public sealed class VideoPlans(ILibraryStorageResolver storages, ArcanistPaths paths, ILogger<VideoPlans> logger)
{
    private readonly SemaphoreSlim _copying = new(1, 1);

    public async Task<VideoPlan> PlanAsync(
        ArcanistDbContext db, MusicVideo video, VideoAudioMode mode, IQueryable<MediaFile> files, CancellationToken cancellationToken)
    {
        var videoPath = await LocalPathAsync(StorageLocation.From(video.LibrarySource), video.RelativePath, video.ContentHash, cancellationToken);
        var hasAudio = video.AudioCodec is not null || !video.Probed;
        var own = new VideoPlan(new VideoInput(videoPath, null, null, 0, video.DurationMs, 0, hasAudio, video.AudioCodec), "video");
        if (mode == VideoAudioMode.Video)
            return own;

        var song = await VideoAudio.SongAsync(db, video.TrackId, video.RecordingId, files, cancellationToken);
        if (song is null)
            return own;
        var version = AudioAligner.Version;
        var alignment = await db.VideoAlignments.AsNoTracking()
            .SingleOrDefaultAsync(a => a.VideoHash == video.ContentHash && a.AudioHash == song.ContentHash && a.AlignerVersion == version, cancellationToken);
        if (alignment is not { State: VideoAlignmentState.Aligned })
            return own;

        var trackPath = await LocalPathAsync(StorageLocation.From(song.Source), song.RelativePath, song.ContentHash, cancellationToken);
        return new VideoPlan(
            own.Input with { TrackPath = trackPath, Mix = VideoMix.Parse(alignment.Segments), VideoGainDb = alignment.VideoGainDb, TrackOffsetMs = song.StartMs ?? 0 },
            VideoStore.AudioKey(VideoAudioMode.Track, song.ContentHash, version));
    }

    /// <summary>The file's path on this machine; a remote one (S3) is copied once to <c>cache/videos/sources</c>.</summary>
    public async Task<string> LocalPathAsync(StorageLocation location, string relativePath, string hash, CancellationToken cancellationToken)
    {
        var storage = storages.For(location.Kind);
        if (storage.TryGetLocalPath(location, relativePath) is { } local)
            return local;

        var folder = Path.Combine(paths.Cache, "videos", "sources");
        var copy = Path.Combine(folder, hash + Path.GetExtension(relativePath));
        if (File.Exists(copy))
        {
            File.SetLastWriteTimeUtc(copy, DateTime.UtcNow);
            return copy;
        }

        await _copying.WaitAsync(cancellationToken);
        try
        {
            if (File.Exists(copy))
                return copy;
            Directory.CreateDirectory(folder);
            var partial = Path.Combine(folder, $".partial-{Guid.NewGuid():N}");
            await using (var input = await storage.OpenReadAsync(location, relativePath, cancellationToken))
            await using (var output = File.Create(partial))
                await input.CopyToAsync(output, cancellationToken);
            File.Move(partial, copy, overwrite: true);
            Trim(folder);
            return copy;
        }
        finally
        {
            _copying.Release();
        }
    }

    /// <summary>Keeps the copies of remote files within 20 GB, the least recently used going first.</summary>
    private void Trim(string folder)
    {
        const long limit = 20_000_000_000;
        var files = new DirectoryInfo(folder).EnumerateFiles().Where(f => !f.Name.StartsWith('.')).OrderBy(f => f.LastWriteTimeUtc).ToList();
        var total = files.Sum(f => f.Length);
        foreach (var file in files)
        {
            if (total <= limit)
                break;
            try
            {
                file.Delete();
                total -= file.Length;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                logger.LogDebug("Could not remove {File}: {Error}", file.FullName, e.Message);
            }
        }
    }
}
