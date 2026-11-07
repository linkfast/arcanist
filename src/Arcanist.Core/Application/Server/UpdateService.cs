using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Arcanist.Core.Data;
using Arcanist.Core.Data.Entities;
using Arcanist.Core.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Arcanist.Core.Application.Server;

/// <summary>How this server was installed, to tell how it is updated. Part of the API contract.</summary>
public enum InstallKind
{
    /// <summary>The Windows installer: the tray app updates it (and the service, when it runs as one).</summary>
    Windows,
    /// <summary>A container: pull the new image and recreate it.</summary>
    Docker,
    /// <summary>The Synology package: from DSM's Package Center.</summary>
    Synology,
    /// <summary>The .deb package for Ubuntu and Debian.</summary>
    DebianPackage,
    /// <summary>OpenMediaVault's plugin (openmediavault-arcanist, over the .deb).</summary>
    OpenMediaVault,
    /// <summary>The QNAP package (.qpkg), from App Center.</summary>
    Qnap,
    /// <summary>The TrueNAS SCALE app (a container managed by TrueNAS).</summary>
    TrueNas,
    /// <summary>The Unraid template (a container managed by Unraid).</summary>
    Unraid,
    /// <summary>The Rockstor Rock-on (a container managed by Rockstor).</summary>
    Rockstor,
    /// <summary>The Windows installer's server running as a Windows service (NT SERVICE\Arcanist).</summary>
    WindowsService,
    /// <summary>Arcanist.app on macOS (the menu bar app runs the server).</summary>
    MacOS,
    /// <summary>A Linux tarball run by systemd (packaging/linux/arcanist.service), not the .deb.</summary>
    Systemd,
    /// <summary>Anything else (a tarball, a build from source).</summary>
    Other,
}

/// <summary>A published version of Arcanist. Part of the API contract.</summary>
/// <param name="Notes">What changed, in Markdown, as the release says.</param>
/// <param name="Url">Its release page.</param>
public sealed record ArcanistRelease(string Version, string Name, string Notes, string Url, DateTime PublishedAt, bool Prerelease);

/// <summary>Whether there is a newer Arcanist. Part of the API contract.</summary>
/// <param name="Enabled">Checks are on (they are not with external services off in Privacy).</param>
/// <param name="Latest">The newest release this server would update to: stable ones, and pre-releases too while it runs one.</param>
/// <param name="ErrorCode">Why the last check failed, if it did.</param>
/// <param name="Changes">What it brings over the installed version, newest first; empty when it could not be read
/// (a build that is no release, GitHub out of reach): <see cref="ArcanistRelease.Url"/> says it then.</param>
/// <param name="MoreChanges">There are more changes than <paramref name="Changes"/> lists: the release page has them.</param>
/// <param name="VersionsBehind">Published versions between the installed one and the latest, this last included.</param>
/// <param name="Install">How to update this server, while there is something to update to.</param>
public sealed record ServerUpdate(
    bool Enabled,
    string CurrentVersion,
    InstallKind InstallKind,
    ArcanistRelease? Latest,
    bool UpdateAvailable,
    DateTime? CheckedAt,
    string? ErrorCode,
    IReadOnlyList<ArcanistChange> Changes,
    bool MoreChanges,
    int VersionsBehind,
    UpdateInstall? Install);

public sealed record SetServerUpdateSettingsInput(bool Enabled);

/// <summary>Bound from <c>Arcanist:Updates</c>.</summary>
public sealed class UpdateOptions
{
    public const string Section = "Arcanist:Updates";

    /// <summary>The GitHub API's list of releases of Arcanist.</summary>
    public string ReleasesUrl { get; set; } = "https://api.github.com/repos/linkfast/arcanist/releases?per_page=30";

    public TimeSpan Interval { get; set; } = TimeSpan.FromHours(12);

    /// <summary>Checks on their own (the tests switch them off: they never reach the internet).</summary>
    public bool CheckInBackground { get; set; } = true;

    /// <summary>The version to compare with; the running one when empty (tests set it).</summary>
    public string? CurrentVersion { get; set; }

    /// <summary>The panel may install updates where the installation allows it (a managed deployment turns this off).</summary>
    public bool AllowSelfUpdate { get; set; } = true;
}

