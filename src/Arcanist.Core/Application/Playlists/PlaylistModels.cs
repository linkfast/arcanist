using Arcanist.Core.Application.Catalog;

namespace Arcanist.Core.Application.Playlists;

// Types in this file are part of the public API contracts (GraphQL schema and OpenAPI document).

/// <summary>What the user can do with a playlist.</summary>
public enum PlaylistRole
{
    /// <summary>Everything: rename it, share it, invite collaborators, delete it.</summary>
    Owner,

    /// <summary>Rename it, share it with others and manage its public link; not delete it.</summary>
    Manager,

    /// <summary>Add, remove and reorder its tracks.</summary>
    Collaborator,

    /// <summary>Listen to it and copy it (shared to listen, or a public playlist of someone else).</summary>
    Viewer,
}

/// <summary>What a user a playlist is shared with can do.</summary>
public enum PlaylistShareRole
{
    /// <summary>See it and play it.</summary>
    Listen,

    /// <summary>Also add, remove and reorder its tracks.</summary>
    Edit,

    /// <summary>Also rename it, share it with others and manage its public link.</summary>
    Manage,
}

public enum PlaylistType
{
    Normal,

    /// <summary>The queue of a Jukebox Jam (see <c>jukeboxJam</c>).</summary>
    JukeboxJam,
}

/// <summary>Another user of the server, by the name shown to others.</summary>
public sealed record UserReference(Guid Id, string Name);

/// <summary>A user a playlist is shared with.</summary>
public sealed record PlaylistMember(UserReference User, PlaylistShareRole Role);

/// <param name="Url">Where it opens, relative to the server (a player's page): add the server's address to share it.</param>
public sealed record PlaylistLink(string Token, string Url, DateTime CreatedAt);

/// <param name="TrackCount">Tracks the user can play (tracks from libraries they cannot access are left out).</param>
/// <param name="ArtworkIds">Up to four different album covers from the first tracks, for a collage.</param>
/// <param name="Collaborators">The members who can edit it (or manage it); <see cref="Members"/> has them all.</param>
/// <param name="Link">Its public link, to those who manage it (null without one).</param>
/// <param name="JukeboxJamCode">The code of its Jukebox Jam while it lasts (<see cref="PlaylistType.JukeboxJam"/>).</param>
public sealed record Playlist(
    Guid Id,
    string Name,
    string? Description,
    bool IsPublic,
    UserReference Owner,
    PlaylistRole Role,
    IReadOnlyList<UserReference> Collaborators,
    int TrackCount,
    long DurationMs,
    IReadOnlyList<Guid> ArtworkIds,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    PlaylistType Type,
    IReadOnlyList<PlaylistMember> Members,
    PlaylistLink? Link,
    string? JukeboxJamCode);

public sealed record PlaylistMemberInput(Guid UserId, PlaylistShareRole Role);

/// <summary>A change to a playlist the user can see: its tracks, name or sharing, or <see cref="Deleted"/>.</summary>
public sealed record PlaylistChange(Guid PlaylistId, bool Deleted, DateTime At);

/// <summary>A track in a playlist. Entries, not tracks, are removed and moved: the same track can be there twice.</summary>
/// <param name="Position">0-based place in the whole playlist (entries the user cannot play keep their place).</param>
public sealed record PlaylistEntry(Guid Id, int Position, Track Track, DateTime AddedAt, UserReference? AddedBy);

/// <param name="NextCursor">Pass as <c>after</c> to get the next page; null on the last page.</param>
public sealed record PlaylistEntryPage(IReadOnlyList<PlaylistEntry> Items, string? NextCursor, int TotalCount);

/// <param name="TrackIds">Tracks to start with, in order.</param>
public sealed record CreatePlaylistInput(string Name, string? Description, bool IsPublic, IReadOnlyList<Guid>? TrackIds);

public sealed record UpdatePlaylistInput(string Name, string? Description, bool IsPublic);

/// <param name="Position">Where to insert them; at the end when null.</param>
public sealed record AddPlaylistTracksInput(IReadOnlyList<Guid> TrackIds, int? Position);

/// <param name="Position">Where the entries go, as a position in the playlist without them; they keep their order.</param>
public sealed record MovePlaylistEntriesInput(IReadOnlyList<Guid> EntryIds, int Position);
