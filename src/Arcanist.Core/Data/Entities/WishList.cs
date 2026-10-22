namespace Arcanist.Core.Data.Entities;

/// <summary>
/// A song a user wants: its title and artist (and album, MusicBrainz recording or AcoustID when known). When music
/// like it enters a library the user can play, it is obtained, with that track.
/// </summary>
public sealed class WishListItem : Entity
{
    public Guid UserId { get; init; }

    public User User { get; init; } = null!;

    public required string Title { get; set; }

    public required string Artist { get; set; }

    public string? Album { get; set; }

    public Guid? RecordingMbid { get; set; }

    public Guid? AcoustId { get; set; }

    public int? DurationMs { get; set; }

    public string? Note { get; set; }

    public DateTime CreatedAt { get; init; }

    /// <summary>When the song arrived (or was found when the wish was made); null while it is wanted.</summary>
    public DateTime? ObtainedAt { get; set; }

    /// <summary>The track that fulfilled it; null when that track left the catalog since.</summary>
    public Guid? TrackId { get; set; }

    public WishMatch? MatchedBy { get; set; }

    /// <summary>The library already had it when the wish was made.</summary>
    public bool FoundWhenAdded { get; set; }

    /// <summary>The user saw that it arrived.</summary>
    public bool Seen { get; set; }

    /// <summary>The page of a streaming service it was added from (Spotify, Deezer, YouTube Music…).</summary>
    public string? SourceUrl { get; set; }

    public WishLinkService? SourceService { get; set; }

    /// <summary>Its cover: the Cover Art Archive's, or the service's when that has none. Kept in the artwork cache.</summary>
    public Guid? ArtworkId { get; set; }
}

/// <summary>The services a wish can be added from by its link. Part of the API contract.</summary>
public enum WishLinkService
{
    Spotify,
    YouTubeMusic,
    YouTube,
    Deezer,
    AppleMusic,
    MusicBrainz,
    Tidal,
    SoundCloud,
    Bandcamp,
    AmazonMusic,
    Other,
}

public enum WishMatch
{
    MusicBrainz,
    AcoustId,
    Tags,
}
