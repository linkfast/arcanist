using System.IO.Compression;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Arcanist.Core.Hosting;
using Arcanist.Core.Packages;
using Arcanist.Extensibility;
using Microsoft.Extensions.Options;

namespace Arcanist.Core.Application.Extensions;

/// <summary>Bound from <c>Arcanist:Marketplace</c>.</summary>
public sealed class MarketplaceOptions
{
    public const string Section = "Arcanist:Marketplace";

    /// <summary>The catalog of extensions and web players: an index.json in the format of github.com/linkfast/arcanist-extensions.</summary>
    public string Url { get; set; } = "https://raw.githubusercontent.com/linkfast/arcanist-extensions/main/index.json";

    /// <summary>How long a catalog read is reused before reading it again.</summary>
    public TimeSpan CacheFor { get; set; } = TimeSpan.FromHours(1);
}

/// <summary>How an extension of the catalog is offered. Part of the API contract.</summary>
public enum ExtensionPricing
{
    /// <summary>Free, with its source published under an open source license.</summary>
    OpenSource,

    /// <summary>Free of charge, without its source.</summary>
    Free,

    /// <summary>Paid: bought from its maintainer (in the extension or on their site).</summary>
    Paid,

    /// <summary>Free, with paid features bought from its maintainer.</summary>
    Freemium,
}

/// <summary>An extension of the catalog, in its newest version this server can run. Part of the API contract.</summary>
/// <param name="IconUrl">Its icon, served by this server (a path), or null.</param>
/// <param name="Official">Made by the Arcanist project (LinkFast); the others are from the community, reviewed when added.</param>
/// <param name="Version">The version that would be installed: the newest this server can run, or else the newest.</param>
/// <param name="Size">Of its package, in bytes, when the catalog says.</param>
/// <param name="Compatible">Whether this server can run <see cref="Version"/>; when not, it needs Arcanist <see cref="MinServerVersion"/>.</param>
/// <param name="InstalledVersion">The version installed here, if any.</param>
/// <param name="UpdateAvailable">Installed, and <see cref="Version"/> is newer.</param>
/// <param name="BuiltIn">Already part of Arcanist: nothing to install.</param>
/// <param name="Pricing">Open source, free, paid or freemium; null when the catalog does not say. Purchases are handled by
/// each extension's maintainer, in the extension or where <see cref="PricingUrl"/> says.</param>
/// <param name="PricingUrl">Where its maintainer shows its prices or sells it.</param>
public sealed record MarketplaceExtension(
    string Id,
    string Name,
    string? Description,
    string? Author,
    string? Homepage,
    string? License,
    string? IconUrl,
    bool Official,
    IReadOnlyList<string> Categories,
    string Version,
    long? Size,
    string? MinServerVersion,
    bool Compatible,
    string? InstalledVersion,
    bool UpdateAvailable,
    bool BuiltIn,
    ExtensionPricing? Pricing = null,
    string? PricingUrl = null);

/// <summary>The extension catalog. Part of the API contract.</summary>
/// <param name="Available">Whether the catalog could be read; when not, <see cref="ErrorCode"/> says why.</param>
/// <param name="Source">Where it is read from.</param>
public sealed record ExtensionMarketplaceInfo(
    bool Available,
    string? ErrorCode,
    DateTime? FetchedAt,
    string Source,
    IReadOnlyList<MarketplaceExtension> Extensions);

/// <summary>A web player of the catalog, in its newest version this server can run. Part of the API contract.</summary>
/// <param name="IconUrl">Its icon, served by this server (a path), or null.</param>
/// <param name="Official">Made by LinkFast (Ultimate Web Player among them); the others are from the community, reviewed when added.</param>
/// <param name="Version">The version that would be installed: the newest this server can run, or else the newest.</param>
/// <param name="InstalledVersion">The version installed here, if any.</param>
/// <param name="BuiltIn">Comes with this server (it is updated with it): nothing to install.</param>
/// <param name="IsDefault">Installed and served at <c>/</c>.</param>
/// <param name="Pricing">Open source, free, paid or freemium; null when the catalog does not say.</param>
public sealed record MarketplacePlayer(
    string Id,
    string Name,
    string? Description,
    string? Author,
    string? Homepage,
    string? License,
    string? IconUrl,
    bool Official,
    IReadOnlyList<string> Categories,
    string Version,
    long? Size,
    string? MinServerVersion,
    bool Compatible,
    string? InstalledVersion,
    bool UpdateAvailable,
    bool BuiltIn,
    bool IsDefault,
    ExtensionPricing? Pricing = null,
    string? PricingUrl = null);

