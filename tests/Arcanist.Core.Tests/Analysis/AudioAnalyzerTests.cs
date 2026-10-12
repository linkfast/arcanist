using System.Runtime.InteropServices;
using Arcanist.Core.Analysis;
using Arcanist.Core.Tests.Scanning;

namespace Arcanist.Core.Tests.Analysis;

/// <summary>The .NET side of the native analysis library: marshalling, callbacks and error handling.</summary>
public sealed class AudioAnalyzerTests
{
    [Fact]
    public void Native_library_is_built_and_loaded()
    {
        Assert.True(AudioAnalyzer.IsAvailable, NativeAudio.LoadError);
        Assert.True(AudioAnalyzer.Version >= 1);
    }

    [Fact]
    public void Analyzes_an_encoded_stream_through_the_callbacks()
    {
        using var stream = new MemoryStream(AudioFixtures.Music(bpm: 120, seconds: 20));

        var outcome = AudioAnalyzer.Analyze(stream, ".wav");

        Assert.Equal(AnalyzerStatus.Ok, outcome.Status);
        var analysis = outcome.Analysis!;
        Assert.Equal(20_000, analysis.DurationMs);
        Assert.Equal(44100, analysis.SampleRate);
        Assert.Equal(2, analysis.Channels);
        Assert.InRange(analysis.Loudness!.Integrated, -30, -5);
        Assert.InRange(analysis.Tempo!.Bpm, 119, 121);
        Assert.Equal(1000, Convert.FromBase64String(analysis.Waveform!).Length);
        Assert.NotEmpty(analysis.Fingerprint!.Compressed);
        Assert.NotNull(analysis.Key);
    }

    [Fact]
    public void Only_the_requested_parts_are_computed()
    {
        using var stream = new MemoryStream(AudioFixtures.Music(bpm: 100, seconds: 10));

        var analysis = AudioAnalyzer.Analyze(stream, "wav", AnalysisParts.Loudness).Analysis!;

        Assert.NotNull(analysis.Loudness);
        Assert.Null(analysis.Fingerprint);
        Assert.Null(analysis.Waveform);
        Assert.Null(analysis.Tempo);
    }

    [Fact]
    public void Unknown_data_is_reported_as_unsupported_so_ffmpeg_can_try()
    {
        using var stream = new MemoryStream(Enumerable.Range(0, 64 * 1024).Select(i => (byte)(i * 31 % 251)).ToArray());

        var outcome = AudioAnalyzer.Analyze(stream, "xyz");

        Assert.Equal(AnalyzerStatus.Unsupported, outcome.Status);
        Assert.NotNull(outcome.Error);
    }

    [Fact]
    public void A_failing_stream_does_not_crash_the_process()
    {
        using var stream = new FailingStream(AudioFixtures.Music(bpm: 120, seconds: 10), failAfter: 50_000);

        var outcome = AudioAnalyzer.Analyze(stream, "wav");

        Assert.NotEqual(AnalyzerStatus.Ok, outcome.Status);
    }

    [Fact]
    public void Pcm_from_another_decoder_gives_the_same_kind_of_result()
    {
        var wav = AudioFixtures.Music(bpm: 120, seconds: 20);
        var samples = MemoryMarshal.Cast<byte, short>(wav.AsSpan(44)).ToArray().Select(s => s / 32768f).ToArray();

        using var pcm = AudioAnalyzer.BeginPcm(44100, 2)!;
        foreach (var chunk in samples.Chunk(8192))
            Assert.True(pcm.Push(chunk));
        var outcome = pcm.Finish();

        using var encoded = new MemoryStream(wav);
        var direct = AudioAnalyzer.Analyze(encoded, "wav").Analysis!;
        Assert.Equal(AnalyzerStatus.Ok, outcome.Status);
        Assert.Equal(direct.DurationMs, outcome.Analysis!.DurationMs);
        Assert.Equal(direct.Loudness!.Integrated, outcome.Analysis.Loudness!.Integrated, 2);
        Assert.Equal(direct.Fingerprint!.Raw, outcome.Analysis.Fingerprint!.Raw);
    }

    [Fact]
    public void Fingerprints_tell_the_same_audio_from_different_audio()
    {
        static uint[] Fingerprint(byte[] wav)
        {
            using var stream = new MemoryStream(wav);
            return AudioAnalyzer.DecodeRawFingerprint(AudioAnalyzer.Analyze(stream, "wav", AnalysisParts.Fingerprint).Analysis!.Fingerprint!.Raw);
        }

        var song = Fingerprint(AudioFixtures.Music(bpm: 120, seconds: 30, chordSeed: 1));
        var same = Fingerprint(AudioFixtures.Music(bpm: 120, seconds: 30, chordSeed: 1));
        var other = Fingerprint(AudioFixtures.Music(bpm: 95, seconds: 30, chordSeed: 6));

        Assert.Equal(1.0, AudioAnalyzer.Compare(song, same), 3);
        var otherSimilarity = AudioAnalyzer.Compare(song, other);
        Assert.True(otherSimilarity < 0.6, $"similarity {otherSimilarity}");
    }

    /// <summary>Throws once more than <c>failAfter</c> bytes have been read, like a disconnected share.</summary>
    private sealed class FailingStream(byte[] data, int failAfter) : MemoryStream(data)
    {
        public override int Read(Span<byte> buffer)
        {
            if (Position > failAfter)
                throw new IOException("The network name is no longer available.");
            return base.Read(buffer);
        }
    }
}
