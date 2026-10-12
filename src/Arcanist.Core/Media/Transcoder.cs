using System.Diagnostics;
using Microsoft.Extensions.Options;

namespace Arcanist.Core.Media;

/// <summary>Formats audio can be transcoded to. Part of the API contract.</summary>
public enum TranscodeFormat
{
    /// <summary>Opus in Ogg: the best quality for its size.</summary>
    Opus,
    /// <summary>MP3: plays everywhere.</summary>
    Mp3,
    /// <summary>AAC in ADTS: native on Apple devices.</summary>
    Aac,
}

/// <param name="Encoder">The ffmpeg encoder that must be available.</param>
/// <param name="BitRate">Default bit rate, kbit/s.</param>
public sealed record TranscodeProfile(
    TranscodeFormat Format,
    string Encoder,
    string Container,
    string ContentType,
    int BitRate,
    int MinBitRate,
    int MaxBitRate)
{
    public static readonly IReadOnlyList<TranscodeProfile> All =
    [
        new(TranscodeFormat.Opus, "libopus", "ogg", "audio/ogg", 128, 32, 256),
        new(TranscodeFormat.Mp3, "libmp3lame", "mp3", "audio/mpeg", 192, 64, 320),
        new(TranscodeFormat.Aac, "aac", "adts", "audio/aac", 160, 48, 320),
    ];

    public static TranscodeProfile For(TranscodeFormat format) => All.Single(p => p.Format == format);

    /// <summary>The requested bit rate within the profile's range, or its default.</summary>
    public int Clamp(int? bitRate) => Math.Clamp(bitRate ?? BitRate, MinBitRate, MaxBitRate);
}

/// <summary>Bound from <c>Arcanist:Transcoding</c>.</summary>
public sealed class TranscodingOptions
{
    public const string Section = "Arcanist:Transcoding";

    /// <summary>Transcodes running at once; each keeps a core busy for a fraction of real time.</summary>
    public int MaxConcurrent { get; set; } = Math.Max(2, Environment.ProcessorCount);
}

/// <summary>Runs ffmpeg to convert audio for playback, streaming its output. Limits how many run at once.</summary>
public sealed class Transcoder(FfmpegLocator locator, IOptions<TranscodingOptions> options, ILogger<Transcoder> logger)
{
    private readonly SemaphoreSlim _slots = new(options.Value.MaxConcurrent, options.Value.MaxConcurrent);

    /// <summary>Formats the available ffmpeg can produce (empty without ffmpeg).</summary>
    public IReadOnlyList<TranscodeFormat> Formats(FfmpegInstallation? ffmpeg) =>
        ffmpeg is null ? [] : TranscodeProfile.All.Where(p => ffmpeg.Encoders.Contains(p.Encoder)).Select(p => p.Format).ToList();

    /// <summary>
    /// Starts converting. <paramref name="localPath"/> lets ffmpeg open and seek the file directly; otherwise the
    /// audio is piped from <paramref name="openInput"/>. Null when no slot frees up in time or ffmpeg is missing.
    /// </summary>
    public async Task<TranscodeSession?> StartAsync(
        string? localPath,
        Func<Task<Stream>> openInput,
        TranscodeProfile profile,
        int bitRate,
        double offsetSeconds,
        CancellationToken cancellationToken)
    {
        var ffmpeg = await locator.GetAsync(cancellationToken);
        if (ffmpeg is null || !ffmpeg.Encoders.Contains(profile.Encoder))
            return null;

        if (!await _slots.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken))
            return null;

        try
        {
            var start = new ProcessStartInfo(ffmpeg.Path)
            {
                RedirectStandardInput = localPath is null,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            var arguments = start.ArgumentList;
            arguments.Add("-hide_banner");
            arguments.Add("-loglevel");
            arguments.Add("error");
            // With a file input, stdin is not ffmpeg's to read.
            if (localPath is not null)
                arguments.Add("-nostdin");

            // Before -i: fast seeking in a file; on a pipe ffmpeg decodes up to the offset.
            if (offsetSeconds > 0)
            {
                arguments.Add("-ss");
                arguments.Add(offsetSeconds.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
            }

            arguments.Add("-i");
            arguments.Add(localPath ?? "pipe:0");
            foreach (var argument in new[] { "-map", "0:a:0", "-vn", "-map_metadata", "-1", "-ac", "2", "-c:a", profile.Encoder, "-b:a", $"{bitRate}k", "-f", profile.Container, "pipe:1" })
                arguments.Add(argument);

            var process = Process.Start(start)!;
            Task feeding = Task.CompletedTask;
            if (localPath is null)
            {
                var input = await openInput();
                feeding = Task.Run(async () =>
                {
                    try
                    {
                        await using (input)
                            await input.CopyToAsync(process.StandardInput.BaseStream, CancellationToken.None);
                    }
                    catch (IOException)
                    {
                        // ffmpeg stopped reading: the client went away or the audio ended early.
                    }
                    finally
                    {
                        try
                        {
                            process.StandardInput.Close();
                        }
                        catch (IOException)
                        {
                        }
                    }
                }, CancellationToken.None);
            }

            // Drained so a chatty ffmpeg never blocks on a full pipe; the first line explains failures.
            var errors = process.StandardError.ReadToEndAsync(CancellationToken.None);
            return new TranscodeSession(process, feeding, errors, _slots, logger);
        }
        catch
        {
            _slots.Release();
            throw;
        }
    }
}

/// <summary>A running conversion: read <see cref="Output"/>, then dispose to stop ffmpeg and free the slot.</summary>
public sealed class TranscodeSession(Process process, Task feeding, Task<string> errors, SemaphoreSlim slots, ILogger logger) : IAsyncDisposable
{
    private int _disposed;

    public Stream Output => process.StandardOutput.BaseStream;

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            await feeding;
            if (process.ExitCode is not (0 or -1 or 137 or 255) && (await errors).Split('\n').FirstOrDefault() is { Length: > 0 } error)
                logger.LogDebug("ffmpeg ended with {ExitCode}: {Error}", process.ExitCode, error);
        }
        catch (InvalidOperationException)
        {
        }
        finally
        {
            process.Dispose();
            slots.Release();
        }
    }
}
