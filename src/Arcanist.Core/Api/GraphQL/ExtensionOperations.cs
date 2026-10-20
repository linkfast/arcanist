using System.ComponentModel;
using Arcanist.Core.Application.Extensions;
using GraphQL;

namespace Arcanist.Core.Api.GraphQL;

public sealed partial class Query
{
    [Name("extensions")]
    [Description("Built-in and installed extensions, with what happened to them at startup. Administrators only.")]
    public static IReadOnlyList<Extension> Extensions([FromServices] ExtensionService extensions) => extensions.List();

    [Name("extensionApps")]
    [Description("Pages of the running extensions the user may open from a player, in a window (an iframe) or a tab of its own, named in `language` (or the request's Accept-Language). They get the session and the player's theme with the host protocol of docs/developers/extensions.md.")]
    public static Task<IReadOnlyList<ExtensionAppInfo>> ExtensionApps(string? language, [FromServices] ExtensionService extensions, CancellationToken cancellationToken) =>
        extensions.AppsAsync(language, cancellationToken);

    [Name("extensionMarketplace")]
    [Description("The extension catalog (github.com/linkfast/arcanist-extensions by default), named in `language`, with what is installed here. Read again after an hour, or now with `refresh`. Administrators only.")]
    public static Task<ExtensionMarketplaceInfo> ExtensionMarketplace(string? language, bool? refresh, [FromServices] ExtensionMarketplaceService marketplace, CancellationToken cancellationToken) =>
        marketplace.GetAsync(language, refresh ?? false, cancellationToken);
}

public sealed partial class Mutation
{
    [Name("installExtensionFromUrl")]
    [Description("Downloads an extension's .zip and installs it (or updates it); it runs once enabled and the server restarts. Extensions run inside the server with full trust. Administrators only, with a recent authentication. To upload a .zip, use POST /rest/v1/extensions.")]
    public static Task<Extension> InstallExtensionFromUrl(string url, [FromServices] ExtensionService extensions, CancellationToken cancellationToken) =>
        extensions.InstallFromUrlAsync(url, cancellationToken);

    [Name("installMarketplaceExtension")]
    [Description("Installs (or updates) an extension of the catalog, checked against the SHA-256 the catalog publishes, and switches it on unless `enable` is false; it runs after a restart. Administrators only, with a recent authentication.")]
    public static Task<Extension> InstallMarketplaceExtension(InstallMarketplaceExtensionInput input, [FromServices] ExtensionMarketplaceService marketplace, CancellationToken cancellationToken) =>
        marketplace.InstallAsync(input, cancellationToken);

    [Name("setExtensionEnabled")]
    [Description("Switches an extension on or off. Built-in extensions and switching off apply at once; an installed one runs after a restart. Administrators only, with a recent authentication.")]
    public static Task<Extension> SetExtensionEnabled(string id, bool enabled, [FromServices] ExtensionService extensions, CancellationToken cancellationToken) =>
        extensions.SetEnabledAsync(id, enabled, cancellationToken);

    [Name("uninstallExtension")]
    [Description("Removes an installed extension (it stops at once and is gone after a restart). Administrators only, with a recent authentication.")]
    public static async Task<bool> UninstallExtension(string id, [FromServices] ExtensionService extensions, CancellationToken cancellationToken)
    {
        await extensions.UninstallAsync(id, cancellationToken);
        return true;
    }
}
