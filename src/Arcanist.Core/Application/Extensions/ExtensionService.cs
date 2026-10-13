using Arcanist.Core.Extensions;
using Arcanist.Core.Hosting;
using Arcanist.Core.Packages;
using Arcanist.Extensibility;

namespace Arcanist.Core.Application.Extensions;

/// <summary>An extension: built in or installed. Part of the API contract.</summary>
/// <param name="Enabled">Switched on. Built-in extensions apply it at once; installed ones load at the next start.</param>
/// <param name="Status">What happened at startup: NOT_LOADED, RUNNING or FAILED.</param>
/// <param name="RestartRequired">The server must restart for the extension to run as installed and enabled.</param>
/// <param name="Routes">Prefixes it claims besides its own /ext/&lt;id&gt;.</param>
public sealed record Extension(
    string Id,
    string Name,
    string Version,
    string? Description,
    string? Author,
    string? License,
    string? Homepage,
    bool BuiltIn,
    bool Enabled,
    ExtensionStatus Status,
    string? ErrorCode,
    bool RestartRequired,
    IReadOnlyList<string> Routes,
    string BasePath);

/// <summary>
/// Installing and switching extensions. Administrators only, with a recent authentication for every change: an
/// extension runs inside the server with full trust.
/// </summary>
public sealed class ExtensionService(
    AccessGuard guard,
    ExtensionHost host,
    ArcanistPaths paths,
    ISettingsFileWriter settings,
    IHttpClientFactory clients)
{
    private static readonly SemaphoreSlim Changes = new(1, 1);

    public IReadOnlyList<Extension> List()
    {
        guard.Admin();
        return Current().Select(ToModel).ToList();
    }

    public async Task<Extension> InstallAsync(Stream archive, CancellationToken cancellationToken)
    {
        RequireSensitive();
        await Changes.WaitAsync(cancellationToken);
        try
        {
            var staging = Path.Combine(paths.Extensions, ".staging");
            Directory.CreateDirectory(staging);
            try
            {
                var unpacked = await PackageArchive.ExtractAsync(archive, staging, ExtensionHost.ManifestFile, cancellationToken);
                var manifest = PackageArchive.ReadManifest<ExtensionManifest>(unpacked, ExtensionHost.ManifestFile);
                ExtensionHost.Validate(manifest, unpacked);
                if (host.Find(manifest.Id) is { BuiltIn: true })
                    throw ArcanistException.Field("id", ErrorCodes.Duplicate);

                // Assemblies are loaded from memory, so even a running extension's files can be replaced.
                var target = Path.Combine(paths.Extensions, manifest.Id);
                var previous = target + ".old-" + Guid.NewGuid().ToString("N");
                if (Directory.Exists(target))
                    Directory.Move(target, previous);
                Directory.Move(unpacked, target);
                if (Directory.Exists(previous))
                    Directory.Delete(previous, recursive: true);

                return ToModel(Current().Single(e => e.Manifest.Id == manifest.Id));
            }
            finally
            {
                Directory.Delete(staging, recursive: true);
            }
        }
        finally
        {
            Changes.Release();
        }
    }

    public async Task<Extension> InstallFromUrlAsync(string url, CancellationToken cancellationToken)
    {
        RequireSensitive();
        await using var archive = await PackageArchive.DownloadAsync(clients.CreateClient(Frontends.FrontendService.HttpClientName), url, cancellationToken);
        return await InstallAsync(archive, cancellationToken);
    }

    /// <summary>
    /// Switches an extension on or off. Built-in ones and switching off apply at once; an installed extension that
    /// was not loaded at startup runs after a restart.
    /// </summary>
    public async Task<Extension> SetEnabledAsync(string id, bool enabled, CancellationToken cancellationToken)
    {
        RequireSensitive();
        var extension = Current().SingleOrDefault(e => e.Manifest.Id == id) ?? throw new ArcanistException.NotFound();
        if (settings.IsSetOutsideSettingsFile(ExtensionHost.EnabledKey(id)))
            throw new ArcanistException.Conflict(ErrorCodes.SetByEnvironment);
        if (enabled)
        {
            var claimed = extension.Manifest.Routes ?? [];
            var conflict = Current().Any(other => other.Manifest.Id != id && host.IsEnabled(other.Manifest.Id)
                                                  && (other.Manifest.Routes ?? []).Intersect(claimed, StringComparer.OrdinalIgnoreCase).Any());
            if (conflict)
                throw new ArcanistException.Conflict(ErrorCodes.RouteConflict);
        }

        await settings.SetAsync(new Dictionary<string, object?> { [ExtensionHost.EnabledKey(id)] = enabled }, cancellationToken);
        return ToModel(Current().Single(e => e.Manifest.Id == id));
    }

    /// <summary>Removes an installed extension. Built-in ones can only be switched off.</summary>
    public async Task UninstallAsync(string id, CancellationToken cancellationToken)
    {
        RequireSensitive();
        await Changes.WaitAsync(cancellationToken);
        try
        {
            var extension = Current().SingleOrDefault(e => e.Manifest.Id == id && !e.BuiltIn) ?? throw new ArcanistException.NotFound();
            await settings.SetAsync(new Dictionary<string, object?> { [ExtensionHost.EnabledKey(id)] = false }, cancellationToken);
            Directory.Delete(extension.Root!, recursive: true);
        }
        finally
        {
            Changes.Release();
        }
    }

    /// <summary>What is installed now, with what was loaded at startup.</summary>
    private List<ExtensionRegistration> Current()
    {
        var current = host.Extensions.Where(e => e.BuiltIn).ToList();
        foreach (var (manifest, root) in ExtensionHost.Installed(paths))
        {
            var loaded = host.Find(manifest.Id);
            current.Add(loaded is not null && loaded.Manifest.Version == manifest.Version && !loaded.BuiltIn
                ? loaded
                : new ExtensionRegistration(manifest, builtIn: false, root) { Status = loaded?.Status ?? ExtensionStatus.NotLoaded });
        }

        return current;
    }

    private Extension ToModel(ExtensionRegistration registration)
    {
        var manifest = registration.Manifest;
        var enabled = host.IsEnabled(manifest.Id);
        var loaded = host.Find(manifest.Id);
        // Installed extensions load at startup: a newer version on disk, or one switched on since, waits for a restart.
        var restart = !registration.BuiltIn && enabled
                      && (loaded is not { Status: ExtensionStatus.Running } || loaded.Manifest.Version != manifest.Version);
        return new Extension(manifest.Id, manifest.Name, manifest.Version, manifest.Description, manifest.Author, manifest.License,
            manifest.Homepage, registration.BuiltIn, enabled, registration.Status, registration.ErrorCode, restart,
            manifest.Routes ?? [], $"/ext/{manifest.Id}");
    }

    private void RequireSensitive()
    {
        guard.Admin();
        guard.RecentAuthentication();
    }
}
