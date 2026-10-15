using System.Text.Json.Serialization;
using Arcanist.Core.Auth;
using Arcanist.Core.Extensions;
using Arcanist.Core.Extensions.Mstream;

namespace Arcanist.Core.Application.Mstream;

// Types in this file are part of the public API contracts (GraphQL schema and OpenAPI document).

/// <summary>
/// Functions of the mStream apps that Arcanist offers with what it already has. All are on until switched off in the
/// panel; off, the server tells the apps it does not have them, and they hide them.
/// </summary>
// Stored by name, so the settings survive reordering the values.
[JsonConverter(typeof(JsonStringEnumConverter<MstreamFeature>))]
public enum MstreamFeature
{
    /// <summary>Waveforms of the songs, from Arcanist's analysis (<c>db/waveform</c>).</summary>
    Waveform,

    /// <summary>
    /// mStream 6.27's listening statistics (<c>stats/*</c>): plays reported with their own ids, history, top lists,
    /// summaries. Off, the apps report plays the older way, which Arcanist also records.
    /// </summary>
    Stats,

    /// <summary>
    /// Similar songs and artists, and journeys from one song to another (<c>discovery/local/*</c>), from Arcanist's
    /// tempo and key analysis, genres and related artists.
    /// </summary>
    Similar,

    /// <summary>Progress of scans and of the audio analysis (<c>scan/status</c>, <c>scan/progress</c>).</summary>
    ScanStatus,
}

/// <param name="Enabled">The mStream extension is on.</param>
/// <param name="StarThreshold">
/// In single-star mode: the mStream rating (1 to 5 stars) from which a song gets the star. Lower ratings are kept as
/// such (unseen in single-star mode). 1: any rating gives the star.
/// </param>
/// <param name="PasswordSignIn">The apps may also sign in with the user's own password, not only with app passwords.</param>
/// <param name="Features">The apps' own functions that are on.</param>
/// <param name="QuickConnect">The apps may connect through the P2P network by scanning <paramref name="PairingCode"/>.</param>
/// <param name="P2pEnabled">The P2P network is on: the quick connect needs it.</param>
/// <param name="PairingCode">
/// What the apps scan (<c>mstr1:…</c>), as text or a QR code: the server's P2P address and the secret that opens the
/// tunnel. Null while the quick connect is off or the P2P node is not running.
/// </param>
public sealed record MstreamStatus(
    bool Enabled,
    int StarThreshold,
    bool PasswordSignIn,
    IReadOnlyList<MstreamFeature> Features,
    bool QuickConnect,
    bool P2pEnabled,
    string? PairingCode);

/// <param name="QuickConnect">Let the apps connect through the P2P network by scanning a code; needs the P2P network (<c>p2p_disabled</c>).</param>
public sealed record MstreamSettingsInput(int StarThreshold, bool PasswordSignIn, IReadOnlyList<MstreamFeature> Features, bool QuickConnect = false);

/// <summary>How the mStream extension behaves with the mStream apps. Administrators only.</summary>
public sealed class MstreamService(
    AccessGuard guard,
    ExtensionHost extensions,
    MstreamSettings settings,
    MstreamQuickConnect quickConnect,
    Core.P2P.P2PSettings p2p,
    Core.P2P.P2PNode node)
{
    public async Task<MstreamStatus> StatusAsync(CancellationToken cancellationToken)
    {
        guard.Admin();
        var config = await settings.GetAsync(cancellationToken);
        return new MstreamStatus(extensions.IsEnabled(MstreamExtension.Id), config.StarThreshold, config.PasswordSignIn, config.Features,
            config.QuickConnect, (await p2p.GetAsync(cancellationToken)).Enabled, await quickConnect.PairingCodeAsync(cancellationToken));
    }

    /// <summary>
    /// A new pairing code: apps that scanned the old one are disconnected and must scan the new one. Administrators
    /// only, with a recent authentication.
    /// </summary>
    public async Task<MstreamStatus> RotateQuickConnectAsync(CancellationToken cancellationToken)
    {
        guard.Admin();
        guard.RecentAuthentication();
        await quickConnect.RotateAsync(cancellationToken);
        await node.ApplyNowAsync(cancellationToken);
        return await StatusAsync(cancellationToken);
    }

    /// <summary>Changes take effect at once; tokens given with the user's password stop working when that sign-in is turned off.</summary>
    public async Task<MstreamStatus> SetSettingsAsync(MstreamSettingsInput input, CancellationToken cancellationToken)
    {
        guard.Admin();
        guard.RecentAuthentication();
        var errors = new ValidationErrors();
        if (input.StarThreshold is < 1 or > 5)
            errors.Add("starThreshold", ErrorCodes.InvalidFormat);
        var features = (input.Features ?? []).Distinct().ToList();
        if (features.Any(f => !Enum.IsDefined(f)))
            errors.Add("features", ErrorCodes.InvalidFormat);
        if (input.QuickConnect && !(await p2p.GetAsync(cancellationToken)).Enabled)
            errors.Add("quickConnect", ErrorCodes.P2PDisabled);
        errors.ThrowIfAny();

        var off = Enum.GetValues<MstreamFeature>().Where(f => !features.Contains(f)).ToList();
        var config = new MstreamConfig(input.StarThreshold, input.PasswordSignIn, off, input.QuickConnect);
        var quickConnectChanged = (await settings.GetAsync(cancellationToken)).QuickConnect != config.QuickConnect;
        await settings.SetAsync(config, cancellationToken);
        if (quickConnectChanged)
            await node.ApplyNowAsync(cancellationToken);
        return await StatusAsync(cancellationToken);
    }
}
