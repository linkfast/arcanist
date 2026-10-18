using System.Text.Json;
using Arcanist.Core.Application.Ai;
using Arcanist.Core.Application.Catalog;
using Arcanist.Core.Application.Listening;

namespace Arcanist.Core.Ai;

/// <summary>The blocks a client can ask for with <c>include</c>; all but <see cref="Stats"/> by default.</summary>
[Flags]
public enum AnalysisBlocks
{
    None = 0,
    Beats = 1,
    Onsets = 2,
    Energy = 4,
    Vocals = 8,
    Sections = 16,
    Silences = 32,
    Cues = 64,
    Loudness = 128,
    Key = 256,
    Stats = 512,
    Default = Beats | Onsets | Energy | Vocals | Sections | Silences | Cues | Loudness | Key,
}

/// <summary>Reads the native analyzer's JSON and turns it into the API's <see cref="TrackAnalysisResult"/>.</summary>
public static class DjJson
{
    /// <summary>Parses <c>include</c> (comma-separated names); unknown names are a validation error.</summary>
    public static AnalysisBlocks Parse(string? include)
    {
        if (string.IsNullOrWhiteSpace(include))
            return AnalysisBlocks.Default;
        var blocks = AnalysisBlocks.None;
        foreach (var name in include.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            blocks |= name.ToLowerInvariant() switch
            {
                "beats" => AnalysisBlocks.Beats,
                "onsets" => AnalysisBlocks.Onsets,
                "energy" => AnalysisBlocks.Energy,
                "vocals" => AnalysisBlocks.Vocals,
                "sections" => AnalysisBlocks.Sections,
                "silences" => AnalysisBlocks.Silences,
                "cues" => AnalysisBlocks.Cues,
                "loudness" => AnalysisBlocks.Loudness,
                "key" => AnalysisBlocks.Key,
                "stats" => AnalysisBlocks.Stats,
                _ => throw Application.ArcanistException.Field("include", Application.ErrorCodes.InvalidFormat),
            };
        }

        return blocks;
    }

