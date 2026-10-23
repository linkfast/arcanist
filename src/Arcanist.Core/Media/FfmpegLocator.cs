using System.Diagnostics;
using System.Text.RegularExpressions;
using Arcanist.Core.Data;
using Arcanist.Core.Hosting;
using Microsoft.Extensions.Options;

namespace Arcanist.Core.Media;

/// <summary>Bound from <c>Arcanist:Ffmpeg</c>, for Docker and advanced setups; the admin panel stores its own choice.</summary>
public sealed class FfmpegOptions
{
    public const string Section = "Arcanist:Ffmpeg";

    /// <summary>Path to the ffmpeg executable. Wins over everything else.</summary>
    public string? Path { get; set; }

    /// <summary>Whether to look for ffmpeg on the PATH and in known package locations.</summary>
    public bool SearchSystem { get; set; } = true;
}

public enum FfmpegSource
{
    /// <summary>From <c>Arcanist:Ffmpeg:Path</c> or the path set in the setup wizard or admin panel.</summary>
    Configured,
    /// <summary>Downloaded by Arcanist into its data directory.</summary>
    Downloaded,
    /// <summary>Found on the system: the PATH or a known package location (e.g. Synology's ffmpeg package).</summary>
    System,
}

/// <param name="Encoders">Audio encoders this build has (e.g. libopus, libmp3lame, aac), which decide the transcoding formats.</param>
public sealed record FfmpegInstallation(string Path, string Version, FfmpegSource Source, IReadOnlySet<string> Encoders);