/// <summary>The web players of the catalog. Part of the API contract.</summary>
/// <param name="Available">Whether the catalog could be read; when not, <see cref="ErrorCode"/> says why.</param>
public sealed record PlayerMarketplaceInfo(
    bool Available,
    string? ErrorCode,
    DateTime? FetchedAt,
    string Source,
    IReadOnlyList<MarketplacePlayer> Players);

/// <param name="MakeDefault">Serves it at <c>/</c> as well.</param>
public sealed record InstallMarketplacePlayerInput(string Id, bool MakeDefault = false);

/// <param name="Enable">Switches it on as well (it runs after a restart). True by default: installing from the catalog is one click.</param>
public sealed record InstallMarketplaceExtensionInput(string Id, bool Enable = true);

/// <summary>The catalog as read, kept for <see cref="MarketplaceOptions.CacheFor"/>.</summary>
public sealed class MarketplaceCatalog(IHttpClientFactory clients, IOptionsMonitor<MarketplaceOptions> options, TimeProvider time, ILogger<MarketplaceCatalog> logger)
{
    private readonly SemaphoreSlim _reading = new(1, 1);
    private (CatalogFile File, DateTime At, string Url)? _cached;

    public string Url => options.CurrentValue.Url;

    /// <summary>The catalog, read again when older than <paramref name="maxAge"/> (or the configured time).</summary>
    public async Task<(CatalogFile File, DateTime At)> GetAsync(TimeSpan? maxAge, CancellationToken cancellationToken)
    {
        await _reading.WaitAsync(cancellationToken);
        try
        {
            var now = time.GetUtcNow().UtcDateTime;
            if (_cached is { } cached && cached.Url == Url && now - cached.At < (maxAge ?? options.CurrentValue.CacheFor))
                return (cached.File, cached.At);

            CatalogFile? file;
            try
            {
                file = await clients.CreateClient(Frontends.FrontendService.HttpClientName)
                    .GetFromJsonAsync<CatalogFile>(Url, JsonSerializerOptions.Web, cancellationToken);
            }
            catch (Exception e) when (e is HttpRequestException or JsonException or NotSupportedException
                                          or TaskCanceledException && !cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning("The extension catalog at {Url} could not be read: {Error}", Url, e.Message);
                throw new ArcanistException.Unavailable(ErrorCodes.MarketplaceUnavailable);
            }

            if (file is not { Format: 1 })
                throw new ArcanistException.Unavailable(ErrorCodes.MarketplaceUnavailable);
            // Entries that do not hold together are left out, not the whole catalog.
            file = file with
            {
                Extensions = (file.Extensions ?? []).Where(e => e.IsValid).ToList(),
                Players = (file.Players ?? []).Where(e => e.IsValid).ToList(),
            };
            _cached = (file, now, Url);
            _icons.Clear();
            return (file, now);
        }
        finally
        {
            _reading.Release();
        }
    }

    private const int MaxIconBytes = 256 * 1024;

    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, (byte[] Bytes, string ContentType)?> _icons = new();

    /// <summary>
    /// The icon of an extension (or of a web player, with <paramref name="player"/>), read from where the catalog
    /// says (a URL, or a path next to the index) and kept until the catalog is read again: players never reach the
    /// catalog's host, and its SVGs get the right type.
    /// </summary>
    public async Task<(byte[] Bytes, string ContentType)?> IconAsync(string id, CancellationToken cancellationToken, bool player = false)
    {
        var cacheKey = (player ? "player:" : "") + id;
        if (_icons.TryGetValue(cacheKey, out var cached))
            return cached;
        if ((player ? _cached?.File.Players : _cached?.File.Extensions)?.SingleOrDefault(e => e.Id == id)?.Icon is not { } icon
            || !Uri.TryCreate(new Uri(Url), icon, out var uri) || uri.Scheme is not ("https" or "http"))
            return null;

        (byte[], string)? found = null;
        try
        {
            using var response = await clients.CreateClient(Frontends.FrontendService.HttpClientName).GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            var type = Path.GetExtension(uri.AbsolutePath).ToLowerInvariant() switch
            {
                ".svg" => "image/svg+xml",
                ".png" => "image/png",
                ".webp" => "image/webp",
                _ => null,
            };
            if (response.IsSuccessStatusCode && type is not null && response.Content.Headers.ContentLength is null or <= MaxIconBytes)
            {
                var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
                if (bytes.Length <= MaxIconBytes)
                    found = (bytes, type);
            }
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            logger.LogDebug("The icon of {Id} could not be read: {Error}", id, e.Message);
        }

        _icons[cacheKey] = found;
        return found;
    }
}

