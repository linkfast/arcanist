using System.Diagnostics;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Arcanist.Core.Application;
using Arcanist.Core.Hosting;
using Microsoft.Extensions.Options;

namespace Arcanist.Core.Cloud;

/// <summary>What <c>ultimate-audio tag</c> writes into a file (the fields Ultimate clients restore on download).</summary>
/// <param name="CoverPath">A JPEG or PNG to embed as the front cover.</param>
public sealed record UltimateAudioMetadata(
    string? Title = null,
    string? Artist = null,
    string? Album = null,
    string? Genre = null,
    int? TrackNumber = null,
    int? DiscNumber = null,
    int? Year = null,
    int? Bpm = null,
    string? MusicBrainzRecordingId = null,
    string? MusicBrainzReleaseGroupId = null,
    string? MusicBrainzReleaseId = null,
    string? MusicBrainzArtistId = null,
    string? CoverPath = null);

/// <summary>The audio hash and tag handling of Ultimate clients (<see cref="UltimateAudioTool"/>; tests replace it).</summary>
public interface IUltimateAudio
{
    bool IsInstalled { get; }

    Task<string> HashAsync(string path, CancellationToken cancellationToken);

    Task<(string AudioHash, string Sha1, long Size)> StripAsync(string path, string output, CancellationToken cancellationToken);

    Task TagAsync(string path, UltimateAudioMetadata metadata, CancellationToken cancellationToken);
}

