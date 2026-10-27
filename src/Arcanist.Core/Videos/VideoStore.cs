using Arcanist.Core.Caching;
using Arcanist.Core.Hosting;

namespace Arcanist.Core.Videos;

/// <summary>
/// Where converted music videos are kept. Progressive MP4 versions made when played go to a cache
/// (<c>cache/videos</c>, least recently used out first). The renditions of adaptive streaming are made ahead of time
/// into <c>transcodes/videos/&lt;video hash&gt;/</c>: one folder per picture rendition (<c>v-1080p</c>,
/// <c>v-native</c>) and per audio (<c>a-video</c>, and <c>a-track-&lt;audio hash&gt;-&lt;aligner version&gt;</c> for
/// the mix), each written to a hidden folder and renamed when finished.
/// </summary>
public sealed class VideoStore
{
    private readonly string _prepared;

    public VideoStore(ArcanistPaths paths, VideoSettings settings, TimeProvider time, ILogger<VideoStore> logger)
    {
        Cache = new DiskCache(Path.Combine(paths.Cache, "videos", "versions"), () => settings.Current.CacheMaxBytes, time, logger);
        _prepared = Path.Combine(paths.Transcodes, "videos");
    }

    public DiskCache Cache { get; }

    /// <summary>
    /// A hash as names on disk use it: its first 24 characters (96 bits, plenty to tell files apart) keep the paths
    /// of renditions within what Windows allows.
    /// </summary>
    public static string Short(string hash) => hash.Length <= 24 ? hash : hash[..24];

    /// <summary>The audio part of a key: the video's own, or the mix with an audio file by an aligner version.</summary>
    public static string AudioKey(VideoAudioMode mode, string? audioHash, int alignerVersion) =>
        mode == VideoAudioMode.Track && audioHash is not null ? $"track-{Short(audioHash)}-{alignerVersion}" : "video";

    public static string CacheKey(string videoHash, string audioKey, VideoRendition rendition) => $"{Short(videoHash)}-{audioKey}-{rendition.Name}.mp4";

    public string PreparedRoot(string videoHash) => Path.Combine(_prepared, Short(videoHash));

    public string PictureFolder(string videoHash, VideoRendition rendition) => Path.Combine(PreparedRoot(videoHash), $"v-{rendition.Name}");

    public string AudioFolder(string videoHash, string audioKey) => Path.Combine(PreparedRoot(videoHash), $"a-{audioKey}");

    public static bool IsReady(string folder) => File.Exists(Path.Combine(folder, "index.m3u8"));

    /// <summary>A hidden folder to write a rendition to before <see cref="Commit"/>.</summary>
    public static string Partial(string folder) => Path.Combine(Path.GetDirectoryName(folder)!, $".partial-{Guid.NewGuid().ToString("N")[..12]}");

    public static void Commit(string partial, string folder)
    {
        try
        {
            Directory.Move(partial, folder);
        }
        catch (IOException)
        {
            // Made by another pass meanwhile.
            Discard(partial);
        }
    }

    public static void Discard(string folder)
    {
        try
        {
            if (Directory.Exists(folder))
                Directory.Delete(folder, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>The videos with prepared renditions (their hashes).</summary>
    public IEnumerable<string> PreparedVideos() =>
        Directory.Exists(_prepared) ? Directory.EnumerateDirectories(_prepared).Select(Path.GetFileName).OfType<string>() : [];

    /// <summary>Bytes of everything prepared ahead of time.</summary>
    public long PreparedBytes() =>
        Directory.Exists(_prepared) ? new DirectoryInfo(_prepared).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length) : 0;
}
