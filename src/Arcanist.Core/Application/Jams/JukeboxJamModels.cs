using Arcanist.Core.Application.Catalog;

namespace Arcanist.Core.Application.Jams;

// Types in this file are part of the public API contracts (GraphQL schema and OpenAPI document).

/// <summary>Where a Jukebox Jam plays.</summary>
public enum JukeboxJamPlaysOn
{
    /// <summary>A client of the host's that implements the host API: a web player tab, Ultimate Desktop…</summary>
    Client,

    /// <summary>The server's own audio output.</summary>
    Server,

    /// <summary>A DLNA renderer of the server's network.</summary>
    Dlna,
}

public enum JukeboxJamClientKind
{
    Web,
    Desktop,
    Mobile,
    Other,
}

public enum JukeboxJamDjKind
{
    None,

    /// <summary>Ultimate's AI DJ chooses what plays (the host is Ultimate Desktop): say so to everyone.</summary>
    UltimateAiDj,
}

/// <summary>Who is asking about a Jam.</summary>
public enum JukeboxJamParticipation
{
    /// <summary>Started it: controls it and plays it.</summary>
    Host,

    /// <summary>A user who manages its playlist: controls the queue and the guests.</summary>
    Manager,

    /// <summary>A user of the server taking part.</summary>
    Member,

    /// <summary>Someone without an account, by the name they gave.</summary>
    Guest,

    /// <summary>Has the link but has not joined yet.</summary>
    Visitor,
}

public enum JukeboxJamEntryState
{
    Waiting,
    Playing,
    Played,
}

/// <summary>What a song is, to show it (anyone in the Jam sees it, with or without an account).</summary>
public sealed record JukeboxJamSong(Guid? TrackId, string Title, IReadOnlyList<string> Artists, string? Album, int DurationMs, Guid? ArtworkId,
    string? ArtworkUrl);

/// <param name="AddedBy">The name of who asked for it (a user's or a guest's); null when the DJ put it.</param>
/// <param name="Votes">Votes it has while it waits (voting Jams).</param>
/// <param name="Pinned">The host chose it to play next.</param>
public sealed record JukeboxJamEntry(
    Guid Id,
    JukeboxJamSong Song,
    JukeboxJamEntryState State,
    string? AddedBy,
    bool AddedByGuest,
    bool AddedByDj,
    bool AddedByYou,
    int Votes,
    bool Pinned,
    DateTime AddedAt,
    DateTime? PlayedAt);

/// <param name="Entry">The entry playing, when it is one of the Jam's.</param>
/// <param name="External">A song the host plays that is not on the server (from Ultimate Desktop's own library).</param>
/// <param name="PositionMs">Where it was at <see cref="At"/>; it moves on from there while not paused.</param>
public sealed record JukeboxJamNowPlaying(JukeboxJamEntry? Entry, JukeboxJamSong? External, bool Paused, int PositionMs, DateTime? At);

/// <summary>The client of the host's that plays the Jam (<see cref="JukeboxJamPlaysOn.Client"/>).</summary>
/// <param name="Online">It reported in the last minute.</param>
public sealed record JukeboxJamHostClient(JukeboxJamClientKind Kind, string? PlayerId, string? Name, string? Version, bool Online, DateTime? SeenAt);

/// <summary>The DJ that chooses what plays: show it to everyone (for Ultimate's AI DJ, as an Ultimate AI DJ session).</summary>
public sealed record JukeboxJamDjSession(JukeboxJamDjKind Kind, string? Name, string? Message, DateTime Since);

public sealed record JukeboxJamGuestInfo(Guid Id, string Name, DateTime JoinedAt, int SongsAdded, bool Removed);

/// <param name="Name">The user's or guest's name.</param>
/// <param name="NextVoteAt">When they can vote again (voting Jams); null: now.</param>
/// <param name="AddsLeft">Songs a guest can still add in these 10 minutes (null: no limit).</param>
public sealed record JukeboxJamYou(JukeboxJamParticipation Participation, string? Name, Guid? GuestId, bool CanControl, DateTime? NextVoteAt, int? AddsLeft);

/// <summary>A Jukebox Jam as the one asking sees it.</summary>
/// <param name="Url">Its link (relative to the server): the same for everyone; a QR code of it lets others join.</param>
/// <param name="RendererId">The renderer it plays on (<c>server</c> for the server's audio).</param>
/// <param name="Queue">The songs waiting, in the order they will play (by votes in voting Jams; the DJ may choose another).</param>
/// <param name="Played">The last songs played, latest first.</param>
/// <param name="Guests">Its guests, to those who control it.</param>
/// <param name="GuestLibraryIds">Libraries songs are added from, to those who control it; null: all the host's.</param>
public sealed record JukeboxJam(
    string Code,
    string Name,
    Guid PlaylistId,
    string Host,
    string Url,
    JukeboxJamPlaysOn PlaysOn,
    string? RendererId,
    JukeboxJamHostClient? HostClient,
    bool Voting,
    int VoteIntervalSeconds,
    bool AllowGuests,
    JukeboxJamDjSession? Dj,
    JukeboxJamNowPlaying NowPlaying,
    IReadOnlyList<JukeboxJamEntry> Queue,
    IReadOnlyList<JukeboxJamEntry> Played,
    JukeboxJamYou You,
    IReadOnlyList<JukeboxJamGuestInfo>? Guests,
    IReadOnlyList<Guid>? GuestLibraryIds,
    DateTime StartedAt,
    DateTime? EndedAt);

