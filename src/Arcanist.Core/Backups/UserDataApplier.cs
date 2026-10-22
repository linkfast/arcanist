using Arcanist.Core.Application.Catalog;
using Arcanist.Core.Data;
using Arcanist.Core.Data.Entities;
using Arcanist.Core.Data.Search;
using Arcanist.Core.Scanning;
using Microsoft.EntityFrameworkCore;

namespace Arcanist.Core.Backups;

/// <summary>How much user data was brought in.</summary>
public sealed class UserDataCounts
{
    public int Ratings { get; set; }

    public int PlayCounts { get; set; }

    public int Plays { get; set; }

    public int Playlists { get; set; }

    /// <summary>References to songs this server does not have.</summary>
    public int Unmatched { get; set; }
}

/// <summary>
/// Brings user data in the backups' format (<see cref="BackupUserData"/>, with its <see cref="BackupCatalog"/>) into this
/// server: restoring a backup and migrating from another Arcanist. Safe to repeat.
/// </summary>
internal static class UserDataApplier
{
    /// <summary>The tracks of this server that are the backup's: by audio, MusicBrainz recording, AcoustID and tags.</summary>
    public static async Task<Dictionary<Guid, Guid>> MatchAsync(ArcanistDbContext db, BackupCatalog catalog, CancellationToken cancellationToken)
    {
        var albums = catalog.Albums.ToDictionary(a => a.Id);
        var matches = new Dictionary<Guid, Guid>();
        var present = db.Tracks.Where(t => t.Files.Any(f => f.MissingSince == null));
        foreach (var chunk in catalog.Tracks.Chunk(250))
        {
            var candidates = chunk.SelectMany(t =>
            {
                var album = albums.GetValueOrDefault(t.AlbumId);
                var artist = t.Artists.FirstOrDefault() ?? album?.Artist;
                return (t.ContentHashes.Count == 0 ? [null] : t.ContentHashes.Select(h => (string?)h))
                    .Select(hash => new TrackCandidate(t.Id.ToString(), hash, t.AcoustId?.ToString(), t.MusicBrainzId, t.Title, artist, album?.Title,
                        t.Disc, t.Number, t.DurationMs));
            }).ToList();
            foreach (var (key, found) in await TrackMatchService.FindAsync(present, candidates, cancellationToken))
                matches[Guid.Parse(key)] = found.TrackId;
        }

        return matches;
    }

    /// <summary>
    /// One user's ratings (filling gaps), play counts (the highest), plays (once each: <paramref name="externalPrefix"/>
    /// and the play's id) and playlists (those whose name the owner does not have). The caller saves.
    /// </summary>
    public static async Task ApplyUserAsync(
        ArcanistDbContext db, UserDataCounts job, BackupUser user, Guid userId, Dictionary<Guid, Guid> matches, Func<string, Guid?> userOf,
        string externalPrefix, string playSource, CancellationToken cancellationToken)
    {
        // Ratings only fill gaps.
        var rated = (await db.TrackRatings.Where(r => r.UserId == userId).Select(r => r.TrackId).ToListAsync(cancellationToken)).ToHashSet();
        foreach (var rating in user.Ratings)
        {
            if (!matches.TryGetValue(rating.Track, out var trackId))
                job.Unmatched++;
            else if (rated.Add(trackId))
            {
                db.TrackRatings.Add(new TrackRating { UserId = userId, TrackId = trackId, Rating = Math.Clamp(rating.Rating, 1, 5), RatedAt = rating.RatedAt });
                job.Ratings++;
            }
        }

        // Play counts keep the highest.
        var stats = await db.TrackPlayStats.Where(s => s.UserId == userId).ToDictionaryAsync(s => s.TrackId, cancellationToken);
        foreach (var count in user.PlayCounts)
        {
            if (!matches.TryGetValue(count.Track, out var trackId))
            {
                job.Unmatched++;
                continue;
            }

            if (stats.TryGetValue(trackId, out var existing))
            {
                if (count.PlayCount <= existing.PlayCount && count.SkipCount <= existing.SkipCount && count.LastPlayedAt <= existing.LastPlayedAt)
                    continue;
                existing.PlayCount = Math.Max(existing.PlayCount, count.PlayCount);
                existing.SkipCount = Math.Max(existing.SkipCount, count.SkipCount);
                existing.LastPlayedAt = existing.LastPlayedAt is { } last && count.LastPlayedAt is { } other ? (last > other ? last : other) : existing.LastPlayedAt ?? count.LastPlayedAt;
            }
            else
            {
                stats[trackId] = db.TrackPlayStats.Add(new TrackPlayStats
                {
                    UserId = userId, TrackId = trackId, PlayCount = count.PlayCount, SkipCount = count.SkipCount, LastPlayedAt = count.LastPlayedAt,
                }).Entity;
            }

            job.PlayCounts++;
        }

        // Plays are added once: each remembers the play it was in the backup.
        var known = (await db.PlayEvents.Where(p => p.UserId == userId && p.ExternalId != null && p.ExternalId.StartsWith(externalPrefix))
            .Select(p => p.ExternalId!).ToListAsync(cancellationToken)).ToHashSet(StringComparer.Ordinal);
        foreach (var play in user.Plays)
        {
            var externalId = $"{externalPrefix}{play.Id:N}";
            if (!matches.TryGetValue(play.Track, out var trackId))
                job.Unmatched++;
            else if (known.Add(externalId))
            {
                db.PlayEvents.Add(new PlayEvent
                {
                    UserId = userId, TrackId = trackId, PlayedAt = play.PlayedAt, PlayedMs = play.PlayedMs, Counted = play.Counted, Skipped = play.Skipped,
                    Client = play.Client, ExternalId = externalId, Source = playSource,
                });
                job.Plays++;
            }
        }

        // Playlists come back when their owner has none of that name.
        var names = (await db.Playlists.Where(p => p.OwnerId == userId).Select(p => p.NormalizedName).ToListAsync(cancellationToken)).ToHashSet(StringComparer.Ordinal);
        var now = DateTime.UtcNow;
        foreach (var source in user.Playlists)
        {
            var normalized = TextNormalizer.Normalize(source.Name);
            if (!names.Add(normalized))
                continue;
            var playlist = new Playlist
            {
                OwnerId = userId,
                Name = source.Name,
                NormalizedName = normalized,
                Description = source.Description,
                SearchText = FullTextSearch.Text(source.Name, source.Description),
                IsPublic = source.IsPublic,
                CreatedAt = source.CreatedAt,
                UpdatedAt = now,
            };
            foreach (var entry in source.Entries.Take(Application.Playlists.PlaylistService.MaxEntries))
            {
                if (!matches.TryGetValue(entry.Track, out var trackId))
                {
                    job.Unmatched++;
                    continue;
                }

                playlist.Entries.Add(new PlaylistEntry
                {
                    PlaylistId = playlist.Id, TrackId = trackId, Position = playlist.Entries.Count, AddedAt = entry.AddedAt,
                    AddedByUserId = entry.AddedBy is { } by ? userOf(by) : null,
                });
            }

            foreach (var collaborator in source.Collaborators.Select(userOf).OfType<Guid>().Where(id => id != userId).Distinct())
                playlist.Collaborators.Add(new PlaylistCollaborator { PlaylistId = playlist.Id, UserId = collaborator, AddedAt = now });
            db.Playlists.Add(playlist);
            job.Playlists++;
        }
    }
}
