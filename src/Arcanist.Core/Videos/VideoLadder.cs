namespace Arcanist.Core.Videos;

/// <summary>
/// One version of a video: its height (and width, keeping the shape), the bit rates and the codec. The native one
/// keeps the video stream as it is (<see cref="Copy"/>) when browsers play its codec.
/// </summary>
public sealed record VideoRendition(int Height, int Width, int VideoBitRate, int AudioBitRate, string Codec, bool Copy)
{
    /// <summary>For URLs and cache keys: "native", or the height.</summary>
    public string Name => Copy ? "native" : $"{Height}p";

    public int Bandwidth => VideoBitRate + AudioBitRate;
}

/// <summary>The versions a video can be streamed in: from UHD down to 360p, never above its own height.</summary>
public static class VideoLadder
{
    public static readonly IReadOnlyList<int> Heights = [2160, 1440, 1080, 720, 480, 360];

    /// <summary>Video codecs MP4 carries and browsers play, kept as they are in the native version.</summary>
    private static readonly HashSet<string> Copyable = new(StringComparer.OrdinalIgnoreCase) { "h264", "hevc", "av1", "vp9" };

    private static int H264BitRate(int height) => height switch
    {
        >= 2160 => 20_000_000,
        >= 1440 => 10_000_000,
        >= 1080 => 6_000_000,
        >= 720 => 3_500_000,
        >= 480 => 1_600_000,
        _ => 900_000,
    };

    private static int AudioBitRate(int height) => height >= 1080 ? 256_000 : height >= 720 ? 192_000 : 128_000;

    /// <summary>
    /// The versions of a video <paramref name="width"/>×<paramref name="height"/> in <paramref name="codec"/>: the
    /// native one (kept as it is when its codec plays in browsers) and each lower step of the ladder that the
    /// installed ffmpeg can encode (HEVC above 1080p when it has an encoder for it).
    /// </summary>
    public static IReadOnlyList<VideoRendition> For(int? width, int? height, string? codec, IReadOnlySet<string> encoders)
    {
        var list = new List<VideoRendition>();
        if (width is not > 0 || height is not > 0)
            return list;
        var h264 = encoders.Contains("libx264");
        var hevc = encoders.Contains("libx265");

        if (codec is not null && Copyable.Contains(codec))
            list.Add(new VideoRendition(height.Value, width.Value, H264BitRate(height.Value), AudioBitRate(height.Value), codec.ToLowerInvariant(), true));
        else if (h264)
            list.Add(Encoded(width.Value, height.Value, height.Value, hevc));

        if (!h264)
            return list;
        foreach (var step in Heights.Where(h => h < height.Value - 16))
            list.Add(Encoded(width.Value, height.Value, step, hevc));
        return list;
    }

    private static VideoRendition Encoded(int width, int height, int step, bool hevc)
    {
        // Even width with the same shape: H.264 needs even sizes.
        var scaled = (int)Math.Round(width * (double)step / height / 2) * 2;
        var useHevc = hevc && step > 1080;
        var bitRate = useHevc ? H264BitRate(step) * 6 / 10 : H264BitRate(step);
        return new VideoRendition(step, scaled, bitRate, AudioBitRate(step), useHevc ? "hevc" : "h264", false);
    }

    /// <summary>The best version within <paramref name="maxHeight"/> and <paramref name="maxBitRate"/> (the lowest when none fits).</summary>
    public static VideoRendition? Pick(IReadOnlyList<VideoRendition> ladder, int? maxHeight, int? maxBitRate) =>
        ladder.Where(r => (maxHeight is not { } h || r.Height <= h) && (maxBitRate is not { } b || r.Bandwidth <= b))
            .OrderByDescending(r => r.Height).ThenByDescending(r => r.Copy)
            .FirstOrDefault()
        ?? ladder.OrderBy(r => r.Height).FirstOrDefault();
}
