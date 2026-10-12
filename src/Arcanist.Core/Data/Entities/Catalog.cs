namespace Arcanist.Core.Data.Entities;

// Catalog model, from logical to physical:
//   Recording – a performance, identified by MusicBrainz recording ID or AcoustID when known.
//   Track     – a Recording as it appears on an Album (disc/track number). What users browse and play.
//   MediaFile – a physical file. A Track can have several (copies, or FLAC and MP3 of the same thing),
//               so duplicates collapse into one Track instead of appearing twice.
// User data (plays, ratings, playlists) hangs off Track/Recording, never off file paths.

public sealed class Artist : Entity
{
    public required string Name { get; set; }

    public required string NormalizedName { get; set; }

    public string? SortName { get; set; }

    public Guid? MusicBrainzId { get; set; }
}

public sealed class Album : Entity
{
    public required string Title { get; set; }

    public required string NormalizedTitle { get; set; }

    public Guid AlbumArtistId { get; set; }

    public Artist AlbumArtist { get; set; } = null!;

    public int? Year { get; set; }

    public bool IsCompilation { get; set; }

    public Guid? MusicBrainzReleaseId { get; set; }

    public Guid? ArtworkId { get; set; }

    public Artwork? Artwork { get; set; }

    /// <summary>EBU R128 loudness of the whole album (its best copy of each track), LUFS, once all are analyzed.</summary>
    public double? Loudness { get; set; }

    /// <summary>Highest true peak over the album, linear.</summary>
    public double? TruePeak { get; set; }

    public List<Track> Tracks { get; } = [];
}

public sealed class Recording : Entity
{
    public Guid? MusicBrainzId { get; set; }

    public Guid? AcoustId { get; set; }

    public List<Track> Tracks { get; } = [];
}

public sealed class Track : Entity
{
    public required string Title { get; set; }

    public required string NormalizedTitle { get; set; }

    public Guid AlbumId { get; set; }

    public Album Album { get; set; } = null!;

    public Guid RecordingId { get; set; }

    public Recording Recording { get; set; } = null!;

    public int? DiscNumber { get; set; }

    public int? TrackNumber { get; set; }

    public int DurationMs { get; set; }

    /// <summary>Track-specific artwork when it differs from the album's.</summary>
    public Guid? ArtworkId { get; set; }

    public Artwork? Artwork { get; set; }

    public DateTime AddedAt { get; init; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public List<TrackArtist> Artists { get; } = [];

    public List<TrackGenre> Genres { get; } = [];

    public List<MediaFile> Files { get; } = [];
}

public enum ArtistRole
{
    Primary,
    Featured,
    Composer,
    Conductor,
    Remixer,
    Lyricist,
    Producer,
}

public sealed class TrackArtist
{
    public Guid TrackId { get; init; }

    public Track Track { get; init; } = null!;

    public Guid ArtistId { get; init; }

    public Artist Artist { get; init; } = null!;

    public ArtistRole Role { get; init; }

    /// <summary>Order in which the artist is credited.</summary>
    public int Position { get; set; }
}

public sealed class Genre : Entity
{
    public required string Name { get; set; }

    public required string NormalizedName { get; set; }
}

public sealed class TrackGenre
{
    public Guid TrackId { get; init; }

    public Track Track { get; init; } = null!;

    public Guid GenreId { get; init; }

    public Genre Genre { get; init; } = null!;
}

public sealed class MediaFile : Entity
{
    public Guid TrackId { get; set; }

    public Track Track { get; set; } = null!;

    public Guid LibrarySourceId { get; init; }

    public LibrarySource LibrarySource { get; init; } = null!;

    /// <summary>Path inside the source, '/'-separated and exactly as stored on disk (not Unicode-normalized).</summary>
    public required string RelativePath { get; set; }

    public long Size { get; set; }

    public DateTime ModifiedAt { get; set; }

    /// <summary>Hash of the audio data only (tags excluded), stable across re-tagging and moves.</summary>
    public required string ContentHash { get; set; }

    /// <summary>Container/codec as reported by the tag reader, e.g. "FLAC", "MPEG-1 Layer 3".</summary>
    public string? Format { get; set; }

    public int? BitRate { get; set; }

    public int? SampleRate { get; set; }

    public int? BitDepth { get; set; }

    public int? Channels { get; set; }

    public int DurationMs { get; set; }

    public double? ReplayGainTrackGain { get; set; }

    public double? ReplayGainTrackPeak { get; set; }

    public double? ReplayGainAlbumGain { get; set; }

    public double? ReplayGainAlbumPeak { get; set; }

    public DateTime AddedAt { get; init; } = DateTime.UtcNow;

    public DateTime ScannedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Set when a scan no longer finds the file; it is purged after a grace period.</summary>
    public DateTime? MissingSince { get; set; }

    /// <summary>
    /// Joined to its track by duplicate detection or by an administrator rather than by its tags: scans keep it on
    /// that track and do not let its tags change the track.
    /// </summary>
    public bool TrackLocked { get; set; }
}

public enum ArtworkSource
{
    Embedded,
    Folder,
    Online,
}

/// <summary>Cached image, stored once per content hash under the data directory's cache.</summary>
public sealed class Artwork : Entity
{
    public required string Hash { get; init; }

    public required string MimeType { get; init; }

    public int Width { get; init; }

    public int Height { get; init; }

    public ArtworkSource Source { get; init; }
}
