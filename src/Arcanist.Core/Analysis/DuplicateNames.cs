using System.Text.RegularExpressions;
using Arcanist.Core.Data;

namespace Arcanist.Core.Analysis;

/// <summary>
/// Whether two names are of the same song, beyond the words that say which pressing it is: "Song (2011 Remaster)",
/// "Song - Remastered" and "Song (feat. Someone)" are all "song". What says it is another performance ("Live",
/// "Acoustic", "Remix"…) stays in the name, so those stay different songs.
/// </summary>
public static partial class DuplicateNames
{
    /// <summary>The title without its pressing's words, on an already normalized title.</summary>
    public static string CoreTitle(string normalizedTitle)
    {
        var title = Featuring().Replace(normalizedTitle, "");
        for (var previous = ""; previous != title;)
        {
            previous = title;
            title = Qualifier().Replace(title, m => IsAnotherPerformance(m.Value) || !IsPressing(m.Value) ? m.Value : "").Trim();
        }

        return title.Length > 0 ? title : normalizedTitle;
    }

    /// <summary>Whether the names are of one artist: the same name, or one inside the other ("Queen", "Queen &amp; David Bowie").</summary>
    public static bool SameArtist(IReadOnlyCollection<string> a, IReadOnlyCollection<string> b) =>
        a.Any(x => b.Any(y => x == y || Inside(x, y) || Inside(y, x)));

    private static bool Inside(string shorter, string longer) =>
        shorter.Length >= 3 && Regex.IsMatch(longer, $@"(^|\W){Regex.Escape(shorter)}($|\W)");

    private static bool IsPressing(string qualifier) => Pressing().IsMatch(qualifier);

    private static bool IsAnotherPerformance(string qualifier) => Performance().IsMatch(qualifier);

    /// <summary>A qualifier at the end of a title, in brackets or after a dash.</summary>
    [GeneratedRegex(@"(\s*[\(\[][^\)\]]*[\)\]]|\s+[-–—]\s+[^-–—]+)$")]
    private static partial Regex Qualifier();

    /// <summary>"feat. Someone" at the end of a title, with or without brackets.</summary>
    [GeneratedRegex(@"\s*[\(\[]?\b(feat\.?|ft\.?|featuring)\s[^\)\]]*[\)\]]?\s*$")]
    private static partial Regex Featuring();

    [GeneratedRegex(@"\b(remaster\w*|remasteriza\w*|\d{4}|deluxe|anniversary|aniversario|mono|stereo|estereo|explicit|clean|bonus|album|single|original|version)\b")]
    private static partial Regex Pressing();

    [GeneratedRegex(@"\b(live|en vivo|directo|acoustic\w*|acustic\w*|unplugged|remix\w*|mix|demo|instrumental|karaoke|reprise|edit|radio|session\w*)\b")]
    private static partial Regex Performance();
}
