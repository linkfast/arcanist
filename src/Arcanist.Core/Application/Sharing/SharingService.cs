using Arcanist.Core.Data.Entities;

namespace Arcanist.Core.Application.Sharing;

/// <summary>Whether users may share outside the server (public links, guests in Jams), set by administrators.</summary>
public sealed class SharingService(AccessGuard guard, SharingSettings settings, IServiceProvider services)
{
    /// <summary>The server's settings and what the user asking may do with them.</summary>
    public async Task<SharingStatus> GetAsync(CancellationToken cancellationToken)
    {
        var access = guard.Authenticated();
        var config = await settings.GetAsync(cancellationToken);
        return ToModel(config, access.Has(Permissions.ShareLinks), access.Has(Permissions.Invite));
    }

    /// <summary>Turns public links and guests on or off: turned off, those there were stop working at once.</summary>
    public async Task<SharingStatus> SetAsync(SharingSettingsInput input, CancellationToken cancellationToken)
    {
        guard.Admin();
        guard.RecentAuthentication();
        var current = await settings.GetAsync(cancellationToken);
        // Null keeps the public URL (clients from before it); empty takes it off.
        var publicUrl = input.PublicUrl is null ? current.PublicUrl : SharingSettings.NormalizeUrl(input.PublicUrl);
        if (publicUrl != current.PublicUrl && settings.EnvironmentPublicUrl is not null)
            throw new ArcanistException.Conflict(ErrorCodes.SetByEnvironment);
        // Null keeps what a Jam does with its playlist and whether invitations make accounts (clients from before them).
        var config = await settings.SetAsync(
            new SharingConfig(input.PublicLinks, input.JamGuests, input.GuestAddLimit, input.MaxGuests, publicUrl,
                input.JamDeletePlaylist ?? current.JamDeletePlaylist, input.InvitationSignup ?? current.InvitationSignup), cancellationToken);
        return ToModel(config, true, true);
    }

    private SharingStatus ToModel(SharingConfig config, bool canShare, bool canInvite)
    {
        var environment = settings.EnvironmentPublicUrl;
        var publicUrl = environment ?? config.PublicUrl;
        var node = services.GetService<Arcanist.Core.P2P.P2PNode>()?.State;
        return new(config.PublicLinks, config.JamGuests, config.GuestAddLimit, config.MaxGuests, canShare && (config.PublicLinks || config.JamGuests),
            publicUrl, environment is not null, node is { Running: true } ? node.NodeId : null,
            config.JamDeletePlaylist, config.InvitationSignup, canInvite && publicUrl is not null);
    }
}
