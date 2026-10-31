namespace Arcanist.Core.Data.Entities;

/// <summary>
/// A Jukebox Jam: a live queue (its <see cref="Playlist"/>, of kind <see cref="PlaylistKind.Jam"/>) that several people
/// add songs to while it plays on one player of the host's: a client of theirs (a web player, Ultimate Desktop), the
/// server's audio or a DLNA renderer. Guests without an account can join with its code.
/// </summary>
public sealed class JamSession : Entity
{
    public Guid PlaylistId { get; init; }

    public Playlist Playlist { get; init; } = null!;

    public Guid HostUserId { get; init; }

    /// <summary>The public code of its link (random, base64url). Nobody can join once it ended.</summary>
    public required string Code { get; init; }

    public JamTarget Target { get; set; }

    /// <summary>The DLNA renderer it plays on (<c>server</c> for the server's audio).</summary>
    public string? RendererId { get; set; }

    /// <summary>The tab or device of the host that plays it (<see cref="JamTarget.Client"/>).</summary>
    public string? HostDeviceId { get; set; }

    /// <summary>Which client that is (JSON: kind, player id, name and version), to show it.</summary>
    public string? HostClient { get; set; }

    /// <summary>When the playing client last reported; it is shown as gone after a while without reports.</summary>
    public DateTime? HostSeenAt { get; set; }

    /// <summary>Whether its queue is ordered by the votes of those taking part.</summary>
    public bool Voting { get; set; }

    /// <summary>Guests without an account may join (also needs the server's setting).</summary>
    public bool AllowGuests { get; set; }

    /// <summary>Libraries guests search and add from (JSON array); null: every library the host can listen to.</summary>
    public string? GuestLibraryIds { get; set; }

    public JamDj DjKind { get; set; }

    public string? DjName { get; set; }

    public string? DjMessage { get; set; }

    public DateTime? DjSince { get; set; }

    /// <summary>The entry playing now.</summary>
    public Guid? CurrentEntryId { get; set; }

    /// <summary>What plays now when it is not on the server (JSON): a song from the host's own library.</summary>
    public string? ExternalNowPlaying { get; set; }

    public bool Paused { get; set; }

    public int PositionMs { get; set; }

    /// <summary>When <see cref="PositionMs"/> was reported, to move it on while it plays.</summary>
    public DateTime? PositionAt { get; set; }

    /// <summary>An entry the host chose to play next, whatever the votes say.</summary>
    public Guid? PinnedEntryId { get; set; }

    /// <summary>Anyone taking part may replace the song playing with one of theirs (the host and its managers always can).</summary>
    public bool AllowReplace { get; set; }

    /// <summary>When someone last replaced the song playing: others wait a while before doing it again.</summary>
    public DateTime? LastReplacedAt { get; set; }

    public DateTime StartedAt { get; init; }

    public DateTime LastActivityAt { get; set; }

    public DateTime? EndedAt { get; set; }

    public List<JamGuest> Guests { get; } = [];
}

/// <summary>Where a Jukebox Jam plays.</summary>
public enum JamTarget
{
    /// <summary>A client of the host's: a web player tab or an app such as Ultimate Desktop.</summary>
    Client,

    /// <summary>The server's own audio output.</summary>
    Server,

    /// <summary>A DLNA renderer.</summary>
    Dlna,
}

public enum JamDj
{
    None,

    /// <summary>Ultimate's AI DJ chooses what plays (the host is Ultimate Desktop).</summary>
    UltimateAiDj,
}

/// <summary>Someone without an account taking part in a Jukebox Jam, by the name they gave.</summary>
public sealed class JamGuest : Entity
{
    public Guid JamId { get; init; }

    public JamSession Jam { get; init; } = null!;

    public required string Name { get; set; }

    /// <summary>SHA-256 of the guest's token, which only their browser has.</summary>
    public required string TokenHash { get; init; }

    public DateTime JoinedAt { get; init; }

    public DateTime LastSeenAt { get; set; }

    /// <summary>The host removed them: their token no longer works.</summary>
    public bool Banned { get; set; }
}

/// <summary>A vote for a song waiting in a Jukebox Jam, by a user or a guest.</summary>
public sealed class JamVote : Entity
{
    public Guid JamId { get; init; }

    public JamSession Jam { get; init; } = null!;

    /// <summary>The playlist entry voted for. Not a foreign key: entries are renumbered and removed.</summary>
    public Guid EntryId { get; init; }

    public Guid? UserId { get; init; }

    public Guid? GuestId { get; init; }

    public DateTime CastAt { get; init; }

    /// <summary>The vote a song gets from whoever adds it: it does not make them wait for their next one.</summary>
    public bool FromAdding { get; init; }
}
