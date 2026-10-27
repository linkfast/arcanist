using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Arcanist.Core.Videos;

/// <summary>A stretch of a video and whose audio plays during it.</summary>
/// <param name="TrackStartMs">Where the track is at <paramref name="VideoStartMs"/> (track segments).</param>
/// <param name="Rate">Track seconds per video second (track segments).</param>
public sealed record AlignedSegment(int VideoStartMs, int VideoEndMs, bool FromTrack, int TrackStartMs, double Rate, double? Similarity);

/// <summary>
/// The ffmpeg audio graph of a music video with its song: each stretch takes the track's audio (trimmed at its
/// offset, sped up when the video runs faster) or the video's own (with the gain that matches its loudness to the
/// track's), every stretch padded to its exact length so the audio never drifts from the picture, and crossfades of
/// a quarter of a second at each switch.
/// </summary>
public static class VideoMix
{
    public const double CrossfadeSeconds = 0.25;

    public static IReadOnlyList<AlignedSegment> Parse(string? json)
    {
        if (string.IsNullOrEmpty(json))
            return [];
        using var document = JsonDocument.Parse(json);
        return document.RootElement.EnumerateArray().Select(s => new AlignedSegment(
            s.GetProperty("videoStartMs").GetInt32(),
            s.GetProperty("videoEndMs").GetInt32(),
            s.GetProperty("source").GetString() == "track",
            s.TryGetProperty("trackStartMs", out var start) ? start.GetInt32() : 0,
            s.TryGetProperty("rate", out var rate) ? rate.GetDouble() : 1.0,
            s.TryGetProperty("similarity", out var similarity) ? similarity.GetDouble() : null)).ToList();
    }

    /// <summary>
    /// The <c>-filter_complex</c> that builds the mixed audio as <c>[mix]</c>, from the video as input 0 and the
    /// track as input 1. <paramref name="trackOffsetMs"/> is where the song starts in its file (a cue sheet track).
    /// </summary>
    public static string Filter(IReadOnlyList<AlignedSegment> segments, double videoGainDb, int videoDurationMs, int trackOffsetMs, bool videoHasAudio)
    {
        var graph = new StringBuilder();
        var count = segments.Count;
        for (var k = 0; k < count; k++)
        {
            var segment = segments[k];
            // Every stretch but the last runs on under the crossfade into the next.
            var extension = k + 1 < count ? CrossfadeSeconds : 0;
            var start = segment.VideoStartMs / 1000.0;
            var length = (segment.VideoEndMs - segment.VideoStartMs) / 1000.0 + extension;
            if (segment.FromTrack)
            {
                var from = (trackOffsetMs + segment.TrackStartMs) / 1000.0;
                var span = length * segment.Rate;
                graph.Append($"[1:a]atrim=start={S(from)}:end={S(from + span)},asetpts=PTS-STARTPTS");
                if (Math.Abs(segment.Rate - 1) > 1e-6)
                    graph.Append($",atempo={S(segment.Rate)}");
            }
            else if (videoHasAudio)
            {
                graph.Append($"[0:a]atrim=start={S(start)}:end={S(start + length)},asetpts=PTS-STARTPTS");
                if (Math.Abs(videoGainDb) > 0.05)
                    graph.Append($",volume={S(videoGainDb)}dB");
            }
            else
            {
                graph.Append("anullsrc=r=48000:cl=stereo");
            }
            graph.Append($",aresample=48000,aformat=sample_fmts=fltp:channel_layouts=stereo,apad,atrim=end={S(length)},asetpts=PTS-STARTPTS[s{k}];");
        }

        if (count == 1)
            return graph.Append("[s0]anull[mix]").ToString();
        var previous = "s0";
        for (var k = 1; k < count; k++)
        {
            var output = k + 1 == count ? "mix" : $"x{k}";
            graph.Append($"[{previous}][s{k}]acrossfade=d={S(CrossfadeSeconds)}:c1=tri:c2=tri[{output}]");
            if (k + 1 < count)
                graph.Append(';');
            previous = output;
        }
        return graph.ToString();
    }

    /// <summary>The video's own audio for its whole length, as one segment (no alignment, or it did not match).</summary>
    public static IReadOnlyList<AlignedSegment> Whole(int videoDurationMs) => [new AlignedSegment(0, videoDurationMs, false, 0, 1, null)];

    private static string S(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
}