/// <param name="RendererId">For <see cref="JukeboxJamPlaysOn.Dlna"/>: the renderer (<c>server</c> also works for the server's audio).</param>
/// <param name="GuestLibraryIds">Libraries songs are added from; all the host's when null or empty.</param>
public sealed record StartJukeboxJamInput(string Name, JukeboxJamPlaysOn PlaysOn, string? RendererId, bool Voting, bool AllowGuests,
    IReadOnlyList<Guid>? GuestLibraryIds);

public sealed record UpdateJukeboxJamInput(string? Name, bool? Voting, bool? AllowGuests, IReadOnlyList<Guid>? GuestLibraryIds);

/// <summary>A guest's session: send <see cref="Token"/> with every request of theirs (<c>guestToken</c>, or <c>X-Arcanist-Guest</c> in REST).</summary>
public sealed record JukeboxJamGuestSession(string Token, Guid GuestId, string Name, JukeboxJam Jam);

public enum JukeboxJamAddSource
{
    /// <summary>Someone asked for it.</summary>
    Request,

    /// <summary>The host's DJ put it (host only): no limit and no vote.</summary>
    Dj,
}

public enum JukeboxJamAction
{
    /// <summary>The song playing ends now: the next one plays.</summary>
    Skip,
    Pause,
    Resume,

    /// <summary>Takes <see cref="ControlJukeboxJamInput.EntryId"/> out of the queue.</summary>
    Remove,

    /// <summary><see cref="ControlJukeboxJamInput.EntryId"/> plays next, whatever the votes say.</summary>
    Pin,
    Unpin,

    /// <summary><see cref="ControlJukeboxJamInput.EntryId"/> to <see cref="ControlJukeboxJamInput.Position"/> of the queue (Jams without voting).</summary>
    Move,
}

public sealed record ControlJukeboxJamInput(JukeboxJamAction Action, Guid? EntryId, int? Position);

/// <param name="DeviceId">The tab or device that plays it; reports from any other are refused (<c>jam_not_host</c>).</param>
/// <param name="PlayerId">For web players, the player's id (its manifest's).</param>
public sealed record ClaimJukeboxJamHostInput(string DeviceId, JukeboxJamClientKind Kind, string? PlayerId, string? Name, string? Version);

public enum JukeboxJamPlaybackState
{
    Playing,
    Paused,
    Stopped,
}

/// <summary>A song the host plays that is not on the server, to show it.</summary>
public sealed record JukeboxJamExternalSongInput(string Title, IReadOnlyList<string>? Artists, string? Album, int? DurationMs, string? ArtworkUrl);

/// <param name="EntryId">The entry playing (it leaves the queue); null with <see cref="External"/> or when nothing plays.</param>
public sealed record ReportJukeboxJamPlaybackInput(string DeviceId, Guid? EntryId, JukeboxJamExternalSongInput? External, JukeboxJamPlaybackState State, int PositionMs);

public sealed record SetJukeboxJamDjInput(JukeboxJamDjKind Kind, string? Name, string? Message);

/// <summary>The song the host should play next, with its stream (for the host).</summary>
public sealed record JukeboxJamNext(Guid EntryId, Track Track);

public enum JukeboxJamChangeKind
{
    Queue,
    NowPlaying,
    Settings,
    Dj,
    Host,
    Guests,
    Ended,

    /// <summary>For the host client: do <see cref="JukeboxJamChange.Command"/> (skip, pause, resume).</summary>
    Command,
}

/// <summary>Something changed in a Jam: read it again (<c>jukeboxJam</c>). Host clients also act on <see cref="Command"/>.</summary>
/// <param name="HostDeviceId">The device now playing it, after <see cref="JukeboxJamChangeKind.Host"/>: another one stops.</param>
public sealed record JukeboxJamChange(string Code, JukeboxJamChangeKind Kind, JukeboxJamAction? Command, string? HostDeviceId, DateTime At);

/// <summary>A song that can be added to a Jam.</summary>
public sealed record JukeboxJamSearchResult(JukeboxJamSong Song, bool InQueue);

/// <summary>A Jam of the user's (hosted or taking part), to find it again.</summary>
public sealed record JukeboxJamSummary(string Code, string Name, string Host, bool Hosting, JukeboxJamPlaysOn PlaysOn, int Waiting, DateTime StartedAt);
