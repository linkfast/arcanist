using System.Runtime.InteropServices;
using System.Text.Json;

namespace Arcanist.Core.Analysis;

/// <summary>The outcome of a DJ analysis: the native library's JSON (see <c>native/arcanist-audio/src/dj</c>) or why it failed.</summary>
public sealed record DjOutcome(AnalyzerStatus Status, string? Json, string? Error)
{
    public static DjOutcome Unavailable { get; } = new(AnalyzerStatus.Unavailable, null, "The DJ analysis is not available on this platform.");
}

/// <summary>
/// The analysis for DJ software (beat grid, sections, energy, voice, cues…) of the native library. Synchronous and
/// CPU-bound (about a hundredth of the audio's duration): run it on background workers, never on request threads.
/// </summary>
public static class DjAudioAnalyzer
{
    public static bool IsAvailable => NativeAudio.HasDjAnalysis;

    /// <summary>Version of the DJ analysis; stored results from another version are recomputed.</summary>
    public static int Version => IsAvailable ? (int)NativeAudio.DjVersion() : 0;

    /// <summary>Analyzes an encoded, seekable stream: all of it, or from <paramref name="startMs"/> to <paramref name="endMs"/>.</summary>
    public static unsafe DjOutcome Analyze(Stream stream, string? extension, int? startMs = null, int? endMs = null)
    {
        if (!IsAvailable)
            return DjOutcome.Unavailable;

        var handle = GCHandle.Alloc(stream);
        try
        {
            var source = new NativeAudio.Source
            {
                Context = GCHandle.ToIntPtr(handle),
                Read = &NativeAudio.ReadCallback,
                Seek = &NativeAudio.SeekCallback,
                Length = stream.CanSeek ? stream.Length : -1,
            };
            var (start, end) = endMs is { } until ? ((ulong)Math.Max(0, startMs ?? 0), (ulong)until) : (0UL, 0UL);
            var status = NativeAudio.DjAnalyze(&source, extension?.TrimStart('.'), start, end, out var json);
            return Outcome(status, NativeAudio.TakeString(json));
        }
        finally
        {
            handle.Free();
        }
    }

    /// <summary>Starts analyzing PCM decoded elsewhere (ffmpeg).</summary>
    public static DjPcmAnalysis? BeginPcm(int sampleRate, int channels)
    {
        if (!IsAvailable)
            return null;
        var analyzer = NativeAudio.DjNew((uint)sampleRate, (uint)channels);
        return analyzer == 0 ? null : new DjPcmAnalysis(analyzer);
    }

    internal static DjOutcome Outcome(int status, string json) => status switch
    {
        NativeAudio.StatusOk => new(AnalyzerStatus.Ok, json, null),
        NativeAudio.StatusUnsupported => new(AnalyzerStatus.Unsupported, null, ErrorOf(json)),
        _ => new(AnalyzerStatus.Invalid, null, ErrorOf(json)),
    };

    private static string? ErrorOf(string json)
    {
        try
        {
            return JsonDocument.Parse(json).RootElement.GetProperty("error").GetString();
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return json;
        }
    }
}

/// <summary>Native DJ analysis fed with interleaved float samples; finish it or dispose it.</summary>
public sealed unsafe class DjPcmAnalysis : IDisposable
{
    private nint _analyzer;

    internal DjPcmAnalysis(nint analyzer) => _analyzer = analyzer;

    public bool Push(ReadOnlySpan<float> samples)
    {
        ObjectDisposedException.ThrowIf(_analyzer == 0, this);
        fixed (float* pointer = samples)
            return NativeAudio.DjPush(_analyzer, pointer, (nuint)samples.Length) == NativeAudio.StatusOk;
    }

    public DjOutcome Finish()
    {
        ObjectDisposedException.ThrowIf(_analyzer == 0, this);
        var analyzer = _analyzer;
        _analyzer = 0;
        var status = NativeAudio.DjFinish(analyzer, out var json);
        return DjAudioAnalyzer.Outcome(status, NativeAudio.TakeString(json));
    }

    public void Dispose()
    {
        if (_analyzer != 0)
        {
            NativeAudio.DjFree(_analyzer);
            _analyzer = 0;
        }
    }
}
