using System.Security.Cryptography;
using Arcanist.Core.Hosting;
using SkiaSharp;

namespace Arcanist.Core.Scanning;

public sealed record StoredArtwork(string Hash, string MimeType, int Width, int Height);

/// <summary>
/// Artwork cache under <c>&lt;data&gt;/cache/artwork</c>, one file per distinct image (by SHA-256), so an album's
/// cover embedded in every track is stored once.
/// </summary>
public sealed class ArtworkStore(ArcanistPaths paths)
{
    private string Root => Path.Combine(paths.Cache, "artwork");

    /// <summary>Stores the image if it is new and returns its description, or null if it is not a supported image.</summary>
    public StoredArtwork? Store(byte[] data)
    {
        using var codec = SKCodec.Create(new SKMemoryStream(data));
        if (codec is null)
            return null;

        var mime = codec.EncodedFormat switch
        {
            SKEncodedImageFormat.Jpeg => "image/jpeg",
            SKEncodedImageFormat.Png => "image/png",
            SKEncodedImageFormat.Webp => "image/webp",
            SKEncodedImageFormat.Gif => "image/gif",
            SKEncodedImageFormat.Bmp => "image/bmp",
            _ => null,
        };
        if (mime is null || codec.Info.Width <= 0 || codec.Info.Height <= 0)
            return null;

        var hash = Convert.ToHexStringLower(SHA256.HashData(data));
        var path = PathFor(hash, mime);
        if (!File.Exists(path))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllBytes(temporary, data);
            File.Move(temporary, path, overwrite: true);
        }

        return new StoredArtwork(hash, mime, codec.Info.Width, codec.Info.Height);
    }

    public string PathFor(string hash, string mimeType) =>
        Path.Combine(Root, hash[..2], hash + Extension(mimeType));

    public void Delete(string hash, string mimeType)
    {
        var path = PathFor(hash, mimeType);
        if (File.Exists(path))
            File.Delete(path);
    }

    private static string Extension(string mimeType) => mimeType switch
    {
        "image/png" => ".png",
        "image/webp" => ".webp",
        "image/gif" => ".gif",
        "image/bmp" => ".bmp",
        _ => ".jpg",
    };
}
