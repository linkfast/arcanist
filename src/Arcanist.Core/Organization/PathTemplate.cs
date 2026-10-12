using System.Globalization;
using System.Text;
using Arcanist.Core.Application;

namespace Arcanist.Core.Organization;

/// <summary>What a template fills in for one file.</summary>
/// <param name="MultiDisc">Whether the album has more than one disc: <c>{disc}</c> is empty otherwise.</param>
public sealed record PathValues(
    string? AlbumArtist,
    string? Artist,
    string? Album,
    int? Year,
    int? Disc,
    bool MultiDisc,
    int? Track,
    string? Title,
    string? Genre);

/// <summary>
/// Where an organized file goes, relative to its library location and without its extension, e.g.
/// <c>{albumartist}/&lt;{year} - &gt;{album}/&lt;{disc}-&gt;&lt;{track} &gt;{title}</c>.
/// <list type="bullet">
/// <item><c>/</c> separates folders; the last part names the file and must contain <c>{title}</c>.</item>
/// <item>Placeholders: <c>{albumartist}</c>, <c>{artist}</c>, <c>{album}</c>, <c>{year}</c>, <c>{disc}</c> (only on
/// albums of several discs), <c>{track}</c> (two digits), <c>{title}</c> and <c>{genre}</c>.</item>
/// <item>Text between <c>&lt;</c> and <c>&gt;</c> is left out when a placeholder inside it has no value. Those
/// characters cannot appear in file names on Windows, so they never clash with literal text.</item>
/// </list>
/// Values are made safe for every file system Arcanist runs on (Windows rules, the strictest), so a library stays
/// usable when it is shared over the network.
/// </summary>
public sealed class PathTemplate
{
    public const string Default = "{albumartist}/<{year} - >{album}/<{disc}-><{track} >{title}";

    public const int MaxLength = 1024;

    public const string UnknownArtist = "Unknown Artist";

    public const string UnknownAlbum = "Unknown Album";

    public static readonly IReadOnlyList<string> Placeholders = ["albumartist", "artist", "album", "year", "disc", "track", "title", "genre"];

    /// <summary>Longest folder or file name produced, leaving room for an extension and a " (2)" suffix.</summary>
    private const int MaxSegmentChars = 150;

    /// <summary>Linux file systems limit names to 255 bytes of UTF-8.</summary>
    private const int MaxSegmentBytes = 230;

    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    // Each segment is a list of pieces; a piece is literal text, a placeholder, or an optional group of both.
    private abstract record Piece;
    private sealed record Literal(string Text) : Piece;
    private sealed record Placeholder(string Name) : Piece;
    private sealed record Optional(IReadOnlyList<Piece> Pieces) : Piece;

    private readonly IReadOnlyList<IReadOnlyList<Piece>> _segments;

    private PathTemplate(string text, IReadOnlyList<IReadOnlyList<Piece>> segments)
    {
        Text = text;
        _segments = segments;
    }

    public string Text { get; }

    public static PathTemplate DefaultTemplate { get; } = Parse(Default);

    /// <summary>Parses a template, or reports why it is not one as an <see cref="ErrorCodes"/> value.</summary>
    public static bool TryParse(string? text, out PathTemplate template, out string? error)
    {
        template = null!;
        error = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            error = ErrorCodes.Required;
            return false;
        }

        text = text.Trim().Replace('\\', '/');
        if (text.Length > MaxLength)
        {
            error = ErrorCodes.InvalidFormat;
            return false;
        }

        var segments = new List<IReadOnlyList<Piece>>();
        foreach (var segment in text.Split('/'))
        {
            var pieces = ParseSegment(segment, out error);
            if (pieces is null)
                return false;
            if (pieces.Count == 0)
            {
                error = ErrorCodes.InvalidFormat;
                return false;
            }

            segments.Add(pieces);
        }

        if (!segments[^1].Any(Mentions("title")))
        {
            error = ErrorCodes.TemplateTitleRequired;
            return false;
        }

        template = new PathTemplate(text, segments);
        return true;

