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
public sealed record SharingStatus(bool PublicLinks, bool JamGuests, int GuestAddLimit, int MaxGuests, bool CanShareLinks);

public sealed record SharingSettingsInput(bool PublicLinks, bool JamGuests, int GuestAddLimit, int MaxGuests);
