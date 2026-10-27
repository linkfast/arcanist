using System.Diagnostics;
using System.Globalization;
using Arcanist.Core.Media;

namespace Arcanist.Core.Videos;

/// <summary>What goes into a version of a music video.</summary>
/// <param name="VideoPath">The video file on this machine.</param>
/// <param name="TrackPath">The song's file on this machine, when its audio is mixed in.</param>
/// <param name="Mix">The alignment to mix by; null keeps the video's own audio.</param>
/// <param name="TrackOffsetMs">Where the song starts in its file (a cue sheet track).</param>
public sealed record VideoInput(
    string VideoPath, string? TrackPath, IReadOnlyList<AlignedSegment>? Mix, double VideoGainDb, int DurationMs, int TrackOffsetMs,
    bool VideoHasAudio, string? AudioCodec);

/// <summary>
/// Runs ffmpeg for music videos: a version as fragmented MP4 to stream while it is made, a finished file made
/// seekable (index first), and the HLS renditions of adaptive streaming (video without audio, and each audio on its
/// own, so players switch between the song's audio and the video's without touching the picture).
/// </summary>
public sealed class VideoTranscoder(FfmpegLocator ffmpeg, ILogger<VideoTranscoder> logger)
{
    /// <summary>Video conversions at once: each can take every core.</summary>
    private readonly SemaphoreSlim _slots = new(2, 2);

    /// <summary>Starts a version as fragmented MP4 on ffmpeg's output, from <paramref name="offsetMs"/>; null without ffmpeg.</summary>
    public async Task<TranscodeSession?> StartAsync(VideoInput input, VideoRendition rendition, int offsetMs, CancellationToken cancellationToken)
    {
        if (await ffmpeg.GetAsync(cancellationToken) is not { } installation)
            return null;
        await _slots.WaitAsync(cancellationToken);
        try
        {
            var arguments = Arguments(input, rendition, includeVideo: true, includeAudio: true, offsetMs);
            arguments.AddRange(["-f", "mp4", "-movflags", "frag_keyframe+empty_moov+default_base_moof", "pipe:1"]);
            var process = Start(installation.Path, arguments);
            var errors = process.StandardError.ReadToEndAsync(CancellationToken.None);
            return new TranscodeSession(process, Task.CompletedTask, errors, _slots, logger);
        }
        catch
        {
            _slots.Release();
            throw;
        }
    }

    /// <summary>Rewrites a finished fragmented MP4 with its index first, so players can seek in it by ranges.</summary>
    public async Task<bool> MakeSeekableAsync(string fragmented, string output, CancellationToken cancellationToken)
    {
        if (await ffmpeg.GetAsync(cancellationToken) is not { } installation)
            return false;
        return await RunAsync(installation.Path, ["-hide_banner", "-loglevel", "error", "-y", "-i", fragmented, "-map", "0", "-c", "copy",
            "-movflags", "+faststart", "-f", "mp4", output], lower: true, cancellationToken);
    }

    /// <summary>
    /// Makes one HLS rendition in <paramref name="directory"/> (<c>index.m3u8</c>, <c>init.mp4</c> and segments):
    /// the picture only (<paramref name="rendition"/>), or the audio only (<paramref name="audioOnly"/>: the mix,
    /// or the video's own when <see cref="VideoInput.Mix"/> is null). Runs below normal priority.
    /// </summary>
    public async Task<bool> HlsAsync(VideoInput input, VideoRendition? rendition, bool audioOnly, string directory, CancellationToken cancellationToken)
    {
        if (await ffmpeg.GetAsync(cancellationToken) is not { } installation)
            return false;
        Directory.CreateDirectory(directory);
        var picture = rendition ?? new VideoRendition(0, 0, 0, 192_000, "h264", true);
        var arguments = Arguments(input, picture, includeVideo: !audioOnly, includeAudio: audioOnly, 0);
        // Run inside the folder: ffmpeg writes the init segment where it runs, and the playlist names files by their names.
        arguments.AddRange([
            "-f", "hls", "-hls_time", "6", "-hls_playlist_type", "vod", "-hls_segment_type", "fmp4",
            "-hls_fmp4_init_filename", "init.mp4", "-hls_segment_filename", "seg%04d.m4s", "index.m3u8",
        ]);
        await _slots.WaitAsync(cancellationToken);
        try
        {
            return await RunAsync(installation.Path, arguments, lower: true, cancellationToken, Path.GetFullPath(directory));
        }
        finally
        {
            _slots.Release();
        }
    }