/// <summary>
/// Finds ffmpeg, which Arcanist does not ship: a configured path, then one downloaded from the admin panel, then
/// known package locations and the PATH. The result is cached until <see cref="RefreshAsync"/>.
/// </summary>
public sealed partial class FfmpegLocator(
    IOptionsMonitor<FfmpegOptions> options,
    IOptionsMonitor<Setup.SetupOptions> setup,
    IArcanistDbContextFactory databases,
    SettingsStore settings,
    ArcanistPaths paths,
    ILogger<FfmpegLocator> logger)
{
    private static readonly string ExecutableName = OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";

    /// <summary>Synology packages from SynoCommunity, newest first.</summary>
    private static readonly string[] KnownLocations =
    [
        "/var/packages/ffmpeg8/target/bin/ffmpeg",
        "/var/packages/ffmpeg7/target/bin/ffmpeg",
        "/var/packages/ffmpeg6/target/bin/ffmpeg",
        "/var/packages/ffmpeg/target/bin/ffmpeg",
    ];

    private readonly SemaphoreSlim _lock = new(1, 1);
    private Resolution? _resolution;

    /// <summary>Where downloads go: <c>&lt;data&gt;/tools/ffmpeg</c>.</summary>
    public string DownloadDirectory => Path.Combine(paths.Root, "tools", "ffmpeg");

    public sealed record Resolution(FfmpegInstallation? Installation, string? ConfiguredPath, bool ConfiguredPathWorks);

    /// <summary>The oldest ffmpeg Arcanist is happy with; older ones work, but the panel recommends the download.</summary>
    public const int RecommendedMajor = 6;

    /// <summary>The downloaded build, whether or not it is the one in use.</summary>
    public string DownloadedExecutable => Path.Combine(DownloadDirectory, ExecutableName);

    /// <summary>
    /// A release older than <see cref="RecommendedMajor"/> (<c>4.4.2-0ubuntu0.22.04.1</c>, <c>n5.1</c>). Builds from git
    /// (<c>N-117…</c>) and anything not read as a version count as recent.
    /// </summary>
    public static bool IsOutdated(string? version) =>
        version is not null
        && System.Text.RegularExpressions.Regex.Match(version, @"^n?(\d+)\.\d+") is { Success: true } match
        && int.TryParse(match.Groups[1].Value, out var major)
        && major < RecommendedMajor;

    public async Task<FfmpegInstallation?> GetAsync(CancellationToken cancellationToken) =>
        (await ResolveAsync(cancellationToken)).Installation;

    /// <summary>The last result without looking again; null until the first lookup (done at startup).</summary>
    public FfmpegInstallation? Current => _resolution?.Installation;

    public async Task<Resolution> ResolveAsync(CancellationToken cancellationToken)
    {
        if (_resolution is { } cached)
            return cached;
        return await RefreshAsync(cancellationToken);
    }

    /// <summary>Looks again, after the configured path changed or a download finished.</summary>
    public async Task<Resolution> RefreshAsync(CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            var configured = options.CurrentValue.Path;
            // The database only has settings once setup has created (and migrated) it.
            if (string.IsNullOrWhiteSpace(configured) && setup.CurrentValue.Completed && databases.IsConfigured)
                configured = await settings.GetAsync<string?>(SettingKeys.FfmpegPath, null, cancellationToken);

            var candidates = new List<(string Path, FfmpegSource Source)>();
            if (!string.IsNullOrWhiteSpace(configured))
                candidates.Add((configured, FfmpegSource.Configured));
            candidates.Add((Path.Combine(DownloadDirectory, ExecutableName), FfmpegSource.Downloaded));
            if (options.CurrentValue.SearchSystem)
            {
                candidates.AddRange(KnownLocations.Select(path => (path, FfmpegSource.System)));
                candidates.AddRange(SearchPath().Select(path => (path, FfmpegSource.System)));
            }

            FfmpegInstallation? found = null;
            var configuredWorks = false;
            foreach (var (path, source) in candidates)
            {
                if (await ProbeAsync(path, cancellationToken) is not { } version)
                    continue;
                found = new FfmpegInstallation(Path.GetFullPath(path), version, source, await EncodersAsync(path, cancellationToken));
                configuredWorks = source == FfmpegSource.Configured;
                break;
            }

            if (!string.IsNullOrWhiteSpace(configured) && !configuredWorks)
                logger.LogWarning("The configured ffmpeg ({Path}) does not work", configured);

            _resolution = new Resolution(found, string.IsNullOrWhiteSpace(configured) ? null : configured, configuredWorks);
            return _resolution;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>The version reported by <c>ffmpeg -version</c>, or null if <paramref name="path"/> is not a working ffmpeg.</summary>
    public static async Task<string?> ProbeAsync(string path, CancellationToken cancellationToken) =>
        File.Exists(path) && await RunAsync(path, "-version", cancellationToken) is { } output && VersionLine().Match(output) is { Success: true } match
            ? match.Groups[1].Value
            : null;

    private static async Task<IReadOnlySet<string>> EncodersAsync(string path, CancellationToken cancellationToken)
    {
        // Lines look like " A....D libopus              libopus Opus"; the second column is the name.
        var output = await RunAsync(path, "-encoders", cancellationToken) ?? "";
        return output.Split('\n')
            .Select(line => line.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Where(parts => parts.Length >= 2 && parts[0].StartsWith('A'))
            .Select(parts => parts[1])
            .ToHashSet();
    }

    private static async Task<string?> RunAsync(string path, string argument, CancellationToken cancellationToken)
    {
        try
        {
            var start = new ProcessStartInfo(path)
            {
                ArgumentList = { "-hide_banner", argument },
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var process = Process.Start(start)!;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
            await process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            return process.ExitCode == 0 ? await output : null;
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or IOException or OperationCanceledException or InvalidOperationException)
        {
            return null;
        }
    }

    private static IEnumerable<string> SearchPath() =>
        (Environment.GetEnvironmentVariable("PATH") ?? "")
        .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(directory => Path.Combine(directory, ExecutableName))
        .Where(File.Exists);

    [GeneratedRegex(@"^ffmpeg version (\S+)", RegexOptions.Multiline)]
    private static partial Regex VersionLine();
}
