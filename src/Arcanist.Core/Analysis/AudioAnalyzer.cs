using System.Runtime.InteropServices;
using System.Text.Json;

namespace Arcanist.Core.Analysis;

/// <summary>What to compute; mirrors the <c>FLAG_*</c> constants of the native library.</summary>
[Flags]
public enum AnalysisParts : uint
{
    Loudness = 1,
    Fingerprint = 2,
    Waveform = 4,
    Tempo = 8,
    Key = 16,
    All = Loudness | Fingerprint | Waveform | Tempo | Key,
}

/// <summary>Result of the native analysis, as its JSON describes it.</summary>
public sealed record AnalyzerOutput(
    int AnalyzerVersion,
    long DurationMs,
    int SampleRate,
    int Channels,
    LoudnessResult? Loudness,
    FingerprintResult? Fingerprint,
    string? Waveform,
    TempoResult? Tempo,
    KeyResult? Key);

/// <param name="Integrated">EBU R128 integrated loudness, LUFS.</param>
/// <param name="Range">Loudness range, LU.</param>
/// <param name="TruePeak">Highest true peak, linear.</param>
/// <param name="Histogram">Block-loudness histogram, to combine tracks into an album loudness.</param>
public sealed record LoudnessResult(double Integrated, double Range, double TruePeak, string Histogram);

/// <param name="Compressed">Chromaprint's compressed form, as AcoustID expects it.</param>
/// <param name="Raw">Raw sub-fingerprints (base64 of little-endian u32), for local comparisons.</param>
public sealed record FingerprintResult(string Compressed, string Raw, long DurationMs);

public sealed record TempoResult(float Bpm, float Confidence);

/// <param name="Tonic">Pitch class: 0 = C … 11 = B.</param>
public sealed record KeyResult(int Tonic, bool Minor, float Confidence);

public enum AnalyzerStatus
{
    Ok,
    /// <summary>The native decoder does not handle this format or codec; decode it with ffmpeg instead.</summary>
    Unsupported,
    /// <summary>Corrupt or empty audio.</summary>
    Invalid,
    /// <summary>The native library is not available on this build or platform.</summary>
    Unavailable,
}

public sealed record AnalysisOutcome(AnalyzerStatus Status, AnalyzerOutput? Analysis, string? Error)
{
    public static AnalysisOutcome Unavailable { get; } = new(AnalyzerStatus.Unavailable, null, NativeAudio.LoadError);
}

/// <summary>
/// Decodes and analyzes audio with the native library. Calls are synchronous and CPU-bound (about a second per
/// four-minute track): run them on background workers, never on request threads.
/// </summary>
public static class AudioAnalyzer
{
    public static bool IsAvailable => NativeAudio.IsAvailable;

    /// <summary>Version of the analysis algorithms; stored results from another version are recomputed.</summary>
    public static int Version => IsAvailable ? (int)NativeAudio.Version() : 0;

    /// <summary>Analyzes an encoded, seekable stream (as opened from library storage).</summary>
    public static unsafe AnalysisOutcome Analyze(Stream stream, string? extension, AnalysisParts parts = AnalysisParts.All)
    {
        if (!IsAvailable)
            return AnalysisOutcome.Unavailable;

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
            var status = NativeAudio.Analyze(&source, extension?.TrimStart('.'), (uint)parts, out var json);
            return Outcome(status, NativeAudio.TakeString(json));
        }
        finally
        {
            handle.Free();
        }
    }

    /// <summary>Starts analyzing PCM decoded elsewhere (ffmpeg), for formats the native decoder does not read.</summary>
    public static PcmAnalysis? BeginPcm(int sampleRate, int channels, AnalysisParts parts = AnalysisParts.All)
    {
        if (!IsAvailable)
            return null;

        var analyzer = NativeAudio.AnalyzerNew((uint)sampleRate, (uint)channels, (uint)parts);
        return analyzer == 0 ? null : new PcmAnalysis(analyzer);
    }

    /// <summary>Similarity of two raw fingerprints (<see cref="FingerprintResult.Raw"/>): 0 unrelated, 1 identical.</summary>
    public static unsafe double Compare(ReadOnlySpan<uint> a, ReadOnlySpan<uint> b)
    {
        fixed (uint* first = a, second = b)
            return NativeAudio.Compare(first, (nuint)a.Length, second, (nuint)b.Length);
    }

    /// <summary>Decodes <see cref="FingerprintResult.Raw"/> into sub-fingerprints.</summary>
    public static uint[] DecodeRawFingerprint(string raw) => MemoryMarshal.Cast<byte, uint>(Convert.FromBase64String(raw)).ToArray();

    internal static AnalysisOutcome Outcome(int status, string json) => status switch
    {
        NativeAudio.StatusOk => new(AnalyzerStatus.Ok, JsonSerializer.Deserialize<AnalyzerOutput>(json, JsonSerializerOptions.Web), null),
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

/// <summary>Native analyzer fed with interleaved float samples; finish it or dispose it.</summary>
public sealed unsafe class PcmAnalysis : IDisposable
{
    private nint _analyzer;

    internal PcmAnalysis(nint analyzer) => _analyzer = analyzer;

    public bool Push(ReadOnlySpan<float> samples)
    {
        ObjectDisposedException.ThrowIf(_analyzer == 0, this);
        fixed (float* pointer = samples)
            return NativeAudio.AnalyzerPush(_analyzer, pointer, (nuint)samples.Length) == NativeAudio.StatusOk;
    }

    public AnalysisOutcome Finish()
    {
        ObjectDisposedException.ThrowIf(_analyzer == 0, this);
        var analyzer = _analyzer;
        _analyzer = 0;
        var status = NativeAudio.AnalyzerFinish(analyzer, out var json);
        return AudioAnalyzer.Outcome(status, NativeAudio.TakeString(json));
    }

    public void Dispose()
    {
        if (_analyzer != 0)
        {
            NativeAudio.AnalyzerFree(_analyzer);
            _analyzer = 0;
        }
    }
}
