using System.Diagnostics;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Arcanist.Core.Hosting;
using Microsoft.Extensions.Options;

namespace Arcanist.Core.Application.Server;

/// <summary>Whether the panel can install an update on this server. Part of the API contract.</summary>
public enum SelfUpdateMode
{
    None,
    /// <summary><c>installServerUpdate</c> installs it (the .deb package, OpenMediaVault, QNAP).</summary>
    Panel,
}

/// <summary>Where an update being installed from the panel is. Part of the API contract.</summary>
public enum SelfUpdateState
{
    Idle,
    Downloading,
    Installing,
    Failed,
}

/// <summary>An update being installed from the panel. Part of the API contract.</summary>
/// <param name="Version">The version being installed.</param>
/// <param name="ErrorCode">Why it failed: the file and the commands in <see cref="UpdateInstall"/> still work.</param>
public sealed record ServerUpdateInstall(SelfUpdateState State, string? Version, string? ErrorCode);

/// <summary>A release's package could not be brought or installed. The code is an <see cref="ErrorCodes"/> one.</summary>
internal sealed class SelfUpdateException(string code, string? detail = null) : Exception(detail ?? code)
{
    public string Code { get; } = code;
}

/// <summary>The packages of a release for an installation, downloaded and checked against the SHA-256 GitHub gives.</summary>
internal static class ReleasePackages
{
    /// <summary>
    /// Downloads into <paramref name="folder"/> the packages of <paramref name="version"/> that fit this installation.
    /// A package without a checksum is not installed: nothing else says it is the one published.
    /// </summary>
    public static async Task<List<string>> DownloadAsync(
        HttpClient http, string repositoryApi, string version, InstallKind kind, string folder, CancellationToken cancellationToken)
    {
        var release = await http.GetFromJsonAsync<UpdateChecker.GitHubRelease>(
            $"{repositoryApi}/releases/tags/v{version}", JsonSerializerOptions.Web, cancellationToken)
            ?? throw new SelfUpdateException(ErrorCodes.UpdateInstallFailed);
        var assets = (release.Assets ?? []).Select(UpdateChecker.ToAsset).ToList();
        var packages = UpdateInstructions.Downloads(kind, RuntimeInformation.OSArchitecture == Architecture.Arm64, assets);
        if (packages.Count == 0 || packages.Count != UpdateInstructions.PackagesFor(kind))
            throw new SelfUpdateException(ErrorCodes.UpdateInstallFailed, $"No package of {version} for {kind}");

        Directory.CreateDirectory(folder);
        var files = new List<string>();
        foreach (var package in packages)
        {
            // Its name comes from GitHub: only the name, never a path.
            var file = Path.Combine(folder, Path.GetFileName(package.Name));
            await using (var output = File.Create(file))
            await using (var input = await http.GetStreamAsync(package.Url, cancellationToken))
                await input.CopyToAsync(output, cancellationToken);
            await using var written = File.OpenRead(file);
            var hash = Convert.ToHexStringLower(await SHA256.HashDataAsync(written, cancellationToken));
            if (package.Sha256 is null || !hash.Equals(package.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new SelfUpdateException(ErrorCodes.UpdateChecksumMismatch, $"{package.Name}: {hash}");
            files.Add(file);
        }
        return files;
    }
}

/// <summary>
/// Installs an update from the panel, only ever the latest release of the official repository: the panel cannot name a
/// file or a URL. The .deb's server runs without privileges, so it only leaves a request that a root service of the
/// package (<see cref="SelfUpdateTool"/>) picks up; QNAP's runs as root and hands the package to <c>qpkg_cli</c>.
/// </summary>
public sealed class SelfUpdater(IHttpClientFactory clients, ArcanistPaths paths, IOptionsMonitor<UpdateOptions> options, ILogger<SelfUpdater> logger)
{
    private const string QpkgCli = "/sbin/qpkg_cli";

    private readonly Lock _lock = new();
    private ServerUpdateInstall _qnap = new(SelfUpdateState.Idle, null, null);

    public SelfUpdateMode Mode(InstallKind kind) => !options.CurrentValue.AllowSelfUpdate ? SelfUpdateMode.None : kind switch
    {
        InstallKind.DebianPackage or InstallKind.OpenMediaVault when File.Exists(SelfUpdateTool.ServiceUnit) => SelfUpdateMode.Panel,
        InstallKind.Qnap when File.Exists(QpkgCli) => SelfUpdateMode.Panel,
        _ => SelfUpdateMode.None,
    };

    public ServerUpdateInstall Status(InstallKind kind)
    {
        if (kind == InstallKind.Qnap)
            lock (_lock)
                return _qnap;
        try
        {
            return File.Exists(SelfUpdateTool.StatusFile)
                ? JsonSerializer.Deserialize<ServerUpdateInstall>(File.ReadAllText(SelfUpdateTool.StatusFile), SelfUpdateTool.Json) ?? new(SelfUpdateState.Idle, null, null)
                : new(SelfUpdateState.Idle, null, null);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return new(SelfUpdateState.Idle, null, null);
        }
    }

    public async Task RequestAsync(InstallKind kind, string version, CancellationToken cancellationToken)
    {
        if (kind == InstallKind.Qnap)
        {
            lock (_lock)
            {
                if (_qnap.State is SelfUpdateState.Downloading or SelfUpdateState.Installing)
                    return;
                _qnap = new(SelfUpdateState.Downloading, version, null);
            }
            // Beyond this request: installing stops this very server.
            _ = Task.Run(() => InstallQpkgAsync(version), CancellationToken.None);
            return;
        }

        var request = Path.Combine(paths.Root, SelfUpdateTool.RequestName);
        var partial = request + ".partial";
        await File.WriteAllTextAsync(partial, version, cancellationToken);
        File.Move(partial, request, overwrite: true);
    }

    private async Task InstallQpkgAsync(string version)
    {
        try
        {
            var folder = Path.Combine(paths.Root, "cache", "update");
            if (Directory.Exists(folder))
                Directory.Delete(folder, recursive: true);
            var files = await ReleasePackages.DownloadAsync(clients.CreateClient(UpdateChecker.HttpClientName),
                UpdateChecker.RepositoryApi(options.CurrentValue.ReleasesUrl), version, InstallKind.Qnap, folder, CancellationToken.None);
            lock (_lock)
                _qnap = new(SelfUpdateState.Installing, version, null);

            // In a session of its own: App Center stops the package, and this server with it, while it installs.
            var log = Path.Combine(paths.Root, "update.log");
            using var process = Process.Start(new ProcessStartInfo("/bin/sh")
            {
                ArgumentList = { "-c", """(setsid "$0" -m "$1" >> "$2" 2>&1 || "$0" -m "$1" >> "$2" 2>&1) &""", QpkgCli, files[0], log },
                UseShellExecute = false,
            });
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Could not install Arcanist {Version}", version);
            lock (_lock)
                _qnap = new(SelfUpdateState.Failed, version, (e as SelfUpdateException)?.Code ?? ErrorCodes.UpdateInstallFailed);
        }
    }
}

/// <summary>
/// The .deb's update service: <c>arcanist --self-update-apply</c>, as root, when the server leaves a request
/// (<c>arcanist-update.path</c>). What the request says is not trusted: the server that writes it runs unprivileged. So
/// it is only a version number, the package comes from the official repository, must match its checksum and be newer.
/// </summary>
internal static class SelfUpdateTool
{
    public const string Argument = "--self-update-apply";
    public const string RequestName = "update-request";
    public const string ServiceUnit = "/usr/lib/systemd/system/arcanist-update.service";
    public const string StatusFile = StatusFolder + "/status.json";

    private const string RequestFile = "/var/lib/private/arcanist/" + RequestName;
    private const string StatusFolder = "/run/arcanist-update";
    private const string Downloads = "/var/cache/arcanist-update";
    private const string OfficialApi = "https://api.github.com/repos/linkfast/arcanist";

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    public static async Task<int> RunAsync()
    {
        string? version = null;
        try
        {
            version = TakeRequest();
            if (version is null)
                return 0;
            if (ArcanistVersion.Compare(version, ArcanistVersion.Current) <= 0)
                throw new SelfUpdateException(ErrorCodes.UpdateInstallFailed, $"{version} is not newer than {ArcanistVersion.Current}");

            Write(new(SelfUpdateState.Downloading, version, null));
            // Only the unit can change this (a drop-in, as the tests do), and it is root's.
            var api = Environment.GetEnvironmentVariable("ARCANIST_UPDATE_API") is { Length: > 0 } other ? other : OfficialApi;
            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(15) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd($"Arcanist/{ArcanistVersion.Current}");
            if (Directory.Exists(Downloads))
                Directory.Delete(Downloads, recursive: true);
            var kind = File.Exists("/var/lib/dpkg/info/openmediavault-arcanist.list") ? InstallKind.OpenMediaVault : InstallKind.DebianPackage;
            var files = await ReleasePackages.DownloadAsync(http, api, version, kind, Downloads, CancellationToken.None);

            Write(new(SelfUpdateState.Installing, version, null));
            var start = new ProcessStartInfo("apt-get") { WorkingDirectory = Downloads, UseShellExecute = false };
            foreach (var argument in new[] { "install", "-y" }.Concat(files.Select(f => "./" + Path.GetFileName(f))))
                start.ArgumentList.Add(argument);
            start.Environment["DEBIAN_FRONTEND"] = "noninteractive";
            using var apt = Process.Start(start) ?? throw new SelfUpdateException(ErrorCodes.UpdateInstallFailed);
            await apt.WaitForExitAsync();
            if (apt.ExitCode != 0)
                throw new SelfUpdateException(ErrorCodes.UpdateInstallFailed, $"apt-get exited with {apt.ExitCode}");

            Directory.Delete(Downloads, recursive: true);
            Write(new(SelfUpdateState.Idle, version, null));
            return 0;
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"Could not install Arcanist {version}: {e.Message}");
            Write(new(SelfUpdateState.Failed, version, (e as SelfUpdateException)?.Code ?? ErrorCodes.UpdateInstallFailed));
            return 1;
        }
    }

    /// <summary>The version asked for, taking the request away; null if there is none or it is not a version.</summary>
    private static string? TakeRequest()
    {
        var request = new FileInfo(RequestFile);
        if (!request.Exists)
            return null;
        // A link would have root read whatever the server pointed it at.
        var text = request.LinkTarget is null && request.Length <= 64 ? File.ReadAllText(RequestFile).Trim() : "";
        request.Delete();
        return IsVersion(text) ? text : throw new SelfUpdateException(ErrorCodes.UpdateInstallFailed, "The request is not a version");
    }

    /// <summary>A version as a tag writes it: nothing that would make another URL.</summary>
    internal static bool IsVersion(string text) =>
        text.Length is > 0 and <= 64 && text.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-') && ArcanistVersion.TryNormalize(text, out _);

    /// <summary>In a folder only root writes: the server reads it, and could not point it anywhere.</summary>
    private static void Write(ServerUpdateInstall status)
    {
        Directory.CreateDirectory(StatusFolder);
        var partial = StatusFile + ".partial";
        File.WriteAllText(partial, JsonSerializer.Serialize(status, Json));
        File.SetUnixFileMode(partial, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead);
        File.Move(partial, StatusFile, overwrite: true);
    }
}
