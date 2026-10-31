using Arcanist.Core.Data;

namespace Arcanist.Core.Application.Sharing;

/// <summary>What the server lets its users share outside it. Part of the API contract.</summary>
/// <param name="PublicLinks">Public links to playlists: anyone with one sees the playlist and hears 30-second previews.</param>
/// <param name="JamGuests">Guests without an account in Jukebox Jams: they search and add songs, they hear nothing.</param>
/// <param name="GuestAddLimit">Songs a guest can add to a Jam every 10 minutes.</param>
/// <param name="MaxGuests">Guests a Jam takes at most.</param>
public sealed record SharingConfig(bool PublicLinks, bool JamGuests, int GuestAddLimit, int MaxGuests)
{
    public static readonly SharingConfig Default = new(false, false, 5, 100);

    public SharingConfig Normalized() => this with
    {
        GuestAddLimit = Math.Clamp(GuestAddLimit, 1, 100),
        MaxGuests = Math.Clamp(MaxGuests, 1, 1000),
    };
}

/// <summary>The server's <see cref="SharingConfig"/>, cached; off until an admin turns it on.</summary>
public sealed class SharingSettings(SettingsStore store)
{
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
}
