using System.Runtime.InteropServices;

namespace Arcanist.Core.Analysis;

/// <summary>The outcome of an alignment: the native library's JSON (see <c>native/arcanist-audio/src/align</c>) or why it failed.</summary>
public sealed record AlignmentOutcome(AnalyzerStatus Status, string? Json, string? Error)
{
    public static AlignmentOutcome Unavailable { get; } = new(AnalyzerStatus.Unavailable, null, "Music video alignment is not available on this platform.");
}

/// <summary>
/// Aligns the soundtrack of a music video (side <see cref="Video"/>) with a track of the library (side
/// <see cref="Track"/>), each fed from an encoded stream or from PCM decoded elsewhere. Synchronous and CPU-bound
/// (about a second per pair): run it on background workers.
/// </summary>
public sealed unsafe class AudioAligner : IDisposable
{
    public const int Video = 0;
    public const int Track = 1;

    private nint _aligner;

    private AudioAligner(nint aligner) => _aligner = aligner;

    public static bool IsAvailable => NativeAudio.HasAlignment;

    /// <summary>Version of the alignment; stored alignments from another version are recomputed.</summary>
    public static int Version => IsAvailable ? (int)NativeAudio.AlignVersion() : 0;

    public static AudioAligner? Create() => IsAvailable && NativeAudio.AlignNew() is var aligner && aligner != 0 ? new AudioAligner(aligner) : null;

    /// <summary>
    /// Decodes an encoded, seekable stream into a side: all of it, or from <paramref name="startMs"/> to
    /// <paramref name="endMs"/>. <see cref="AnalyzerStatus.Unsupported"/> means ffmpeg must decode it.
    /// </summary>
    public AnalyzerStatus Decode(int side, Stream stream, string? extension, int? startMs = null, int? endMs = null)
    {
        ObjectDisposedException.ThrowIf(_aligner == 0, this);
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
            return NativeAudio.AlignDecode(_aligner, (uint)side, &source, extension?.TrimStart('.'), start, end) switch
            {
                NativeAudio.StatusOk => AnalyzerStatus.Ok,
                NativeAudio.StatusUnsupported => AnalyzerStatus.Unsupported,
                _ => AnalyzerStatus.Invalid,
            };
        }
        finally
        {
            handle.Free();
        }
    }

    /// <summary>Starts feeding a side with interleaved float samples decoded elsewhere.</summary>
    public bool Begin(int side, int sampleRate, int channels)
    {
        ObjectDisposedException.ThrowIf(_aligner == 0, this);
        return NativeAudio.AlignBegin(_aligner, (uint)side, (uint)sampleRate, (uint)channels) == NativeAudio.StatusOk;
    }

    public bool Push(int side, ReadOnlySpan<float> samples)
    {
        ObjectDisposedException.ThrowIf(_aligner == 0, this);
        fixed (float* pointer = samples)
            return NativeAudio.AlignPush(_aligner, (uint)side, pointer, (nuint)samples.Length) == NativeAudio.StatusOk;
    }

    public AlignmentOutcome Finish()
    {
        ObjectDisposedException.ThrowIf(_aligner == 0, this);
        var aligner = _aligner;
        _aligner = 0;
        var status = NativeAudio.AlignFinish(aligner, out var json);
        var text = NativeAudio.TakeString(json);
        return status == NativeAudio.StatusOk
            ? new AlignmentOutcome(AnalyzerStatus.Ok, text, null)
            : new AlignmentOutcome(AnalyzerStatus.Invalid, null, text);
    }

    public void Dispose()
    {
        if (_aligner != 0)
        {
            NativeAudio.AlignFree(_aligner);
            _aligner = 0;
        }
    }
}
