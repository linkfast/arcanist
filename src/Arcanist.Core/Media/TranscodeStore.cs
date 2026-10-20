using Arcanist.Core.Caching;
using Arcanist.Core.Data;
using Arcanist.Core.Hosting;

namespace Arcanist.Core.Media;

/// <summary>A version made ahead of time, in a format and bit rate. Part of the API contract.</summary>
/// <param name="BitRate">kbit/s; 0 for FLAC.</param>
public sealed record TranscodeTarget(TranscodeFormat Format, int BitRate);

/// <summary>How converted audio is kept. Part of the API contract.</summary>
/// <param name="CacheEnabled">Keep whole conversions to play them again without converting (seeking included).</param>
/// <param name="CacheMaxBytes">Size of that cache; the least recently played go first.</param>
/// <param name="CacheMaxAgeDays">Conversions not played for this long are removed.</param>
/// <param name="Pregenerate">Versions to make ahead of time for every song (passive transcoding).</param>
/// <param name="PregenerateLibraries">Libraries to make them for; null for all of them (local and S3 ones).</param>
/// <param name="PregenerateOnlyLossless">Only of lossless originals (lossy ones are usually small already).</param>
public sealed record TranscodingConfig(
    bool CacheEnabled,
    long CacheMaxBytes,
    int CacheMaxAgeDays,
    IReadOnlyList<TranscodeTarget> Pregenerate,
    IReadOnlyList<Guid>? PregenerateLibraries,
    bool PregenerateOnlyLossless)
{
    public static readonly TranscodingConfig Default = new(true, 2_000_000_000, 30, [], null, false);

    public const long MinCacheBytes = 64_000_000;

    public TranscodingConfig Normalized() => this with
    {
        CacheMaxBytes = Math.Max(MinCacheBytes, CacheMaxBytes),
        CacheMaxAgeDays = Math.Clamp(CacheMaxAgeDays, 1, 3650),
        Pregenerate = Pregenerate
            .Select(t => t with { BitRate = TranscodeProfile.For(t.Format).IsLossless ? 0 : TranscodeProfile.For(t.Format).Clamp(t.BitRate) })
            .Distinct().ToList(),
        PregenerateLibraries = PregenerateLibraries?.Distinct().ToList(),
    };
}

/// <summary>The setting <see cref="SettingKeys.Transcoding"/>, cached.</summary>
public sealed class TranscodingSettings(SettingsStore store)
{
    private volatile TranscodingConfig? _current;

    /// <summary>The last value read (the default until then): for the hot path of streaming.</summary>
    public TranscodingConfig Current => _current ?? TranscodingConfig.Default;

    public event Action? Changed;

    public async Task<TranscodingConfig> GetAsync(CancellationToken cancellationToken) =>
        _current ??= (await store.GetAsync(SettingKeys.Transcoding, TranscodingConfig.Default, cancellationToken)).Normalized();

    public async Task<TranscodingConfig> SetAsync(TranscodingConfig config, CancellationToken cancellationToken)
    {
        config = config.Normalized();
        await store.SetAsync(SettingKeys.Transcoding, config, cancellationToken);
        _current = config;
        Changed?.Invoke();
        return config;
    }

    public void Invalidate() => _current = null;
}

/// <summary>
/// Converted audio kept on disk: the cache of whole conversions as they were streamed (<c>cache/transcodes</c>,
/// least recently used first out) and the versions made ahead of time (<c>transcodes</c>, kept while their song
/// and their setting are). Both are named by the audio's content hash, format and bit rate.
/// </summary>
public sealed class TranscodeStore
{
    private readonly TranscodingSettings _settings;

    public TranscodeStore(ArcanistPaths paths, TranscodingSettings settings, TimeProvider time, ILogger<TranscodeStore> logger)
    {
        _settings = settings;
        Cache = new DiskCache(Path.Combine(paths.Cache, "transcodes"), () => settings.Current.CacheMaxBytes, time, logger);
        // Never trimmed by size: what is kept is what the settings ask for.
        Pregenerated = new DiskCache(paths.Transcodes, () => long.MaxValue, time, logger);
    }

    public DiskCache Cache { get; }

    public DiskCache Pregenerated { get; }

    public static string Key(string contentHash, TranscodeProfile profile, int bitRate) =>
        $"{contentHash}-{(profile.Format?.ToString() ?? "lpcm").ToLowerInvariant()}-{(profile.IsLossless ? 0 : bitRate)}";

    /// <summary>A kept conversion of the audio, made ahead of time or cached.</summary>
    public string? Find(string contentHash, TranscodeProfile profile, int bitRate)
    {
        var key = Key(contentHash, profile, bitRate);
        return Pregenerated.TryGet(key) ?? (_settings.Current.CacheEnabled ? Cache.TryGet(key) : null);
    }
}
