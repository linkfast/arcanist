using Arcanist.Core.Application.Catalog;

namespace Arcanist.Core.Application.Sharing;

// Types in this file are part of the public API contracts (GraphQL schema and OpenAPI document).

/// <summary>
/// A playlist opened by its public link. Anyone sees its songs and hears 30-second previews; a signed-in user also gets
/// the songs they can play in full (<see cref="SharedPlaylistEntry.Track"/>).
/// </summary>
/// <param name="Owner">The name its owner shows to others.</param>
/// <param name="PlaylistId">Its id, to a signed-in user who can also see it in their playlists (null otherwise).</param>
public sealed record SharedPlaylist(
    string Token,
    string Name,
    string? Description,
    string Owner,
    int TrackCount,
    long DurationMs,
    IReadOnlyList<Guid> ArtworkIds,
    DateTime UpdatedAt,
    Guid? PlaylistId,
    SharedPlaylistPage Entries);

/// <param name="PreviewUrl">30 seconds of it, for anyone (relative to the server); null when there is none (no ffmpeg,
/// or the song is only on another server or in Ultimate Cloud).</param>
/// <param name="Track">The song, to a signed-in user who can play it in full; null otherwise.</param>
public sealed record SharedPlaylistEntry(
    Guid Id,
    int Position,
    string Title,
    IReadOnlyList<string> Artists,
    string? Album,
    int DurationMs,
    Guid? ArtworkId,
    string? PreviewUrl,
    Track? Track);

/// <param name="NextCursor">Pass as <c>after</c> to get the next page; null on the last page.</param>
public sealed record SharedPlaylistPage(IReadOnlyList<SharedPlaylistEntry> Items, string? NextCursor, int TotalCount);

/// <summary>What the server lets its users share outside it, and what the user asking may do.</summary>
/// <param name="CanShareLinks">The user may make public links and open Jams to guests (SHARE_LINKS) — with the settings on.</param>
/// <param name="PublicUrl">Where the server is reached from outside (<c>https://music.example.com</c>), without a trailing
/// slash: shared links (playlists, Jams) start with it. Null: they are relative to the address the client uses.</param>
/// <param name="PublicUrlSetByEnvironment">The public URL comes from the environment (Arcanist__Network__PublicUrl): the
/// panel cannot change it.</param>
/// <param name="P2pNodeId">The server's node id while its P2P network runs, null otherwise: with <see cref="PublicUrl"/>, the
/// addresses the server announces, so a player can move to them without signing in again (a device still needs to be paired
/// to come in through P2P).</param>
/// <param name="JamDeletePlaylist">A new Jukebox Jam deletes its playlist when it ends unless whoever starts it keeps it
/// (<c>saveAsPlaylist</c>): clients start their "delete the playlist when the Jam ends" choice from this.</param>
public sealed record SharingStatus(
    bool PublicLinks, bool JamGuests, int GuestAddLimit, int MaxGuests, bool CanShareLinks, string? PublicUrl, bool PublicUrlSetByEnvironment,
    string? P2pNodeId = null, bool JamDeletePlaylist = true);

/// <param name="PublicUrl">The server's public URL (http or https; a bare host is taken as https). Null keeps the one
/// there is; empty takes it off.</param>
/// <param name="JamDeletePlaylist">Whether a new Jukebox Jam deletes its playlist when it ends by default. Null keeps it.</param>
public sealed record SharingSettingsInput(
    bool PublicLinks, bool JamGuests, int GuestAddLimit, int MaxGuests, string? PublicUrl = null, bool? JamDeletePlaylist = null);
