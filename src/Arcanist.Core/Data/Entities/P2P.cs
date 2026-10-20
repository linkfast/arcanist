namespace Arcanist.Core.Data.Entities;

public enum P2PPeerKind
{
    /// <summary>A user's device (an app) that reaches this server through the P2P network and signs in as usual.</summary>
    Device,

    /// <summary>Another Arcanist server this one shares libraries with.</summary>
    Server,
}

public enum P2PPeerSource
{
    /// <summary>Paired with an invitation (a QR code or a link).</summary>
    Pairing,

    /// <summary>A device of the linked Ultimate Cloud account (Cloud plan): trusted while it is registered there.</summary>
    UltimateCloud,
}

/// <summary>What a peer may do and how much it may use. Null limits mean none (the global ones still apply).</summary>
public sealed class P2PRules
{
    /// <summary>Downloads of originals, and copies into the other server's own libraries.</summary>
    public bool AllowDownload { get; set; }

    /// <summary>kbit/s: audio is always converted to at most this. Null sends the original when it fits.</summary>
    public int? MaxBitRate { get; set; }

    /// <summary>kbit/s for everything the peer receives.</summary>
    public int? BandwidthKbps { get; set; }

    /// <summary>Audio streams the peer may receive at once.</summary>
    public int? MaxStreams { get; set; }

    /// <summary>
    /// A server may keep what it plays from the shared libraries in its cache, to play it again (and seek in it)
    /// without asking for it: never as a download or a copy in its own libraries.
    /// </summary>
    public bool AllowCache { get; set; } = true;

    /// <summary>The plays of a server's users count here, under their names on that server.</summary>
    public bool CountPlays { get; set; }
}

/// <summary>
/// A node allowed through the P2P network: a paired device, or a server the libraries in
/// <see cref="Libraries"/> are shared with. Identified by its iroh node id.
/// </summary>
public sealed class P2PPeer : Entity
{
    public P2PPeerKind Kind { get; init; }

    /// <summary>The iroh node id, as lowercase hex.</summary>
    public required string NodeId { get; init; }

    public required string Name { get; set; }

    /// <summary>The user a device belongs to.</summary>
    public Guid? UserId { get; init; }

    public User? User { get; init; }

    /// <summary>How it came to be trusted.</summary>
    public P2PPeerSource Source { get; set; }

    public P2PRules Rules { get; set; } = new();

    public DateTime CreatedAt { get; init; }

    /// <summary>Access ends then; null for no end.</summary>
    public DateTime? ExpiresAt { get; set; }

    public DateTime? RevokedAt { get; set; }

    public DateTime? LastSeenAt { get; set; }

    public long BytesSent { get; set; }

    public long BytesReceived { get; set; }

    /// <summary>Time with at least one open connection, for the average bandwidth.</summary>
    public long ConnectedSeconds { get; set; }

    /// <summary>The most it ever received in one second.</summary>
    public long PeakBytesPerSecond { get; set; }

    /// <summary>Audio streams it started.</summary>
    public long Streams { get; set; }

    /// <summary>For a server that keeps its backups here: the folder of this server they go to.</summary>
    public string? BackupFolder { get; set; }

    /// <summary>How much its backups may take, in bytes; null for no limit.</summary>
    public long? BackupQuotaBytes { get; set; }

    public List<P2PPeerLibrary> Libraries { get; } = [];
}

/// <summary>A library shared with a server peer.</summary>
public sealed class P2PPeerLibrary
{
    public Guid PeerId { get; init; }

    public P2PPeer Peer { get; init; } = null!;

    public Guid LibraryId { get; init; }

    public Library Library { get; init; } = null!;
}

/// <summary>
/// A one-time invitation, shown as a link or QR code: to pair a device with a user, or for another server to
/// accept a share. Only a hash of its token is kept.
/// </summary>
public sealed class P2PInvitation : Entity
{
    public P2PPeerKind Kind { get; init; }

    /// <summary>What the peer will be called: the device's or the other server's name.</summary>
    public required string Name { get; init; }

    /// <summary>SHA-256 of the token, as lowercase hex.</summary>
    public required string TokenHash { get; init; }

    public Guid CreatedByUserId { get; init; }

