using System.Text;
using ATL;
using SkiaSharp;

namespace Arcanist.Core.Tests.Scanning;

public sealed record FixtureTags(
    string? Title = null,
    string? Artist = null,
    string? Album = null,
    string? AlbumArtist = null,
    int? TrackNumber = null,
    int? DiscNumber = null,
    int? Year = null,
    string? Genre = null,
    string? RecordingMbid = null,
    bool WithCover = false,
    string? Lyrics = null);

/// <summary>Tiny tagged WAV files generated on the fly, so tests need no binary fixtures in the repository.</summary>
public static class AudioFixtures
{
    /// <summary>Writes a short PCM WAV whose audio content is unique per <paramref name="seed"/>, then tags it.</summary>
    public static string WriteWav(string path, int seed, FixtureTags tags)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, Wav(seed));
        Tag(path, tags);
        return path;
    }

    /// <summary>Writes <see cref="Music"/> to a file and tags it.</summary>
    public static string WriteMusic(string path, FixtureTags tags, double bpm = 120, double seconds = 15, int chordSeed = 0, int variant = 0)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, Music(bpm, seconds, chordSeed, variant: variant));
        Tag(path, tags);
        return path;
    }

    /// <summary>Rewrites the tags of an existing file, leaving its audio untouched.</summary>
    public static void Tag(string path, FixtureTags tags)
    {
        var track = new Track(path)
        {
            Title = tags.Title,
            Artist = tags.Artist,
            Album = tags.Album,
            AlbumArtist = tags.AlbumArtist,
            TrackNumber = tags.TrackNumber,
            DiscNumber = tags.DiscNumber,
            Year = tags.Year,
            Genre = tags.Genre,
        };

        if (tags.RecordingMbid is not null)
            track.AdditionalFields["MUSICBRAINZ_TRACKID"] = tags.RecordingMbid;

        if (tags.Lyrics is not null)
            track.Lyrics = [new LyricsInfo { UnsynchronizedLyrics = tags.Lyrics }];

        if (tags.WithCover)
            track.EmbeddedPictures.Add(PictureInfo.fromBinaryData(Png(seed: 1), PictureInfo.PIC_TYPE.Front));

        if (!track.Save())
            throw new InvalidOperationException($"Could not tag {path}.");
    }

    /// <summary>A small solid-color PNG; different seeds give different images.</summary>
    public static byte[] Png(int seed, int size = 16)
    {
        using var bitmap = new SKBitmap(size, size);
        bitmap.Erase(new SKColor((byte)(seed * 40), (byte)(seed * 80), (byte)(seed * 120)));
        using var image = SKImage.FromBitmap(bitmap);
        return image.Encode(SKEncodedImageFormat.Png, 100).ToArray();
    }

    /// <summary>
    /// Music-like stereo WAV for analysis tests: a sustained triad (<paramref name="chordSeed"/> picks it, so
    /// different seeds sound different) under noise bursts on every beat at <paramref name="bpm"/>.
    /// A non-zero <paramref name="variant"/> silences that many tenths of a second at the end: different bytes (and
    /// content hash), practically the same audio, like another rip or encoding of a song.
    /// </summary>
    public static byte[] Music(double bpm, double seconds, int chordSeed = 0, int sampleRate = 44100, int variant = 0)
    {
        var frames = (int)(seconds * sampleRate);
        var root = 220.0 * Math.Pow(2, chordSeed % 12 / 12.0);
        double[] chord = [root, root * Math.Pow(2, 4 / 12.0), root * Math.Pow(2, 7 / 12.0)];
        var beat = (int)(60.0 / bpm * sampleRate);
        var random = new Random(chordSeed);

        var pcm = new short[frames * 2];
        for (var i = 0; i < frames; i++)
        {
            var t = (double)i / sampleRate;
            var tone = chord.Sum(f => Math.Sin(2 * Math.PI * f * t)) / chord.Length * 0.3;
            var offset = i % beat;
            var click = offset < 600 ? (random.NextDouble() - 0.5) * (1 - offset / 600.0) * 1.2 : 0;
            pcm[2 * i] = pcm[2 * i + 1] = (short)(Math.Clamp(tone + click, -1, 1) * 32000);
        }

        if (variant > 0)
            Array.Clear(pcm, pcm.Length - Math.Min(pcm.Length, variant * sampleRate / 10 * 2), Math.Min(pcm.Length, variant * sampleRate / 10 * 2));

        return Wav(pcm, sampleRate, channels: 2);
    }

    /// <summary>
    /// A melody (harmonic notes of 0.2–0.45 s from a scale, picked by <paramref name="seed"/>, with a drum hit on
    /// each) as mono samples from <paramref name="from"/> seconds: unlike <see cref="Music"/> its fingerprint changes
    /// all the time, so where a stretch of it lies in a longer recording can be found.
    /// </summary>
    public static float[] Melody(int seed, double seconds, double from = 0, int sampleRate = 44100)
    {
        var random = new Random(seed);
        int[] scale = [0, 2, 4, 5, 7, 9, 11, 12, 14, 16];
        var notes = new List<(double Start, double Length, double Frequency)>();
        for (var t = 0.0; t < from + seconds + 1;)
        {
            var length = 0.2 + 0.25 * random.NextDouble();
            notes.Add((t, length, 440 * Math.Pow(2, (48 + scale[random.Next(scale.Length)] - 69) / 12.0)));
            t += length;
        }

        var samples = new float[(int)(seconds * sampleRate)];
        var note = 0;
        for (var i = 0; i < samples.Length; i++)
        {
            var t = from + (double)i / sampleRate;
            while (note + 1 < notes.Count && notes[note + 1].Start <= t)
                note++;
            var (start, length, frequency) = notes[note];
            var local = t - start;
            var envelope = Math.Min(local * 40, 1) * Math.Exp(-local / length * 2);
            var value = 0.0;
            for (var harmonic = 1; harmonic < 5; harmonic++)
                value += envelope * Math.Sin(2 * Math.PI * frequency * harmonic * t) / harmonic;
            if (local < 0.03)
                value += 0.6 * Math.Sin(local * 9871) * (1 - local / 0.03);
            samples[i] = (float)(0.3 * value);
        }
        return samples;
    }

    /// <summary>Quiet filtered noise, like talking in a scene: something that is not the song.</summary>
    public static float[] Noise(int seed, double seconds, int sampleRate = 44100)
    {
        var random = new Random(seed);
        var samples = new float[(int)(seconds * sampleRate)];
        var previous = 0.0;
        for (var i = 0; i < samples.Length; i++)
        {
            var slow = Math.Abs(Math.Sin(2 * Math.PI * 3 * i / sampleRate));
            previous = 0.7 * previous + 0.3 * (random.NextDouble() - 0.5);
            samples[i] = (float)(0.3 * slow * previous);
        }
        return samples;
    }

    /// <summary>A stereo 16-bit WAV of mono samples.</summary>
    public static byte[] Wav(float[] mono, int sampleRate = 44100)
    {
        var pcm = new short[mono.Length * 2];
        for (var i = 0; i < mono.Length; i++)
            pcm[2 * i] = pcm[2 * i + 1] = (short)(Math.Clamp(mono[i], -1, 1) * 32000);
        return Wav(pcm, sampleRate, channels: 2);
    }

    /// <summary>Joins WAVs made by <see cref="Music"/> (same format) into one, like a whole album ripped to one file.</summary>
    public static byte[] Concatenate(params byte[][] wavs)
    {
        // Music() writes a plain 44-byte header followed by the samples.
        var samples = wavs.SelectMany(w => w.Skip(44)).ToArray();
        return Wav(System.Runtime.InteropServices.MemoryMarshal.Cast<byte, short>(samples).ToArray(), 44100, channels: 2);
    }

    private static byte[] Wav(short[] pcm, int sampleRate, short channels)
    {
        var dataSize = pcm.Length * 2;
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.ASCII);
        writer.Write("RIFF"u8);
        writer.Write(36 + dataSize);
        writer.Write("WAVE"u8);
        writer.Write("fmt "u8);
        writer.Write(16);
        writer.Write((short)1);
        writer.Write(channels);
        writer.Write(sampleRate);
        writer.Write(sampleRate * channels * 2);
        writer.Write((short)(channels * 2));
        writer.Write((short)16);
        writer.Write("data"u8);
        writer.Write(dataSize);
        foreach (var sample in pcm)
            writer.Write(sample);
        writer.Flush();
        return stream.ToArray();
    }

    private static byte[] Wav(int seed)
    {
        const int sampleRate = 8000;
        const short bitsPerSample = 16;
        const short channels = 1;
        var samples = sampleRate / 4; // a quarter of a second
        var dataSize = samples * channels * bitsPerSample / 8;

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.ASCII);
        writer.Write("RIFF"u8);
        writer.Write(36 + dataSize);
        writer.Write("WAVE"u8);
        writer.Write("fmt "u8);
        writer.Write(16);
        writer.Write((short)1);
        writer.Write(channels);
        writer.Write(sampleRate);
        writer.Write(sampleRate * channels * bitsPerSample / 8);
        writer.Write((short)(channels * bitsPerSample / 8));
        writer.Write(bitsPerSample);
        writer.Write("data"u8);
        writer.Write(dataSize);

        var random = new Random(seed);
        for (var i = 0; i < samples; i++)
            writer.Write((short)random.Next(short.MinValue, short.MaxValue));

        writer.Flush();
        return stream.ToArray();
    }
}
