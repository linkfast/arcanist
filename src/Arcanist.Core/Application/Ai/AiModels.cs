using Arcanist.Core.Application.Listening;

namespace Arcanist.Core.Application.Ai;

// Types in this file are part of the public API contracts (GraphQL schema and OpenAPI document).

public enum TrackAnalysisStatus
{
    /// <summary>Analyzed: the blocks asked for are here (with <c>stale</c>, from an older analyzer, being redone).</summary>
    Ready,

    /// <summary>Not analyzed yet: <c>position</c> says how many are ahead of it.</summary>
    Pending,

    /// <summary>It could not be analyzed (<c>errorCode</c>); it is not tried again until the analyzer changes.</summary>
    Failed,
}

/// <summary>
/// The analysis of a song for DJ software. Times are seconds (three decimals); dense series are base64: float32
/// little endian for times, one byte (0–255) per value for levels. Blocks not asked for with <c>include</c> are null.
/// </summary>
/// <param name="AnalysisVersion">The analyzer that made it; a newer one redoes it in the background (meanwhile <c>stale</c>).</param>
/// <param name="Position">While pending: how many songs are ahead of it in line.</param>
/// <param name="MusicStart">Where the music starts, after a leading silence.</param>
/// <param name="MusicEnd">Where it ends, before a trailing silence.</param>
/// <param name="Phrases">Groups of 8, 16 or 32 bars from section starts (with <c>beats</c>).</param>
/// <param name="Stats">How the user listens to it (with <c>include=stats</c>).</param>
public sealed record TrackAnalysisResult(
    Guid TrackId,
    TrackAnalysisStatus Status,
    bool Stale,
    int? Position,
    string? ErrorCode,
    string? ContentHash,
    int? AnalysisVersion,
    DateTime? AnalyzedAt,
    double? DurationSeconds,
    double? MusicStart,
    double? MusicEnd,
    AnalysisTempo? Tempo,
    IReadOnlyList<AnalysisPhrase>? Phrases,
    AnalysisLoudness? Loudness,
    AnalysisKey? Key,
    AnalysisEnergy? Energy,
    AnalysisVocals? Vocals,
    AnalysisOnsets? Onsets,
    IReadOnlyList<AnalysisSilence>? Silences,
    AnalysisFades? Fades,
    IReadOnlyList<AnalysisSection>? Sections,
    AnalysisHighlight? Highlight,
    AnalysisMixPoints? MixPoints,
    IReadOnlyList<AnalysisScratchCue>? ScratchCues,
    TrackListeningStats? Stats);

/// <param name="Stable">The tempo holds (within 1%) through the song: the grid is <c>firstBeat</c> plus multiples of 60/<c>bpm</c>, and <c>beats</c> is not sent.</param>
/// <param name="Beats">Every beat (base64 float32 LE), when the tempo is not stable and <c>include</c> has <c>beats</c>.</param>
/// <param name="Downbeats">The first beat of every bar, likewise.</param>
public sealed record AnalysisTempo(
    double Bpm,
    double Confidence,
    bool Stable,
    int BeatsPerBar,
    double? FirstBeat,
    double? FirstDownbeat,
    string? Beats,
    string? Downbeats);

public sealed record AnalysisPhrase(double Start, int Bars);

/// <param name="IntegratedLufs">EBU R128 integrated loudness.</param>
/// <param name="Range">Loudness range (LU).</param>
public sealed record AnalysisLoudness(double IntegratedLufs, double Range, double TruePeakDb);

/// <param name="Tonic">C, C#, D… (sharps and flats as musicians write them: Eb, F#, Bb).</param>
/// <param name="Mode"><c>major</c> or <c>minor</c>.</param>
public sealed record AnalysisKey(string Tonic, string Mode, string Camelot, double Confidence);

/// <param name="Values">One byte per step: 0 is −60 dBFS or less, 255 full scale (base64).</param>
public sealed record AnalysisEnergy(double StepSeconds, string Values);

/// <param name="Presence">One byte per step, 0–255: how likely a voice is (base64). A light estimate, not a separation.</param>
public sealed record AnalysisVocals(double StepSeconds, string Presence);

/// <param name="Times">base64 float32 LE.</param>
/// <param name="Strengths">One byte per onset, 0–255 (base64).</param>
public sealed record AnalysisOnsets(string Times, string Strengths);

