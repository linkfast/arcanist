namespace Arcanist.Extensibility;

// —— Pronunciation and translation of lyrics ——

/// <summary>
/// A part of a line with its reading above it (furigana: <c>夜空</c> read <c>よぞら</c>); a part without a reading is
/// written as it is (kana, punctuation).
/// </summary>
public sealed record RubySegment(string Text, string? Reading);

/// <summary>How a line of lyrics is pronounced.</summary>
/// <param name="Romanized">The line in the Latin alphabet (romaji, pinyin, Revised Romanization…).</param>
/// <param name="Ruby">The line in parts with their readings (furigana for Japanese); null when the script has none.</param>
public sealed record LyricsReading(string? Romanized, IReadOnlyList<RubySegment>? Ruby);

/// <summary>
/// Pronunciation of lyrics written in a script many cannot read (Japanese, Chinese, Korean, Cyrillic, Arabic…).
/// Registered by an extension; Arcanist asks it when a player reads a song's lyrics, and keeps what it says for those
/// lyrics (by their text) until <see cref="Version"/> changes. Calls must be quick: no network.
/// </summary>
public interface ILyricsReadings
{
    /// <summary>Changes whenever the readings would come out different: Arcanist works them out again.</summary>
    string Version { get; }

    /// <summary>The language of the lyrics (ISO 639-1, such as <c>ja</c>), from their script; null for those that need no reading (Latin script).</summary>
    string? LanguageOf(IReadOnlyList<string> lines);

    /// <summary>A reading per line, in order (null for a line that needs none).</summary>
    IReadOnlyList<LyricsReading?> Read(IReadOnlyList<string> lines, string language);
}

/// <summary>
/// Translation of lyrics into another language. Registered by an extension; Arcanist calls it in the background, one
/// song at a time, when a player asks for the lyrics in a language, and keeps the result for everyone.
/// </summary>
public interface ILyricsTranslator
{
    /// <summary>The languages it translates into (ISO 639-1); empty when it cannot translate now (a model to install).</summary>
    IReadOnlyList<string> Languages { get; }

    /// <summary>What translates, said to users ("Ollama · llama3.1", "M2M100").</summary>
    string Engine { get; }

    /// <summary>
    /// The lines in <paramref name="to"/>, as many and in the same order (an empty line stays empty);
    /// <paramref name="from"/> is the language of the lyrics when known.
    /// </summary>
    Task<IReadOnlyList<string>> TranslateAsync(IReadOnlyList<string> lines, string? from, string to, CancellationToken cancellationToken);
}
