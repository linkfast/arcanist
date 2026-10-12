using Arcanist.Core.Scanning;

namespace Arcanist.Core.Organization;

/// <summary>
/// Orders copies of the same song by quality: lossless above lossy; among lossless, bit depth then sample rate;
/// among lossy, bit rate then sample rate.
/// </summary>
internal readonly record struct AudioQuality(bool Lossless, int BitDepth, int SampleRate, int BitRate) : IComparable<AudioQuality>
{
    public static AudioQuality Of(string path, int? bitDepth, int? sampleRate, int? bitRate) =>
        new(AudioFormats.IsLossless(path), bitDepth ?? 0, sampleRate ?? 0, bitRate ?? 0);

    public int CompareTo(AudioQuality other)
    {
        if (Lossless != other.Lossless)
            return Lossless ? 1 : -1;

        return Lossless
            ? (BitDepth, SampleRate, BitRate).CompareTo((other.BitDepth, other.SampleRate, other.BitRate))
            : (BitRate, SampleRate).CompareTo((other.BitRate, other.SampleRate));
    }

    public static bool operator >(AudioQuality a, AudioQuality b) => a.CompareTo(b) > 0;

    public static bool operator <(AudioQuality a, AudioQuality b) => a.CompareTo(b) < 0;
}