/// <summary>The catalog's file. Format 1 is documented in github.com/linkfast/arcanist-extensions.</summary>
/// <param name="Players">Web players, described like extensions (their packages hold arcanist-frontend.json).</param>
public sealed record CatalogFile(int Format, List<CatalogExtension>? Extensions, List<CatalogExtension>? Players = null);

public sealed record CatalogExtension(
    string Id,
    string Name,
    Dictionary<string, string>? Names,
    string? Description,
    Dictionary<string, string>? Descriptions,
    string? Author,
    string? Homepage,
    string? License,
    string? Icon,
    bool Official,
    List<string>? Categories,
    List<CatalogRelease>? Releases,
    string? Pricing = null,
    string? PricingUrl = null)
{
    /// <summary><c>open-source</c>, <c>free</c>, <c>paid</c> or <c>freemium</c> in the file.</summary>
    internal ExtensionPricing? PricingKind => Pricing?.ToLowerInvariant() switch
    {
        "open-source" => ExtensionPricing.OpenSource,
        "free" => ExtensionPricing.Free,
        "paid" => ExtensionPricing.Paid,
        "freemium" => ExtensionPricing.Freemium,
        _ => null,
    };

    internal string? SafePricingUrl =>
        Uri.TryCreate(PricingUrl, UriKind.Absolute, out var uri) && uri.Scheme == "https" ? PricingUrl : null;

    internal bool IsValid =>
        PackageArchive.IsValidId(Id) && !string.IsNullOrWhiteSpace(Name)
        && Releases is { Count: > 0 } && Releases.All(r => r.IsValid);
}

public sealed record CatalogRelease(string Version, string Url, string Sha256, long? Size, string? MinServerVersion)
{
    internal bool IsValid =>
        ArcanistVersion.TryNormalize(Version, out _)
        && Uri.TryCreate(Url, UriKind.Absolute, out var uri) && uri.Scheme == "https"
        && Sha256 is { Length: 64 } && Sha256.All(Uri.IsHexDigit)
        && (MinServerVersion is null || ArcanistVersion.TryNormalize(MinServerVersion, out _));

    internal bool RunsOn(string server) => MinServerVersion is null || ArcanistVersion.Compare(server, MinServerVersion) >= 0;
}