/// <param name="Kind"><c>leading</c>, <c>gap</c> or <c>trailing</c>.</param>
public sealed record AnalysisSilence(double Start, double End, string Kind);

/// <param name="FadeInEnd">Where a fade in reaches the song's level; null when it starts at full level.</param>
/// <param name="FadeOutStart">Where a fade out begins; null when it ends at full level.</param>
public sealed record AnalysisFades(double? FadeInEnd, double? FadeOutStart);

/// <param name="Label">A, B, C…: sections alike share their letter.</param>
/// <param name="Name"><c>intro</c>, <c>verse</c>, <c>build</c>, <c>chorus</c>, <c>drop</c>, <c>break</c>, <c>bridge</c>, <c>outro</c> or <c>section</c>.</param>
/// <param name="Energy">0–1, against the loudest section.</param>
/// <param name="Trend"><c>rising</c>, <c>falling</c> or <c>steady</c>.</param>
/// <param name="Occurrence">Which time this label comes (1 the first).</param>
public sealed record AnalysisSection(double Start, double End, string Label, string Name, double Energy, string Trend, int Occurrence);

/// <summary>The most representative part (the loudest chorus or drop).</summary>
public sealed record AnalysisHighlight(double Start, double End, double Confidence);

/// <param name="MixIn">Phrase starts without voice early in the song, to mix into.</param>
/// <param name="MixOut">Phrase starts without voice late in the song, to mix out from.</param>
public sealed record AnalysisMixPoints(double IntroEnd, double OutroStart, IReadOnlyList<double> MixIn, IReadOnlyList<double> MixOut);

/// <param name="T">The attack itself (no pre-roll).</param>
/// <param name="Kind"><c>vocal</c>, <c>stab</c> or <c>drum</c>.</param>
/// <param name="Isolation">0–1: how much quieter the 80 ms before it are.</param>
public sealed record AnalysisScratchCue(double T, double Duration, string Kind, double Isolation, double Strength);

/// <param name="NextCursor">Pass as <c>after</c> for the next page; null on the last.</param>
public sealed record TrackAnalysisPage(IReadOnlyList<TrackAnalysisResult> Items, string? NextCursor);

/// <summary>A song's analysis finished (subscription <c>trackAnalysisReady</c>).</summary>
public sealed record TrackAnalysisReady(Guid TrackId, int AnalysisVersion);

/// <summary>What the AI extension's analysis is doing.</summary>
public enum AiActivity
{
    /// <summary>The extension is off.</summary>
    Disabled,

    /// <summary>The native analyzer is not available on this platform.</summary>
    Unavailable,

    Idle,

    /// <summary>Waiting for scans or streams to end (as the settings ask).</summary>
    Paused,

    Running,
}

/// <summary>The analysis' progress over the library (local songs, plus those asked for).</summary>
/// <param name="Pending">Songs waiting: the local ones not analyzed yet and those clients asked for.</param>
public sealed record TrackAnalysisProgress(int AnalysisVersion, int Analyzed, int Pending, int Failed);

/// <param name="Threads">Songs analyzed at once (each takes a core while it lasts).</param>
/// <param name="PauseWhileScanning">Wait while libraries are scanned.</param>
/// <param name="PauseWhileStreaming">Wait while anyone is listening (streams, DLNA, server audio).</param>
/// <param name="OnDemandSources">Analyze songs of S3, other servers and Ultimate Cloud when a client asks (they are downloaded once).</param>
/// <param name="ShareServerStats">Clients may read the server's listening statistics per song (everyone's together, nobody's in particular).</param>
public sealed record AiSettingsModel(int Threads, bool PauseWhileScanning, bool PauseWhileStreaming, bool OnDemandSources, bool ShareServerStats);

public sealed record AiStatus(
    bool Enabled,
    AiActivity Activity,
    TrackAnalysisProgress Progress,
    double SongsPerMinute,
    DateTime? LastAnalyzedAt,
    string? Error,
    AiSettingsModel Settings,
    int MaxThreads);

public sealed record SetAiSettingsInput(int Threads, bool PauseWhileScanning, bool PauseWhileStreaming, bool OnDemandSources, bool ShareServerStats);
