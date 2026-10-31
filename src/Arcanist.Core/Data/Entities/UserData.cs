namespace Arcanist.Core.Data.Entities;

// What users do with the music: ratings, plays and playlists. Everything hangs off Track, never off files, so it
// survives files being moved, re-tagged or replaced by better copies; merging two tracks moves it to the survivor.

/// <summary>How users rate tracks, chosen for the whole server. Part of the API contract.</summary>
public enum RatingMode
{
    /// <summary>One to five stars. Five stars is a StarTrack.</summary>
    Stars,

    /// <summary>The StarTrack alone: a track is one or not. It is stored as five stars, so switching modes keeps it.</summary>
    Star,
}

/// <summary>A user's rating of a track, 1–5. A starred track is rated 5.</summary>
public sealed class TrackRating
{
    public Guid UserId { get; init; }

    public User User { get; init; } = null!;

    public Guid TrackId { get; set; }

    public Track Track { get; init; } = null!;

    public int Rating { get; set; }

    public DateTime RatedAt { get; set; }
}

/// <summary>A track a user listened to (or skipped), as reported by a player.</summary>
public sealed class PlayEvent : Entity
{
    public Guid UserId { get; init; }

    public User User { get; init; } = null!;

    /// <summary>Null once the track left the catalog: the time listened still counts in statistics.</summary>
    public Guid? TrackId { get; set; }

    public Track? Track { get; init; }

    /// <summary>When playback started.</summary>
    public DateTime PlayedAt { get; init; }

    public int PlayedMs { get; init; }

    /// <summary>Long enough to count as a play: half the track or four minutes, and at least 30 seconds.</summary>
    public bool Counted { get; init; }

    /// <summary>Stopped within the first 30 seconds of a longer track.</summary>
    public bool Skipped { get; init; }

    /// <summary>The player that reported it, as it names itself.</summary>
    public string? Client { get; init; }

    /// <summary>The id the player gave the play (mStream's statistics), so a report sent again is recognized.</summary>
    public string? ExternalId { get; init; }

    /// <summary>How the play was queued, as the player says (manual, shuffle, autodj…).</summary>
    public string? Source { get; init; }
}

/// <summary>Running totals of a user's plays of a track, kept with each play so lists stay fast.</summary>
public sealed class TrackPlayStats
{
    public Guid UserId { get; init; }

    public User User { get; init; } = null!;

    public Guid TrackId { get; set; }

    public Track Track { get; init; } = null!;

    public int PlayCount { get; set; }

    public int SkipCount { get; set; }

    public DateTime? LastPlayedAt { get; set; }
}

public sealed class Playlist : Entity
{
    public Guid OwnerId { get; init; }

    public User Owner { get; init; } = null!;

    public required string Name { get; set; }

    public required string NormalizedName { get; set; }

    public string? Description { get; set; }

    /// <summary>Normalized words the full-text index searches: the name and description.</summary>
    public string? SearchText { get; set; }

    /// <summary>Every user of the server can see it (and copy it); only its owner and collaborators change it.</summary>
    public bool IsPublic { get; set; }

    /// <summary>A normal playlist, or the queue of a Jukebox Jam (<see cref="JamSession"/>).</summary>
    public PlaylistKind Kind { get; set; }

    /// <summary>
    /// The token of its public link (anyone with it sees the playlist and hears previews), or null without one. Kept as
    /// is, not hashed: it identifies the playlist and its managers copy it again; revoking it removes it.
    /// </summary>
    public string? LinkToken { get; set; }

    public DateTime? LinkCreatedAt { get; set; }

    public DateTime CreatedAt { get; init; }

    public DateTime UpdatedAt { get; set; }

    public List<PlaylistEntry> Entries { get; } = [];

    public List<PlaylistCollaborator> Collaborators { get; } = [];
}

/// <summary>A track in a playlist. The same track can appear more than once.</summary>
public sealed class PlaylistEntry : Entity
{
    public Guid PlaylistId { get; init; }

    public Playlist Playlist { get; init; } = null!;

    public Guid TrackId { get; set; }

    public Track Track { get; init; } = null!;

    /// <summary>0-based order within the playlist, kept contiguous.</summary>
    public int Position { get; set; }

    public DateTime AddedAt { get; init; }

    /// <summary>Who added it (the owner or a collaborator). Not a foreign key: the user may have been deleted since.</summary>
    public Guid? AddedByUserId { get; init; }

    /// <summary>The guest without an account who added it to a Jukebox Jam (<see cref="JamGuest"/>). Not a foreign key.</summary>
    public Guid? AddedByGuestId { get; init; }

    /// <summary>Added to a Jukebox Jam by the host's DJ (Ultimate's AI DJ), not asked for by anyone.</summary>
    public bool AddedByDj { get; init; }

    /// <summary>In a Jukebox Jam, when it started playing: it is no longer waiting in the queue.</summary>
    public DateTime? PlayedAt { get; set; }
}

public enum PlaylistKind
{
    Normal,

    /// <summary>The queue of a Jukebox Jam.</summary>
    Jam,
}

/// <summary>What a user the playlist is shared with can do.</summary>
public enum PlaylistMemberRole
{
    /// <summary>See it and play it.</summary>
    Listen,

    /// <summary>Also add, remove and reorder its tracks.</summary>
    Edit,

    /// <summary>Also rename it, share it with others and manage its public link.</summary>
    Manage,
}

/// <summary>A user a playlist is shared with (the name comes from when every one of them could edit it).</summary>
public sealed class PlaylistCollaborator
{
    public Guid PlaylistId { get; init; }

    public Playlist Playlist { get; init; } = null!;

    public Guid UserId { get; init; }

    public User User { get; init; } = null!;

    public PlaylistMemberRole Role { get; set; } = PlaylistMemberRole.Edit;

    public DateTime AddedAt { get; init; }
}
