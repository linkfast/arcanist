using Arcanist.Core.Data;

namespace Arcanist.Core.Application.Sharing;

/// <summary>What the server lets its users share outside it. Part of the API contract.</summary>
/// <param name="PublicLinks">Public links to playlists: anyone with one sees the playlist and hears 30-second previews.</param>
/// <param name="JamGuests">Guests without an account in Jukebox Jams: they search and add songs, they hear nothing.</param>
/// <param name="GuestAddLimit">Songs a guest can add to a Jam every 10 minutes.</param>
/// <param name="MaxGuests">Guests a Jam takes at most.</param>
/// <param name="PublicUrl">Where the server is reached from outside (<c>https://music.example.com</c>): the links it shares
/// start with it. Null: they are relative, and clients complete them with the address they opened the server with.</param>
/// <param name="JamDeletePlaylist">What a new Jukebox Jam does with its playlist when it ends, unless whoever starts it says
/// otherwise: deleted (the default, also for servers from before the setting) or kept as a playlist of the host's.</param>
public sealed record SharingConfig(
    bool PublicLinks, bool JamGuests, int GuestAddLimit, int MaxGuests, string? PublicUrl = null, bool JamDeletePlaylist = true)
{
    public static readonly SharingConfig Default = new(false, false, 5, 100);

    public SharingConfig Normalized() => this with
    {
        GuestAddLimit = Math.Clamp(GuestAddLimit, 1, 100),
        MaxGuests = Math.Clamp(MaxGuests, 1, 1000),
        PublicUrl = SharingSettings.NormalizeUrl(PublicUrl),
    };
}

/// <summary>The server's <see cref="SharingConfig"/>, cached; off until an admin turns it on.</summary>
public sealed class SharingSettings(SettingsStore store, IConfiguration configuration)
{
    /// <summary>The public URL set outside the panel (<c>Arcanist__Network__PublicUrl</c>): it wins over the setting.</summary>
    public const string PublicUrlKey = "Arcanist:Network:PublicUrl";

    private volatile SharingConfig? _current;

    public event Action? Changed;

    public async Task<SharingConfig> GetAsync(CancellationToken cancellationToken) =>
        _current ??= (await store.GetAsync(SettingKeys.Sharing, SharingConfig.Default, cancellationToken)).Normalized();

    public async Task<SharingConfig> SetAsync(SharingConfig config, CancellationToken cancellationToken)
    {
        config = config.Normalized();
        await store.SetAsync(SettingKeys.Sharing, config, cancellationToken);
        _current = config;
        Changed?.Invoke();
        return config;
    }

    public void Invalidate() => _current = null;

    /// <summary>The public URL from the environment, normalized; null when it is not set there (or is not a URL).</summary>
    public string? EnvironmentPublicUrl => configuration[PublicUrlKey] is { } value && TryNormalizeUrl(value, out var url) ? url : null;

    /// <summary>The server's public URL (the environment's, otherwise the setting's), without a trailing slash; null if none.</summary>
    public async Task<string?> PublicUrlAsync(CancellationToken cancellationToken) =>
        EnvironmentPublicUrl ?? (await GetAsync(cancellationToken)).PublicUrl;

    /// <summary>An absolute http(s) URL without query or fragment, its trailing slash taken off; null when blank.</summary>
    /// <exception cref="ArcanistException.Validation">It is not such a URL (<c>publicUrl: invalid_format</c>).</exception>
    public static string? NormalizeUrl(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null
        : TryNormalizeUrl(value, out var url) ? url
        : throw ArcanistException.Field("publicUrl", ErrorCodes.InvalidFormat);

    private static bool TryNormalizeUrl(string value, out string? url)
    {
        url = null;
        value = value.Trim();
        if (value.Length == 0)
            return false;
        // A bare host ("music.example.com") is taken as https.
        if (!value.Contains("://", StringComparison.Ordinal))
            value = "https://" + value;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") || uri.Host.Length == 0
            || uri.Query.Length > 0 || uri.Fragment.Length > 0 || uri.UserInfo.Length > 0)
            return false;
        url = uri.GetLeftPart(UriPartial.Path).TrimEnd('/');
        return true;
    }
}
