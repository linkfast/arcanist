using Arcanist.Core.Hosting;
using Arcanist.Core.Packages;

namespace Arcanist.Core.Application.Frontends;

/// <summary>An installed web player. Part of the API contract.</summary>
/// <param name="BuiltIn">Bundled with the server; it cannot be uninstalled.</param>
/// <param name="IsDefault">Served at <c>/</c>.</param>
/// <param name="Path">Where it is always available: <c>/players/&lt;id&gt;/</c>.</param>
public sealed record Frontend(
    string Id,
    string Name,
    string Version,
    string? Description,
    string? Author,
    string? License,
    string? Homepage,
    int MinApiVersion,
    bool BuiltIn,
    bool IsDefault,
    string Path,
    DateTime InstalledAt,
    bool OpenSource = true,
    bool Blocked = false);

/// <summary>
/// Installing and choosing web players. Administrators only, and changes need a recent authentication: a frontend
/// runs on the same origin as the admin panel, with the signed-in user's session.
/// </summary>
public sealed class FrontendService(AccessGuard guard, FrontendCatalog catalog, IHttpClientFactory clients)
{
    public const string HttpClientName = "packages";

    public async Task<IReadOnlyList<Frontend>> ListAsync(CancellationToken cancellationToken)
    {
        guard.Admin();
        var current = await catalog.DefaultAsync(cancellationToken);
        return catalog.List().Select(f => ToModel(f, current) with
        {
            OpenSource = Hosting.OpenSource.IsOpenLicense(f.Manifest.License), Blocked = !catalog.IsUsable(f),
        }).ToList();
    }

    public async Task<Frontend> InstallAsync(Stream archive, CancellationToken cancellationToken)
    {
        RequireSensitive();
        var frontend = await catalog.InstallAsync(archive, cancellationToken);
        return ToModel(frontend, await catalog.DefaultAsync(cancellationToken));
    }

    /// <summary>Downloads a frontend's .zip (e.g. from a GitHub release) and installs it.</summary>
    public async Task<Frontend> InstallFromUrlAsync(string url, CancellationToken cancellationToken)
    {
        RequireSensitive();
        await using var archive = await PackageArchive.DownloadAsync(clients.CreateClient(HttpClientName), url, cancellationToken);
        return await InstallAsync(archive, cancellationToken);
    }

    public async Task<Frontend> SetDefaultAsync(string id, CancellationToken cancellationToken)
    {
        RequireSensitive();
        await catalog.SetDefaultAsync(id, cancellationToken);
        return ToModel(catalog.Find(id)!, await catalog.DefaultAsync(cancellationToken));
    }

    public async Task UninstallAsync(string id, CancellationToken cancellationToken)
    {
        RequireSensitive();
        await catalog.UninstallAsync(id, cancellationToken);
    }

    private void RequireSensitive()
    {
        guard.Admin();
        guard.RecentAuthentication();
    }

    private static Frontend ToModel(InstalledFrontend frontend, InstalledFrontend? current)
    {
        var manifest = frontend.Manifest;
        return new Frontend(
            manifest.Id, manifest.Name, manifest.Version, manifest.Description, manifest.Author, manifest.License, manifest.Homepage,
            manifest.MinApiVersion, frontend.BuiltIn, current?.Manifest.Id == manifest.Id, $"{FrontendHost.PlayersPath}/{manifest.Id}/",
            frontend.InstalledAt);
    }
}
