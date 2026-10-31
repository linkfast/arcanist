namespace Arcanist.Core.Data.Entities;

/// <summary>
/// Lyrics a model recognized in a song that had none (Lyrics Sync), waiting for a person to review them: nobody
/// sees them as the song's lyrics until someone corrects what the model got wrong and accepts them (they become
/// <see cref="Lyrics"/> with <see cref="LyricsSource.Transcribed"/>). One per track.
/// </summary>
public sealed class LyricsDraft
{
    public Guid TrackId { get; init; }

    public Track Track { get; init; } = null!;

    /// <summary>
    /// The lines, as JSON: <c>[{"timeMs":…,"text":…,"confidence":…}]</c>. Confidence (0–1) is the model's, per line;
    /// lines a person edited lose it.
    /// </summary>
    public required string Lines { get; set; }

    /// <summary>The language the model heard (ISO 639-1), if it said.</summary>
    public string? Language { get; set; }

    /// <summary>What recognized it (the extension and its model).</summary>
    public string? Model { get; set; }

    public DateTime CreatedAt { get; init; }

    public DateTime UpdatedAt { get; set; }

    /// <summary>Who changed it last; null while it is as the model left it.</summary>
    public Guid? EditedByUserId { get; set; }
}
