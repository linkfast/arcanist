namespace Arcanist.Core.Application.Catalog;

/// <summary>Where the recognition of a song's words stands. Part of the API contract.</summary>
public enum LyricsTranscriptionState
{
    /// <summary>It cannot be asked for: see <see cref="LyricsTranscriptionStatus.Reason"/>.</summary>
    Unavailable,
    /// <summary>The song has lyrics (or is known to be instrumental): nothing to recognize.</summary>
    HasLyrics,
    /// <summary>It can be asked for (requestLyricsTranscription).</summary>
    NotRequested,
    /// <summary>Asked for, waiting for the extension (it waits while the server scans or someone listens).</summary>
    Queued,
    /// <summary>The extension is listening to the song.</summary>
    Working,
    /// <summary>The model wrote the words: a draft waits for a person to review and accept it.</summary>
    NeedsReview,
    /// <summary>Nothing sung was heard (lately): an instrumental, or a voice the model cannot make out.</summary>
    NoVoice,
    /// <summary>It failed (lately); it can be asked for again.</summary>
    Failed,
}

/// <summary>A line of recognized lyrics: when it starts, its words and how sure the model was (null once a person edited it).</summary>
public sealed record LyricsDraftLine(int TimeMs, string Text, double? Confidence);

/// <summary>Lyrics a model recognized in a song, waiting for review.</summary>
/// <param name="Language">The language the model heard (ISO 639-1).</param>
/// <param name="EditedBy">Who changed them last (display name); null while they are as the model left them.</param>
public sealed record LyricsDraftInfo(IReadOnlyList<LyricsDraftLine> Lines, string? Language, string? Model, DateTime CreatedAt, DateTime UpdatedAt, string? EditedBy);

/// <summary>
/// Whether a song's words can be recognized by a model (the Lyrics Sync extension), where that stands, and the draft
/// waiting for review.
/// </summary>
/// <param name="Reason">Why it is unavailable: lyrics_sync_unavailable, no_local_copy, or library_read_only (for review).</param>
/// <param name="CanRequest">The user can ask for it now (requestLyricsTranscription).</param>
/// <param name="CanReview">The user can edit, accept and discard the draft: EDIT_METADATA and a library with the song they may change.</param>
/// <param name="Draft">The draft, for those who can review it.</param>
public sealed record LyricsTranscriptionStatus(
    Guid TrackId, LyricsTranscriptionState State, string? Reason, bool CanRequest, bool CanReview, LyricsDraftInfo? Draft);

/// <summary>A line as a person leaves it in the editor.</summary>
public sealed record LyricsLineInput(int TimeMs, string Text);

/// <summary>A draft waiting for review, with its song.</summary>
/// <param name="Uncertain">Lines the model was not sure of (confidence below 0.6), to look at first.</param>
public sealed record LyricsDraftSummary(
    Guid TrackId, string Title, string? Artist, string Album, Guid? ArtworkId, int DurationMs, int Lines, int Uncertain, string? Language,
    DateTime CreatedAt, DateTime UpdatedAt, string? EditedBy);

public sealed record LyricsDraftPage(IReadOnlyList<LyricsDraftSummary> Items, int TotalCount);
