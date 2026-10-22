using System.Reflection;
using System.Runtime.Loader;
using Arcanist.Core.Api;
using Arcanist.Core.Application;
using Arcanist.Core.Application.Server;
using Arcanist.Core.Hosting;
using Arcanist.Core.Packages;
using Arcanist.Extensibility;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;

namespace Arcanist.Core.Extensions;

/// <summary>An extension that ships inside the server (mStream compatibility, Subsonic).</summary>
internal interface IBuiltInExtension : IArcanistExtension
{
    ExtensionManifest Manifest { get; }

    /// <summary>Whether it runs until an administrator switches it off (most start off).</summary>
    bool EnabledByDefault => false;
}

/// <summary>What became of an extension at startup.</summary>
public enum ExtensionStatus
{
    /// <summary>Not loaded: disabled when the server started.</summary>
    NotLoaded,

    /// <summary>Loaded, with its endpoints mapped.</summary>
    Running,

    /// <summary>Loading or mapping it failed; see the server log.</summary>
    Failed,
}

/// <summary>An extension the server knows about, built in or installed.</summary>
public sealed class ExtensionRegistration(ExtensionManifest manifest, bool builtIn, string? root)
{
    public ExtensionManifest Manifest { get; } = manifest;

    public bool BuiltIn { get; } = builtIn;

    /// <summary>The installed package's folder; null for built-in extensions.</summary>
    public string? Root { get; } = root;

    public ExtensionStatus Status { get; internal set; }

    /// <summary>Why it failed, as an <see cref="ErrorCodes"/> value.</summary>
    public string? ErrorCode { get; internal set; }

    internal IArcanistExtension? Instance { get; set; }

    /// <summary>On when nothing in the configuration says otherwise (built-in ones that say so).</summary>
    internal bool EnabledByDefault { get; init; }

    internal ExtensionContext? Context { get; set; }
}

/// <summary>
/// Loads extensions at startup and keeps track of them. Built-in extensions are always loaded and switched on or off
/// at once; installed ones are loaded only when enabled at startup, each in its own <see cref="AssemblyLoadContext"/>
/// that shares the SDK, the server and the framework with the host. Enabled state lives in <c>arcanist.json</c>
/// (<c>Arcanist:Extensions:&lt;id&gt;:Enabled</c>), so it is known before the database is.
/// </summary>
public sealed class ExtensionHost
{
    public const string ManifestFile = "arcanist-extension.json";

    /// <summary>Prefixes extensions may claim: those Arcanist keeps for compatibility layers.</summary>
    public static readonly IReadOnlyList<string> ClaimablePrefixes = ["/api", "/media", "/album-art", "/transcode", "/shared", "/rest"];

    private readonly List<ExtensionRegistration> _extensions = [];
    private readonly ArcanistPaths _paths;
    private readonly IConfiguration _configuration;

    private ExtensionHost(ArcanistPaths paths, IConfiguration configuration)
    {
        _paths = paths;
        _configuration = configuration;
    }

    public IReadOnlyList<ExtensionRegistration> Extensions => _extensions;

    public ExtensionRegistration? Find(string id) => _extensions.FirstOrDefault(e => e.Manifest.Id == id);

    /// <summary>Whether the extension is enabled in the current configuration (it may need a restart to apply).</summary>
    public bool IsEnabled(string id) =>
        _managed.Contains(id) || ((_configuration.GetValue<bool?>(EnabledKey(id)) ?? Find(id)?.EnabledByDefault ?? false) && !IsBlocked(id));

    /// <summary>The 100% open source mode is on: closed source extensions neither install nor run.</summary>
    public bool OpenSourceOnly => OpenSource.IsOn(_configuration);

    /// <summary>An installed extension that is not open source (by its manifest's license); built-in ones are Arcanist's own.</summary>
    public bool IsClosed(string id) => Find(id) is { BuiltIn: false } extension && !OpenSource.IsOpenLicense(extension.Manifest.License);

