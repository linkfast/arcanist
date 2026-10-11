using Arcanist.Core.Data;
using Arcanist.Core.Hosting;
using Arcanist.Core.Scanning;
using Microsoft.EntityFrameworkCore;
using SkiaSharp;

namespace Arcanist.Core.Application.Artwork;

/// <summary>An image file to send, with a validator that never changes for the same content.</summary>
public sealed record ArtworkFile(string Path, string ContentType, string ETag);

/// <summary>
/// Serves cached artwork, scaled down on demand. Thumbnails are generated once per size and kept under
/// <c>&lt;data&gt;/cache/thumbnails</c>; they are derived from content hashes, so they can be cached forever.
/// </summary>
public sealed class ArtworkService(ArcanistDbContext db, ArtworkStore store, ArcanistPaths paths)
{
    /// <summary>Sizes players can ask for (longest side, in pixels); other values are rounded up to one of these.</summary>
    public static readonly int[] Sizes = [64, 128, 256, 512, 1024];

    public async Task<ArtworkFile> GetAsync(Guid id, int? size, CancellationToken cancellationToken)
    {
        var artwork = await db.Artworks.AsNoTracking().SingleOrDefaultAsync(a => a.Id == id, cancellationToken)
                      ?? throw new ArcanistException.NotFound();

        var original = store.PathFor(artwork.Hash, artwork.MimeType);
        if (!File.Exists(original))
            throw new ArcanistException.NotFound();

        var target = size is null ? (int?)null : Sizes.FirstOrDefault(s => s >= size, Sizes[^1]);
        if (target is null || Math.Max(artwork.Width, artwork.Height) <= target)
            return new ArtworkFile(original, artwork.MimeType, $"\"{artwork.Hash}\"");

        var thumbnail = Path.Combine(paths.Cache, "thumbnails", artwork.Hash[..2], $"{artwork.Hash}-{target}.jpg");
        if (!File.Exists(thumbnail))
            CreateThumbnail(original, thumbnail, target.Value);

        return new ArtworkFile(thumbnail, "image/jpeg", $"\"{artwork.Hash}-{target}\"");
    }

    private static void CreateThumbnail(string source, string destination, int size)
    {
        using var bitmap = SKBitmap.Decode(source) ?? throw new ArcanistException.NotFound();
        var scale = (double)size / Math.Max(bitmap.Width, bitmap.Height);
        var info = new SKImageInfo(Math.Max(1, (int)Math.Round(bitmap.Width * scale)), Math.Max(1, (int)Math.Round(bitmap.Height * scale)));

        using var resized = bitmap.Resize(info, new SKSamplingOptions(SKCubicResampler.Mitchell))
                            ?? throw new InvalidOperationException("Could not resize the image.");
        using var image = SKImage.FromBitmap(resized);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, 85);

        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        using (var file = File.Create(temporary))
            data.SaveTo(file);
        File.Move(temporary, destination, overwrite: true);
    }
}
