using System.Text.Json;
using Arcanist.Core.Data;
using Arcanist.Core.Data.Entities;
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
/// A page of a running extension that the user may open from a player (in a window or a tab of its own). Part of the
/// API contract.
/// </summary>
/// <param name="ExtensionId">The extension it belongs to.</param>
/// <param name="Id">The app's id within its extension.</param>
/// <param name="Name">In the language asked for when the extension has it, otherwise in English.</param>
/// <param name="Url">The page, on this server (a path).</param>
/// <param name="IconUrl">A square SVG icon (a path); null when it has none.</param>
/// <param name="Width">The window's preferred width in CSS pixels; null to let the player choose.</param>
public sealed record ExtensionAppInfo(
    string ExtensionId,
    string Id,
    string Name,
    string? Description,
    string Url,
    string? IconUrl,
    int? Width,
    int? Height);

/// <summary>
/// Installing and switching extensions. Administrators only, with a recent authentication for every change: an
/// extension runs inside the server with full trust.
/// </summary>
public sealed class ExtensionService(
    AccessGuard guard,
    ExtensionHost host,
    ArcanistPaths paths,
    ISettingsFileWriter settings,
    IHttpClientFactory clients,
    Server.PrivacySettings privacy,
    SettingsStore settingsStore,
    IHttpContextAccessor http)
{
    private static readonly SemaphoreSlim Changes = new(1, 1);

    /// <summary>
    /// The apps of the extensions running now (switched on and loaded) that the user may open: those whose
    /// permissions they have, and those for administrators when they are one. Names in <paramref name="language"/>,
    /// or the request's, or the server's default.
    /// </summary>
    public async Task<IReadOnlyList<ExtensionAppInfo>> AppsAsync(string? language, CancellationToken cancellationToken)
    {
        var access = guard.Authenticated();
        var languages = new List<string>();
        if (!string.IsNullOrWhiteSpace(language))
            languages.Add(language.Trim());
        if (http.HttpContext?.Request.Headers.AcceptLanguage.ToString() is { Length: > 0 } accept)
            languages.AddRange(accept.Split(',').Select(part => part.Split(';')[0].Trim()).Where(tag => tag.Length > 0));
        if (await settingsStore.GetAsync<string?>(SettingKeys.DefaultLanguage, null, cancellationToken) is { Length: > 0 } fallback)
            languages.Add(fallback);

        return host.Extensions
            .Where(e => e.Status == ExtensionStatus.Running && host.IsEnabled(e.Manifest.Id))
            .SelectMany(e => (e.Manifest.Apps ?? []).Select(app => (Extension: e.Manifest.Id, App: app)))
            .Where(entry => (!entry.App.AdminOnly || access.IsAdmin)
                            && (entry.App.Permissions ?? []).All(name => ParsePermission(name) is { } permission && access.Has(permission)))
            .Select(entry => new ExtensionAppInfo(entry.Extension, entry.App.Id,
                Localized(entry.App.Names, languages) ?? entry.App.Name,
                Localized(entry.App.Descriptions, languages) ?? entry.App.Description,
                entry.App.Path, entry.App.Icon, entry.App.Width, entry.App.Height))
            .OrderBy(app => app.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>The first of <paramref name="languages"/> the texts have, also by its base language (es-PE → es).</summary>
    private static string? Localized(IReadOnlyDictionary<string, string>? texts, IEnumerable<string> languages)
    {
        if (texts is null || texts.Count == 0)
            return null;
        foreach (var tag in languages)
        {
            // The base texts are in English: asked for English, they win over the server's default language.
            if (tag.StartsWith("en", StringComparison.OrdinalIgnoreCase) && !texts.Keys.Any(k => k.StartsWith("en", StringComparison.OrdinalIgnoreCase)))
                return null;
            foreach (var candidate in new[] { tag, tag.Split('-')[0] })
            {
                var match = texts.FirstOrDefault(t => string.Equals(t.Key, candidate, StringComparison.OrdinalIgnoreCase));
                if (match.Value is { Length: > 0 })
                    return match.Value;
            }
        }

        return null;
    }

    /// <summary>A permission by its API name (<c>UPLOAD</c>, <c>PLAY_ON_SERVER</c>).</summary>
    public static Permissions? ParsePermission(string? name) =>
        Enum.GetValues<Permissions>().Where(p => p != Permissions.None)
            .Select(p => (Permission: p, Name: JsonNamingPolicy.SnakeCaseUpper.ConvertName(p.ToString())))
            .FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.Ordinal)) is { Name: not null } found
            ? found.Permission
            : null;

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
        if (enabled && id == Core.Extensions.UltimateCloud.UltimateCloudExtension.Id
                    && (await privacy.GetAsync(cancellationToken)).ExternalServices == Server.ExternalServices.None)
            throw new ArcanistException.Conflict(ErrorCodes.ExternalServicesDisabled);
        if (enabled)
        {
            var claimed = extension.Manifest.Routes ?? [];
            var conflict = Current().Any(other => other.Manifest.Id != id && host.IsEnabled(other.Manifest.Id)
                                                  && (other.Manifest.Routes ?? []).Intersect(claimed, StringComparer.OrdinalIgnoreCase).Any());
            if (conflict)
                throw new ArcanistException.Conflict(ErrorCodes.RouteConflict);
        }

        await settings.SetAsync(new Dictionary<string, object?> { [ExtensionHost.EnabledKey(id)] = enabled }, cancellationToken);
        host.OnEnabledChanged(id);
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