    internal static List<string> Arguments(VideoInput input, VideoRendition rendition, bool includeVideo, bool includeAudio, int offsetMs)
    {
        var mix = includeAudio && input.Mix is { Count: > 0 } segments && input.TrackPath is not null ? segments : null;
        var arguments = new List<string> { "-hide_banner", "-loglevel", "error", "-nostdin" };
        // Seeking in the inputs would shift the mix: the graph is built for the whole video and the output trimmed.
        arguments.AddRange(["-i", input.VideoPath]);
        if (mix is not null)
        {
            arguments.AddRange(["-i", input.TrackPath!]);
            arguments.AddRange(["-filter_complex", VideoMix.Filter(mix, input.VideoGainDb, input.DurationMs, input.TrackOffsetMs, input.VideoHasAudio)]);
        }

        if (includeVideo)
        {
            arguments.AddRange(["-map", "0:v:0"]);
            if (rendition.Copy)
                arguments.AddRange(["-c:v", "copy"]);
            else if (rendition.Codec == "hevc")
                arguments.AddRange(["-c:v", "libx265", "-preset", "fast", "-tag:v", "hvc1", .. RateControl(rendition), "-vf", $"scale=-2:{rendition.Height}", "-pix_fmt", "yuv420p"]);
            else
                arguments.AddRange(["-c:v", "libx264", "-preset", "veryfast", "-profile:v", "high", .. RateControl(rendition), "-vf", $"scale=-2:{rendition.Height}", "-pix_fmt", "yuv420p"]);
        }
        else
        {
            arguments.Add("-vn");
        }

        if (!includeAudio)
            arguments.Add("-an");
        else if (mix is not null)
            arguments.AddRange(["-map", "[mix]", "-c:a", "aac", "-b:a", $"{rendition.AudioBitRate}"]);
        else if (input.VideoHasAudio)
        {
            arguments.AddRange(["-map", "0:a:0"]);
            // The video's own AAC goes as it is with the picture as it is.
            if (rendition.Copy && includeVideo && string.Equals(input.AudioCodec, "aac", StringComparison.OrdinalIgnoreCase))
                arguments.AddRange(["-c:a", "copy"]);
            else
                arguments.AddRange(["-c:a", "aac", "-b:a", $"{rendition.AudioBitRate}", "-ac", "2"]);
        }
        else
            arguments.Add("-an");

        if (offsetMs > 0)
            arguments.AddRange(["-ss", Seconds(offsetMs)]);
        if (input.DurationMs > 0)
            arguments.AddRange(["-t", Seconds(Math.Max(1, input.DurationMs - offsetMs))]);
        arguments.AddRange(["-map_metadata", "-1"]);
        return arguments;
    }

    private static string[] RateControl(VideoRendition rendition) =>
        ["-b:v", $"{rendition.VideoBitRate}", "-maxrate", $"{rendition.VideoBitRate * 3 / 2}", "-bufsize", $"{rendition.VideoBitRate * 2}",
            "-g", "48", "-keyint_min", "48", "-sc_threshold", "0"];

    private static string Seconds(int milliseconds) => (milliseconds / 1000.0).ToString("0.###", CultureInfo.InvariantCulture);

    private static Process Start(string ffmpegPath, IEnumerable<string> arguments, string? workingDirectory = null)
    {
        var start = new ProcessStartInfo(ffmpegPath)
        {
            WorkingDirectory = workingDirectory ?? "",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);
        return Process.Start(start)!;
    }

    private async Task<bool> RunAsync(string ffmpegPath, IEnumerable<string> arguments, bool lower, CancellationToken cancellationToken,
        string? workingDirectory = null)
    {
        using var process = Start(ffmpegPath, arguments, workingDirectory);
        if (lower)
        {
            try
            {
                process.PriorityClass = ProcessPriorityClass.BelowNormal;
            }
            catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception or PlatformNotSupportedException)
            {
            }
        }
        var output = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        var errors = process.StandardError.ReadToEndAsync(CancellationToken.None);
        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
            }
            throw;
        }
        await output;
        if (process.ExitCode != 0)
            logger.LogWarning("ffmpeg failed on a music video ({Code}): {Error}", process.ExitCode, (await errors).Split('\n').FirstOrDefault());
        return process.ExitCode == 0;
    }
}
