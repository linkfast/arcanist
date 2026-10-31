using Arcanist.Core.Application.Catalog;

namespace Arcanist.Core.Api.Rest;

/// <summary>The lines of a lyrics draft as the reviewer leaves them.</summary>
public sealed record LyricsDraftInput(IReadOnlyList<LyricsLineInput> Lines);

/// <summary>Accepting a draft: the reviewed lines, or none to accept it as it is.</summary>
public sealed record AcceptLyricsDraftInput(IReadOnlyList<LyricsLineInput>? Lines);

public static class LyricsTranscriptionEndpoints
{
    public static IEndpointRouteBuilder MapLyricsTranscriptionEndpoints(this IEndpointRouteBuilder rest)
    {
        var group = rest.MapGroup("").WithTags("Lyrics recognition");

        group.MapGet("/tracks/{trackId:guid}/lyrics/transcription", (Guid trackId, LyricsTranscriptionService service, CancellationToken ct) =>
                service.StatusAsync(trackId, ct))
            .WithName("LyricsTranscription")
            .WithSummary("Whether a song's words can be recognized by a model (the Lyrics Sync extension), where that stands, and the draft waiting for review (for those who can review it).");

        group.MapPost("/tracks/{trackId:guid}/lyrics/transcription", (Guid trackId, LyricsTranscriptionService service, CancellationToken ct) =>
                service.RequestAsync(trackId, ct))
            .WithName("RequestLyricsTranscription")
            .WithSummary("Asks for a song without lyrics to have its words recognized by a model (Lyrics Sync). What it writes is a draft that needs review before anyone sees it as the song's lyrics. Any user who can hear the song. 409 with has_lyrics, lyrics_sync_unavailable or no_local_copy.");

        group.MapPut("/tracks/{trackId:guid}/lyrics/draft", (Guid trackId, LyricsDraftInput input, LyricsTranscriptionService service, CancellationToken ct) =>
                service.SaveDraftAsync(trackId, input.Lines, ct))
            .WithName("SaveLyricsDraft")
            .WithSummary("Saves a reviewer's corrections to a song's lyrics draft (it stays a draft). Requires EDIT_METADATA and a library with the song the user may change.");

        group.MapPost("/tracks/{trackId:guid}/lyrics/draft/accept", (Guid trackId, AcceptLyricsDraftInput? input, LyricsTranscriptionService service,
                CancellationToken ct) => service.AcceptAsync(trackId, input?.Lines, ct))
            .WithName("AcceptLyricsDraft")
            .WithSummary("Accepts a song's lyrics draft (with the lines given, or as it is) as its lyrics, timed line by line (source TRANSCRIBED). Requires EDIT_METADATA. 409 with has_lyrics when the song got lyrics meanwhile.");

        group.MapDelete("/tracks/{trackId:guid}/lyrics/draft", (Guid trackId, bool? again, LyricsTranscriptionService service, CancellationToken ct) =>
                service.DiscardAsync(trackId, again ?? false, ct))
            .WithName("DiscardLyricsDraft")
            .WithSummary("Throws a song's lyrics draft away; with again=true the song is recognized again. Requires EDIT_METADATA.");

        group.MapGet("/lyrics/drafts", (int? offset, int? first, LyricsTranscriptionService service, CancellationToken ct) =>
                service.DraftsAsync(offset, first, ct))
            .WithName("LyricsDrafts")
            .WithSummary("The lyrics drafts waiting for review, of songs the user may review (EDIT_METADATA), most recently recognized first, with how many lines the model was unsure of.");

        return rest;
    }
}
