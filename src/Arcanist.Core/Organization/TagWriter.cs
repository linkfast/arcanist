using System.Runtime.CompilerServices;
using Arcanist.Core.Application.Organization;
using Arcanist.Core.Scanning;

namespace Arcanist.Core.Organization;

/// <summary>
/// Writes tags with ATL.NET. Only used on files being imported from a drop folder: when an administrator completes
/// their tags, and to carry tags over to a converted copy. Library files are never re-tagged.
/// </summary>
internal static class TagWriter
{
    // ATL's settings are process-wide and set by the tag reader: make sure they are in place before writing.
    static TagWriter() => RuntimeHelpers.RunClassConstructor(typeof(TagReader).TypeHandle);

    public static void Write(string path, ImportTagsInput tags)
    {
        var track = new ATL.Track(path);
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

        if (!track.Save())
            throw new IOException($"Could not write tags to '{path}'.");
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