        static Func<Piece, bool> Mentions(string name) => piece => piece switch
        {
            Placeholder p => p.Name == name,
            Optional o => o.Pieces.Any(Mentions(name)),
            _ => false,
        };
    }

    public static PathTemplate Parse(string text) =>
        TryParse(text, out var template, out var error) ? template : throw new FormatException($"Invalid path template ({error}): {text}");

    private static List<Piece>? ParseSegment(string segment, out string? error)
    {
        error = null;
        var pieces = new List<Piece>();
        List<Piece>? group = null;
        var literal = new StringBuilder();

        for (var i = 0; i < segment.Length; i++)
        {
            var c = segment[i];
            switch (c)
            {
                case '{':
                    var end = segment.IndexOf('}', i + 1);
                    if (end < 0)
                    {
                        error = ErrorCodes.InvalidFormat;
                        return null;
                    }

                    var name = segment[(i + 1)..end].Trim().ToLowerInvariant();
                    if (!Placeholders.Contains(name))
                    {
                        error = ErrorCodes.TemplateUnknownPlaceholder;
                        return null;
                    }

                    Flush();
                    (group ?? pieces).Add(new Placeholder(name));
                    i = end;
                    break;
                case '}':
                    error = ErrorCodes.InvalidFormat;
                    return null;
                case '<':
                    if (group is not null)
                    {
                        error = ErrorCodes.InvalidFormat;
                        return null;
                    }

                    Flush();
                    group = [];
                    break;
                case '>':
                    if (group is null)
                    {
                        error = ErrorCodes.InvalidFormat;
                        return null;
                    }

                    Flush();
                    pieces.Add(new Optional(group));
                    group = null;
                    break;
                default:
                    literal.Append(c);
                    break;
            }
        }

        if (group is not null)
        {
            error = ErrorCodes.InvalidFormat;
            return null;
        }

        Flush();
        return pieces;

        void Flush()
        {
            if (literal.Length == 0)
                return;
            (group ?? pieces).Add(new Literal(literal.ToString()));
            literal.Clear();
        }
    }

    /// <summary>The '/'-separated path for a file, without extension.</summary>
    public string Render(PathValues values) =>
        string.Join('/', _segments.Select(segment => SanitizeName(Render(segment, values))));

    /// <summary>The folder part of <see cref="Render"/> ("" when the template has no folders).</summary>
    public string RenderFolder(PathValues values)
    {
        var path = Render(values);
        var slash = path.LastIndexOf('/');
        return slash < 0 ? "" : path[..slash];
    }

    private static string Render(IEnumerable<Piece> pieces, PathValues values)
    {
        var text = new StringBuilder();
        foreach (var piece in pieces)
        {
            switch (piece)
            {
                case Literal literal:
                    text.Append(literal.Text);
                    break;
                case Placeholder placeholder:
                    text.Append(Value(placeholder.Name, values));
                    break;
                case Optional optional:
                    // Left out when any of its placeholders is empty.
                    if (optional.Pieces.OfType<Placeholder>().All(p => Value(p.Name, values).Length > 0))
                        text.Append(Render(optional.Pieces, values));
                    break;
            }
        }

        return text.ToString();
    }

    private static string Value(string name, PathValues v) => name switch
    {
        "albumartist" => Clean(v.AlbumArtist) ?? Clean(v.Artist) ?? UnknownArtist,
        "artist" => Clean(v.Artist) ?? Clean(v.AlbumArtist) ?? UnknownArtist,
        "album" => Clean(v.Album) ?? UnknownAlbum,
        "year" => v.Year is > 0 ? v.Year.Value.ToString(CultureInfo.InvariantCulture) : "",
        "disc" => v.MultiDisc && v.Disc is > 0 ? v.Disc.Value.ToString(CultureInfo.InvariantCulture) : "",
        "track" => v.Track is > 0 ? v.Track.Value.ToString("00", CultureInfo.InvariantCulture) : "",
        "title" => Clean(v.Title) ?? "",
        "genre" => Clean(v.Genre) ?? "",
        _ => "",
    };

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>Turns text into a folder or file name valid on Windows, macOS, Linux and NAS shares.</summary>
    public static string SanitizeName(string name)
    {
        var text = new StringBuilder(name.Length);
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            switch (c)
            {
                // Control characters (tabs, line breaks in a tag) are not allowed; as spaces they still separate words.
                case < ' ':
                    text.Append(' ');
                    break;
                // "Title: Subtitle" reads better as "Title - Subtitle" than with an underscore.
                case ':' when i + 1 < name.Length && name[i + 1] == ' ':
                    text.Append(" -");
                    break;
                case ':':
                    text.Append('-');
                    break;
                case '?':
                    break;
                case '"':
                    text.Append('\'');
                    break;
                case '/' or '\\' or '|' or '*' or '<' or '>':
                    text.Append('_');
                    break;
                default:
                    text.Append(c);
                    break;
            }
        }

        var result = CollapseSpaces(text.ToString());
        result = Truncate(result);
        // A leading dot hides a file on Unix (and scans skip hidden files); trailing dots and spaces vanish on Windows.
        result = result.TrimStart('.', ' ').TrimEnd('.', ' ');
        if (result.Length == 0)
            return "_";

        var stem = result.Split('.')[0].TrimEnd();
        return ReservedNames.Contains(stem) ? result + "_" : result;
    }

    private static string CollapseSpaces(string text)
    {
        var result = new StringBuilder(text.Length);
        var space = false;
        foreach (var c in text)
        {
            if (char.IsWhiteSpace(c))
            {
                space = true;
                continue;
            }

            if (space && result.Length > 0)
                result.Append(' ');
            space = false;
            result.Append(c);
        }

        return result.ToString();
    }

    private static string Truncate(string text)
    {
        if (text.Length <= MaxSegmentChars && Encoding.UTF8.GetByteCount(text) <= MaxSegmentBytes)
            return text;

        // Whole text elements only: never half a surrogate pair or a letter without its accent.
        var result = new StringBuilder();
        var bytes = 0;
        var elements = StringInfo.GetTextElementEnumerator(text);
        while (elements.MoveNext())
        {
            var element = elements.GetTextElement();
            var size = Encoding.UTF8.GetByteCount(element);
            if (result.Length + element.Length > MaxSegmentChars || bytes + size > MaxSegmentBytes)
                break;
            result.Append(element);
            bytes += size;
        }

        return result.ToString();
    }
}
