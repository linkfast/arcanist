using Arcanist.Core.Data;
using Microsoft.EntityFrameworkCore;

namespace Arcanist.Core.Scanning;

/// <summary>
/// Removes files missing for longer than the retention period, then everything left without files: tracks,
/// albums, recordings, artists, genres, audio analyses and artwork (including cached images).
/// </summary>
internal sealed class CatalogCleaner(ArcanistDbContext db, ArtworkStore artworkStore, TimeProvider time)
{
    public async Task CleanAsync(TimeSpan missingRetention, CancellationToken cancellationToken)
    {
        var cutoff = time.GetUtcNow().UtcDateTime - missingRetention;
        await db.MediaFiles.Where(f => f.MissingSince != null && f.MissingSince < cutoff).ExecuteDeleteAsync(cancellationToken);

        await db.Tracks.Where(t => !t.Files.Any()).ExecuteDeleteAsync(cancellationToken);
        await db.Albums.Where(a => !a.Tracks.Any()).ExecuteDeleteAsync(cancellationToken);
        await db.Recordings.Where(r => !r.Tracks.Any()).ExecuteDeleteAsync(cancellationToken);
        await db.Artists
            .Where(a => !db.Albums.Any(al => al.AlbumArtistId == a.Id) && !db.TrackArtists.Any(ta => ta.ArtistId == a.Id))
            .ExecuteDeleteAsync(cancellationToken);
        await db.Genres.Where(g => !db.TrackGenres.Any(tg => tg.GenreId == g.Id)).ExecuteDeleteAsync(cancellationToken);
        await db.DuplicateCandidates
            .Where(c => !db.Tracks.Any(t => t.Id == c.TrackAId) || !db.Tracks.Any(t => t.Id == c.TrackBId))
            .ExecuteDeleteAsync(cancellationToken);
        // Analyses belong to the audio, not to a file: kept while any copy of that audio is in the catalog.
        await db.AudioAnalyses.Where(a => !db.MediaFiles.Any(f => f.ContentHash == a.ContentHash)).ExecuteDeleteAsync(cancellationToken);

        // Music videos: missing ones after the same retention; links to songs that left the catalog; alignments
        // whose video or audio no copy has any more.
        await db.MusicVideos.Where(v => v.MissingSince != null && v.MissingSince < cutoff).ExecuteDeleteAsync(cancellationToken);
        await db.MusicVideos.Where(v => v.TrackId != null && !db.Tracks.Any(t => t.Id == v.TrackId))
            .ExecuteUpdateAsync(s => s.SetProperty(v => v.TrackId, (Guid?)null), cancellationToken);
        await db.MusicVideos.Where(v => v.RecordingId != null && !db.Recordings.Any(r => r.Id == v.RecordingId))
            .ExecuteUpdateAsync(s => s
                .SetProperty(v => v.RecordingId, (Guid?)null)
                .SetProperty(v => v.LinkMethod, (Data.Entities.VideoLinkMethod?)null)
                .SetProperty(v => v.LinkLocked, false), cancellationToken);
        await db.VideoAlignments
            .Where(a => !db.MusicVideos.Any(v => v.ContentHash == a.VideoHash) || !db.MediaFiles.Any(f => f.ContentHash == a.AudioHash))
            .ExecuteDeleteAsync(cancellationToken);
        await db.VideoAlignmentRequests.Where(r => !db.MusicVideos.Any(v => v.Id == r.VideoId)).ExecuteDeleteAsync(cancellationToken);
        await db.VideoLyricsTimings.Where(t => !db.MusicVideos.Any(v => v.ContentHash == t.VideoHash)).ExecuteDeleteAsync(cancellationToken);

        var unusedArtwork = await db.Artworks
            .Where(a => !db.Albums.Any(al => al.ArtworkId == a.Id) && !db.Tracks.Any(t => t.ArtworkId == a.Id)
                        && !db.WishListItems.Any(w => w.ArtworkId == a.Id) && !db.MusicVideos.Any(v => v.ArtworkId == a.Id))
            .Select(a => new { a.Id, a.Hash, a.MimeType })
            .ToListAsync(cancellationToken);

        if (unusedArtwork.Count > 0)
        {
            var ids = unusedArtwork.Select(a => a.Id).ToList();
            await db.Artworks.Where(a => ids.Contains(a.Id)).ExecuteDeleteAsync(cancellationToken);
            foreach (var artwork in unusedArtwork)
                artworkStore.Delete(artwork.Hash, artwork.MimeType);
        }
    }
}
