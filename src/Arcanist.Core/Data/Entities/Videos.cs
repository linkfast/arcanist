namespace Arcanist.Core.Data.Entities;

/// <summary>How a music video was linked to its song.</summary>
public enum VideoLinkMethod
{
    /// <summary>It sits next to an audio file with the same name.</summary>
    Sidecar,
    /// <summary>Its tags (or file name) name the same artist and title.</summary>
    Tags,
    /// <summary>Its soundtrack matched the song's audio.</summary>
    Fingerprint,
    /// <summary>A person linked it; scans never change it.</summary>
    Manual,
}

/// <summary>
/// A music video file in a library: a catalog entry of its own, linked to the song it shows when the library has
/// it. Found by scans like <see cref="MediaFile"/>, but never needs a <see cref="Track"/>.
/// </summary>
public sealed class MusicVideo : Entity
{
    public Guid LibrarySourceId { get; set; }

    public LibrarySource LibrarySource { get; set; } = null!;

    /// <summary>Path relative to the source's location, '/'-separated, as on disk.</summary>
    public required string RelativePath { get; set; }

    public long Size { get; set; }

    public DateTime ModifiedAt { get; set; }

    /// <summary>Hash of the file (see <c>ContentHasher</c>): the key of its alignments and converted versions.</summary>
    public required string ContentHash { get; set; }

    public required string Title { get; set; }

    public required string NormalizedTitle { get; set; }

    public string? Artist { get; set; }

    public string? NormalizedArtist { get; set; }

    /// <summary>Normalized words of the title and artist, for full-text search.</summary>
    public string? SearchText { get; set; }

    public int DurationMs { get; set; }

    public int? Width { get; set; }

    public int? Height { get; set; }

    /// <summary>Container format as ffprobe names it (<c>mov,mp4,m4a…</c>, <c>matroska,webm</c>).</summary>
    public string? Container { get; set; }

    public string? VideoCodec { get; set; }

    public string? AudioCodec { get; set; }

    /// <summary>
    /// False until ffprobe read the file: without ffmpeg a video is listed by its name, and read once ffmpeg is
    /// available.
    /// </summary>
    public bool Probed { get; set; }

    /// <summary>A live performance: linked only to a live track, and its own audio wins unless it is that recording.</summary>
    public bool IsLive { get; set; }

    /// <summary>Whether <see cref="IsLive"/> was set by a person (scans keep it).</summary>
    public bool LiveLocked { get; set; }

    /// <summary>A frame of the video, stored like album artwork.</summary>
    public Guid? ArtworkId { get; set; }

    public Artwork? Artwork { get; set; }

    /// <summary>The song shown; no foreign key (a track that disappears just unlinks the video).</summary>
    public Guid? RecordingId { get; set; }

    /// <summary>The track whose audio plays with it, among the recording's.</summary>
    public Guid? TrackId { get; set; }

    public VideoLinkMethod? LinkMethod { get; set; }

    /// <summary>Linked or unlinked by a person: scans and the linker leave it alone.</summary>
    public bool LinkLocked { get; set; }

    public DateTime AddedAt { get; set; }

    public DateTime ScannedAt { get; set; }

    /// <summary>When a scan stopped finding the file; purged after the retention, like media files.</summary>
    public DateTime? MissingSince { get; set; }
}

public enum VideoAlignmentState
{
    /// <summary>Enough of the video plays the track: its audio replaces the video's where they match.</summary>
    Aligned,
    /// <summary>They do not match (another performance, another song): the video keeps its own audio.</summary>
    NoMatch,
    /// <summary>Not aligned yet because ffmpeg is needed to read one of them.</summary>
    NeedsFfmpeg,
    /// <summary>One of them could not be read. Not retried until the aligner version changes.</summary>
    Failed,
}

/// <summary>
/// How the soundtrack of a video lines up with an audio file, by content: the same video and audio aligned once
/// per aligner version, whatever copies they have.
/// </summary>
public sealed class VideoAlignment : Entity
{
    public required string VideoHash { get; init; }

    public required string AudioHash { get; init; }

    public int AlignerVersion { get; set; }

    public VideoAlignmentState State { get; set; }

    public string? Error { get; set; }

    /// <summary>Share of the video that plays the track (0–1).</summary>
    public double Coverage { get; set; }

    /// <summary>Gain for the video's own audio, so it sounds as loud as the track around the switches.</summary>
    public double VideoGainDb { get; set; }

    /// <summary>The segments as the native aligner returns them (JSON array).</summary>
    public string? Segments { get; set; }

    public DateTime AlignedAt { get; set; }
}

/// <summary>
/// Lyrics timed on a video's own audio, by content: for videos whose audio is not their song's (a live video,
/// another edit), where the song's timing cannot be carried over. Worked out by a timing extension (Lyrics Sync).
/// </summary>
public sealed class VideoLyricsTiming : Entity
{
    public required string VideoHash { get; init; }

    /// <summary>The song's words it was timed for (<c>LyricsTiming.HashOf</c>): other words need timing again.</summary>
    public required string PlainHash { get; set; }

    public required string Lrc { get; set; }

    public DateTime AlignedAt { get; set; }
}

/// <summary>A video someone wants aligned before the background order would reach it.</summary>
public sealed class VideoAlignmentRequest : Entity
{
    public Guid VideoId { get; init; }

    /// <summary>Higher goes first; each request gets the highest so far.</summary>
    public long Priority { get; set; }

    public DateTime RequestedAt { get; set; }
}
