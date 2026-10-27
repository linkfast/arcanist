using Arcanist.Core.Data;
using Arcanist.Core.Data.Entities;
using Arcanist.Core.Scanning;
using Microsoft.EntityFrameworkCore;

namespace Arcanist.Core.Videos;

/// <summary>
/// Links music videos to the songs they show, after each scan: first a video next to an audio file with the same
/// name, then one whose artist and title name a song of the catalog. A live video is only linked to a live track
/// (and a studio video prefers studio tracks); among the candidates, those with a copy here and the closest
/// duration win. Whether the audio really matches is the aligner's call: when it does not, the video keeps its own
/// audio. Links made by a person are never changed.
/// </summary>
public sealed partial class VideoLinker(IArcanistDbContextFactory databases, ILogger<VideoLinker> logger)
{
    /// <summary>Raised after a pass that linked something, so the aligner looks for work.</summary>
    public event Action? Linked;

    public async Task LinkAsync(CancellationToken cancellationToken)
    {
        await using var db = databases.CreateDbContext();
        var videos = await db.MusicVideos
            .Where(v => v.MissingSince == null && !v.LinkLocked)
            .Where(v => v.RecordingId == null || v.TrackId == null || !db.Tracks.Any(t => t.Id == v.TrackId))
            .ToListAsync(cancellationToken);
        var linked = 0;
        foreach (var video in videos)
        {
            if (await FindAsync(db, video, cancellationToken) is not { } link)
                continue;
            video.RecordingId = link.RecordingId;
            video.TrackId = link.TrackId;
            video.LinkMethod = link.Method;
            linked++;
        }

        // A video linked by tags whose track is still there keeps it; one whose track left looks again above.
        if (linked > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Linked {Count} music videos to their songs", linked);
            Linked?.Invoke();
        }
    }

    public sealed record Link(Guid RecordingId, Guid TrackId, VideoLinkMethod Method);

    /// <summary>The song a video shows, or null when the catalog does not have it.</summary>
    public static async Task<Link?> FindAsync(ArcanistDbContext db, MusicVideo video, CancellationToken cancellationToken)
    {
        // Next to its audio: "Album/03 Song.mp4" (or another cut, "03 Song (2).mp4") with "Album/03 Song.flac".
        var stem = CopyNumber().Replace(AudioFormats.WithoutExtension(video.RelativePath), "");
        var sidecar = (await db.MediaFiles.AsNoTracking()
                .Where(f => f.LibrarySourceId == video.LibrarySourceId && f.MissingSince == null && f.RelativePath.StartsWith(stem + "."))
                .Select(f => new { f.RelativePath, f.TrackId, f.Track.RecordingId })
                .ToListAsync(cancellationToken))
            .FirstOrDefault(f => AudioFormats.WithoutExtension(f.RelativePath) == stem && AudioFormats.IsAudio(f.RelativePath));
        if (sidecar is not null)
            return new Link(sidecar.RecordingId, sidecar.TrackId, VideoLinkMethod.Sidecar);

        if (string.IsNullOrWhiteSpace(video.Artist))
            return null;
        var title = VideoTitles.Core(video.Title);
        var artist = VideoTitles.Words(video.Artist);
        if (title.Length == 0 || artist.Length == 0)
            return null;

        // Tracks titled the same, then the artist's tracks whose title is the same without its qualifiers.
        var byTitle = db.Tracks.Where(t => t.NormalizedTitle == video.NormalizedTitle);
        var artistIds = await db.Artists.Where(a => a.NormalizedName == video.NormalizedArtist).Select(a => a.Id).ToListAsync(cancellationToken);
        var byArtist = db.Tracks.Where(t => t.Artists.Any(a => artistIds.Contains(a.ArtistId)) || artistIds.Contains(t.Album.AlbumArtistId));
        var ids = await byTitle.Select(t => t.Id).Union(byArtist.Select(t => t.Id)).OrderBy(id => id).Take(2000).ToListAsync(cancellationToken);
        var candidates = await db.Tracks
            .Where(t => ids.Contains(t.Id) && t.Files.Any(f => f.MissingSince == null))
            .Select(t => new Candidate(
                t.Id,
                t.RecordingId,
                t.Title,
                t.Album.Title,
                t.Album.AlbumArtist.Name,
                t.Artists.Select(a => a.Artist.Name).ToList(),
                t.DurationMs,
                t.Album.IsCompilation,
                t.Album.Year,
                t.Files.Any(f => f.MissingSince == null && (f.LibrarySource.Kind == LibrarySourceKind.Local || f.LibrarySource.Kind == LibrarySourceKind.S3))))
            .ToListAsync(cancellationToken);

        var matches = candidates
            .Where(c => VideoTitles.Core(c.Title) == title)
            .Where(c => VideoTitles.Words(c.AlbumArtist) == artist || c.Artists.Any(a => VideoTitles.Words(a) == artist))
            .ToList();
        // Live only with live; a studio video takes a live track only when nothing else is there.
        if (video.IsLive)
            matches = matches.Where(IsLive).ToList();
        else if (matches.Where(c => !IsLive(c)).ToList() is { Count: > 0 } studio)
            matches = studio;

        var best = matches
            .OrderByDescending(c => c.Readable)
            .ThenBy(c => c.IsCompilation)
            .ThenBy(c => video.DurationMs > 0 ? Math.Abs(c.DurationMs - video.DurationMs) : 0)
            .ThenBy(c => c.Year ?? int.MaxValue)
            .FirstOrDefault();
        return best is null ? null : new Link(best.RecordingId, best.Id, VideoLinkMethod.Tags);
    }

    private static bool IsLive(Candidate candidate) => VideoTitles.IsLive(candidate.Title) || VideoTitles.IsLiveAlbum(candidate.Album);

    /// <param name="Readable">Has a copy the aligner can read (local or S3).</param>
    private sealed record Candidate(
        Guid Id, Guid RecordingId, string Title, string Album, string AlbumArtist, List<string> Artists, int DurationMs, bool IsCompilation, int? Year,
        bool Readable);

    [System.Text.RegularExpressions.GeneratedRegex(@" \(\d+\)$")]
    private static partial System.Text.RegularExpressions.Regex CopyNumber();
}
