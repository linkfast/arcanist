namespace Arcanist.Core.Scanning;

public static class AudioFormats
{
    /// <summary>Audio file extensions the scanner reads, with the MIME type used to pick the tag reader and to stream.</summary>
    private static readonly Dictionary<string, string> MimeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".mp3"] = "audio/mpeg",
        [".flac"] = "audio/flac",
        [".ogg"] = "audio/ogg",
        [".oga"] = "audio/ogg",
        [".opus"] = "audio/opus",
        [".m4a"] = "audio/mp4",
        [".m4b"] = "audio/mp4",
        [".aac"] = "audio/aac",
        [".wav"] = "audio/wav",
        [".aif"] = "audio/x-aiff",
        [".aiff"] = "audio/x-aiff",
        [".wma"] = "audio/x-ms-wma",
        [".ape"] = "audio/ape",
        [".wv"] = "audio/wavpack",
        [".dsf"] = "audio/dsf",
    };

    /// <summary>Folder images used as album artwork when files carry none, in order of preference.</summary>
    private static readonly string[] ArtworkNames = ["cover", "folder", "front", "album", "albumart"];
    private static readonly string[] ArtworkExtensions = [".jpg", ".jpeg", ".png", ".webp"];

    public static bool IsLyrics(string path) => path.EndsWith(".lrc", StringComparison.OrdinalIgnoreCase);

    /// <summary>The path without its extension, to pair a file with its sidecars.</summary>
    public static string WithoutExtension(string path)
    {
        var dot = path.LastIndexOf('.');
        return dot > path.LastIndexOf('/') ? path[..dot] : path;
    }

    public static bool IsAudio(string path) => MimeTypes.ContainsKey(Path.GetExtension(path));

    public static string MimeType(string path) =>
        MimeTypes.TryGetValue(Path.GetExtension(path), out var mime) ? mime : "application/octet-stream";

    /// <summary>Preference of a file as folder artwork (lower is better), or null when it is not one.</summary>
    public static int? ArtworkRank(string path)
    {
        if (!ArtworkExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
            return null;

        var index = Array.FindIndex(ArtworkNames, n => n.Equals(Path.GetFileNameWithoutExtension(path), StringComparison.OrdinalIgnoreCase));
        return index < 0 ? null : index;
    }

    /// <summary>The folder part of a '/'-separated relative path ("" for the source root).</summary>
    public static string Folder(string relativePath)
    {
        var slash = relativePath.LastIndexOf('/');
        return slash < 0 ? "" : relativePath[..slash];
    }
}
