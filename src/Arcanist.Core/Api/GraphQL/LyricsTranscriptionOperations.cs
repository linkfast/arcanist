using System.ComponentModel;
using Arcanist.Core.Application.Catalog;
using GraphQL;

namespace Arcanist.Core.Api.GraphQL;

public sealed partial class Query
{
    [Name("lyricsTranscription")]
    [Description("Whether a song's words can be recognized by a model (the Lyrics Sync extension), where that stands, and the draft waiting for review (only for those who can review it: EDIT_METADATA and a library with the song they may change).")]
    public static Task<LyricsTranscriptionStatus> LyricsTranscription([Id] Guid trackId, [FromServices] LyricsTranscriptionService service,
        CancellationToken cancellationToken) =>
        service.StatusAsync(trackId, cancellationToken);

    [Name("lyricsDrafts")]
    [Description("The lyrics drafts waiting for review, of songs the user may review (EDIT_METADATA), most recently recognized first, with how many lines the model was unsure of.")]
    public static Task<LyricsDraftPage> LyricsDrafts(int? offset, int? first, [FromServices] LyricsTranscriptionService service,
        CancellationToken cancellationToken) =>
        service.DraftsAsync(offset, first, cancellationToken);
}

public sealed partial class Mutation
{
    [Name("requestLyricsTranscription")]
    [Description("Asks for a song without lyrics to have its words recognized by a model (Lyrics Sync), first of all (also again after NO_VOICE or FAILED). What it writes is a draft (NEEDS_REVIEW) that nobody sees as the song's lyrics until someone reviews and accepts it. Any user who can hear the song. Errors: not_found, has_lyrics, lyrics_sync_unavailable, no_local_copy.")]
    public static Task<LyricsTranscriptionStatus> RequestLyricsTranscription([Id] Guid trackId, [FromServices] LyricsTranscriptionService service,
        CancellationToken cancellationToken) =>
        service.RequestAsync(trackId, cancellationToken);

    [Name("saveLyricsDraft")]
    [Description("Saves a reviewer's corrections to a song's lyrics draft; it stays a draft. Lines a person changed lose the model's confidence. Requires EDIT_METADATA and a library with the song the user may change. Errors: not_found (no draft), library_read_only, validation (at most 2000 lines of 500 characters, times not negative).")]
    public static Task<LyricsTranscriptionStatus> SaveLyricsDraft([Id] Guid trackId, IReadOnlyList<LyricsLineInput> lines,
        [FromServices] LyricsTranscriptionService service, CancellationToken cancellationToken) =>
        service.SaveDraftAsync(trackId, lines, cancellationToken);

    [Name("acceptLyricsDraft")]
    [Description("Accepts a song's lyrics draft (with the lines given, or as it is) as its lyrics, timed line by line (source TRANSCRIBED: lyrics a file brings later replace them). Requires EDIT_METADATA. Errors: not_found, has_lyrics (it got lyrics meanwhile), library_read_only.")]
    public static Task<TrackLyrics> AcceptLyricsDraft([Id] Guid trackId, IReadOnlyList<LyricsLineInput>? lines,
        [FromServices] LyricsTranscriptionService service, CancellationToken cancellationToken) =>
        service.AcceptAsync(trackId, lines, cancellationToken);

    [Name("discardLyricsDraft")]
    [Description("Throws a song's lyrics draft away; with again: true the song is recognized again. Requires EDIT_METADATA.")]
    public static Task<LyricsTranscriptionStatus> DiscardLyricsDraft([Id] Guid trackId, bool? again, [FromServices] LyricsTranscriptionService service,
        CancellationToken cancellationToken) =>
        service.DiscardAsync(trackId, again ?? false, cancellationToken);
}
