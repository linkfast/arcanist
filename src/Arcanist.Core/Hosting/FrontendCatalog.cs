using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Arcanist.Core.Application;
using Arcanist.Core.Application.Server;
using Arcanist.Core.Data;
using Arcanist.Core.Packages;
using Arcanist.Core.Setup;
using Microsoft.Extensions.Options;

namespace Arcanist.Core.Hosting;

/// <summary>
/// What a frontend package declares in <c>arcanist-frontend.json</c> at its root. Part of the frontend packaging
/// contract (see the developer guide).
/// </summary>
/// <param name="Id">Lowercase letters, digits and dashes; it is the frontend's path under <c>/players/</c>.</param>
/// <param name="MinApiVersion">Oldest Arcanist API major version it works with.</param>
/// <param name="Entry">The page loaded for every client-side route; <c>index.html</c> by default.</param>
public sealed record FrontendManifest(
    string Id,
    string Name,
    string Version,
    string? Description = null,
    string? Author = null,
    string? License = null,
    string? Homepage = null,
    int MinApiVersion = 1,
    string Entry = "index.html",
    IReadOnlyList<string>? Features = null)
{
    /// <summary>Opens public playlist links (<c>#/s/&lt;token&gt;</c>) and Jukebox Jams (<c>#/jam/&lt;code&gt;</c>).</summary>
    public const string SharedLinks = "shared-links";

    /// <summary>Can be the player a Jukebox Jam plays on (implements the host API).</summary>
    public const string JukeboxJamHost = "jukebox-jam-host";

    public bool Has(string feature) => Features?.Contains(feature, StringComparer.OrdinalIgnoreCase) == true;
}

/// <summary>An installed frontend: its manifest and where its files are.</summary>
public sealed record InstalledFrontend(FrontendManifest Manifest, string Root, bool BuiltIn, DateTime InstalledAt);

