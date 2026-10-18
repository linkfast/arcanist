namespace Arcanist.Core.Data.Entities;

/// <summary>
/// The DJ analysis of a piece of audio (by its content hash, so identical copies share it): beat grid, sections,
/// energy, voice, cues… as the native analyzer's JSON. Kept while a newer analyzer version redoes it.
/// </summary>
public sealed class TrackAnalysis : Entity
{
    public required string ContentHash { get; init; }

    public int AnalysisVersion { get; set; }

    public TrackAnalysisState State { get; set; }

    public string? Error { get; set; }

    public DateTime AnalyzedAt { get; set; }

    public int? DurationMs { get; set; }

    /// <summary>The analysis as the native analyzer wrote it (see <c>native/arcanist-audio/src/dj</c>).</summary>
    public string? Json { get; set; }
}

public enum TrackAnalysisState
{
    Analyzed,

    /// <summary>The built-in decoder does not read it: analyzed again when there is ffmpeg.</summary>
    NeedsFfmpeg,

    Failed,
}

/// <summary>
/// A piece of audio a client asked to have analyzed: first in line by <see cref="Priority"/>, then in order. Songs of
/// sources the background analysis does not walk (S3, other servers, Ultimate Cloud) are only analyzed this way.
/// </summary>
public sealed class TrackAnalysisRequest : Entity
{
    public required string ContentHash { get; init; }

    /// <summary>The copy to read (another present copy with the same audio does as well).</summary>
    public Guid FileId { get; set; }

    /// <summary>0 for a plain request; prioritizing gives the highest so far, putting it first.</summary>
    public int Priority { get; set; }

    public DateTime RequestedAt { get; set; }
}
