using Arcanist.Core.Data;

namespace Arcanist.Core.Videos;

/// <summary>Whose audio plays with a music video. Part of the API contract.</summary>
public enum VideoAudioMode
{
    /// <summary>The song's audio from the library wherever the video plays it; the video's own elsewhere.</summary>
    Track,
    /// <summary>The video's own soundtrack, as it came.</summary>
    Video,
}

/// <summary>How the server handles music videos.</summary>
/// <param name="DefaultAudio">Whose audio plays when a user has not chosen.</param>
/// <param name="CacheMaxBytes">Size of the cache of converted and mixed videos (made when played).</param>
/// <param name="CacheMaxAgeDays">Days an unused entry stays in that cache.</param>
/// <param name="PrepareLibraryIds">Libraries whose videos are prepared ahead of time (for adaptive streaming).</param>
/// <param name="PrepareHeights">Heights prepared ahead of time (never above a video's own).</param>
/// <param name="PrepareWhenPlayed">Whether a video someone plays is prepared too, so the next time it streams adaptively.</param>
public sealed record VideoConfig(
    VideoAudioMode DefaultAudio,
    long CacheMaxBytes,
    int CacheMaxAgeDays,
    IReadOnlyList<Guid> PrepareLibraryIds,
    IReadOnlyList<int> PrepareHeights,
    bool PrepareWhenPlayed)
{
    public static readonly VideoConfig Default = new(VideoAudioMode.Track, 20_000_000_000, 30, [], [1080, 480], true);

    public const long MinCacheBytes = 1_000_000_000;

    public VideoConfig Normalized() => this with
    {
        CacheMaxBytes = Math.Max(MinCacheBytes, CacheMaxBytes),
        CacheMaxAgeDays = Math.Clamp(CacheMaxAgeDays, 1, 3650),
        PrepareLibraryIds = PrepareLibraryIds.Distinct().ToList(),
        PrepareHeights = PrepareHeights.Where(VideoLadder.Heights.Contains).Distinct().OrderDescending().ToList(),
    };
}

/// <summary>A user's own choices for music videos; null follows the server.</summary>
public sealed record UserVideoConfig(VideoAudioMode? Audio);

public sealed class VideoSettings(SettingsStore store)
{
    private volatile VideoConfig? _current;

    /// <summary>The last value read (the default until then).</summary>
    public VideoConfig Current => _current ?? VideoConfig.Default;

    public event Action? Changed;

    public async Task<VideoConfig> GetAsync(CancellationToken cancellationToken) =>
        _current ??= (await store.GetAsync(SettingKeys.Videos, VideoConfig.Default, cancellationToken)).Normalized();

    public async Task<VideoConfig> SetAsync(VideoConfig config, CancellationToken cancellationToken)
    {
        config = config.Normalized();
        await store.SetAsync(SettingKeys.Videos, config, cancellationToken);
        _current = config;
        Changed?.Invoke();
        return config;
    }

    public void Invalidate() => _current = null;

    public Task<UserVideoConfig> UserAsync(Guid userId, CancellationToken cancellationToken) =>
        store.GetAsync(SettingKeys.UserVideos(userId), new UserVideoConfig(null), cancellationToken);

    public Task SetUserAsync(Guid userId, UserVideoConfig config, CancellationToken cancellationToken) =>
        store.SetAsync(SettingKeys.UserVideos(userId), config, cancellationToken);

    /// <summary>Whose audio plays for the user: their own choice, or the server's default.</summary>
    public async Task<VideoAudioMode> AudioForAsync(Guid userId, CancellationToken cancellationToken) =>
        (await UserAsync(userId, cancellationToken)).Audio ?? (await GetAsync(cancellationToken)).DefaultAudio;
}