/// <summary>
/// Browsing the extension catalog and installing from it with one click: the package is checked against the
/// SHA-256 the catalog publishes before it is installed (as any other, with full trust). Administrators only.
/// </summary>
public sealed class ExtensionMarketplaceService(
    AccessGuard guard,
    MarketplaceCatalog catalog,
    ExtensionService extensions,
    Server.PrivacySettings privacy,
    IHttpClientFactory clients)
{
    public async Task<ExtensionMarketplaceInfo> GetAsync(string? language, bool refresh, CancellationToken cancellationToken)
    {
        guard.Admin();
        if ((await privacy.GetAsync(cancellationToken)).ExternalServices == Server.ExternalServices.None)
            return new ExtensionMarketplaceInfo(false, ErrorCodes.ExternalServicesDisabled, null, catalog.Url, []);

        CatalogFile file;
        DateTime at;
        try
        {
            (file, at) = await catalog.GetAsync(refresh ? TimeSpan.Zero : null, cancellationToken);
        }
        catch (ArcanistException e)
        {
            return new ExtensionMarketplaceInfo(false, e.Code, null, catalog.Url, []);
        }

        var languages = await extensions.LanguagesAsync(language, cancellationToken);
        var installed = extensions.List().ToDictionary(e => e.Id);
        var entries = (file.Extensions ?? []).Select(entry =>
            {
                var release = Pick(entry);
                var current = installed.GetValueOrDefault(entry.Id);
                return new MarketplaceExtension(entry.Id,
                    ExtensionService.Localized(entry.Names, languages) ?? entry.Name,
                    ExtensionService.Localized(entry.Descriptions, languages) ?? entry.Description,
                    entry.Author, entry.Homepage, entry.License,
                    entry.Icon is null ? null : $"/rest/v1/extensions/marketplace/icons/{entry.Id}", entry.Official, entry.Categories ?? [],
                    release.Version, release.Size, release.MinServerVersion, release.RunsOn(ArcanistVersion.Current),
                    current is { BuiltIn: false } ? current.Version : null,
                    current is { BuiltIn: false } && ArcanistVersion.Compare(release.Version, current.Version) > 0,
                    current is { BuiltIn: true }, entry.PricingKind, entry.SafePricingUrl);
            })
            .OrderByDescending(e => e.Official).ThenBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        return new ExtensionMarketplaceInfo(true, null, at, catalog.Url, entries);
    }

    /// <summary>Installs (or updates) an extension of the catalog and, unless asked not to, switches it on.</summary>
    public async Task<Extension> InstallAsync(InstallMarketplaceExtensionInput input, CancellationToken cancellationToken)
    {
        guard.Admin();
        guard.RecentAuthentication();
        if ((await privacy.GetAsync(cancellationToken)).ExternalServices == Server.ExternalServices.None)
            throw new ArcanistException.Conflict(ErrorCodes.ExternalServicesDisabled);

        // Read again if older than a few minutes, so a just-published version is what gets installed.
        var (file, _) = await catalog.GetAsync(TimeSpan.FromMinutes(5), cancellationToken);
        var entry = (file.Extensions ?? []).SingleOrDefault(e => e.Id == input.Id) ?? throw new ArcanistException.NotFound();
        if (extensions.List().Any(e => e.Id == entry.Id && e.BuiltIn))
            throw ArcanistException.Field("id", ErrorCodes.Duplicate);
        var release = Pick(entry);
        if (!release.RunsOn(ArcanistVersion.Current))
            throw new ArcanistException.Conflict(ErrorCodes.MarketplaceIncompatible);

        await using var archive = await PackageArchive.DownloadAsync(clients.CreateClient(Frontends.FrontendService.HttpClientName), release.Url, cancellationToken);
        var hash = Convert.ToHexStringLower(await SHA256.HashDataAsync(archive, cancellationToken));
        if (!string.Equals(hash, release.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new ArcanistException.Conflict(ErrorCodes.PackageChecksumMismatch);
        archive.Position = 0;
        if (ManifestId(archive) != entry.Id)
            throw new ArcanistException.Conflict(ErrorCodes.PackageMismatch);
        archive.Position = 0;

        var extension = await extensions.InstallAsync(archive, cancellationToken);
        if (!input.Enable || extension.Enabled)
            return extension;
        try
        {
            return await extensions.SetEnabledAsync(extension.Id, true, cancellationToken);
        }
        catch (ArcanistException)
        {
            // Installed, but it cannot be switched on (another extension claims its routes): the list says why.
            return extension;
        }
    }

    /// <summary>The newest release this server can run, or else the newest (shown as incompatible).</summary>
    internal static CatalogRelease Pick(CatalogExtension entry)
    {
        var releases = entry.Releases!.OrderByDescending(r => r.Version, Comparer<string>.Create(ArcanistVersion.Compare)).ToList();
        return releases.FirstOrDefault(r => r.RunsOn(ArcanistVersion.Current)) ?? releases[0];
    }

    private static string? ManifestId(Stream archive) => ManifestId(archive, Core.Extensions.ExtensionHost.ManifestFile);

    /// <summary>The id in a package's manifest (<paramref name="file"/> at its root); null without one.</summary>
    internal static string? ManifestId(Stream archive, string file)
    {
        try
        {
            using var zip = new ZipArchive(archive, ZipArchiveMode.Read, leaveOpen: true);
            var manifest = zip.GetEntry(file);
            if (manifest is null)
                return null;
            using var stream = manifest.Open();
            return JsonSerializer.Deserialize<ExtensionManifest>(stream, JsonSerializerOptions.Web)?.Id;
        }
        catch (Exception e) when (e is InvalidDataException or JsonException)
        {
            return null;
        }
    }
}

/// <summary>
/// The web players of the catalog (the official Ultimate Web Player and those anyone publishes), installed with one
/// click: checked against the SHA-256 the catalog publishes and installed as any other player, with full trust (a
/// player runs with its users' sessions). Administrators only.
/// </summary>
public sealed class PlayerMarketplaceService(
    AccessGuard guard,
    MarketplaceCatalog catalog,
    Frontends.FrontendService frontends,
    Core.Hosting.FrontendCatalog installed,
    ExtensionService extensions,
    Server.PrivacySettings privacy,
    IHttpClientFactory clients)
{
    public async Task<PlayerMarketplaceInfo> GetAsync(string? language, bool refresh, CancellationToken cancellationToken)
    {
        guard.Admin();
        if ((await privacy.GetAsync(cancellationToken)).ExternalServices == Server.ExternalServices.None)
            return new PlayerMarketplaceInfo(false, ErrorCodes.ExternalServicesDisabled, null, catalog.Url, []);

        CatalogFile file;
        DateTime at;
        try
        {
            (file, at) = await catalog.GetAsync(refresh ? TimeSpan.Zero : null, cancellationToken);
        }
        catch (ArcanistException e)
        {
            return new PlayerMarketplaceInfo(false, e.Code, null, catalog.Url, []);
        }

        var languages = await extensions.LanguagesAsync(language, cancellationToken);
        var here = installed.List().ToDictionary(f => f.Manifest.Id);
        var current = (await installed.DefaultAsync(cancellationToken))?.Manifest.Id;
        var entries = (file.Players ?? []).Select(entry =>
            {
                var release = ExtensionMarketplaceService.Pick(entry);
                var player = here.GetValueOrDefault(entry.Id);
                return new MarketplacePlayer(entry.Id,
                    ExtensionService.Localized(entry.Names, languages) ?? entry.Name,
                    ExtensionService.Localized(entry.Descriptions, languages) ?? entry.Description,
                    entry.Author, entry.Homepage, entry.License,
                    entry.Icon is null ? null : $"/rest/v1/frontends/marketplace/icons/{entry.Id}", entry.Official, entry.Categories ?? [],
                    release.Version, release.Size, release.MinServerVersion, release.RunsOn(ArcanistVersion.Current),
                    player is { BuiltIn: false } ? player.Manifest.Version : null,
                    player is { BuiltIn: false } && ArcanistVersion.Compare(release.Version, player.Manifest.Version) > 0,
                    player is { BuiltIn: true }, player is not null && current == entry.Id, entry.PricingKind, entry.SafePricingUrl);
            })
            .OrderByDescending(e => e.Official).ThenBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        return new PlayerMarketplaceInfo(true, null, at, catalog.Url, entries);
    }

    /// <summary>Installs (or updates) a web player of the catalog and, when asked, serves it at <c>/</c>.</summary>
    public async Task<Frontends.Frontend> InstallAsync(InstallMarketplacePlayerInput input, CancellationToken cancellationToken)
    {
        guard.Admin();
        guard.RecentAuthentication();
        if ((await privacy.GetAsync(cancellationToken)).ExternalServices == Server.ExternalServices.None)
            throw new ArcanistException.Conflict(ErrorCodes.ExternalServicesDisabled);

        var (file, _) = await catalog.GetAsync(TimeSpan.FromMinutes(5), cancellationToken);
        var entry = (file.Players ?? []).SingleOrDefault(e => e.Id == input.Id) ?? throw new ArcanistException.NotFound();
        if (installed.Find(entry.Id) is { BuiltIn: true })
            throw ArcanistException.Field("id", ErrorCodes.Duplicate);
        var release = ExtensionMarketplaceService.Pick(entry);
        if (!release.RunsOn(ArcanistVersion.Current))
            throw new ArcanistException.Conflict(ErrorCodes.MarketplaceIncompatible);

        await using var archive = await PackageArchive.DownloadAsync(clients.CreateClient(Frontends.FrontendService.HttpClientName), release.Url, cancellationToken);
        var hash = Convert.ToHexStringLower(await SHA256.HashDataAsync(archive, cancellationToken));
        if (!string.Equals(hash, release.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new ArcanistException.Conflict(ErrorCodes.PackageChecksumMismatch);
        archive.Position = 0;
        if (ExtensionMarketplaceService.ManifestId(archive, Core.Hosting.FrontendCatalog.ManifestFile) != entry.Id)
            throw new ArcanistException.Conflict(ErrorCodes.PackageMismatch);
        archive.Position = 0;

        var player = await frontends.InstallAsync(archive, cancellationToken);
        return input.MakeDefault ? await frontends.SetDefaultAsync(player.Id, cancellationToken) : player;
    }
}