/// <summary>The last check, kept in the settings so it survives restarts.</summary>
/// <param name="ChangesFrom">The version <paramref name="Changes"/> counts from: after updating they no longer apply.</param>
public sealed record StoredUpdateCheck(
    ArcanistRelease? Latest,
    DateTime CheckedAt,
    string? ErrorCode,
    IReadOnlyList<ReleaseAsset>? Assets = null,
    IReadOnlyList<ArcanistChange>? Changes = null,
    bool MoreChanges = false,
    int VersionsBehind = 0,
    string? ChangesFrom = null);

/// <summary>Reads Arcanist's releases from GitHub and keeps the newest one that applies.</summary>
public sealed class UpdateChecker(
    IHttpClientFactory clients,
    SettingsStore settings,
    PrivacySettings privacy,
    IOptionsMonitor<UpdateOptions> options,
    TimeProvider time,
    ILogger<UpdateChecker> logger)
{
    public const string HttpClientName = "updates";

    private readonly SemaphoreSlim _checking = new(1, 1);

    public string CurrentVersion => options.CurrentValue.CurrentVersion is { Length: > 0 } version ? version : ArcanistVersion.Current;

    public async Task<bool> EnabledAsync(CancellationToken cancellationToken) =>
        (await privacy.GetAsync(cancellationToken)).ExternalServices != ExternalServices.None
        && await settings.GetAsync(SettingKeys.UpdatesEnabled, true, cancellationToken);

    public Task<StoredUpdateCheck?> LastAsync(CancellationToken cancellationToken) =>
        settings.GetAsync<StoredUpdateCheck?>(SettingKeys.UpdatesLastCheck, null, cancellationToken);

    /// <summary>Checks now (when enabled) and keeps the result.</summary>
    public async Task<StoredUpdateCheck?> CheckAsync(CancellationToken cancellationToken)
    {
        if (!await EnabledAsync(cancellationToken))
            return await LastAsync(cancellationToken);

        await _checking.WaitAsync(cancellationToken);
        try
        {
            StoredUpdateCheck check;
            try
            {
                var releases = await clients.CreateClient(HttpClientName)
                    .GetFromJsonAsync<List<GitHubRelease>>(options.CurrentValue.ReleasesUrl, JsonSerializerOptions.Web, cancellationToken) ?? [];
                var latest = Newest(releases, CurrentVersion);
                var (changes, more) = latest is not null && ArcanistVersion.Compare(latest.Version, CurrentVersion) > 0
                    ? FromNotes(releases, CurrentVersion) is { } listed ? listed : await ChangesAsync(latest.Version, cancellationToken)
                    : ([], false);
                var assets = releases.FirstOrDefault(r => latest is not null && ArcanistVersion.TryNormalize(r.TagName, out var version) && version == latest.Version)?.Assets
                    ?.Select(ToAsset).ToList();
                check = new StoredUpdateCheck(latest, time.GetUtcNow().UtcDateTime, null, assets, changes, more, Behind(releases, CurrentVersion), CurrentVersion);
            }
            catch (Exception e) when (e is HttpRequestException or JsonException or NotSupportedException
                                          or TaskCanceledException && !cancellationToken.IsCancellationRequested)
            {
                logger.LogInformation("Could not check for a newer Arcanist: {Error}", e.Message);
                // The release found before stays known.
                var last = await LastAsync(cancellationToken);
                check = last is null
                    ? new StoredUpdateCheck(null, time.GetUtcNow().UtcDateTime, ErrorCodes.UpdateCheckFailed)
                    : last with { CheckedAt = time.GetUtcNow().UtcDateTime, ErrorCode = ErrorCodes.UpdateCheckFailed };
            }

            await settings.SetAsync(SettingKeys.UpdatesLastCheck, check, cancellationToken);
            return check;
        }
        finally
        {
            _checking.Release();
        }
    }

    /// <summary>
    /// The newest published release a server on <paramref name="current"/> would update to: pre-releases only while
    /// it runs one, as the Windows tray app does.
    /// </summary>
    internal static ArcanistRelease? Newest(IEnumerable<GitHubRelease> releases, string current) =>
        Applicable(releases, current)
            .OrderByDescending(r => r.Version, Comparer<string>.Create(ArcanistVersion.Compare))
            .FirstOrDefault();

    /// <summary>How many published versions a server on <paramref name="current"/> has yet to get to.</summary>
    internal static int Behind(IEnumerable<GitHubRelease> releases, string current) =>
        Applicable(releases, current).Count(r => ArcanistVersion.Compare(r.Version, current) > 0);

    private static IEnumerable<ArcanistRelease> Applicable(IEnumerable<GitHubRelease> releases, string current)
    {
        var prerelease = ArcanistVersion.IsPrerelease(current);
        return releases
            .Where(r => !r.Draft && (prerelease || !r.Prerelease) && ArcanistVersion.TryNormalize(r.TagName, out _))
            .Select(r =>
            {
                ArcanistVersion.TryNormalize(r.TagName, out var version);
                return new ArcanistRelease(version, string.IsNullOrWhiteSpace(r.Name) ? $"Arcanist {version}" : r.Name,
                    r.Body ?? "", r.HtmlUrl, r.PublishedAt ?? DateTime.MinValue, r.Prerelease);
            });
    }

    /// <summary>Most changes listed: beyond them, the release pages have the rest.</summary>
    private const int MaxChanges = 300;

    /// <summary>
    /// What the versions after <paramref name="current"/> changed, as their release notes list it (newest version first),
    /// when every one of them has the section (<see cref="UpdateInstructions.ReleaseChanges"/>); null otherwise, and the
    /// comparison of the tags says it.
    /// </summary>
    internal static (IReadOnlyList<ArcanistChange> Changes, bool More)? FromNotes(IEnumerable<GitHubRelease> releases, string current)
    {
        var newer = Applicable(releases, current)
            .Where(r => ArcanistVersion.Compare(r.Version, current) > 0)
            .OrderByDescending(r => r.Version, Comparer<string>.Create(ArcanistVersion.Compare))
            .Select(r => UpdateInstructions.ReleaseChanges(r.Notes))
            .ToList();
        if (newer.Count == 0 || newer.Any(n => n is null))
            return null;
        var all = newer.SelectMany(n => n!).ToList();
        return (all.Take(MaxChanges).ToList(), all.Count > MaxChanges);
    }

    /// <summary>
    /// What the versions between this one and <paramref name="latest"/> changed, from GitHub's comparison of their tags.
    /// Never fails the check: a build that is no release has no tag to compare with.
    /// </summary>
    private async Task<(IReadOnlyList<ArcanistChange> Changes, bool More)> ChangesAsync(string latest, CancellationToken cancellationToken)
    {
        try
        {
            var comparison = await clients.CreateClient(HttpClientName).GetFromJsonAsync<GitHubComparison>(
                $"{RepositoryApi(options.CurrentValue.ReleasesUrl)}/compare/v{CurrentVersion}...v{latest}", JsonSerializerOptions.Web, cancellationToken);
            if (comparison?.Commits is null)
                return ([], false);
            return (UpdateInstructions.Changes(comparison.Commits.Select(c => (c.Commit?.Message ?? "", c.HtmlUrl))),
                comparison.TotalCommits > comparison.Commits.Count);
        }
        catch (Exception e) when (e is HttpRequestException or JsonException or NotSupportedException
                                      or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            logger.LogInformation("Could not read what changed in Arcanist {Version}: {Error}", latest, e.Message);
            return ([], false);
        }
    }

    /// <summary>
    /// How this server was installed, from where it runs. The NAS apps that run the image say it with
    /// <c>ARCANIST_INSTALL_KIND</c> (Unraid already says <c>HOST_OS=Unraid</c>).
    /// </summary>
    public static InstallKind DetectInstallKind()
    {
        var home = AppContext.BaseDirectory.Replace('\\', '/');
        if (Enum.TryParse<InstallKind>(Environment.GetEnvironmentVariable("ARCANIST_INSTALL_KIND")?.Replace("_", ""), ignoreCase: true, out var declared)
            && Enum.IsDefined(declared))
            return declared;
        // The tray (Windows) and the menu bar app (macOS) start the server with ExitWhenInputCloses.
        var fromApp = Environment.GetCommandLineArgs().Any(a => a.Contains("ExitWhenInputCloses=true", StringComparison.OrdinalIgnoreCase));
        if (OperatingSystem.IsWindows())
            return Microsoft.Extensions.Hosting.WindowsServices.WindowsServiceHelpers.IsWindowsService() ? InstallKind.WindowsService
                : fromApp ? InstallKind.Windows
                : InstallKind.Other;
        if (OperatingSystem.IsMacOS())
            return fromApp ? InstallKind.MacOS : InstallKind.Other;
        if (Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") == "true")
            return string.Equals(Environment.GetEnvironmentVariable("HOST_OS"), "Unraid", StringComparison.OrdinalIgnoreCase)
                ? InstallKind.Unraid
                : InstallKind.Docker;
        if (Environment.GetEnvironmentVariable("QNAP_QPKG") is { Length: > 0 } || home.Contains("/.qpkg/Arcanist", StringComparison.Ordinal))
            return InstallKind.Qnap;
        if (home.Contains("/@appstore/arcanist", StringComparison.Ordinal) || home.Contains("/var/packages/arcanist", StringComparison.Ordinal))
            return InstallKind.Synology;
        if (home.StartsWith("/opt/arcanist", StringComparison.Ordinal) && File.Exists("/var/lib/dpkg/info/arcanist.list"))
            return File.Exists("/var/lib/dpkg/info/openmediavault-arcanist.list") ? InstallKind.OpenMediaVault : InstallKind.DebianPackage;
        // systemd sets INVOCATION_ID for the processes of a unit.
        if (Environment.GetEnvironmentVariable("INVOCATION_ID") is { Length: > 0 })
            return InstallKind.Systemd;
        return InstallKind.Other;
    }

    internal sealed record GitHubRelease(
        [property: JsonPropertyName("tag_name")] string TagName,
        string? Name,
        string? Body,
        [property: JsonPropertyName("html_url")] string HtmlUrl,
        bool Draft,
        bool Prerelease,
        [property: JsonPropertyName("published_at")] DateTime? PublishedAt,
        List<GitHubAsset>? Assets = null);

    /// <summary>The repository in GitHub's API, from its list of releases.</summary>
    internal static string RepositoryApi(string releasesUrl) => releasesUrl.Split("/releases")[0];

    internal static ReleaseAsset ToAsset(GitHubAsset asset) => new(asset.Name, asset.BrowserDownloadUrl, asset.Size,
        asset.Digest?.StartsWith("sha256:", StringComparison.Ordinal) == true ? asset.Digest["sha256:".Length..] : null);

    internal sealed record GitHubAsset(
        string Name,
        [property: JsonPropertyName("browser_download_url")] string BrowserDownloadUrl,
        long Size,
        string? Digest);

    private sealed record GitHubComparison(
        [property: JsonPropertyName("total_commits")] int TotalCommits,
        List<GitHubCommit>? Commits);

    private sealed record GitHubCommit([property: JsonPropertyName("html_url")] string? HtmlUrl, GitHubCommitDetail? Commit);

    private sealed record GitHubCommitDetail(string? Message);
}

/// <summary>Checks for a newer Arcanist a minute after starting, then every <see cref="UpdateOptions.Interval"/>.</summary>
public sealed class UpdateCheckWorker(UpdateChecker checker, IOptionsMonitor<Setup.SetupOptions> setup, IOptionsMonitor<UpdateOptions> options, TimeProvider time)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMinutes(1), time, stoppingToken);
            while (!stoppingToken.IsCancellationRequested)
            {
                // Before setup there is no database to keep the result in.
                if (setup.CurrentValue.Completed && options.CurrentValue.CheckInBackground)
                    await checker.CheckAsync(stoppingToken);
                await Task.Delay(options.CurrentValue.Interval, time, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }
}

