using System.Globalization;
using System.Text;

namespace Arcanist.Core.Scanning;

/// <summary>A track of a cue sheet: where it starts in the file (its INDEX 01) and its own tags.</summary>
public sealed record CueTrack(int Number, string? Title, string? Performer, int StartMs);

/// <summary>A FILE of a cue sheet and its tracks.</summary>
public sealed record CueFile(string Name, IReadOnlyList<CueTrack> Tracks);

/// <summary>
/// A cue sheet: a whole album stored as one audio file, with the start of each track. Only the parts Arcanist
/// uses are read: album and track titles and performers, REM DATE, GENRE and DISCNUMBER, files, tracks and indexes.
/// </summary>
public sealed record CueSheet(string? Title, string? Performer, int? Year, string? Genre, int? DiscNumber, IReadOnlyList<CueFile> Files)
{
    /// <summary>
    /// Decodes a cue sheet file: UTF-8 (with or without BOM), or else Windows-1252/Latin-1, which older rippers
    /// wrote. Null when it does not describe any track.
    /// </summary>
    public static CueSheet? Parse(byte[] content)
    {
        string text;
        try
        {
            text = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(content).TrimStart('﻿');
        }
        catch (DecoderFallbackException)
        {
            text = Encoding.Latin1.GetString(content);
        }

        return Parse(text);
    }

    public static CueSheet? Parse(string text)
    {
        string? title = null, performer = null, genre = null;
        int? year = null, disc = null;
        var files = new List<(string Name, List<CueTrack> Tracks)>();
        (int Number, string? Title, string? Performer, int? Index00, int? Index01)? track = null;

        void CloseTrack()
        {
            if (track is { } t && files.Count > 0 && (t.Index01 ?? t.Index00) is { } start)
                files[^1].Tracks.Add(new CueTrack(t.Number, t.Title, t.Performer, start));
            track = null;
        }

        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            var (command, rest) = Split(line);
            switch (command.ToUpperInvariant())
            {
                case "REM":
                    var (field, value) = Split(rest);
                    switch (field.ToUpperInvariant())
                    {
                        case "DATE" when value.Length >= 4 && int.TryParse(value[..4], NumberStyles.None, CultureInfo.InvariantCulture, out var y):
                            year = y;
                            break;
                        case "GENRE":
                            genre = Unquote(value);
                            break;
                        case "DISCNUMBER" when int.TryParse(Unquote(value), NumberStyles.None, CultureInfo.InvariantCulture, out var d):
                            disc = d;
                            break;
                    }

                    break;
                case "TITLE":
                    if (track is { } t1)
                        track = t1 with { Title = Unquote(rest) };
                    else
                        title = Unquote(rest);
                    break;
                case "PERFORMER":
                    if (track is { } t2)
                        track = t2 with { Performer = Unquote(rest) };
                    else
                        performer = Unquote(rest);
                    break;
                case "FILE":
                    CloseTrack();
                    files.Add((FileName(rest), []));
                    break;
                case "TRACK":
                    CloseTrack();
                    var (number, type) = Split(rest);
                    if (type.StartsWith("AUDIO", StringComparison.OrdinalIgnoreCase) && int.TryParse(number, NumberStyles.None, CultureInfo.InvariantCulture, out var n))
                        track = (n, null, null, null, null);
                    break;
                case "INDEX" when track is { } t3:
                    var (index, time) = Split(rest);
                    if (Time(time) is { } ms)
                    {
                        if (index == "01")
                            track = t3 with { Index01 = ms };
                        else if (index == "00")
                            track = t3 with { Index00 = ms };
                    }

                    break;
            }
        }

        CloseTrack();
        var parsed = files.Where(f => f.Tracks.Count > 0).Select(f => new CueFile(f.Name, f.Tracks)).ToList();
        return parsed.Count == 0 ? null : new CueSheet(Clean(title), Clean(performer), year, Clean(genre), disc, parsed);
    }

    /// <summary>mm:ss:ff, with 75 frames per second as on audio CDs.</summary>
    private static int? Time(string value)
    {
        var parts = value.Split(':');
        return parts.Length == 3
               && int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var minutes)
               && int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var seconds)
               && int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var frames)
            ? (minutes * 60 + seconds) * 1000 + frames * 1000 / 75
            : null;
    }

    /// <summary>FILE "name" TYPE, where the name may be quoted or not.</summary>
    private static string FileName(string rest)
    {
        if (rest.StartsWith('"') && rest.IndexOf('"', 1) is var end and > 0)
            return rest[1..end];
        var space = rest.LastIndexOf(' ');
        return space > 0 ? rest[..space] : rest;
    }

    private static (string Head, string Tail) Split(string value)
    {
        var space = value.IndexOf(' ');
        return space < 0 ? (value, "") : (value[..space], value[(space + 1)..].Trim());
    }

    private static string Unquote(string value) =>
        value.Length >= 2 && value[0] == '"' && value[^1] == '"' ? value[1..^1] : value;

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
