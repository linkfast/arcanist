using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Arcanist.Core.Media;

/// <summary>What a video file holds, as ffprobe (or ffmpeg) reports it.</summary>
public sealed record VideoInfo(
    int DurationMs,
    int? Width,
    int? Height,
    string? Container,
    string? VideoCodec,
    string? AudioCodec,
    string? Title,
    string? Artist,
    string? Comment,
    double? FrameRate,
    long? BitRate);

/// <summary>
/// Reads music videos with the ffmpeg Arcanist uses: their streams and tags (with the <c>ffprobe</c> next to it, or
/// ffmpeg's own report when there is none) and a frame for the thumbnail.
/// </summary>
public sealed partial class VideoProbe(FfmpegLocator ffmpeg, ILogger<VideoProbe> logger)
{
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(1);

    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken) => await ffmpeg.GetAsync(cancellationToken) is not null;

    /// <summary>The streams and tags of the file at <paramref name="path"/>; null when it cannot be read.</summary>
    public async Task<VideoInfo?> ProbeAsync(string path, CancellationToken cancellationToken)
    {
        if (await ffmpeg.GetAsync(cancellationToken) is not { } installation)
            return null;

        var ffprobe = Path.Combine(Path.GetDirectoryName(installation.Path)!, OperatingSystem.IsWindows() ? "ffprobe.exe" : "ffprobe");
        if (File.Exists(ffprobe))
        {
            var (code, output, _) = await RunAsync(ffprobe, ["-v", "error", "-print_format", "json", "-show_format", "-show_streams", path], cancellationToken);
            if (code == 0 && Parse(output) is { } info)
                return info;
        }

        // ffmpeg reports the same on stderr when given no output (and exits with an error).
        var (_, _, errors) = await RunAsync(installation.Path, ["-hide_banner", "-i", path], cancellationToken);
        return ParseReport(errors);
    }

    /// <summary>A JPEG frame at <paramref name="atMs"/>, at most 640 pixels wide; null when it cannot be taken.</summary>
    public async Task<byte[]?> FrameAsync(string path, int atMs, CancellationToken cancellationToken)
    {
        if (await ffmpeg.GetAsync(cancellationToken) is not { } installation)
            return null;
        var start = new ProcessStartInfo(installation.Path)
        {
            ArgumentList =
            {
                "-hide_banner", "-loglevel", "error", "-ss", (atMs / 1000.0).ToString("0.###", CultureInfo.InvariantCulture), "-i", path,
                "-frames:v", "1", "-vf", "scale='min(640,iw)':-2", "-f", "image2", "-c:v", "mjpeg", "-q:v", "3", "pipe:1",
            },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var process = Process.Start(start)!;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Timeout);
        try
        {
            var errors = process.StandardError.ReadToEndAsync(timeout.Token);
            using var memory = new MemoryStream();
            await process.StandardOutput.BaseStream.CopyToAsync(memory, timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            if (process.ExitCode != 0 || memory.Length == 0)
            {
                logger.LogDebug("No frame from {Path}: {Error}", path, await errors);
                return null;
            }
            return memory.ToArray();
        }
        catch (OperationCanceledException)
        {
            Kill(process);
            cancellationToken.ThrowIfCancellationRequested();
            return null;
        }
    }

    internal static VideoInfo? Parse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (!root.TryGetProperty("format", out var format))
                return null;

            JsonElement? video = null, audio = null;
            if (root.TryGetProperty("streams", out var streams))
            {
                foreach (var stream in streams.EnumerateArray())
                {
                    var type = stream.TryGetProperty("codec_type", out var t) ? t.GetString() : null;
                    // Cover art in an audio file is a "video" stream too: it is a still picture.
                    var still = stream.TryGetProperty("disposition", out var disposition)
                                && disposition.TryGetProperty("attached_pic", out var picture) && picture.GetInt32() == 1;
                    if (type == "video" && !still)
                        video ??= stream;
                    else if (type == "audio")
                        audio ??= stream;
                }
            }
            if (video is null)
                return null;

            var tags = format.TryGetProperty("tags", out var tagElement) ? tagElement : (JsonElement?)null;
            return new VideoInfo(
                DurationMs: (int)Math.Round(Number(format, "duration") * 1000),
                Width: Integer(video.Value, "width"),
                Height: Integer(video.Value, "height"),
                Container: Text(format, "format_name"),
                VideoCodec: Text(video.Value, "codec_name"),
                AudioCodec: audio is { } a ? Text(a, "codec_name") : null,
                Title: Tag(tags, "title"),
                Artist: Tag(tags, "artist") ?? Tag(tags, "album_artist"),
                Comment: Tag(tags, "comment") ?? Tag(tags, "description"),
                FrameRate: Rate(Text(video.Value, "avg_frame_rate") ?? Text(video.Value, "r_frame_rate")),
                BitRate: (long?)Number(format, "bit_rate") is > 0 and var rate ? rate : null);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Reads ffmpeg's report (<c>ffmpeg -i file</c>) when there is no ffprobe.</summary>
    internal static VideoInfo? ParseReport(string report)
    {
        var video = VideoLine().Match(report);
        if (!video.Success || video.Value.Contains("(attached pic)", StringComparison.Ordinal))
            return null;
        var duration = DurationLine().Match(report);
        var audio = AudioLine().Match(report);
        var container = InputLine().Match(report);
        var size = SizeOf().Match(video.Value);
        var fps = FpsOf().Match(video.Value);
        return new VideoInfo(
            DurationMs: duration.Success
                ? (int)TimeSpan.ParseExact(duration.Groups[1].Value, @"hh\:mm\:ss\.ff", CultureInfo.InvariantCulture).TotalMilliseconds
                : 0,
            Width: size.Success ? int.Parse(size.Groups[1].Value, CultureInfo.InvariantCulture) : null,
            Height: size.Success ? int.Parse(size.Groups[2].Value, CultureInfo.InvariantCulture) : null,
            Container: container.Success ? container.Groups[1].Value : null,
            VideoCodec: video.Groups[1].Value,
            AudioCodec: audio.Success ? audio.Groups[1].Value : null,
            Title: MetadataValue(report, "title"),
            Artist: MetadataValue(report, "artist"),
            Comment: MetadataValue(report, "comment"),
            FrameRate: fps.Success ? double.Parse(fps.Groups[1].Value, CultureInfo.InvariantCulture) : null,
            BitRate: null);
    }

    private static string? MetadataValue(string report, string key)
    {
        var match = Regex.Match(report, $@"^\s+{key}\s*:\s*(.+)$", RegexOptions.Multiline | RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value.Trim() : null;
    }

    private static string? Tag(JsonElement? tags, string key)
    {
        if (tags is not { } element)
            return null;
        foreach (var property in element.EnumerateObject())
        {
            if (property.Name.Equals(key, StringComparison.OrdinalIgnoreCase) && property.Value.GetString() is { Length: > 0 } value)
                return value.Trim();
        }
        return null;
    }

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static int? Integer(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetInt32() : null;

    private static double Number(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && double.TryParse(value.ValueKind == JsonValueKind.String ? value.GetString() : value.GetRawText(),
            NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
            ? number
            : 0;

    private static double? Rate(string? fraction)
    {
        if (fraction?.Split('/') is not [var numerator, var denominator]
            || !double.TryParse(numerator, NumberStyles.Float, CultureInfo.InvariantCulture, out var n)
            || !double.TryParse(denominator, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) || d == 0)
            return null;
        return n / d;
    }

    private static async Task<(int Code, string Output, string Errors)> RunAsync(string program, IEnumerable<string> arguments, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo(program)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Timeout);
        try
        {
            var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var errors = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            return (process.ExitCode, await output, await errors);
        }
        catch (OperationCanceledException)
        {
            Kill(process);
            cancellationToken.ThrowIfCancellationRequested();
            return (-1, "", "timed out");
        }
    }

    private static void Kill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
        }
    }

    [GeneratedRegex(@"Stream #\d+:\d+(?:\[\w+\])?(?:\(\w+\))?: Video: (\w+)[^\n]*")]
    private static partial Regex VideoLine();

    [GeneratedRegex(@"Stream #\d+:\d+(?:\[\w+\])?(?:\(\w+\))?: Audio: (\w+)")]
    private static partial Regex AudioLine();

    [GeneratedRegex(@"Duration: (\d{2}:\d{2}:\d{2}\.\d{2})")]
    private static partial Regex DurationLine();

    [GeneratedRegex(@"Input #0, ([^,\n]+(?:,[^,\n ]+)*), from")]
    private static partial Regex InputLine();

    [GeneratedRegex(@", (\d{2,5})x(\d{2,5})")]
    private static partial Regex SizeOf();

    [GeneratedRegex(@", ([\d.]+) fps")]
    private static partial Regex FpsOf();
}