/// <summary>
/// <c>ultimate-audio</c>: the audio hash and tag handling of Ultimate clients, which syncing with Ultimate Cloud needs
/// to be compatible with them. It is Ultimate's own (proprietary) program, so it is not part of Arcanist: Ultimate
/// Cloud serves it to linked servers, and Arcanist runs it as a separate program, as it runs ffmpeg.
/// <para>
/// Nothing runs unless it comes from Ultimate Cloud: the manifest must carry a valid signature of the key below (or
/// <see cref="UltimateCloudOptions.ToolsPublicKey"/>), and the downloaded file the SHA-256 the manifest gives. It is
/// installed in <c>&lt;data&gt;/tools/ultimate-audio/</c> when the account is linked, and updated when the plan is checked.
/// </para>
/// </summary>
public sealed class UltimateAudioTool(
    UltimateCloudAuth auth,
    IHttpClientFactory clients,
    IOptionsMonitor<UltimateCloudOptions> options,
    ArcanistPaths paths,
    IConfiguration configuration,
    ILogger<UltimateAudioTool> logger) : IUltimateAudio
{
    /// <summary>The public half of the key that signs what Ultimate Cloud serves (ECDSA P-256, SHA-256).</summary>
    public const string PublicKey = """
        -----BEGIN PUBLIC KEY-----
        MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEMuEshCUOpF3UMT0hcrATwDh8IT01
        L2Zi4y17jVIIYSo/1pO4YYiyuWTKeazUl5Hc1R8x+MuTgP4KYVfvqU1PIA==
        -----END PUBLIC KEY-----
        """;

    /// <summary>The command protocol Arcanist speaks (see ultimate-audio's Program.cs).</summary>
    public const int Protocol = 1;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly SemaphoreSlim _installing = new(1, 1);
    private Installed? _installed;
    private bool _loaded;

    private sealed record Installed(string Version, string Name, string Sha256);

    private sealed record ManifestFile(string Rid, string Name, long Size, string Sha256);

    private sealed record Manifest(string Tool, string Version, int Protocol, IReadOnlyList<ManifestFile> Files);

    private string Folder => Path.Combine(paths.Root, "tools", "ultimate-audio");

    /// <summary>The installed version, or null.</summary>
    public string? Version => Current?.Version;

    public bool IsInstalled => Current is not null;

    /// <summary>Why the last install or update failed: <c>cloud_tool_unavailable</c>, <c>cloud_tool_invalid</c>, <c>cloud_tool_unsupported</c>…</summary>
    public string? ErrorCode { get; private set; }

    private Installed? Current
    {
        get
        {
            if (!_loaded)
            {
                try
                {
                    var file = Path.Combine(Folder, "installed.json");
                    _installed = File.Exists(file) ? JsonSerializer.Deserialize<Installed>(File.ReadAllText(file), Json) : null;
                    if (_installed is not null && !File.Exists(Path.Combine(Folder, _installed.Name)))
                        _installed = null;
                }
                catch (Exception e) when (e is IOException or JsonException)
                {
                    _installed = null;
                }

                _loaded = true;
            }

            return _installed;
        }
    }

    /// <summary>The platform name of this server, as the manifest names files; null where there is no build.</summary>
    internal static string? Platform =>
        (OperatingSystem.IsWindows(), OperatingSystem.IsLinux(), RuntimeInformation.OSArchitecture) switch
        {
            (true, _, Architecture.X64) => "win-x64",
            (_, true, Architecture.X64) => "linux-x64",
            (_, true, Architecture.Arm64) => "linux-arm64",
            _ => null,
        };

    /// <summary>Installs the version Ultimate Cloud serves, if it is not installed yet. Failures are kept in <see cref="ErrorCode"/>.</summary>
    public async Task EnsureAsync(CancellationToken cancellationToken)
    {
        // Closed source: not installed nor run while the 100% open source mode is on.
        if (OpenSource.IsOn(configuration))
        {
            ErrorCode = ErrorCodes.ClosedSourceDisabled;
            return;
        }

        await _installing.WaitAsync(cancellationToken);
        try
        {
            ErrorCode = await InstallAsync(cancellationToken);
            if (ErrorCode is not null)
                logger.LogWarning("ultimate-audio could not be installed: {Error}", ErrorCode);
        }
        finally
        {
            _installing.Release();
        }
    }

    private async Task<string?> InstallAsync(CancellationToken cancellationToken)
    {
        if (Platform is not { } platform)
            return ErrorCodes.CloudToolUnsupported;
        if (ToolsUrl() is not { } tools)
            return ErrorCodes.CloudNotConfigured;

        // The manifest, and the signature of its exact bytes.
        JsonNode? answer;
        try
        {
            using var response = await SendAsync(new Uri(tools, "manifest"), cancellationToken);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                return ErrorCodes.CloudToolUnavailable;
            if (!response.IsSuccessStatusCode)
                return ErrorCodes.CloudUnreachable;
            answer = JsonNode.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        }
        catch (Exception e) when (e is HttpRequestException or JsonException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return ErrorCodes.CloudUnreachable;
        }
        catch (UltimateCloudException e)
        {
            return e.Code;
        }

        Manifest? manifest;
        try
        {
            var bytes = Convert.FromBase64String(answer?["manifest"]?.GetValue<string>() ?? "");
            var signature = Convert.FromBase64String(answer?["signature"]?.GetValue<string>() ?? "");
            using var key = ECDsa.Create();
            key.ImportFromPem(options.CurrentValue.ToolsPublicKey ?? PublicKey);
            if (!key.VerifyData(bytes, signature, HashAlgorithmName.SHA256))
                return ErrorCodes.CloudToolInvalid;
            manifest = JsonSerializer.Deserialize<Manifest>(bytes, Json);
        }
        catch (Exception e) when (e is FormatException or JsonException or CryptographicException or ArgumentException)
        {
            return ErrorCodes.CloudToolInvalid;
        }

        if (manifest is not { Tool: "ultimate-audio" } || manifest.Protocol != Protocol)
            return ErrorCodes.CloudToolUnsupported;
        if (manifest.Files.FirstOrDefault(f => f.Rid == platform) is not { } file
            || Path.GetFileName(file.Name) != file.Name || file.Name.StartsWith('.') || file.Name == "installed.json")
            return ErrorCodes.CloudToolUnsupported;
        if (Current is { } current && current.Sha256 == file.Sha256)
            return null;

        Directory.CreateDirectory(Folder);
        var download = Path.Combine(Folder, $".download-{Guid.NewGuid():N}");
        try
        {
            using (var response = await SendAsync(new Uri(tools, "files/" + Uri.EscapeDataString(file.Name)), cancellationToken))
            {
                if (!response.IsSuccessStatusCode)
                    return ErrorCodes.CloudUnreachable;
                await using var target = File.Create(download);
                await response.Content.CopyToAsync(target, cancellationToken);
            }

            await using (var written = File.OpenRead(download))
            {
                if (written.Length != file.Size || !Convert.ToHexStringLower(await SHA256.HashDataAsync(written, cancellationToken)).Equals(file.Sha256, StringComparison.OrdinalIgnoreCase))
                    return ErrorCodes.CloudToolInvalid;
            }

            var path = Path.Combine(Folder, file.Name);
            File.Move(download, path, overwrite: true);
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                                           UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
            }

            var installed = new Installed(manifest.Version, file.Name, file.Sha256.ToLowerInvariant());
            await File.WriteAllTextAsync(Path.Combine(Folder, "installed.json"), JsonSerializer.Serialize(installed, Json), cancellationToken);
            (_installed, _loaded) = (installed, true);

            // Earlier versions go.
            foreach (var old in Directory.EnumerateFiles(Folder).Where(f => Path.GetFileName(f) is var name && name != file.Name && name != "installed.json"))
            {
                try
                {
                    File.Delete(old);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    logger.LogDebug(e, "Could not delete {File}", old);
                }
            }

            logger.LogInformation("ultimate-audio {Version} installed", manifest.Version);
            return null;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return ErrorCodes.CloudUnreachable;
        }
        catch (UltimateCloudException e)
        {
            return e.Code;
        }
        finally
        {
            File.Delete(download);
        }
    }

    /// <summary>The audio hash Ultimate Cloud deduplicates by.</summary>
    public async Task<string> HashAsync(string path, CancellationToken cancellationToken) =>
        (await RunAsync(["hash", path], cancellationToken))["audioHash"]!.GetValue<string>();

    /// <summary>
    /// A copy without tags at <paramref name="output"/>, as it is uploaded: its audio hash, and the SHA-1 and size of the
    /// whole copy. Fails (<c>hash_changed</c>) when the file cannot be uploaded as it is.
    /// </summary>
    public async Task<(string AudioHash, string Sha1, long Size)> StripAsync(string path, string output, CancellationToken cancellationToken)
    {
        var result = await RunAsync(["strip", path, output], cancellationToken);
        return (result["audioHash"]!.GetValue<string>(), result["sha1"]!.GetValue<string>(), result["size"]!.GetValue<long>());
    }

    /// <summary>Writes tags into a file (a tag-free file downloaded from Ultimate Cloud).</summary>
    public async Task TagAsync(string path, UltimateAudioMetadata metadata, CancellationToken cancellationToken)
    {
        var file = Path.Combine(Path.GetTempPath(), $"arcanist-tags-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(file, JsonSerializer.Serialize(metadata, Json), cancellationToken);
        try
        {
            await RunAsync(["tag", path, file], cancellationToken);
        }
        finally
        {
            File.Delete(file);
        }
    }

    /// <summary>Runs a command and returns its answer.</summary>
    /// <exception cref="UltimateCloudException"><c>cloud_tool_missing</c>, or the tool's own error code (prefixed <c>tool:</c>) with its message.</exception>
    private async Task<JsonNode> RunAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        if (OpenSource.IsOn(configuration))
            throw new UltimateCloudException(ErrorCodes.ClosedSourceDisabled);
        if (Current is not { } installed)
            throw new UltimateCloudException(ErrorCodes.CloudToolMissing);

        var start = new ProcessStartInfo(Path.Combine(Folder, installed.Name))
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
        };
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);

        using var process = Process.Start(start) ?? throw new UltimateCloudException(ErrorCodes.CloudToolMissing);
        string output;
        try
        {
            var reading = process.StandardOutput.ReadToEndAsync(cancellationToken);
            _ = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            output = await reading;
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
            }

            throw;
        }

        JsonNode? answer;
        try
        {
            answer = JsonNode.Parse(output.Trim().Split('\n')[^1]);
        }
        catch (JsonException)
        {
            answer = null;
        }

        if (process.ExitCode == 0 && answer is not null)
            return answer;
        throw new UltimateCloudException("tool:" + (answer?["error"]?.GetValue<string>() ?? "failed"), answer?["message"]?.GetValue<string>());
    }

    private Uri? ToolsUrl()
    {
        var current = options.CurrentValue;
        if (!string.IsNullOrWhiteSpace(current.ToolsUrl))
            return new Uri(current.ToolsUrl.TrimEnd('/') + "/");
        return Uri.TryCreate(current.ApiUrl, UriKind.Absolute, out var api) ? new Uri(api, "/arcanist/tools/") : null;
    }

    private async Task<HttpResponseMessage> SendAsync(Uri url, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await auth.AccessTokenAsync(cancellationToken, UltimateCloudClient.CurrentUser));
        request.Headers.Add("X-Device-Id", await auth.DeviceIdAsync(cancellationToken));
        request.Headers.Add("X-Client-App", UltimateCloudClient.ClientApp);
        return await clients.CreateClient(UltimateCloudStorage.FilesClientName).SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
    }
}
