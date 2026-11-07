namespace Arcanist.Core.Data.Entities;

/// <summary>
/// How lyrics are pronounced (romaji, furigana, pinyin…), as the extension that reads them said, kept by the text of
/// the lyrics (<see cref="TextHash"/>, the hash of their lines): the same lyrics on several tracks share it, and lyrics
/// that change get it again. Gone when no lyrics have that text any more.
/// </summary>
public sealed class LyricsReadingCache : Entity
{
    /// <summary><c>LyricsTiming.HashOf</c> of the lines, one per line.</summary>
    public required string TextHash { get; init; }

    /// <summary>The reader's version: another one works them out again.</summary>
    public required string Version { get; set; }

    /// <summary>The language of the lyrics (ISO 639-1); null for lyrics that need no reading.</summary>
    public string? Language { get; set; }

    /// <summary>A reading per line, as JSON (<c>[{"romanized":…,"ruby":[{"text":…,"reading":…}]}|null]</c>); null with no language.</summary>
    public string? Readings { get; set; }

    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// Lyrics translated into a language, kept for everyone by the text of the lyrics (<see cref="TextHash"/>): one per
/// text and language. Gone when no lyrics have that text any more.
/// </summary>
public sealed class LyricsTranslation : Entity
{
    public required string TextHash { get; init; }

    /// <summary>The language translated into (ISO 639-1).</summary>
    public required string Language { get; init; }

    /// <summary>The language of the lyrics, when known.</summary>
    public string? From { get; set; }

    /// <summary>The lines, as a JSON array of strings: as many as the lyrics, in their order.</summary>
    public required string Lines { get; set; }

    /// <summary>What translated them ("Ollama · llama3.1", "M2M100").</summary>
    public string? Engine { get; set; }

    public DateTime CreatedAt { get; set; }
}
