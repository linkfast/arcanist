using System.ComponentModel;
using Arcanist.Core.Application.Extensions;
using Arcanist.Core.Application.Frontends;
using GraphQL;

namespace Arcanist.Core.Api.GraphQL;

public sealed partial class Query
{
    [Name("frontends")]
    [Description("Installed web players; the default one is served at /. Administrators only.")]
    public static Task<IReadOnlyList<Frontend>> Frontends([FromServices] FrontendService frontends, CancellationToken cancellationToken) =>
        frontends.ListAsync(cancellationToken);

    [Name("availablePlayers")]
    [Description("The web players any user can open (to switch from one to another), the default one first.")]
    public static Task<IReadOnlyList<AvailablePlayer>> AvailablePlayers([FromServices] FrontendService frontends, CancellationToken cancellationToken) =>
        frontends.AvailableAsync(cancellationToken);

    [Name("playerMarketplace")]
    [Description("The web players of the catalog (github.com/linkfast/arcanist-extensions by default: Ultimate Web Player and those anyone publishes), named in `language`, with what is installed here. Read again after an hour, or now with `refresh`. Administrators only.")]
    public static Task<PlayerMarketplaceInfo> PlayerMarketplace(string? language, bool? refresh, [FromServices] PlayerMarketplaceService marketplace, CancellationToken cancellationToken) =>
        marketplace.GetAsync(language, refresh ?? false, cancellationToken);
}

public sealed partial class Mutation
{
    [Name("installMarketplacePlayer")]
    [Description("Installs (or updates) a web player of the catalog, checked against the SHA-256 the catalog publishes, and serves it at / when `makeDefault`. It runs with its users' sessions. Administrators only, with a recent authentication.")]
    public static Task<Frontend> InstallMarketplacePlayer(InstallMarketplacePlayerInput input, [FromServices] PlayerMarketplaceService marketplace, CancellationToken cancellationToken) =>
        marketplace.InstallAsync(input, cancellationToken);

    [Name("installFrontendFromUrl")]
    [Description("Downloads a web player's .zip and installs it (or updates it). It runs with the user's session: install only what you trust. Administrators only, with a recent authentication. To upload a .zip, use POST /rest/v1/frontends.")]
    public static Task<Frontend> InstallFrontendFromUrl(string url, [FromServices] FrontendService frontends, CancellationToken cancellationToken) =>
        frontends.InstallFromUrlAsync(url, cancellationToken);

    [Name("setDefaultFrontend")]
    [Description("Serves this web player at /. Administrators only, with a recent authentication.")]
    public static Task<Frontend> SetDefaultFrontend(string id, [FromServices] FrontendService frontends, CancellationToken cancellationToken) =>
        frontends.SetDefaultAsync(id, cancellationToken);

    [Name("uninstallFrontend")]
    [Description("Removes an installed web player. Administrators only, with a recent authentication.")]
    public static async Task<bool> UninstallFrontend(string id, [FromServices] FrontendService frontends, CancellationToken cancellationToken)
    {
        await frontends.UninstallAsync(id, cancellationToken);
        return true;
    }
}
