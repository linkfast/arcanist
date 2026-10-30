using System.ComponentModel;
using Arcanist.Core.Application.Catalog;
using GraphQL;

namespace Arcanist.Core.Api.GraphQL;

public sealed partial class Query
{
    [Name("lyricsTiming")]
    [Description("How many songs have lyrics, how many of them are timed (and how many timed on this server), how many plain lyrics could be timed here, and whether the official extension that does it (Lyrics Sync) is installed and running. Administrators only.")]
    public static Task<LyricsTimingStatus> LyricsTiming([FromServices] LyricsTimingService service, CancellationToken cancellationToken) =>
        service.StatusAsync(cancellationToken);
}

public sealed partial class Mutation
{
    [Name("resyncTrackLyrics")]
    [Description("Asks the Lyrics Sync extension to work out the timing of a song's lyrics again from its audio, whatever timing they have (from the file, LRCLib or worked out here). The lyrics as they are now come back with resyncPending; their current timing stays until the new one is saved (syncedSource ALIGNED, resyncPending false), and is kept if the extension cannot match the words to the singing (resyncPending false, nothing changed). Requires EDIT_METADATA. Errors: not_found, lyrics_sync_unavailable (the extension is not installed or not running), no_lyrics (none, or instrumental), library_read_only, no_local_copy (no copy on this server's disks to listen to).")]
    public static Task<TrackLyrics> ResyncTrackLyrics([Id] Guid trackId, [FromServices] CatalogService catalog, CancellationToken cancellationToken) =>
        catalog.ResyncLyricsAsync(trackId, cancellationToken);
}