    /// <summary>The user a device will belong to.</summary>
    public Guid? UserId { get; init; }

    public DateTime CreatedAt { get; init; }

    /// <summary>The invitation can be used until then.</summary>
    public DateTime ExpiresAt { get; init; }

    public DateTime? UsedAt { get; set; }

    public Guid? PeerId { get; set; }

    /// <summary>The rules and libraries the peer will get.</summary>
    public P2PRules Rules { get; init; } = new();

    /// <summary>When the peer's access will end; null for no end.</summary>
    public DateTime? AccessExpiresAt { get; init; }

    /// <summary>Libraries for a server, as JSON (<c>["id", …]</c>).</summary>
    public string LibraryIds { get; init; } = "[]";

    /// <summary>
    /// For an invitation to keep another server's backups (and nothing else): the folder of this server they go to.
    /// Joining with it leaves what the server had already (shared libraries, rules) as it was.
    /// </summary>
    public string? BackupFolder { get; init; }

    public long? BackupQuotaBytes { get; init; }
}

public enum P2PRemoteStatus
{
    Active,

    /// <summary>The other server revoked the share, or it expired.</summary>
    Revoked,
}

/// <summary>A server that shares libraries with this one. Its music is the library <see cref="LibraryId"/>.</summary>
public sealed class P2PRemote : Entity
{
    /// <summary>The other server's node id, as lowercase hex.</summary>
    public required string NodeId { get; init; }

    /// <summary>How to reach it: the ticket JSON from the invitation, refreshed when it reports a newer one.</summary>
    public required string Ticket { get; set; }

    /// <summary>The other server's name.</summary>
    public required string Name { get; set; }

    public Guid LibraryId { get; init; }

    public Library Library { get; init; } = null!;

    /// <summary>What the other server allows, as it last said.</summary>
    public P2PRules Rules { get; set; } = new();

    public P2PRemoteStatus Status { get; set; }

    public DateTime CreatedAt { get; init; }

    public DateTime? LastContactAt { get; set; }

    /// <summary>
    /// Plays of the shared music go to the other server when it counts them (<see cref="P2PRules.CountPlays"/>),
    /// with the user's name: an administrator here can keep them home.
    /// </summary>
    public bool SharePlays { get; set; } = true;
}

/// <summary>
/// A play of a shared song by a user of another server (a peer that shares libraries with this one counts them,
/// <see cref="P2PRules.CountPlays"/>): known by their name there, not as a user here.
/// </summary>
public sealed class RemotePlayEvent : Entity
{
    public Guid PeerId { get; init; }

    public P2PPeer Peer { get; init; } = null!;

    /// <summary>The user's name on the other server.</summary>
    public required string RemoteUser { get; init; }

    /// <summary>Null once the track left the catalog.</summary>
    public Guid? TrackId { get; set; }

    public Track? Track { get; init; }

    public DateTime PlayedAt { get; init; }

    public int PlayedMs { get; init; }

    public bool Counted { get; init; }

    public bool Skipped { get; init; }

    /// <summary>The play's id on the other server, so a report sent again is recognized.</summary>
    public required string ExternalId { get; init; }
}

public enum P2PActivityKind
{
    /// <summary>A device was paired, or a server accepted a share.</summary>
    Joined,

    /// <summary>A peer connected after being away.</summary>
    Connected,

    /// <summary>A peer started an audio stream (the detail names the track).</summary>
    Streamed,

    /// <summary>A peer downloaded an original, or copied a file.</summary>
    Downloaded,

    /// <summary>A peer was refused: too many streams, revoked, expired or unknown.</summary>
    Refused,

    /// <summary>A peer's rules or libraries changed.</summary>
    Changed,

    Revoked,
}

/// <summary>What happened in the P2P network, for the activity log.</summary>
public sealed class P2PActivity : Entity
{
    public DateTime At { get; init; }

    public P2PActivityKind Kind { get; init; }

    public Guid? PeerId { get; init; }

    /// <summary>The peer's name when it happened (it may be gone since).</summary>
    public required string PeerName { get; init; }

    /// <summary>A track title, an error code…</summary>
    public string? Detail { get; init; }
}
