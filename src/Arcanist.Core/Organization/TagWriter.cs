using System.Runtime.CompilerServices;
using Arcanist.Core.Application.Organization;
using Arcanist.Core.Scanning;

namespace Arcanist.Core.Organization;

/// <summary>
/// Writes tags with ATL.NET. Only used on files being imported, before they enter a library: when an administrator
/// completes the tags of a dropped file, to carry tags over to a converted copy, to fill the tags a file copied
/// from a DLNA server lacks with the server's description, and by extensions on the files they bring (before
/// <c>UploadService.UploadAsync</c>). Library files are re-tagged only when a user edits a track (<see cref="Edit"/>):
/// on a copy, which then replaces the file (the original goes to the location's trash).
/// </summary>
public static class TagWriter
{
    // ATL's settings are process-wide and set by the tag reader: make sure they are in place before writing.
    static TagWriter() => RuntimeHelpers.RunClassConstructor(typeof(TagReader).TypeHandle);

    /// <param name="frontCover">An image (JPEG or PNG) to embed as the front cover, replacing the file's pictures.</param>
    public static void Write(string path, ImportTagsInput tags, byte[]? frontCover = null)
    {
        var track = new ATL.Track(path);
        if (frontCover is { Length: > 0 })
        {
            track.EmbeddedPictures.Clear();
            track.EmbeddedPictures.Add(ATL.PictureInfo.fromBinaryData(frontCover, ATL.PictureInfo.PIC_TYPE.Front));
        }
        if (tags.Title is { } title)
            track.Title = title.Trim();
        if (tags.Artist is { } artist)
            track.Artist = artist.Trim();
        if (tags.Album is { } album)
            track.Album = album.Trim();
        if (tags.AlbumArtist is { } albumArtist)
            track.AlbumArtist = albumArtist.Trim();
        if (tags.Year is { } year)
            track.Year = year;
        if (tags.TrackNumber is { } number)
            track.TrackNumber = number;
        if (tags.DiscNumber is { } disc)
            track.DiscNumber = disc;
        if (tags.Genre is { } genre)
            track.Genre = genre.Trim();
        // The keys the tag reader (and Picard, in Vorbis comments and APE) use for MusicBrainz identifiers.
        if (tags.RecordingMbid is { } recording)
            track.AdditionalFields["MUSICBRAINZ_TRACKID"] = recording.ToString();
        if (tags.ReleaseMbid is { } release)
            track.AdditionalFields["MUSICBRAINZ_ALBUMID"] = release.ToString();

        if (!track.Save())
            throw new IOException($"Could not write tags to '{path}'.");
    }

    /// <summary>
    /// Applies an edit of a library track to a copy of one of its files (<c>TrackEditService</c>): fields left out stay,
    /// empty ones are removed. <paramref name="picture"/>: null leaves the pictures, an empty array removes them, an image
    /// replaces them with it as the front cover.
    /// </summary>
    /// <exception cref="IOException">The format's tags could not be written.</exception>
    public static void Edit(string path, Application.Catalog.TrackEditInput changes, byte[]? picture = null)
    {
        var track = new ATL.Track(path);
        if (picture is not null)
        {
            track.EmbeddedPictures.Clear();
            if (picture.Length > 0)
                track.EmbeddedPictures.Add(ATL.PictureInfo.fromBinaryData(picture, ATL.PictureInfo.PIC_TYPE.Front));
        }

        if (changes.Title is { } title)
            track.Title = title.Trim();
        if (changes.Artists is { } artists)
            track.Artist = Join(artists);
        if (changes.AlbumArtist is { } albumArtist)
            track.AlbumArtist = albumArtist.Trim();
        if (changes.Album is { } album)
            track.Album = album.Trim();
        if (changes.Genres is { } genres)
            track.Genre = Join(genres);
        if (changes.Year is { } year)
        {
            track.Year = year > 0 ? year : null;
            if (year <= 0)
                track.Date = DateTime.MinValue;
        }
        if (changes.TrackNumber is { } number)
            track.TrackNumber = number > 0 ? number : null;
        if (changes.DiscNumber is { } disc)
            track.DiscNumber = disc > 0 ? disc : null;
        if (changes.Composers is { } composers)
            track.Composer = Join(composers);
        if (changes.Lyricists is { } lyricists)
            track.Lyricist = Join(lyricists);
        if (changes.Conductor is { } conductor)
            track.Conductor = conductor.Trim();
        if (changes.Comment is { } comment)
            track.Comment = comment.Trim();
        if (changes.Bpm is { } bpm)
            track.BPM = bpm > 0 ? (float)bpm : null;
        if (changes.Lyrics is { } lyrics)
        {
            track.Lyrics.Clear();
            if (!string.IsNullOrWhiteSpace(lyrics))
                track.Lyrics.Add(new ATL.LyricsInfo { UnsynchronizedLyrics = lyrics.Replace("\r\n", "\n").Trim() });
        }

        // The keys the tag reader (and Picard) use for MusicBrainz identifiers, whatever the format spells them.
        if (changes.MusicBrainzRecordingId is { } recording)
            SetIdentifier(track, "musicbrainztrackid", "MUSICBRAINZ_TRACKID", recording);
        if (changes.MusicBrainzReleaseId is { } release)
            SetIdentifier(track, "musicbrainzalbumid", "MUSICBRAINZ_ALBUMID", release);

        if (!track.Save())
            throw new IOException($"Could not write tags to '{path}'.");
    }

    private static string Join(IReadOnlyList<string> values) =>
        string.Join("; ", values.Select(v => v.Trim()).Where(v => v.Length > 0));

    /// <summary>Removes every spelling of an identifier (TXXX:…, MusicBrainz Album Id…) and writes it again unless nil.</summary>
    private static void SetIdentifier(ATL.Track track, string normalized, string key, Guid value)
    {
        foreach (var existing in track.AdditionalFields.Keys.Where(k => Normalize(k) == normalized).ToList())
            track.AdditionalFields.Remove(existing);
        if (value != Guid.Empty)
            track.AdditionalFields[key] = value.ToString();
    }

    private static string Normalize(string key)
    {
        foreach (var prefix in new[] { "TXXX:", "----:com.apple.iTunes:", "WM/" })
        {
            if (key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                key = key[prefix.Length..];
        }

        return new string(key.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
    }

    /// <summary>Copies tags and pictures from one file to another (the original and its converted copy).</summary>
    public static void Copy(string from, string to)
    {
        var source = new ATL.Track(from);
        var target = new ATL.Track(to);
        source.CopyMetadataTo(target);
        if (!target.Save())
            throw new IOException($"Could not write tags to '{to}'.");
    }
}