/// <summary>New versions of Arcanist, for administrators.</summary>
public sealed class UpdateService(AccessGuard guard, UpdateChecker checker, SettingsStore settings, HostPlatform platform, SelfUpdater selfUpdater, ArcanistDbContext db)
{
    /// <summary>Where an update being installed from the panel is.</summary>
    public ServerUpdateInstall InstallStatus()
    {
        guard.Admin();
        return selfUpdater.Status(UpdateChecker.DetectInstallKind());
    }

    /// <summary>Installs the latest release, where the installation lets the panel do it. Never another version.</summary>
    public async Task<ServerUpdateInstall> InstallAsync(CancellationToken cancellationToken)
    {
        guard.Admin();
        guard.RecentAuthentication();
        var kind = UpdateChecker.DetectInstallKind();
        if (selfUpdater.Mode(kind) != SelfUpdateMode.Panel)
            throw new ArcanistException.Conflict(ErrorCodes.UpdateSelfUnavailable);
        var latest = await checker.EnabledAsync(cancellationToken) ? (await checker.LastAsync(cancellationToken))?.Latest : null;
        if (latest is null || ArcanistVersion.Compare(latest.Version, checker.CurrentVersion) <= 0)
            throw new ArcanistException.Conflict(ErrorCodes.UpdateSelfUnavailable);
        await selfUpdater.RequestAsync(kind, latest.Version, cancellationToken);
        return new ServerUpdateInstall(SelfUpdateState.Downloading, latest.Version, null);
    }

