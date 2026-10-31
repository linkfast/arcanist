using System.Security.Cryptography;
using Arcanist.Core.Hosting;

namespace Arcanist.Core.Application.Sharing;

/// <summary>
/// Where shared links open: a player's page (<c>/players/&lt;id&gt;/#/s/&lt;token&gt;</c> for a playlist,
/// <c>#/jam/&lt;code&gt;</c> for a Jukebox Jam); links never need a route of their own. Playlists open in the default
/// player when it opens them (<see cref="FrontendManifest.SharedLinks"/>), otherwise the first that does. Jams open in
/// Ultimate Web Player when it is installed and is a Jam client (<see cref="FrontendManifest.JukeboxJam"/>), otherwise
/// in the server's generic client (<see cref="FrontendCatalog.JamClient"/>): a Jam never depends on a player having one.
/// </summary>
public sealed class SharingLinks(FrontendCatalog frontends)
{
    /// <summary>Ultimate Web Player's id: the Jam client of choice.</summary>
    public const string UltimateWebPlayerId = "ultimate-web-player";

    public async Task<string> PlaylistUrlAsync(string token, CancellationToken cancellationToken) =>
        $"{await PlayerPathAsync(cancellationToken)}#/s/{token}";

    public Task<string> JamUrlAsync(string code, CancellationToken cancellationToken)
    {
        var player = frontends.FindUsable(UltimateWebPlayerId) is { } ultimate && ultimate.Manifest.Has(FrontendManifest.JukeboxJam)
            ? ultimate.Manifest.Id
            : FrontendCatalog.JamClientId;
        return Task.FromResult($"/players/{player}/#/jam/{code}");
    }

    private async Task<string> PlayerPathAsync(CancellationToken cancellationToken)
    {
        var standard = await frontends.DefaultAsync(cancellationToken);
        var player = standard is not null && standard.Manifest.Has(FrontendManifest.SharedLinks)
            ? standard
            : frontends.List().Where(frontends.IsUsable).FirstOrDefault(f => f.Manifest.Has(FrontendManifest.SharedLinks)) ?? standard;
        return player is null ? "/" : $"/players/{player.Manifest.Id}/";
    }

    /// <summary>A new random token for a link (128 bits, base64url: 22 characters).</summary>
    public static string NewToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(16)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
