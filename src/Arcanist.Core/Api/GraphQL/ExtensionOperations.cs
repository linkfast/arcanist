using System.ComponentModel;
using Arcanist.Core.Application.Extensions;
using GraphQL;

namespace Arcanist.Core.Api.GraphQL;

public sealed partial class Query
{
    [Name("extensions")]
    [Description("Built-in and installed extensions, with what happened to them at startup. Administrators only.")]
    public static IReadOnlyList<Extension> Extensions([FromServices] ExtensionService extensions) => extensions.List();
}

public sealed partial class Mutation
{
    [Name("installExtensionFromUrl")]
    [Description("Downloads an extension's .zip and installs it (or updates it); it runs once enabled and the server restarts. Extensions run inside the server with full trust. Administrators only, with a recent authentication. To upload a .zip, use POST /rest/v1/extensions.")]
    public static Task<Extension> InstallExtensionFromUrl(string url, [FromServices] ExtensionService extensions, CancellationToken cancellationToken) =>
        extensions.InstallFromUrlAsync(url, cancellationToken);

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
