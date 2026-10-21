using Arcanist.Core.Data;

namespace Arcanist.Core.Media;

/// <summary>A kind of Ultimate client, each with its own default quality. Part of the API contract.</summary>
public enum StreamingClient
{
    /// <summary>Ultimate Desktop.</summary>
    Desktop,
    /// <summary>Ultimate Web Player (and other players in a browser).</summary>
    Web,
    /// <summary>The Ultimate apps for phones and tablets.</summary>
    Mobile,
}

/// <summary>What a client gets when it asks for no quality of its own. Part of the API contract.</summary>
/// <param name="Format">The format it is converted to; null sends the original.</param>
/// <param name="MaxBitRate">kbit/s. With a format, the conversion's bit rate (null: the format's default); with the
/// original, it is converted to MP3 only when above it (null: never).</param>
public sealed record ClientStreamingQuality(TranscodeFormat? Format, int? MaxBitRate)
{
    public static readonly ClientStreamingQuality Original = new(null, null);

    public ClientStreamingQuality Normalized()
    {
        if (Format is { } format)
        {
            var profile = TranscodeProfile.For(format);
            return new(format, profile.IsLossless || MaxBitRate is null ? null : profile.Clamp(MaxBitRate.Value));
        }

        return new(null, MaxBitRate is { } limit ? Math.Clamp(limit, 32, 320) : null);
    }
}

/// <summary>
/// The quality the Ultimate clients (desktop, web, mobile) get by default: they read it, and the stream applies it
/// to requests with <c>client=</c> and no quality of their own. Part of the API contract.
/// </summary>
public sealed record StreamingDefaults(ClientStreamingQuality Desktop, ClientStreamingQuality Web, ClientStreamingQuality Mobile)
{
    /// <summary>The original on computers; Opus at 128 kbit/s on phones, which saves data and sounds the same to most.</summary>
    public static readonly StreamingDefaults Default = new(ClientStreamingQuality.Original, ClientStreamingQuality.Original,
        new ClientStreamingQuality(TranscodeFormat.Opus, 128));

    public ClientStreamingQuality For(StreamingClient client) => client switch
    {
        StreamingClient.Desktop => Desktop,
        StreamingClient.Web => Web,
        _ => Mobile,
    };

    public StreamingDefaults Normalized() =>
        new((Desktop ?? ClientStreamingQuality.Original).Normalized(), (Web ?? ClientStreamingQuality.Original).Normalized(),
            (Mobile ?? ClientStreamingQuality.Original).Normalized());
}

/// <summary>The setting <see cref="SettingKeys.StreamingDefaults"/>, cached for streaming.</summary>
public sealed class StreamingDefaultsSettings(SettingsStore store)
{
    private volatile StreamingDefaults? _current;

    public async Task<StreamingDefaults> GetAsync(CancellationToken cancellationToken) =>
        _current ??= (await store.GetAsync(SettingKeys.StreamingDefaults, StreamingDefaults.Default, cancellationToken)).Normalized();

    public async Task<StreamingDefaults> SetAsync(StreamingDefaults defaults, CancellationToken cancellationToken)
    {
        defaults = defaults.Normalized();
        await store.SetAsync(SettingKeys.StreamingDefaults, defaults, cancellationToken);
        return _current = defaults;
    }
}
