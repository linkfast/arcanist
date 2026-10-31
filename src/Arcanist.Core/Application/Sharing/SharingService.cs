using Arcanist.Core.Data.Entities;

namespace Arcanist.Core.Application.Sharing;

/// <summary>Whether users may share outside the server (public links, guests in Jams), set by administrators.</summary>
public sealed class SharingService(AccessGuard guard, SharingSettings settings)
{
    /// <summary>The server's settings and what the user asking may do with them.</summary>
    public async Task<SharingStatus> GetAsync(CancellationToken cancellationToken)
    {
        var access = guard.Authenticated();
        var config = await settings.GetAsync(cancellationToken);
        return ToModel(config, access.Has(Permissions.ShareLinks));
    }

    /// <summary>Turns public links and guests on or off: turned off, those there were stop working at once.</summary>
    public async Task<SharingStatus> SetAsync(SharingSettingsInput input, CancellationToken cancellationToken)
    {
        guard.Admin();
        guard.RecentAuthentication();
        var config = await settings.SetAsync(new SharingConfig(input.PublicLinks, input.JamGuests, input.GuestAddLimit, input.MaxGuests), cancellationToken);
        return ToModel(config, true);
    }

    private static SharingStatus ToModel(SharingConfig config, bool canShare) =>
        new(config.PublicLinks, config.JamGuests, config.GuestAddLimit, config.MaxGuests, canShare && (config.PublicLinks || config.JamGuests));
}
