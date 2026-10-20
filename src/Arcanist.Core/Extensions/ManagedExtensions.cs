using System.Security.Cryptography;
using Arcanist.Core.Application;
using Arcanist.Core.Hosting;
using Arcanist.Core.Packages;
using Arcanist.Extensibility;

namespace Arcanist.Core.Extensions;

/// <summary>
/// An extension the server's operator installs through its configuration (<c>Arcanist:ManagedExtensions</c>, usually
/// environment variables such as <c>Arcanist__ManagedExtensions__0__Package</c>): for private extensions of a managed
/// deployment. It is always on and cannot be switched off or uninstalled from the panel or the API; installing a newer
/// version of it there (an update) is allowed.
/// </summary>
/// <param name="Id">The extension's id, as in its manifest.</param>
/// <param name="Package">Its .zip: an http(s) URL or a path on this machine (a file baked into an image, say).</param>
/// <param name="Sha256">The SHA-256 of the .zip (64 hex digits): nothing else is installed.</param>
/// <param name="Version">
/// The oldest version to run: an installed one older than this is replaced from <paramref name="Package"/> at
/// startup. Without it, the package is installed only when the extension is missing.
/// </param>
public sealed record ManagedExtension(string? Id, string? Package, string? Sha256, string? Version);

internal static class ManagedExtensions
{
    public const string Section = "Arcanist:ManagedExtensions";

    private static readonly TimeSpan DownloadTimeout = TimeSpan.FromMinutes(5);

    public static IReadOnlyList<ManagedExtension> Read(IConfiguration configuration) =>
        configuration.GetSection(Section).Get<List<ManagedExtension>>() ?? [];

    /// <summary>The ids of the managed extensions (well formed or not, so a broken one is still never switched off).</summary>
    public static HashSet<string> Ids(IConfiguration configuration) =>
        Read(configuration).Select(m => m.Id).OfType<string>().ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// Installs the managed extensions that are missing or older than their <c>Version</c>, before extensions load.
    /// A failure is logged and the server starts all the same (with the extension it had, if any).
    /// </summary>
    public static void Ensure(ArcanistPaths paths, IConfiguration configuration, IEnumerable<string> builtInIds, ILogger logger)
    {
        var builtIn = builtInIds.ToHashSet(StringComparer.Ordinal);
        foreach (var managed in Read(configuration))
        {
            try
            {
                EnsureAsync(paths, managed, builtIn, logger).GetAwaiter().GetResult();
            }
            catch (Exception e)
            {
                logger.LogError(e, "Managed extension {Id} could not be installed from {Package}", managed.Id, managed.Package);
            }
        }
    }

    private static async Task EnsureAsync(ArcanistPaths paths, ManagedExtension managed, HashSet<string> builtIn, ILogger logger)
    {
        if (!PackageArchive.IsValidId(managed.Id) || builtIn.Contains(managed.Id!))
            throw new InvalidOperationException("Id must be the id of an extension that is not built in.");
        if (string.IsNullOrWhiteSpace(managed.Package))
            throw new InvalidOperationException("Package is missing.");
        if (managed.Sha256 is not { Length: 64 } sha || !sha.All(Uri.IsHexDigit))
            throw new InvalidOperationException("Sha256 must be 64 hexadecimal digits.");
        if (managed.Version is { } minimum && !ArcanistVersion.TryNormalize(minimum, out _))
            throw new InvalidOperationException("Version is not a semantic version.");

        var installed = ExtensionHost.Installed(paths).Where(i => i.Manifest.Id == managed.Id).Select(i => i.Manifest).FirstOrDefault();
        if (installed is not null && (managed.Version is null || ArcanistVersion.Compare(installed.Version, managed.Version) >= 0))
            return;

        using var timeout = new CancellationTokenSource(DownloadTimeout);
        await using var archive = await OpenAsync(managed.Package, timeout.Token);
        var hash = Convert.ToHexStringLower(await SHA256.HashDataAsync(archive, timeout.Token));
        if (!hash.Equals(managed.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"The package's SHA-256 is {hash}, not the configured one.");
        archive.Position = 0;

        var manifest = await Place(paths, archive, managed.Id!, timeout.Token);
        logger.LogInformation("Managed extension {Id} {Version} installed from {Package}", manifest.Id, manifest.Version, managed.Package);
    }

    private static async Task<Stream> OpenAsync(string package, CancellationToken cancellationToken)
    {
        if (Uri.TryCreate(package, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
        {
            using var http = new HttpClient();
            return await PackageArchive.DownloadAsync(http, package, cancellationToken);
        }

        var path = uri is { IsFile: true } ? uri.LocalPath : package;
        var buffer = new MemoryStream(await File.ReadAllBytesAsync(path, cancellationToken));
        return buffer;
    }

    /// <summary>Unpacks a package over the extension's folder (as installing from the panel does).</summary>
    private static async Task<ExtensionManifest> Place(ArcanistPaths paths, Stream archive, string id, CancellationToken cancellationToken)
    {
        var staging = Path.Combine(paths.Extensions, ".staging-managed");
        Directory.CreateDirectory(staging);
        try
        {
            var unpacked = await PackageArchive.ExtractAsync(archive, staging, ExtensionHost.ManifestFile, cancellationToken);
            var manifest = PackageArchive.ReadManifest<ExtensionManifest>(unpacked, ExtensionHost.ManifestFile);
            ExtensionHost.Validate(manifest, unpacked);
            if (manifest.Id != id)
                throw new InvalidOperationException($"The package is the extension {manifest.Id}, not {id}.");

            var target = Path.Combine(paths.Extensions, id);
            var previous = target + ".old-" + Guid.NewGuid().ToString("N");
            if (Directory.Exists(target))
                Directory.Move(target, previous);
            Directory.Move(unpacked, target);
            if (Directory.Exists(previous))
                Directory.Delete(previous, recursive: true);
            return manifest;
        }
        catch (ArcanistException e)
        {
            throw new InvalidOperationException($"The package is not a valid extension ({e.Code}).", e);
        }
        finally
        {
            Directory.Delete(staging, recursive: true);
        }
    }
}