    public static Output? Read(string? json)
    {
        if (string.IsNullOrEmpty(json))
            return null;
        try
        {
            return JsonSerializer.Deserialize<Output>(json, JsonSerializerOptions.Web);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>A ready analysis with the blocks asked for.</summary>
    public static TrackAnalysisResult Ready(Guid trackId, string contentHash, int version, DateTime analyzedAt, bool stale, Output o, AnalysisBlocks blocks,
        TrackListeningStats? stats)
    {
        bool Has(AnalysisBlocks block) => (blocks & block) != 0;
        var key = o.Key is { } k ? MusicalKeys.Create(k.PitchClass, k.Minor, k.Confidence) : null;
        return new TrackAnalysisResult(
            trackId,
            TrackAnalysisStatus.Ready,
            stale,
            null,
            null,
            contentHash,
            version,
            analyzedAt,
            o.DurationSeconds,
            o.MusicStart,
            o.MusicEnd,
            o.Tempo is { } t
                ? new AnalysisTempo(t.Bpm, t.Confidence, t.Stable, t.BeatsPerBar, t.FirstBeat, t.FirstDownbeat,
                    Has(AnalysisBlocks.Beats) ? t.Beats : null, Has(AnalysisBlocks.Beats) ? t.Downbeats : null)
                : null,
            Has(AnalysisBlocks.Beats) ? o.Phrases.Select(p => new AnalysisPhrase(p.Start, p.Bars)).ToList() : null,
            Has(AnalysisBlocks.Loudness) ? new AnalysisLoudness(o.Loudness.IntegratedLufs, o.Loudness.Range, o.Loudness.TruePeakDb) : null,
            Has(AnalysisBlocks.Key) && key is not null
                ? new AnalysisKey(key.Notation.TrimEnd('m'), key.Mode == KeyMode.Minor ? "minor" : "major", key.Camelot, key.Confidence)
                : null,
            Has(AnalysisBlocks.Energy) ? new AnalysisEnergy(o.Energy.StepSeconds, o.Energy.Values) : null,
            Has(AnalysisBlocks.Vocals) ? new AnalysisVocals(o.Vocals.StepSeconds, o.Vocals.Presence) : null,
            Has(AnalysisBlocks.Onsets) ? new AnalysisOnsets(o.Onsets.Times, o.Onsets.Strengths) : null,
            Has(AnalysisBlocks.Silences) ? o.Silences.Select(s => new AnalysisSilence(s.Start, s.End, s.Kind)).ToList() : null,
            new AnalysisFades(o.Fades.FadeInEnd, o.Fades.FadeOutStart),
            Has(AnalysisBlocks.Sections) ? o.Sections.Select(s => new AnalysisSection(s.Start, s.End, s.Label, s.Name, s.Energy, s.Trend, s.Occurrence)).ToList() : null,
            o.Highlight is { } h ? new AnalysisHighlight(h.Start, h.End, h.Confidence) : null,
            new AnalysisMixPoints(o.MixPoints.IntroEnd, o.MixPoints.OutroStart, o.MixPoints.MixIn, o.MixPoints.MixOut),
            Has(AnalysisBlocks.Cues) ? o.ScratchCues.Select(c => new AnalysisScratchCue(c.T, c.Duration, c.Kind, c.Isolation, c.Strength)).ToList() : null,
            Has(AnalysisBlocks.Stats) ? stats : null);
    }

    public static TrackAnalysisResult Pending(Guid trackId, int position, TrackListeningStats? stats) =>
        Empty(trackId, TrackAnalysisStatus.Pending, position, null) with { Stats = stats };

    public static TrackAnalysisResult Failed(Guid trackId, string contentHash, int version, DateTime analyzedAt, TrackListeningStats? stats) =>
        Empty(trackId, TrackAnalysisStatus.Failed, null, Application.ErrorCodes.AnalysisUnavailable) with
        {
            ContentHash = contentHash, AnalysisVersion = version, AnalyzedAt = analyzedAt, Stats = stats,
        };

    private static TrackAnalysisResult Empty(Guid trackId, TrackAnalysisStatus status, int? position, string? error) =>
        new(trackId, status, false, position, error, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null,
            null, null, null, null);

    // The native analyzer's JSON (native/arcanist-audio/src/dj/mod.rs).
    public sealed record Output(
        int AnalyzerVersion,
        double DurationSeconds,
        double MusicStart,
        double MusicEnd,
        TempoJson? Tempo,
        List<PhraseJson> Phrases,
        LoudnessJson Loudness,
        KeyJson? Key,
        CurveJson Energy,
        VocalsJson Vocals,
        OnsetsJson Onsets,
        List<SilenceJson> Silences,
        FadesJson Fades,
        List<SectionJson> Sections,
        HighlightJson? Highlight,
        MixPointsJson MixPoints,
        List<CueJson> ScratchCues);

    public sealed record TempoJson(double Bpm, double Confidence, bool Stable, int BeatsPerBar, double? FirstBeat, double? FirstDownbeat, string? Beats, string? Downbeats);

    public sealed record PhraseJson(double Start, int Bars);

    public sealed record LoudnessJson(double IntegratedLufs, double Range, double TruePeakDb);

    public sealed record KeyJson(int PitchClass, bool Minor, double Confidence);

    public sealed record CurveJson(double StepSeconds, string Values);

    public sealed record VocalsJson(double StepSeconds, string Presence);

    public sealed record OnsetsJson(string Times, string Strengths);

    public sealed record SilenceJson(double Start, double End, string Kind);

    public sealed record FadesJson(double? FadeInEnd, double? FadeOutStart);

    public sealed record SectionJson(double Start, double End, string Label, string Name, double Energy, string Trend, int Occurrence);

    public sealed record HighlightJson(double Start, double End, double Confidence);

    public sealed record MixPointsJson(double IntroEnd, double OutroStart, List<double> MixIn, List<double> MixOut);

    public sealed record CueJson(double T, double Duration, string Kind, double Isolation, double Strength);
}
