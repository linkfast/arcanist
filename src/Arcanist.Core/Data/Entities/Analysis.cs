namespace Arcanist.Core.Data.Entities;

public enum AnalysisState
{
    Analyzed,
    /// <summary>The built-in decoder cannot read the format (Opus, WMA…); it is analyzed once ffmpeg is available.</summary>
    NeedsFfmpeg,
    /// <summary>The audio is corrupt or empty. Not retried until the analyzer version changes.</summary>
    Failed,
}

/// <summary>
/// What analyzing a piece of audio found. Keyed by <see cref="MediaFile.ContentHash"/> rather than by file, so
/// copies of the same audio (in two libraries, or moved) are analyzed once and keep their results.
/// </summary>
public sealed class AudioAnalysis : Entity
{
    public required string ContentHash { get; init; }

    /// <summary>Results from an older version of the analysis library are recomputed.</summary>
    public int AnalyzerVersion { get; set; }

    public AnalysisState State { get; set; }

    public string? Error { get; set; }

    public DateTime AnalyzedAt { get; set; }

    public int DurationMs { get; set; }

    /// <summary>EBU R128 integrated loudness, LUFS.</summary>
    public double? Loudness { get; set; }

    /// <summary>Loudness range, LU.</summary>
    public double? LoudnessRange { get; set; }

    /// <summary>Highest true peak, linear (1.0 = full scale).</summary>
    public double? TruePeak { get; set; }

    /// <summary>Block-loudness histogram (see the native library), to compute album loudness without decoding again.</summary>
    public string? LoudnessHistogram { get; set; }

    public float? Bpm { get; set; }

    public float? BpmConfidence { get; set; }

    /// <summary>Pitch class of the key's tonic: 0 = C … 11 = B.</summary>
    public int? KeyTonic { get; set; }

    public bool? KeyMinor { get; set; }

    public float? KeyConfidence { get; set; }

    /// <summary>Chromaprint fingerprint of the first two minutes, compressed as AcoustID expects it.</summary>
    public string? AcoustIdFingerprint { get; set; }

    /// <summary>Raw Chromaprint sub-fingerprints (little-endian u32), for local duplicate detection.</summary>
    public byte[]? Fingerprint { get; set; }

    public int? FingerprintDurationMs { get; set; }

    /// <summary>Overview of the audio: 1000 peak amplitudes, one byte each.</summary>
    public byte[]? Waveform { get; set; }
}

public enum DuplicateStatus
{
    Open,
    Merged,
    Dismissed,
}

/// <summary>
/// Two tracks whose audio is similar but not enough to merge them automatically, for an administrator to decide.
/// Track ids are stored ordered (A before B), so a pair is proposed once; a dismissed pair is never proposed again.
/// </summary>
public sealed class DuplicateCandidate : Entity
{
    public Guid TrackAId { get; init; }

    public Guid TrackBId { get; init; }

    /// <summary>Fingerprint similarity, 0–1.</summary>
    public double Similarity { get; set; }

    public DuplicateStatus Status { get; set; }

    public DateTime DetectedAt { get; init; }

    public DateTime? ResolvedAt { get; set; }

    public Guid? ResolvedByUserId { get; set; }
}
