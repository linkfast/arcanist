namespace Arcanist.Core.Application.Analysis;

// Types in this file are part of the public API contracts (GraphQL schema and OpenAPI document).

public enum AnalysisActivity
{
    /// <summary>Turned off in the settings, or the server is not set up yet.</summary>
    Disabled,
    /// <summary>The analysis library is not available for this platform or build.</summary>
    Unavailable,
    /// <summary>Everything is analyzed.</summary>
    Idle,
    /// <summary>Paused while a library scan runs.</summary>
    WaitingForScan,
    Running,
}

/// <summary>Progress of the background audio analysis (loudness, fingerprints, waveform, tempo and key).</summary>
/// <param name="Pending">Distinct pieces of audio still to analyze (copies of the same audio count once).</param>
/// <param name="NeedsFfmpeg">Audio in formats only ffmpeg can decode, waiting for it to be available.</param>
/// <param name="Failed">Corrupt or empty audio that could not be analyzed.</param>
/// <param name="FilesPerMinute">Recent analysis speed.</param>
/// <param name="Error">Why analysis is unavailable, if it is.</param>
public sealed record AnalysisProgress(
    AnalysisActivity Activity,
    bool Enabled,
    int Pending,
    int Analyzed,
    int NeedsFfmpeg,
    int Failed,
    double FilesPerMinute,
    DateTime? LastAnalyzedAt,
    string? Error);
