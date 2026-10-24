namespace Arcanist.Core.Application.Catalog;

// Types in this file are part of the public API contracts (GraphQL schema and OpenAPI document).

/// <summary>
/// Changes to a track's tags. Every field is optional: one left out (or null) is not changed. To clear a field send an
/// empty text (<c>""</c>), an empty list, <c>0</c> for numbers or the nil UUID
/// (<c>00000000-0000-0000-0000-000000000000</c>) for identifiers. Written to the tags of every copy of the track and then
/// read back into the catalog, as a scan would.
/// </summary>
/// <param name="Title">Cannot be cleared.</param>
/// <param name="Artists">The performers, in order. "A feat. B" in one of them credits B as featured.</param>
/// <param name="AlbumArtist">The album's artist; cleared, the album is grouped by the first performer.</param>
/// <param name="Album">The album's title; cleared, the track's folder names the album.</param>
/// <param name="Genres">The genres, in order.</param>
/// <param name="Year">Release year (1–9999).</param>
/// <param name="TrackNumber">Number on its disc (1–9999).</param>
/// <param name="DiscNumber">Disc of the album (1–999).</param>
/// <param name="Composers">Composers, in order.</param>
/// <param name="Lyricists">Lyricists, in order.</param>
/// <param name="Conductor">The conductor.</param>
/// <param name="Comment">The comment.</param>
/// <param name="Bpm">Tempo in beats per minute (1–999), kept as the tagged tempo (<c>Track.taggedBpm</c>); the analysis
/// still measures its own (<c>Track.bpm</c>).</param>
/// <param name="Lyrics">The words, as plain text or LRC (with times, then they are synchronized). A .lrc file next to the
/// audio still wins over them.</param>
/// <param name="MusicBrainzRecordingId">The recording on MusicBrainz.</param>
/// <param name="MusicBrainzReleaseId">The release (album) on MusicBrainz. Changing the album or album artist without
/// saying it clears it, or the release would keep the track in its old album.</param>
public sealed record TrackEditInput(
    string? Title = null,
    IReadOnlyList<string>? Artists = null,
    string? AlbumArtist = null,
    string? Album = null,
    IReadOnlyList<string>? Genres = null,
    int? Year = null,
    int? TrackNumber = null,
    int? DiscNumber = null,
    IReadOnlyList<string>? Composers = null,
    IReadOnlyList<string>? Lyricists = null,
    string? Conductor = null,
    string? Comment = null,
    double? Bpm = null,
    string? Lyrics = null,
    Guid? MusicBrainzRecordingId = null,
    Guid? MusicBrainzReleaseId = null);

/// <summary>The same changes for several tracks at once (editing many: an album, a selection).</summary>
/// <param name="TrackIds">At most 1000.</param>
public sealed record EditTracksInput(IReadOnlyList<Guid> TrackIds, TrackEditInput Changes);

/// <param name="TrackIds">At most 1000.</param>
/// <param name="LibraryId">Only the copies in this library; without it, every copy of each track.</param>
public sealed record DeleteTracksInput(IReadOnlyList<Guid> TrackIds, Guid? LibraryId = null);

/// <summary>What happened to one track of a deletion.</summary>
public enum TrackDeletionStatus
{
    /// <summary>Its files went to the trash of their locations and it left the catalog (or the library).</summary>
    Deleted,

    /// <summary>There is no such track for the user (or no copy in that library).</summary>
    NotFound,

    /// <summary>A copy is in a library the user only listens to (<c>library_read_only</c>) or lacks DELETE_TRACKS.</summary>
    Forbidden,

    /// <summary>A copy is where the server cannot remove files (another server's library, Ultimate Cloud, a read-only
    /// folder: <c>read_only</c>) or in a file split by a cue sheet (<c>cue_sheet_track</c>).</summary>
    NotDeletable,

    /// <summary>Moving a file failed (<c>write_failed</c>); nothing of that track changed.</summary>
    Failed,
}

/// <param name="Code">The error code when it was not deleted.</param>
public sealed record TrackDeletion(Guid TrackId, TrackDeletionStatus Status, string? Code);

/// <summary>The ids of every track the user can play (optionally of one library), for clients to see which of theirs
/// left the server.</summary>
/// <param name="ServerTime">When the list was read: pass it as <c>changedSince</c> next time.</param>
public sealed record TrackIdList(IReadOnlyList<Guid> Ids, DateTime ServerTime);