    /// <summary>Closed source while the open source mode is on (and not managed by the deployment): off, whatever it says.</summary>
    public bool IsBlocked(string id) => OpenSourceOnly && !_managed.Contains(id) && IsClosed(id);

    /// <summary>Installed through the configuration (<see cref="ManagedExtensions"/>): always on, never uninstalled.</summary>
    public bool IsManaged(string id) => _managed.Contains(id);

    private HashSet<string> _managed = [];

    /// <summary>Raised when an administrator switches an extension on or off, with its id.</summary>
    public event Action<string>? EnabledChanged;

    internal void OnEnabledChanged(string id) => EnabledChanged?.Invoke(id);

    public static string EnabledKey(string id) => $"Arcanist:Extensions:{id}:Enabled";

    /// <summary>
    /// Finds the extensions, loads those that should run and lets them register their services. Runs before the
    /// service provider is built.
    /// </summary>
    internal static ExtensionHost Load(IServiceCollection services, ArcanistPaths paths, IConfiguration configuration,
        IEnumerable<IBuiltInExtension> builtIn, ILogger logger)
    {
        var host = new ExtensionHost(paths, configuration) { _managed = ManagedExtensions.Ids(configuration) };
        foreach (var extension in builtIn)
        {
            host._extensions.Add(new ExtensionRegistration(extension.Manifest, builtIn: true, root: null)
            {
                Instance = extension,
                Status = ExtensionStatus.Running,
                EnabledByDefault = extension.EnabledByDefault,
            });
        }

        foreach (var (manifest, root) in Installed(paths))
        {
            if (host.Find(manifest.Id) is not null)
                continue;
            var registration = new ExtensionRegistration(manifest, builtIn: false, root);
            host._extensions.Add(registration);
            if (!host.IsEnabled(manifest.Id))
                continue;

            try
            {
                registration.Instance = Instantiate(manifest, root);
                registration.Status = ExtensionStatus.Running;
            }
            catch (Exception e)
            {
                logger.LogError(e, "Extension {Id} could not be loaded", manifest.Id);
                registration.Status = ExtensionStatus.Failed;
                registration.ErrorCode = ErrorCodes.ExtensionFailed;
            }
        }

        foreach (var registration in host._extensions.Where(e => e.Instance is not null))
        {
            var dataDirectory = Path.Combine(paths.Root, "extension-data", registration.Manifest.Id);
            Directory.CreateDirectory(dataDirectory);
            registration.Context = new ExtensionContext(registration.Manifest.Id, dataDirectory,
                configuration.GetSection($"Arcanist:Extensions:{registration.Manifest.Id}"), registration.Root);
            try
            {
                registration.Instance!.ConfigureServices(services, registration.Context);
            }
            catch (Exception e)
            {
                logger.LogError(e, "Extension {Id} failed to register its services", registration.Manifest.Id);
                registration.Status = ExtensionStatus.Failed;
                registration.ErrorCode = ErrorCodes.ExtensionFailed;
            }
        }

        services.AddSingleton(host);
        return host;
    }

    /// <summary>Maps the endpoints of running extensions, refusing routes outside what each may use.</summary>
    internal void Map(WebApplication app, ILogger logger)
    {
        foreach (var registration in _extensions.Where(e => e.Status == ExtensionStatus.Running))
        {
            var id = registration.Manifest.Id;
            var group = app.MapGroup("");
            // Switched off (or failed) extensions answer as if they did not exist.
            group.AddEndpointFilter(async (context, next) =>
                IsEnabled(id) && registration.Status == ExtensionStatus.Running
                    ? await next(context)
                    : Problems.Create(StatusCodes.Status404NotFound, ErrorCodes.NotFound));

            try
            {
                registration.Instance!.MapEndpoints(group, registration.Context!);
                var outside = ((IEndpointRouteBuilder)group).DataSources.SelectMany(d => d.Endpoints).OfType<RouteEndpoint>()
                    .Select(e => "/" + (e.RoutePattern.RawText ?? "").TrimStart('/'))
                    .FirstOrDefault(route => !IsAllowed(registration.Manifest, route));
                if (outside is not null)
                {
                    logger.LogError("Extension {Id} maps {Route}, outside /ext/{Id} and the prefixes it claims; it is disabled", id, outside, id);
                    registration.Status = ExtensionStatus.Failed;
                    registration.ErrorCode = ErrorCodes.RouteNotAllowed;
                }
            }
            catch (Exception e)
            {
                logger.LogError(e, "Extension {Id} failed to map its endpoints", id);
                registration.Status = ExtensionStatus.Failed;
                registration.ErrorCode = ErrorCodes.ExtensionFailed;
            }
        }
    }

