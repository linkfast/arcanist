using System.Text.RegularExpressions;
using Arcanist.Core.Data;

namespace Arcanist.Core.Videos;

/// <summary>
/// Reads the song out of a music video's title or file name: "Artist - Title (Official Video) [4K]" is the
/// artist and the title, without what names the kind of video; and whether it is a live performance.
/// </summary>
public static partial class VideoTitles
{
    /// <summary>
    /// Words that make a performance live in the qualifiers of a title ("(Live)", "- En vivo") or in an album
    /// title (matched on normalized text, as whole words).
    /// </summary>
    private static readonly string[] LiveWords =
    [
        "live", "en vivo", "en directo", "ao vivo", "dal vivo", "unplugged", "concert", "concierto", "in concert", "tiny desk",
    ];

    /// <summary>Phrases that say it anywhere in a title ("Live and Let Die" is a song, "Live at Wembley" a concert).</summary>
    private static readonly string[] LivePhrases =
    [
        "live at", "live from", "live in", "en vivo", "en directo", "ao vivo", "dal vivo", "mtv unplugged", "tiny desk", "in concert",
    ];

    /// <summary>The song as a video names it.</summary>
    public sealed record Parsed(string? Artist, string Title, bool IsLive);

    /// <summary>
    /// Splits <paramref name="title"/> (from the tags, or the file name without extension) into artist and title,
    /// using <paramref name="artist"/> from the tags when there is one.
    /// </summary>
    public static Parsed Parse(string title, string? artist)
    {
        var live = IsLive(title);
        var clean = Decorations().Replace(title, " ");
        clean = TrackNumber().Replace(clean, "");
        clean = Spaces().Replace(clean, " ").Trim(' ', '-', '–', '—', '|', '_', '.');

        string? parsedArtist = string.IsNullOrWhiteSpace(artist) ? null : artist.Trim();
        // "Artist - Title": the separator needs spaces around it, so "Jay-Z" stays whole.
        var parts = Separator().Split(clean, 2);
        if (parts.Length == 2 && parts[0].Length > 0 && parts[1].Length > 0)
        {
            if (parsedArtist is null || TextNormalizer.Normalize(parts[0]) == TextNormalizer.Normalize(parsedArtist))
            {
                parsedArtist ??= parts[0].Trim();
                clean = parts[1].Trim();
            }
        }
        // YouTube channels named "Artist - Topic" or "ArtistVEVO".
        if (parsedArtist is not null)
            parsedArtist = ChannelSuffix().Replace(parsedArtist, "").Trim();
        clean = Featuring().Replace(clean, "").Trim();
        if (clean.Length == 0)
            clean = title.Trim();
        return new Parsed(parsedArtist is { Length: > 0 } ? parsedArtist : null, clean, live);
    }

    /// <summary>
    /// Whether the title of a video or a track says it is a live performance: in its qualifiers (brackets, or after a
    /// dash) or with a phrase such as "live at".
    /// </summary>
    public static bool IsLive(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return false;
        if (Contains(title, LivePhrases))
            return true;
        var qualifiers = Qualifiers().Matches(title).Select(m => m.Value).Concat(Separator().Split(title).Skip(1));
        return qualifiers.Any(q => Contains(q, LiveWords));
    }

    /// <summary>Whether an album is a live recording ("Live at Wembley", "MTV Unplugged", "En vivo").</summary>
    public static bool IsLiveAlbum(string? album) => !string.IsNullOrWhiteSpace(album) && Contains(album, LiveWords);

    private static bool Contains(string text, string[] words)
    {
        var normalized = $" {Words(text)} ";
        return words.Any(word => normalized.Contains($" {word} ", StringComparison.Ordinal));
    }

    /// <summary>
    /// The words of a title without its qualifiers ("Song (Radio Edit) [feat. X]" is "song"): how a video and a
    /// track are compared. Live versions are told apart by <see cref="IsLive"/>, not by this.
    /// </summary>
    public static string Core(string title)
    {
        var core = Words(Qualifiers().Replace(title, " "));
        // Qualifiers after a dash: "Song - Radio Edit", "Song - Live at Wembley".
        var dash = Separator().Split(title, 2);
        if (dash.Length == 2 && Words(Qualifiers().Replace(dash[0], " ")) is { Length: > 0 } before)
            core = before;
        return core.Length > 0 ? core : Words(title);
    }

    /// <summary>Normalized words separated by single spaces, without punctuation: what names are compared by.</summary>
    public static string Words(string text) => NotWord().Replace(TextNormalizer.Normalize(text), " ").Trim();

    [GeneratedRegex(@"[^\p{L}\p{N}]+")]
    private static partial Regex NotWord();

    [GeneratedRegex(@"[\(\[\{][^\)\]\}]*[\)\]\}]")]
    private static partial Regex Qualifiers();

    // Brackets that describe the video, not the song: (Official Video), [4K], (Videoclip Oficial), (HD Remaster)…
    [GeneratedRegex(@"[\(\[\{][^\)\]\}]*\b(official|oficial|video|videoclip|clip|lyrics?|letra|visualizer|audio|hd|hq|4k|8k|1080p|720p|remaster(ed)?|mv|m/v)\b[^\)\]\}]*[\)\]\}]",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Decorations();

    [GeneratedRegex(@"^\s*\d{1,3}\s*[\.\-_ ]\s*")]
    private static partial Regex TrackNumber();

    [GeneratedRegex(@"\s+[-–—|]\s+")]
    private static partial Regex Separator();

    [GeneratedRegex(@"\s*(-\s*Topic|VEVO)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ChannelSuffix();

    [GeneratedRegex(@"\s*[\(\[]\s*(feat\.?|ft\.?|featuring|con)\s[^\)\]]*[\)\]]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Featuring();

    [GeneratedRegex(@"\s{2,}")]
    private static partial Regex Spaces();
}
