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
public sealed record MstreamStatus(bool Enabled, int StarThreshold, bool PasswordSignIn, IReadOnlyList<MstreamFeature> Features);

public sealed record MstreamSettingsInput(int StarThreshold, bool PasswordSignIn, IReadOnlyList<MstreamFeature> Features);

/// <summary>How the mStream extension behaves with the mStream apps. Administrators only.</summary>
public sealed class MstreamService(AccessGuard guard, ExtensionHost extensions, MstreamSettings settings)
{
    public async Task<MstreamStatus> StatusAsync(CancellationToken cancellationToken)
    {
        guard.Admin();
        var config = await settings.GetAsync(cancellationToken);
        return new MstreamStatus(extensions.IsEnabled(MstreamExtension.Id), config.StarThreshold, config.PasswordSignIn, config.Features);
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
        errors.ThrowIfAny();

        var off = Enum.GetValues<MstreamFeature>().Where(f => !features.Contains(f)).ToList();
        await settings.SetAsync(new MstreamConfig(input.StarThreshold, input.PasswordSignIn, off), cancellationToken);
        return await StatusAsync(cancellationToken);
    }
}