    /// <summary>A route is the extension's when under <c>/ext/&lt;id&gt;</c> or a prefix it claims (never the core REST API).</summary>
    internal static bool IsAllowed(ExtensionManifest manifest, string route)
    {
        if (Under(route, $"/ext/{manifest.Id}"))
            return true;
        return (manifest.Routes ?? []).Any(prefix => ClaimablePrefixes.Contains(prefix) && Under(route, prefix))
               && !Under(route, "/rest/v1");

        static bool Under(string route, string prefix) =>
            route.Equals(prefix, StringComparison.OrdinalIgnoreCase) || route.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Checks a manifest of an extension being installed.</summary>
    internal static void Validate(ExtensionManifest manifest, string root)
    {
        var errors = new ValidationErrors();
        if (!PackageArchive.IsValidId(manifest.Id))
            errors.Add("id", ErrorCodes.InvalidFormat);
        if (string.IsNullOrWhiteSpace(manifest.Name) || manifest.Name.Length > 128)
            errors.Add("name", ErrorCodes.InvalidFormat);
        if (string.IsNullOrWhiteSpace(manifest.Version) || manifest.Version.Length > 64)
            errors.Add("version", ErrorCodes.InvalidFormat);
        if (manifest.MinApiVersion > ServerInfoService.ApiVersion)
            errors.Add("minApiVersion", ErrorCodes.ApiVersionUnsupported);
        if ((manifest.Routes ?? []).Any(route => !ClaimablePrefixes.Contains(route)))
            errors.Add("routes", ErrorCodes.RouteNotAllowed);
        if (!AppsAreValid(manifest))
            errors.Add("apps", ErrorCodes.InvalidFormat);
        if (manifest.AdminPage is { Icon: { } icon } && !OwnPath(manifest, icon))
            errors.Add("adminPage", ErrorCodes.InvalidFormat);

        var assembly = string.IsNullOrWhiteSpace(manifest.Assembly) ? null : Path.GetFullPath(Path.Combine(root, manifest.Assembly));
        if (assembly is null || !assembly.StartsWith(Path.GetFullPath(root) + Path.DirectorySeparatorChar, StringComparison.Ordinal) || !File.Exists(assembly))
            errors.Add("assembly", ErrorCodes.NotFound);
        errors.ThrowIfAny();
    }

    /// <summary>
    /// An extension's apps: unique ids, pages and icons of its own (under <c>/ext/&lt;id&gt;/</c>), permissions the API
    /// knows and sensible window sizes.
    /// </summary>
    internal static bool AppsAreValid(ExtensionManifest manifest)
    {
        var apps = manifest.Apps ?? [];
        if (apps.Count > 16 || apps.Select(a => a.Id).Distinct(StringComparer.Ordinal).Count() != apps.Count)
            return false;
        return apps.All(app =>
            PackageArchive.IsValidId(app.Id)
            && !string.IsNullOrWhiteSpace(app.Name) && app.Name.Length <= 64
            && OwnPath(manifest, app.Path) && (app.Icon is null || OwnPath(manifest, app.Icon))
            && (app.Permissions ?? []).All(name => Application.Extensions.ExtensionService.ParsePermission(name) is not null)
            && app.Width is null or >= 240 and <= 4096 && app.Height is null or >= 200 and <= 4096);
    }

    /// <summary>A page or file of the extension's own: under <c>/ext/&lt;id&gt;/</c>, without climbing out of it.</summary>
    private static bool OwnPath(ExtensionManifest manifest, string? path) =>
        path is not null && (path + "/").StartsWith($"/ext/{manifest.Id}/", StringComparison.Ordinal) && !path.Contains("..") && !path.Contains('\\')
        && Uri.IsWellFormedUriString(path, UriKind.Relative);

    internal static IEnumerable<(ExtensionManifest Manifest, string Root)> Installed(ArcanistPaths paths)
    {
        if (!Directory.Exists(paths.Extensions))
            yield break;

        foreach (var folder in Directory.GetDirectories(paths.Extensions).Where(f => !Path.GetFileName(f).StartsWith('.')))
        {
            ExtensionManifest? manifest;
            try
            {
                manifest = File.Exists(Path.Combine(folder, ManifestFile)) ? PackageArchive.ReadManifest<ExtensionManifest>(folder, ManifestFile) : null;
            }
            catch (ArcanistException)
            {
                manifest = null;
            }

            if (manifest is not null && manifest.Id == Path.GetFileName(folder))
                yield return (manifest, folder);
        }
    }

    private static IArcanistExtension Instantiate(ExtensionManifest manifest, string root)
    {
        var path = Path.GetFullPath(Path.Combine(root, manifest.Assembly));
        var context = new ExtensionLoadContext(manifest.Id, path);
        var assembly = context.LoadFromFile(path);
        var type = assembly.GetExportedTypes().SingleOrDefault(t => typeof(IArcanistExtension).IsAssignableFrom(t) && t is { IsAbstract: false, IsInterface: false })
                   ?? throw new InvalidOperationException($"{manifest.Assembly} has no public IArcanistExtension implementation.");
        return (IArcanistExtension)Activator.CreateInstance(type)!;
    }

    /// <summary>
    /// Loads an extension's own assemblies from its folder. The SDK, the server and everything the platform provides
    /// come from the host, so types are shared and the extension cannot swap them.
    /// </summary>
    private sealed class ExtensionLoadContext(string id, string mainAssembly) : AssemblyLoadContext($"extension:{id}")
    {
        private static readonly HashSet<string> Platform = ((AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string) ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(Path.GetFileNameWithoutExtension)
            .OfType<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        private readonly AssemblyDependencyResolver _resolver = new(mainAssembly);

        protected override Assembly? Load(AssemblyName name)
        {
            if (name.Name is null || Platform.Contains(name.Name) || Default.Assemblies.Any(a => a.GetName().Name == name.Name))
                return null;
            return _resolver.ResolveAssemblyToPath(name) is { } path ? LoadFromFile(path) : null;
        }

        /// <summary>Loads from memory, so the files stay free to be replaced or deleted while the server runs.</summary>
        public Assembly LoadFromFile(string path)
        {
            using var stream = new MemoryStream(File.ReadAllBytes(path));
            return LoadFromStream(stream);
        }

        protected override IntPtr LoadUnmanagedDll(string unmanagedDllName) =>
            _resolver.ResolveUnmanagedDllToPath(unmanagedDllName) is { } path ? LoadUnmanagedDllFromPath(path) : IntPtr.Zero;
    }
}

public static class ExtensionHostingExtensions
{
    /// <summary>Loads extensions while the server is being configured.</summary>
    public static WebApplicationBuilder AddExtensions(this WebApplicationBuilder builder, ArcanistPaths paths)
    {
        using var loggerFactory = LoggerFactory.Create(logging => logging.AddConsole());
        ManagedExtensions.Ensure(paths, builder.Configuration, BuiltInExtensions.All.Select(e => e.Manifest.Id),
            loggerFactory.CreateLogger(typeof(ManagedExtensions)));
        ExtensionHost.Load(builder.Services, paths, builder.Configuration, BuiltInExtensions.All, loggerFactory.CreateLogger<ExtensionHost>());
        return builder;
    }

    public static WebApplication MapExtensions(this WebApplication app)
    {
        app.Services.GetRequiredService<ExtensionHost>().Map(app, app.Services.GetRequiredService<ILogger<ExtensionHost>>());
        return app;
    }
}
