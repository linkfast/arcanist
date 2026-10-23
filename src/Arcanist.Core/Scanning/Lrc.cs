using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Arcanist.Core.Scanning;

/// <summary>The LRC format of synchronized lyrics: lines like <c>[01:23.45]text</c>, possibly with several stamps.</summary>
public static partial class Lrc
{
    public static string Format(IEnumerable<(int TimeMs, string Text)> lines)
    {
        var builder = new StringBuilder();
        foreach (var (time, text) in lines.OrderBy(l => l.TimeMs))
        {
            var span = TimeSpan.FromMilliseconds(Math.Max(0, time));
            builder.Append(CultureInfo.InvariantCulture, $"[{(int)span.TotalMinutes:00}:{span.Seconds:00}.{span.Milliseconds / 10:00}]{text}\n");
        }

        return builder.ToString();
    }

    /// <summary>Timed lines, in order. Metadata tags ([ar:…]) and lines without a time are ignored.</summary>
    public static IReadOnlyList<(int TimeMs, string Text)> Parse(string lrc)
    {
        var lines = new List<(int, string)>();
        foreach (var line in lrc.Split('\n'))
        {
            var stamps = Stamp().Matches(line);
            if (stamps.Count == 0)
                continue;

            var text = line[(stamps[^1].Index + stamps[^1].Length)..].Trim();
            foreach (Match stamp in stamps)
            {
                var minutes = int.Parse(stamp.Groups["m"].Value, CultureInfo.InvariantCulture);
                var seconds = double.Parse(stamp.Groups["s"].Value, CultureInfo.InvariantCulture);
                lines.Add(((int)Math.Round((minutes * 60 + seconds) * 1000), text));
            }
        }

        return lines.OrderBy(l => l.Item1).ToList();
    }

    /// <summary>
    /// Whether text is LRC rather than plain lyrics: most of its lines with words start with a time.
    /// </summary>
    public static bool LooksTimed(string text)
    {
        var lines = text.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0 && !Metadata().IsMatch(l)).ToList();
        if (lines.Count == 0)
            return false;
        var timed = lines.Count(l => Stamp().Match(l) is { Success: true, Index: 0 });
        return timed >= 2 && timed * 2 >= lines.Count;
    }

    [GeneratedRegex(@"^\[[a-z]+:[^\]]*\]$", RegexOptions.IgnoreCase)]
    private static partial Regex Metadata();

    [GeneratedRegex(@"\[(?<m>\d{1,3}):(?<s>\d{1,2}(?:\.\d{1,3})?)\]")]
    private static partial Regex Stamp();
}
