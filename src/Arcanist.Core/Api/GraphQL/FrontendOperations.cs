using System.ComponentModel;
using Arcanist.Core.Application.Frontends;
using GraphQL;

namespace Arcanist.Core.Api.GraphQL;

public sealed partial class Query
{
    [Name("frontends")]
    [Description("Installed web players; the default one is served at /. Administrators only.")]
    public static Task<IReadOnlyList<Frontend>> Frontends([FromServices] FrontendService frontends, CancellationToken cancellationToken) =>
        frontends.ListAsync(cancellationToken);
}

public sealed partial class Mutation
{
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
