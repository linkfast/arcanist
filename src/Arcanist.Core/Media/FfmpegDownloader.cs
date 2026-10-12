using System.Formats.Tar;
using System.IO.Compression;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Arcanist.Core.Analysis;
using Arcanist.Core.Application;
using SharpCompress.Compressors.Xz;

namespace Arcanist.Core.Media;

public enum FfmpegDownloadState
{
    Idle,
    Downloading,
    Installing,
    Completed,
    Failed,
}

/// <param name="Error">Why the last download failed, as an error code.</param>
public sealed record FfmpegDownloadProgress(FfmpegDownloadState State, long BytesReceived, long? TotalBytes, string? Version, string? Error);

/// <summary>
/// Downloads a static GPL build of ffmpeg from BtbN/FFmpeg-Builds (the release branch, not master) for Windows or
/// Linux, x64 or arm64, checks it against the published SHA-256 and installs ffmpeg and ffprobe into
/// <see cref="FfmpegLocator.DownloadDirectory"/>. Only one download runs at a time.
/// </summary>
public sealed partial class FfmpegDownloader(
    FfmpegLocator locator,
    AnalysisMonitor analysis,
    IHttpClientFactory httpClients,
    ILogger<FfmpegDownloader> logger)
{
    public const string HttpClientName = "ffmpeg-download";

    private const string ReleaseUrl = "https://api.github.com/repos/BtbN/FFmpeg-Builds/releases/latest";

    private readonly Lock _lock = new();
    private Task? _running;

    public FfmpegDownloadProgress Progress { get; private set; } = new(FfmpegDownloadState.Idle, 0, null, null, null);

    /// <summary>The BtbN platform name for this machine, or null where no build exists (macOS, 32-bit).</summary>
    public static string? Platform =>
        (OperatingSystem.IsWindows(), OperatingSystem.IsLinux(), RuntimeInformation.OSArchitecture) switch
        {
            (true, _, Architecture.X64) => "win64",
            (true, _, Architecture.Arm64) => "winarm64",
            (_, true, Architecture.X64) => "linux64",
            (_, true, Architecture.Arm64) => "linuxarm64",
            _ => null,
        };

    public bool IsRunning => _running is { IsCompleted: false };

    /// <summary>Starts the download in the background unless one is already running.</summary>
    public void Start()
    {
        lock (_lock)
        {
            if (IsRunning)
                return;
            Progress = new FfmpegDownloadProgress(FfmpegDownloadState.Downloading, 0, null, null, null);
            _running = Task.Run(RunAsync);
        }
    }

    private async Task RunAsync()
    {
        try
        {
            var platform = Platform ?? throw new DownloadException(ErrorCodes.FfmpegPlatformUnsupported);
            var http = httpClients.CreateClient(HttpClientName);

            var release = await http.GetFromJsonAsync<Release>(ReleaseUrl) ?? throw new DownloadException(ErrorCodes.FfmpegDownloadFailed);
            var asset = PickAsset(release.Assets, platform) ?? throw new DownloadException(ErrorCodes.FfmpegDownloadFailed);
            var checksums = release.Assets.FirstOrDefault(a => a.Name == "checksums.sha256") ?? throw new DownloadException(ErrorCodes.FfmpegDownloadFailed);
            var expected = ExpectedHash(await http.GetStringAsync(checksums.Url), asset.Name) ?? throw new DownloadException(ErrorCodes.FfmpegDownloadFailed);

            Directory.CreateDirectory(locator.DownloadDirectory);
            var archive = Path.Combine(Path.GetDirectoryName(locator.DownloadDirectory)!, asset.Name);
            try
            {
                await DownloadAsync(http, asset, archive);

                Progress = Progress with { State = FfmpegDownloadState.Installing };
                await using (var file = File.OpenRead(archive))
                {
                    if (!Convert.ToHexStringLower(await SHA256.HashDataAsync(file)).Equals(expected, StringComparison.OrdinalIgnoreCase))
                        throw new DownloadException(ErrorCodes.FfmpegChecksumMismatch);
                }

                Install(archive);
            }
            finally
            {
                File.Delete(archive);
            }

            var found = await locator.RefreshAsync(CancellationToken.None);
            Progress = Progress with { State = FfmpegDownloadState.Completed, Version = found.Installation?.Version };
            logger.LogInformation("Installed ffmpeg {Version} from {Asset}", found.Installation?.Version, asset.Name);

            // Files waiting for ffmpeg can be analyzed now.
            analysis.RequestRun();
        }
        catch (Exception e)
        {
            var code = e is DownloadException download ? download.Code : ErrorCodes.FfmpegDownloadFailed;
            logger.LogWarning(e, "Could not download ffmpeg");
            Progress = Progress with { State = FfmpegDownloadState.Failed, Error = code };
        }
    }

    private async Task DownloadAsync(HttpClient http, Asset asset, string target)
    {
        using var response = await http.GetAsync(asset.Url, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        Progress = Progress with { TotalBytes = response.Content.Headers.ContentLength ?? asset.Size };

        await using var source = await response.Content.ReadAsStreamAsync();
        await using var file = File.Create(target);
        var buffer = new byte[81920];
        long received = 0;
        int read;
        while ((read = await source.ReadAsync(buffer)) > 0)
        {
            await file.WriteAsync(buffer.AsMemory(0, read));
            received += read;
            Progress = Progress with { BytesReceived = received };
        }
    }

    /// <summary>Extracts ffmpeg and ffprobe into a fresh folder, then swaps it in, so a failure leaves the old ones.</summary>
    private void Install(string archive)
    {
        var staging = locator.DownloadDirectory + ".new";
        if (Directory.Exists(staging))
            Directory.Delete(staging, recursive: true);
        Directory.CreateDirectory(staging);

        string[] wanted = OperatingSystem.IsWindows() ? ["ffmpeg.exe", "ffprobe.exe"] : ["ffmpeg", "ffprobe"];
        var extracted = 0;

        if (archive.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            using var zip = ZipFile.OpenRead(archive);
            foreach (var entry in zip.Entries.Where(e => IsWanted(e.FullName, wanted)))
            {
                entry.ExtractToFile(Path.Combine(staging, entry.Name));
                extracted++;
            }
        }
        else
        {
            using var file = File.OpenRead(archive);
            using var xz = new XZStream(file);
            using var tar = new TarReader(xz);
            while (tar.GetNextEntry() is { } entry)
            {
                if (entry.EntryType != TarEntryType.RegularFile || !IsWanted(entry.Name, wanted))
                    continue;
                var target = Path.Combine(staging, Path.GetFileName(entry.Name));
                entry.ExtractToFile(target, overwrite: true);
                File.SetUnixFileMode(target, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                                             UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
                extracted++;
            }
        }

        if (extracted < wanted.Length)
            throw new DownloadException(ErrorCodes.FfmpegDownloadFailed);

        if (Directory.Exists(locator.DownloadDirectory))
            Directory.Delete(locator.DownloadDirectory, recursive: true);
        Directory.Move(staging, locator.DownloadDirectory);
    }

    private static bool IsWanted(string path, string[] names)
    {
        var parts = path.Replace('\\', '/').Split('/');
        return parts.Length >= 2 && parts[^2] == "bin" && names.Contains(parts[^1]);
    }

    /// <summary>The newest release-branch, statically linked GPL build for the platform.</summary>
    internal static Asset? PickAsset(IEnumerable<Asset> assets, string platform) =>
        assets
            .Select(asset => (Asset: asset, Match: AssetName().Match(asset.Name)))
            .Where(a => a.Match.Success && a.Match.Groups["platform"].Value == platform)
            .OrderByDescending(a => int.Parse(a.Match.Groups["major"].Value))
            .ThenByDescending(a => int.Parse(a.Match.Groups["minor"].Value))
            .Select(a => a.Asset)
            .FirstOrDefault();

    internal static string? ExpectedHash(string checksums, string name) =>
        checksums.Split('\n')
            .Select(line => line.Trim().Split(' ', 2, StringSplitOptions.TrimEntries))
            .FirstOrDefault(parts => parts.Length == 2 && parts[1].TrimStart('*') == name)?[0];

    [GeneratedRegex(@"^ffmpeg-n(?<major>\d+)\.(?<minor>\d+)-latest-(?<platform>[a-z0-9]+)-gpl-\d+\.\d+\.(zip|tar\.xz)$")]
    private static partial Regex AssetName();

    internal sealed record Release([property: JsonPropertyName("assets")] List<Asset> Assets);

    internal sealed record Asset(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("size")] long Size,
        [property: JsonPropertyName("browser_download_url")] string Url);

    private sealed class DownloadException(string code) : Exception(code)
    {
        public string Code { get; } = code;
    }
}
