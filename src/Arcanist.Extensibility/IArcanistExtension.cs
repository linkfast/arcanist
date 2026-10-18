using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Arcanist.Extensibility;

/// <summary>
/// An Arcanist extension: a .NET assembly the server loads at startup. It registers its services and maps its
/// endpoints, under <c>/ext/&lt;id&gt;/</c> or under the prefixes its manifest claims. It runs inside the server with full
/// trust, and should use the server's application services (in <c>Arcanist.Core.Application</c>) so permissions and
/// library access apply to what it does.
/// </summary>
public interface IArcanistExtension
{
    /// <summary>Registers the extension's services, before the server starts.</summary>
    void ConfigureServices(IServiceCollection services, ExtensionContext context)
    {
    }

    /// <summary>
    /// Maps the extension's endpoints. Every route must start with <see cref="ExtensionContext.BasePath"/> or with a
    /// prefix the manifest claims; the server refuses the extension otherwise. While the extension is disabled its
    /// endpoints answer 404.
    /// </summary>
    void MapEndpoints(IEndpointRouteBuilder endpoints, ExtensionContext context);
}

/// <summary>What the server gives an extension about itself.</summary>
/// <param name="Id">The extension's id, from its manifest.</param>
/// <param name="DataDirectory">A folder of its own for data it keeps, under the server's data directory.</param>
/// <param name="Configuration">Its settings: the <c>Arcanist:Extensions:&lt;id&gt;</c> section.</param>
/// <param name="PackageDirectory">The installed package's folder, to serve its own files from (its assemblies are
/// loaded from memory, so <c>Assembly.Location</c> is empty); null for built-in extensions.</param>
public sealed record ExtensionContext(string Id, string DataDirectory, IConfiguration Configuration, string? PackageDirectory = null)
{
    /// <summary>Where the extension's own endpoints live: <c>/ext/&lt;id&gt;</c>.</summary>
    public string BasePath => $"/ext/{Id}";
}

/// <summary>
/// What an extension package declares in <c>arcanist-extension.json</c> at its root. Built-in extensions declare
/// it in code.
/// </summary>
/// <param name="Id">Lowercase letters, digits and dashes.</param>
/// <param name="Name">Shown in the admin panel.</param>
/// <param name="Version">Shown in the admin panel.</param>
/// <param name="Description">What it does, shown in the admin panel.</param>
/// <param name="Author">Who made it.</param>
/// <param name="License">Its license, which must be compatible with the AGPL-3.0-or-later.</param>
/// <param name="Homepage">Where to learn more about it.</param>
/// <param name="MinApiVersion">Oldest Arcanist API major version it works with.</param>
/// <param name="Assembly">The file, relative to the package root, with the <see cref="IArcanistExtension"/> implementation.</param>
/// <param name="Routes">Prefixes it claims besides <c>/ext/&lt;id&gt;</c>, among those Arcanist keeps for compatibility
/// layers (<c>/api</c>, <c>/media</c>, <c>/album-art</c>, <c>/transcode</c>, <c>/shared</c>, <c>/rest</c>). Two enabled
/// extensions cannot claim the same prefix.</param>
/// <param name="Apps">Pages it offers to users, which players list and open (see <see cref="ExtensionApp"/>).</param>
public sealed record ExtensionManifest(
    string Id,
    string Name,
    string Version,
    string? Description = null,
    string? Author = null,
    string? License = null,
    string? Homepage = null,
    int MinApiVersion = 1,
    string Assembly = "",
    IReadOnlyList<string>? Routes = null,
    IReadOnlyList<ExtensionApp>? Apps = null);

/// <summary>
/// A page of an extension that users open from their players: in a window (an iframe) or a tab of its own. It talks
/// to the player that opened it with the host protocol (<c>docs/developers/extensions.md</c>) to get the session and
/// the player's theme.
/// </summary>
/// <param name="Id">Lowercase letters, digits and dashes; unique within the extension.</param>
/// <param name="Name">Its name, in English.</param>
/// <param name="Path">Where it is served, under <c>/ext/&lt;id&gt;/</c>.</param>
/// <param name="Description">What it is for, in English.</param>
/// <param name="Names">Its name in other languages, by language code (<c>es</c>).</param>
/// <param name="Descriptions">Its description in other languages.</param>
/// <param name="Icon">A square SVG icon, also under <c>/ext/&lt;id&gt;/</c>.</param>
/// <param name="Permissions">What a user needs to see it, as the API names permissions (<c>UPLOAD</c>).</param>
/// <param name="AdminOnly">Only administrators see it.</param>
/// <param name="Width">The window's preferred width, in CSS pixels.</param>
/// <param name="Height">The window's preferred height.</param>
public sealed record ExtensionApp(
    string Id,
    string Name,
    string Path,
    string? Description = null,
    IReadOnlyDictionary<string, string>? Names = null,
    IReadOnlyDictionary<string, string>? Descriptions = null,
    string? Icon = null,
    IReadOnlyList<string>? Permissions = null,
    bool AdminOnly = false,
    int? Width = null,
    int? Height = null);
