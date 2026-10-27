namespace Arcanist.Core.Scanning;

/// <summary>Video files the scanner catalogs as music videos.</summary>
public static class VideoFormats
{
    private static readonly Dictionary<string, string> MimeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".mp4"] = "video/mp4",
        [".m4v"] = "video/mp4",
        [".mkv"] = "video/x-matroska",
        [".webm"] = "video/webm",
        [".mov"] = "video/quicktime",
    };

    public static bool IsVideo(string path) => MimeTypes.ContainsKey(Path.GetExtension(path));

    /// <summary>The extensions the scanner reads, with their dot.</summary>
    public static IReadOnlyCollection<string> Extensions => MimeTypes.Keys;

    public static string MimeType(string path) =>
        MimeTypes.TryGetValue(Path.GetExtension(path), out var mime) ? mime : "application/octet-stream";
}