    public async Task<ServerUpdate> GetAsync(CancellationToken cancellationToken)
    {
        guard.Admin();
        return await ModelAsync(await checker.EnabledAsync(cancellationToken), await checker.LastAsync(cancellationToken), cancellationToken);
    }

    public async Task<ServerUpdate> CheckAsync(CancellationToken cancellationToken)
    {
        guard.Admin();
        var check = await checker.CheckAsync(cancellationToken);
        return await ModelAsync(await checker.EnabledAsync(cancellationToken), check, cancellationToken);
    }

    public async Task<ServerUpdate> SetSettingsAsync(SetServerUpdateSettingsInput input, CancellationToken cancellationToken)
    {
        guard.Admin();
        await settings.SetAsync(SettingKeys.UpdatesEnabled, input.Enabled, cancellationToken);
        return input.Enabled ? await CheckAsync(cancellationToken) : await GetAsync(cancellationToken);
    }

    private async Task<ServerUpdate> ModelAsync(bool enabled, StoredUpdateCheck? check, CancellationToken cancellationToken)
    {
        var latest = enabled ? check?.Latest : null;
        var available = latest is not null && ArcanistVersion.Compare(latest.Version, checker.CurrentVersion) > 0;
        var kind = UpdateChecker.DetectInstallKind();
        // What was counted from another version (the one before an update) no longer says anything.
        var current = available && check?.ChangesFrom == checker.CurrentVersion;
        return new ServerUpdate(enabled, checker.CurrentVersion, kind, latest, available, check?.CheckedAt, enabled ? check?.ErrorCode : null,
            current ? check!.Changes ?? [] : [],
            current && check!.MoreChanges,
            current ? check!.VersionsBehind : 0,
            available ? await InstallAsync(kind, latest!, check!, cancellationToken) : null);
    }

