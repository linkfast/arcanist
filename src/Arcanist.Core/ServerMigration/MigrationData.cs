using Arcanist.Core.Backups;
using Arcanist.Core.Data;
using Arcanist.Core.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Arcanist.Core.ServerMigration;

/// <summary>
/// Users' data in the backups' format (docs/backups.md), for the export API: what a migration brings and applies
/// with <see cref="UserDataApplier"/>, as restoring a backup does. Tracks are named by their id here and described in
/// the catalog, so the other server recognises them by audio hash, MusicBrainz, AcoustID or tags.
/// </summary>
internal static class MigrationData
{
    /// <summary>All the data of <paramref name="userIds"/>: ratings, play counts, plays and the playlists they own.</summary>
    public static async Task<BackupUserData> UserDataAsync(ArcanistDbContext db, IReadOnlySet<Guid> userIds, CancellationToken cancellationToken)
    {
        var ids = userIds.ToList();
        var users = await db.Users.AsNoTracking().OrderBy(u => u.UserName).Select(u => new { u.Id, u.UserName, u.DisplayName }).ToListAsync(cancellationToken);
        var names = users.ToDictionary(u => u.Id, u => u.UserName);

        var ratings = await db.TrackRatings.AsNoTracking().Where(r => ids.Contains(r.UserId)).ToListAsync(cancellationToken);
        var counts = await db.TrackPlayStats.AsNoTracking().Where(s => ids.Contains(s.UserId)).ToListAsync(cancellationToken);
        var plays = await db.PlayEvents.AsNoTracking().Where(p => ids.Contains(p.UserId) && p.TrackId != null)
            .OrderBy(p => p.PlayedAt).ToListAsync(cancellationToken);
        var playlists = await db.Playlists.AsNoTracking()
            .Where(p => ids.Contains(p.OwnerId))
            .Include(p => p.Entries)
            .Include(p => p.Collaborators)
            .ToListAsync(cancellationToken);

        return new BackupUserData(BackupFormat.Version, users.Where(u => userIds.Contains(u.Id)).Select(u => new BackupUser(
                u.Id,
                u.UserName,
                u.DisplayName,
                ratings.Where(r => r.UserId == u.Id).Select(r => new BackupRating(r.TrackId, r.Rating, r.RatedAt)).ToList(),
                counts.Where(c => c.UserId == u.Id).Select(c => new BackupPlayCount(c.TrackId, c.PlayCount, c.SkipCount, c.LastPlayedAt)).ToList(),
                plays.Where(p => p.UserId == u.Id).Select(p => new BackupPlay(p.Id, p.TrackId!.Value, p.PlayedAt, p.PlayedMs, p.Counted, p.Skipped, p.Client)).ToList(),
                playlists.Where(p => p.OwnerId == u.Id).OrderBy(p => p.Name).Select(p => new BackupPlaylist(
                    p.Id, p.Name, p.Description, p.IsPublic, p.CreatedAt,
                    p.Collaborators.Select(c => names.GetValueOrDefault(c.UserId)).OfType<string>().ToList(),
                    p.Entries.OrderBy(e => e.Position)
                        .Select(e => new BackupPlaylistEntry(e.TrackId, e.AddedAt, e.AddedByUserId is { } by ? names.GetValueOrDefault(by) : null)).ToList())).ToList()))
            .ToList());
    }

    /// <summary>What recognises <paramref name="trackIds"/> on another server: titles, artists, albums, numbers, ids and audio hashes.</summary>
    public static async Task<BackupCatalog> CatalogAsync(ArcanistDbContext db, IReadOnlySet<Guid> trackIds, CancellationToken cancellationToken)
    {
        var tracks = new List<BackupCatalogTrack>();
        var albumIds = new HashSet<Guid>();
        foreach (var chunk in trackIds.Chunk(1000))
        {
            var rows = await db.Tracks.AsNoTracking()
                .Where(t => chunk.Contains(t.Id))
                .Select(t => new { t.Id, t.Title, t.AlbumId, t.DiscNumber, t.TrackNumber, t.DurationMs, t.Recording.MusicBrainzId, t.Recording.AcoustId })
                .ToListAsync(cancellationToken);
            var artists = (await db.TrackArtists.AsNoTracking()
                    .Where(a => chunk.Contains(a.TrackId) && (a.Role == ArtistRole.Primary || a.Role == ArtistRole.Featured))
                    .Select(a => new { a.TrackId, a.Position, a.Artist.Name })
                    .ToListAsync(cancellationToken))
                .GroupBy(a => a.TrackId)
                .ToDictionary(g => g.Key, g => g.OrderBy(a => a.Position).Select(a => a.Name).ToList());
            var hashes = (await db.MediaFiles.AsNoTracking()
                    .Where(f => chunk.Contains(f.TrackId) && f.MissingSince == null)
                    .Select(f => new { f.TrackId, f.ContentHash })
                    .ToListAsync(cancellationToken))
                .GroupBy(f => f.TrackId)
                .ToDictionary(g => g.Key, g => g.Select(f => f.ContentHash).Distinct().ToList());
            foreach (var row in rows)
            {
                albumIds.Add(row.AlbumId);
                tracks.Add(new BackupCatalogTrack(row.Id, row.Title, artists.GetValueOrDefault(row.Id) ?? [], row.AlbumId, row.DiscNumber, row.TrackNumber,
                    row.DurationMs, row.MusicBrainzId, row.AcoustId, hashes.GetValueOrDefault(row.Id) ?? [], [], null));
            }
        }

        var albums = new List<BackupCatalogAlbum>();
        foreach (var chunk in albumIds.Chunk(1000))
        {
            albums.AddRange(await db.Albums.AsNoTracking()
                .Where(a => chunk.Contains(a.Id))
                .Select(a => new BackupCatalogAlbum(a.Id, a.Title, a.AlbumArtist.Name, a.Year, a.MusicBrainzReleaseId, null))
                .ToListAsync(cancellationToken));
        }

        return new BackupCatalog(BackupFormat.Version, tracks, albums);
    }
}
