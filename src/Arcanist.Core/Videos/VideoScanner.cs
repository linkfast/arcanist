using Arcanist.Core.Data;
using Arcanist.Core.Data.Entities;
using Arcanist.Core.Data.Search;
using Arcanist.Core.Hosting;
using Arcanist.Core.Media;
using Arcanist.Core.Scanning;
using Arcanist.Core.Storage;
using Microsoft.EntityFrameworkCore;

namespace Arcanist.Core.Videos;

/// <summary>
/// The music video part of a scan: the video files a source listed are compared with the catalog; new and
/// changed ones are hashed, read with ffprobe (or listed by their name while there is no ffmpeg) and given a
/// thumbnail; vanished ones are marked missing. Videos read without ffmpeg are read again once it is there.
/// </summary>
public sealed class VideoScanner(
    IArcanistDbContextFactory databases,
    VideoProbe probe,
    ArtworkStore artworkStore,
    ArcanistPaths paths,
    TimeProvider time,
    ILogger<VideoScanner> logger)
{
    private static readonly TimeSpan ModifiedTolerance = TimeSpan.FromSeconds(2);

    public sealed record Result(int Seen, int Added, int Updated, int Missing);

    public async Task<Result> ScanAsync(
        LibrarySource source, StorageLocation location, ILibraryStorage storage, IReadOnlyList<StorageEntry> entries, bool full,
        CancellationToken cancellationToken)
    {
        await using var db = databases.CreateDbContext();
        var known = await db.MusicVideos.AsNoTracking()
            .Where(v => v.LibrarySourceId == source.Id)
            .Select(v => new { v.Id, v.RelativePath, v.Size, v.ModifiedAt, v.MissingSince, v.Probed })
            .ToDictionaryAsync(v => v.RelativePath, StringComparer.Ordinal, cancellationToken);
        var canProbe = await probe.IsAvailableAsync(cancellationToken);

        var now = time.GetUtcNow().UtcDateTime;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var reappeared = new List<Guid>();
        int added = 0, updated = 0;
        foreach (var entry in entries)
        {
            seen.Add(entry.RelativePath);
            if (known.TryGetValue(entry.RelativePath, out var existing)
                && !full
                && existing.Size == entry.Size
                && (existing.ModifiedAt - entry.ModifiedAt).Duration() <= ModifiedTolerance
                && (existing.Probed || !canProbe))
            {
                if (existing.MissingSince is not null)
                    reappeared.Add(existing.Id);
                continue;
            }

            try
            {
                if (await ReadAsync(db, source, location, storage, entry, existing?.Id, canProbe, now, cancellationToken))
                {
                    if (existing is null)
                        added++;
                    else
                        updated++;
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or HttpRequestException)
            {
                logger.LogWarning(e, "Cannot read the video {Path}", entry.RelativePath);
            }
        }

        if (reappeared.Count > 0)
            await db.MusicVideos.Where(v => reappeared.Contains(v.Id)).ExecuteUpdateAsync(s => s.SetProperty(v => v.MissingSince, (DateTime?)null), cancellationToken);
        var vanished = known.Values.Where(v => v.MissingSince is null && !seen.Contains(v.RelativePath)).Select(v => v.Id).ToList();
        foreach (var chunk in vanished.Chunk(500))
            await db.MusicVideos.Where(v => chunk.Contains(v.Id)).ExecuteUpdateAsync(s => s.SetProperty(v => v.MissingSince, now), cancellationToken);
        return new Result(entries.Count, added, updated, vanished.Count);
    }

    private async Task<bool> ReadAsync(
        ArcanistDbContext db, LibrarySource source, StorageLocation location, ILibraryStorage storage, StorageEntry entry, Guid? existingId,
        bool canProbe, DateTime now, CancellationToken cancellationToken)
    {
        string hash;
        await using (var stream = await storage.OpenReadAsync(location, entry.RelativePath, cancellationToken))
            hash = await ContentHasher.HashAsync(stream, 0, stream.CanSeek ? stream.Length : entry.Size, cancellationToken);

        VideoInfo? info = null;
        byte[]? frame = null;
        if (canProbe)
        {
            // ffprobe needs to seek (an MP4's index may be at its end): remote files are copied first.
            var localPath = storage.TryGetLocalPath(location, entry.RelativePath);
            string? copy = null;
            try
            {
                if (localPath is null)
                {
                    copy = Path.Combine(paths.Cache, "videos", "scan", $"{Guid.NewGuid():N}{Path.GetExtension(entry.RelativePath)}");
                    Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
                    await using var input = await storage.OpenReadAsync(location, entry.RelativePath, cancellationToken);
                    await using var output = File.Create(copy);
                    await input.CopyToAsync(output, cancellationToken);
                }

                var path = localPath ?? copy!;
                info = await probe.ProbeAsync(path, cancellationToken);
                if (info is null)
                {
                    // Not a video (an audio-only MP4, a broken file): not a music video.
                    logger.LogDebug("{Path} has no video stream", entry.RelativePath);
                    if (existingId is { } id)
                        await db.MusicVideos.Where(v => v.Id == id).ExecuteDeleteAsync(cancellationToken);
                    return false;
                }
                // A frame a fifth into the video, past the intro's fade-in.
                frame = await probe.FrameAsync(path, Math.Clamp(info.DurationMs / 5, 0, 60_000), cancellationToken);
            }
            finally
            {
                if (copy is not null)
                    File.Delete(copy);
            }
        }

        var name = Path.GetFileNameWithoutExtension(entry.RelativePath);
        var parsed = VideoTitles.Parse(info?.Title ?? name, info?.Artist);
        var video = existingId is { } existing ? await db.MusicVideos.SingleAsync(v => v.Id == existing, cancellationToken) : null;
        if (video is null)
        {
            video = new MusicVideo
            {
                LibrarySourceId = source.Id,
                RelativePath = entry.RelativePath,
                ContentHash = hash,
                Title = parsed.Title,
                NormalizedTitle = TextNormalizer.Normalize(parsed.Title),
                AddedAt = now,
            };
            db.MusicVideos.Add(video);
        }

        video.Size = entry.Size;
        video.ModifiedAt = entry.ModifiedAt;
        video.ContentHash = hash;
        video.Title = Cut(parsed.Title);
        video.NormalizedTitle = Cut(TextNormalizer.Normalize(parsed.Title));
        video.Artist = parsed.Artist is { } artist ? Cut(artist) : null;
        video.NormalizedArtist = parsed.Artist is { } a ? Cut(TextNormalizer.Normalize(a)) : null;
        video.SearchText = FullTextSearch.Text(parsed.Title, parsed.Artist);
        if (!video.LiveLocked)
            video.IsLive = parsed.IsLive || VideoTitles.IsLive(info?.Comment);
        video.Probed = info is not null;
        video.DurationMs = info?.DurationMs ?? video.DurationMs;
        video.Width = info?.Width;
        video.Height = info?.Height;
        video.Container = info?.Container is { } container ? container[..Math.Min(container.Length, 64)] : null;
        video.VideoCodec = info?.VideoCodec;
        video.AudioCodec = info?.AudioCodec;
        video.ScannedAt = now;
        video.MissingSince = null;
        if (frame is not null && artworkStore.Store(frame) is { } stored)
            video.ArtworkId = await ArtworkIdAsync(db, stored, cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static async Task<Guid> ArtworkIdAsync(ArcanistDbContext db, StoredArtwork stored, CancellationToken cancellationToken)
    {
        if (await db.Artworks.Where(a => a.Hash == stored.Hash).Select(a => (Guid?)a.Id).FirstOrDefaultAsync(cancellationToken) is { } id)
            return id;
        var artwork = new Artwork
        {
            Hash = stored.Hash,
            MimeType = stored.MimeType,
            Width = stored.Width,
            Height = stored.Height,
            Source = ArtworkSource.VideoFrame,
        };
        db.Artworks.Add(artwork);
        return artwork.Id;
    }

    private static string Cut(string value) => value.Length <= 512 ? value : value[..512];
}