/// <summary>
/// The web players this server can serve: the one bundled with the release (<c>wwwroot/player</c>) and those
/// installed from the admin panel into <c>&lt;data&gt;/frontends/&lt;id&gt;</c>. One of them is the default, served at
/// <c>/</c>; every one is also served at <c>/players/&lt;id&gt;/</c>. Frontends run on the same origin as the admin panel,
/// so installing one means trusting it completely.
/// </summary>
public sealed partial class FrontendCatalog(
    ArcanistPaths paths,
    IOptions<FrontendOptions> options,
    IWebHostEnvironment environment,
    SettingsStore settings,
    IArcanistDbContextFactory databases,
    IOptionsMonitor<SetupOptions> setup,
    IConfiguration configuration)
{
    /// <summary>Served: always, unless it is closed source and the 100% open source mode is on.</summary>
    public bool IsUsable(InstalledFrontend frontend) => !OpenSource.IsOn(configuration) || OpenSource.IsOpenLicense(frontend.Manifest.License);

    /// <summary>A player that may be served now (<see cref="IsUsable"/>).</summary>
    public InstalledFrontend? FindUsable(string id) => Find(id) is { } frontend && IsUsable(frontend) ? frontend : null;

    public const string ManifestFile = "arcanist-frontend.json";

    private readonly SemaphoreSlim _changes = new(1, 1);
    private volatile string? _defaultId;
    private bool _defaultLoaded;

    private string Staging => Path.Combine(paths.Frontends, ".staging");

    /// <summary>The bundled player, when the release has one.</summary>
    public InstalledFrontend? BuiltIn
    {
        get
        {
            var root = Path.GetFullPath(options.Value.PlayerRoot ?? Path.Combine("wwwroot", "player"), environment.ContentRootPath);
            if (!File.Exists(Path.Combine(root, "index.html")))
                return null;

            var manifest = File.Exists(Path.Combine(root, ManifestFile))
                ? TryRead(root) ?? new FrontendManifest("player", "Web player", "")
                : new FrontendManifest("player", "Web player", "");
            return new InstalledFrontend(manifest, root, BuiltIn: true, File.GetLastWriteTimeUtc(Path.Combine(root, "index.html")));
        }
    }

    /// <summary>The other players that come with the server (<see cref="FrontendOptions.BundledRoot"/>).</summary>
    public IReadOnlyList<InstalledFrontend> Bundled
    {
        get
        {
            var root = Path.GetFullPath(options.Value.BundledRoot ?? Path.Combine("wwwroot", "players"), environment.ContentRootPath);
            if (!Directory.Exists(root))
                return [];
            return Directory.GetDirectories(root)
                .Select(folder => (Folder: folder, Manifest: TryRead(folder)))
                .Where(f => f.Manifest is { } m && m.Id == Path.GetFileName(f.Folder) && File.Exists(Path.Combine(f.Folder, m.Entry)))
                .OrderBy(f => f.Manifest!.Id, StringComparer.Ordinal)
                .Select(f => new InstalledFrontend(f.Manifest!, f.Folder, BuiltIn: true, File.GetLastWriteTimeUtc(Path.Combine(f.Folder, f.Manifest!.Entry))))
                .ToList();
        }
    }

    public IReadOnlyList<InstalledFrontend> List()
    {
        var installed = new List<InstalledFrontend>();
        if (BuiltIn is { } builtIn)
            installed.Add(builtIn);
        installed.AddRange(Bundled.Where(b => installed.All(i => i.Manifest.Id != b.Manifest.Id)));

        if (Directory.Exists(paths.Frontends))
        {
            foreach (var folder in Directory.GetDirectories(paths.Frontends).Where(f => !Path.GetFileName(f).StartsWith('.')))
            {
                if (TryRead(folder) is { } manifest && manifest.Id == Path.GetFileName(folder) && installed.All(i => i.Manifest.Id != manifest.Id))
                    installed.Add(new InstalledFrontend(manifest, folder, BuiltIn: false, Directory.GetLastWriteTimeUtc(folder)));
            }
        }

        return installed;
    }

    public InstalledFrontend? Find(string id) => List().FirstOrDefault(f => f.Manifest.Id == id);

    /// <summary>The frontend served at <c>/</c>: the chosen one if still installed, else the bundled one (the main one first).</summary>
    public async Task<InstalledFrontend?> DefaultAsync(CancellationToken cancellationToken)
    {
        if (!_defaultLoaded && setup.CurrentValue.Completed && databases.IsConfigured)
        {
            _defaultId = await settings.GetAsync<string?>(SettingKeys.DefaultFrontend, null, cancellationToken);
            _defaultLoaded = true;
        }

        // A closed source player while the open source mode is on is skipped: the next one that may run.
        return (_defaultId is { } id ? FindUsable(id) : null)
               ?? (BuiltIn is { } builtIn && IsUsable(builtIn) ? builtIn : null)
               ?? Bundled.FirstOrDefault(IsUsable);
    }

    /// <summary>Back to the bundled player at <c>/</c>.</summary>
    public async Task ResetDefaultAsync(CancellationToken cancellationToken)
    {
        await settings.SetAsync<string?>(SettingKeys.DefaultFrontend, null, cancellationToken);
        _defaultId = null;
        _defaultLoaded = true;
    }

    public async Task SetDefaultAsync(string id, CancellationToken cancellationToken)
    {
        if (Find(id) is not { } frontend)
            throw new ArcanistException.NotFound();
        if (!IsUsable(frontend))
            throw new ArcanistException.Conflict(ErrorCodes.ClosedSourceDisabled);
        await settings.SetAsync<string?>(SettingKeys.DefaultFrontend, id, cancellationToken);
        _defaultId = id;
        _defaultLoaded = true;
    }

    /// <summary>Installs (or updates) the frontend in a .zip. It goes live at once, replacing the previous version.</summary>
    public async Task<InstalledFrontend> InstallAsync(Stream archive, CancellationToken cancellationToken)
    {
        await _changes.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(Staging);
            var unpacked = await PackageArchive.ExtractAsync(archive, Staging, ManifestFile, cancellationToken);
            try
            {
                var manifest = Validate(PackageArchive.ReadManifest<FrontendManifest>(unpacked, ManifestFile), unpacked);
                if (BuiltIn?.Manifest.Id == manifest.Id || Bundled.Any(b => b.Manifest.Id == manifest.Id))
                    throw ArcanistException.Field("id", ErrorCodes.Duplicate);
                if (OpenSource.IsOn(configuration) && !OpenSource.IsOpenLicense(manifest.License))
                    throw new ArcanistException.Conflict(ErrorCodes.ClosedSourceDisabled);

                // Swapped in by renames, so requests never see half a frontend.
                var target = Path.Combine(paths.Frontends, manifest.Id);
                var previous = target + ".old-" + Guid.NewGuid().ToString("N");
                if (Directory.Exists(target))
                    Directory.Move(target, previous);
                Directory.Move(unpacked, target);
                if (Directory.Exists(previous))
                    Directory.Delete(previous, recursive: true);

                IndexPages.Clear();
                return Find(manifest.Id)!;
            }
            finally
            {
                if (Directory.Exists(Staging))
                    Directory.Delete(Staging, recursive: true);
            }
        }
        finally
        {
            _changes.Release();
        }
    }

    public async Task UninstallAsync(string id, CancellationToken cancellationToken)
    {
        await _changes.WaitAsync(cancellationToken);
        try
        {
            var frontend = Find(id);
            if (frontend is null || frontend.BuiltIn)
                throw new ArcanistException.NotFound();

            // Loads which one is the default before it disappears from the list.
            await DefaultAsync(cancellationToken);
            Directory.Delete(frontend.Root, recursive: true);
            IndexPages.Clear();
            if (_defaultId == id)
            {
                await settings.SetAsync<string?>(SettingKeys.DefaultFrontend, null, cancellationToken);
                _defaultId = null;
            }
        }
        finally
        {
            _changes.Release();
        }
    }

    private static FrontendManifest Validate(FrontendManifest manifest, string root)
    {
        var errors = new ValidationErrors();
        if (!PackageArchive.IsValidId(manifest.Id) || manifest.Id is "admin" or "player")
            errors.Add("id", ErrorCodes.InvalidFormat);
        if (string.IsNullOrWhiteSpace(manifest.Name) || manifest.Name.Length > 128)
            errors.Add("name", ErrorCodes.InvalidFormat);
        if (string.IsNullOrWhiteSpace(manifest.Version) || manifest.Version.Length > 64)
            errors.Add("version", ErrorCodes.InvalidFormat);
        if (manifest.MinApiVersion > ServerInfoService.ApiVersion)
            errors.Add("minApiVersion", ErrorCodes.ApiVersionUnsupported);
        var entry = Path.GetFullPath(Path.Combine(root, manifest.Entry ?? "index.html"));
        if (!entry.StartsWith(Path.GetFullPath(root) + Path.DirectorySeparatorChar, StringComparison.Ordinal) || !File.Exists(entry))
            errors.Add("entry", ErrorCodes.NotFound);
        errors.ThrowIfAny();
        return manifest with { Entry = manifest.Entry ?? "index.html" };
    }

    private static FrontendManifest? TryRead(string folder)
    {
        try
        {
            return File.Exists(Path.Combine(folder, ManifestFile)) ? PackageArchive.ReadManifest<FrontendManifest>(folder, ManifestFile) : null;
        }
        catch (ArcanistException)
        {
            return null;
        }
    }

    /// <summary>Entry pages with their base set to the mount point, cached per file version.</summary>
    private static readonly ConcurrentDictionary<(string File, string Mount, DateTime Modified), string> IndexPages = new();

    /// <summary>
    /// The frontend's entry page with <c>&lt;base href&gt;</c> pointing at where it is served, so the same build works at
    /// <c>/</c> and at <c>/players/&lt;id&gt;/</c> as long as it uses relative URLs.
    /// </summary>
    public static string EntryPage(InstalledFrontend frontend, string mount)
    {
        var file = Path.Combine(frontend.Root, frontend.Manifest.Entry);
        return IndexPages.GetOrAdd((file, mount, File.GetLastWriteTimeUtc(file)), key =>
        {
            var html = File.ReadAllText(key.File);
            var tag = $"""<base href="{System.Net.WebUtility.HtmlEncode(key.Mount)}">""";
            if (BaseTag().IsMatch(html))
                return BaseTag().Replace(html, tag, 1);
            var head = HeadTag().Match(html);
            return head.Success ? html.Insert(head.Index + head.Length, tag) : tag + html;
        });
    }

    [GeneratedRegex(@"<base\s[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex BaseTag();

    [GeneratedRegex(@"<head(\s[^>]*)?>", RegexOptions.IgnoreCase)]
    private static partial Regex HeadTag();
}
