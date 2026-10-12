using System.Diagnostics;
using System.Runtime.InteropServices;
using Arcanist.Core.Analysis;

namespace Arcanist.Core.Media;

/// <summary>
/// Decodes formats the native decoder does not read (Opus, WMA, APE, DSD…) with ffmpeg, feeding the PCM to the
/// native analyzer. The audio goes through ffmpeg's standard input, so it works for any library storage.
/// </summary>
public static class FfmpegDecoder
{
    private const int SampleRate = 48000;
    private const int Channels = 2;

    /// <summary>Synchronous and CPU-bound, like <see cref="AudioAnalyzer.Analyze"/>.</summary>
    public static AnalysisOutcome Analyze(string ffmpeg, Stream input, CancellationToken cancellationToken)
    {
        using var pcm = AudioAnalyzer.BeginPcm(SampleRate, Channels);
        if (pcm is null)
            return AnalysisOutcome.Unavailable;

        var start = new ProcessStartInfo(ffmpeg)
        {
            // First audio stream only (files may embed cover art as a video stream), downmixed and resampled to
            // one fixed format the analyzer is created for.
            ArgumentList = { "-hide_banner", "-loglevel", "error", "-i", "pipe:0", "-map", "0:a:0", "-vn",
                "-f", "f32le", "-ac", $"{Channels}", "-ar", $"{SampleRate}", "pipe:1" },
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = Process.Start(start)!;
        using var kill = cancellationToken.Register(() =>
        {
            try
            {
                process.Kill();
            }
            catch (InvalidOperationException)
            {
            }
        });

        var feeding = Task.Run(() =>
        {
            try
            {
                input.CopyTo(process.StandardInput.BaseStream);
            }
            catch (IOException)
            {
                // ffmpeg stopped reading (a decoding error); its exit code tells the rest.
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
        var errors = process.StandardError.ReadToEndAsync(CancellationToken.None);

        var output = process.StandardOutput.BaseStream;
        var buffer = new byte[64 * 1024];
        var pending = 0;
        long samples = 0;
        int read;
        while ((read = output.Read(buffer, pending, buffer.Length - pending)) > 0)
        {
            var available = pending + read;
            var whole = available - available % sizeof(float);
            if (!pcm.Push(MemoryMarshal.Cast<byte, float>(buffer.AsSpan(0, whole))))
                return new AnalysisOutcome(AnalyzerStatus.Invalid, null, "the analyzer rejected the audio");
            samples += whole / sizeof(float);

            // Keep a partial sample for the next read.
            pending = available - whole;
            buffer.AsSpan(whole, pending).CopyTo(buffer);
        }

        process.WaitForExit();
        feeding.Wait(CancellationToken.None);
        cancellationToken.ThrowIfCancellationRequested();

        if (process.ExitCode != 0 && samples == 0)
            return new AnalysisOutcome(AnalyzerStatus.Invalid, null, FirstLine(errors.Result) ?? $"ffmpeg exited with {process.ExitCode}");

        return pcm.Finish();
    }

    private static string? FirstLine(string text) =>
        text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
}