    private async Task<UpdateInstall> InstallAsync(InstallKind kind, ArcanistRelease latest, StoredUpdateCheck check, CancellationToken cancellationToken)
    {
        var install = UpdateInstructions.Build(kind, platform.Current.Architecture, latest.Version, latest.Url, check.Assets ?? [], Environment.MachineName)
            with { SelfUpdate = selfUpdater.Mode(kind) };
        if (kind != InstallKind.Docker || !File.Exists(MountInfoPath))
            return install;

        // What recreating the container keeps, and what it would lose.
        var mounts = UpdateInstructions.MountsOf(await File.ReadAllTextAsync(MountInfoPath, cancellationToken));
        var folders = await db.LibrarySources.AsNoTracking()
            .Where(s => s.Kind == LibrarySourceKind.Local)
            .Select(s => new { s.Location, s.Library.Name })
            .ToListAsync(cancellationToken);
        return install with
        {
            DataInContainer = !UpdateInstructions.IsMounted(platform.Current.DataDirectory, mounts),
            Mounts = mounts,
            LibrariesOutsideMounts = folders.Where(f => !UpdateInstructions.IsMounted(f.Location, mounts)).Select(f => f.Name).Distinct().ToList(),
        };
    }

    private const string MountInfoPath = "/proc/self/mountinfo";
}
