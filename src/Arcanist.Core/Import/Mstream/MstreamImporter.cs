using System.Text;
using PlaylistService = Arcanist.Core.Application.Playlists.PlaylistService;
using Arcanist.Core.Data;
using Arcanist.Core.Data.Entities;
using Arcanist.Core.Data.Search;
using Microsoft.EntityFrameworkCore;

namespace Arcanist.Core.Import.Mstream;

/// <summary>What an import brought for a user.</summary>
internal sealed record MstreamImportCounts(int Playlists, int Ratings, int Plays, int Unmatched);

/// <summary>
/// Brings a user's mStream data into Arcanist: playlists, ratings and play counts, matching mStream's files to the
/// catalog by path. Safe to repeat: playlists whose name the user already has are left alone, ratings only fill
/// gaps and play counts keep the higher value.
/// </summary>
internal sealed class MstreamImporter(TimeProvider time)
{
    /// <param name="vpaths">The library folder each mStream vpath became.</param>
    public async Task<MstreamImportCounts> ApplyAsync(
        ArcanistDbContext db, Guid userId, MstreamUserContent content, IReadOnlyDictionary<string, Guid> vpaths, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var paths = new PathResolver(db, vpaths);
        var unmatched = 0;
        var rated = 0;
        var played = 0;

        var ratings = await db.TrackRatings.Where(r => r.UserId == userId).Select(r => r.TrackId).ToHashSetAsync(cancellationToken);
        var stats = await db.TrackPlayStats.Where(s => s.UserId == userId).ToDictionaryAsync(s => s.TrackId, cancellationToken);
        foreach (var track in content.Tracks)
        {
            if (await paths.TrackOfAsync(track.Path, cancellationToken) is not { } trackId)
            {
                unmatched++;
                continue;
            }

            // mStream rates 0–10: half stars.
            if (track.Rating is > 0 and var rating && ratings.Add(trackId))
            {
                db.TrackRatings.Add(new TrackRating { UserId = userId, TrackId = trackId, Rating = Math.Clamp((rating + 1) / 2, 1, 5), RatedAt = now });
                rated++;
            }

            if (track.PlayCount > 0)
            {
                if (!stats.TryGetValue(trackId, out var stat))
                    db.TrackPlayStats.Add(stats[trackId] = stat = new TrackPlayStats { UserId = userId, TrackId = trackId });
                stat.PlayCount = Math.Max(stat.PlayCount, track.PlayCount);
                if (track.LastPlayedAt is { } at && (stat.LastPlayedAt is null || stat.LastPlayedAt < at))
                    stat.LastPlayedAt = at;
                played++;
            }
        }

        var names = (await db.Playlists.Where(p => p.OwnerId == userId).Select(p => p.Name).ToListAsync(cancellationToken)).ToHashSet(StringComparer.Ordinal);
        var playlists = 0;
        foreach (var source in content.Playlists)
        {
            var name = source.Name.Trim() is { Length: > 0 } trimmed ? trimmed[..Math.Min(trimmed.Length, PlaylistService.NameMaxLength)] : "mStream";
            if (!names.Add(name))
                continue;

            var playlist = new Playlist
            {
                OwnerId = userId,
                Name = name,
                NormalizedName = TextNormalizer.Normalize(name),
                SearchText = FullTextSearch.Text(name),
                CreatedAt = now,
                UpdatedAt = now,
            };
            foreach (var path in source.Paths.Take(PlaylistService.MaxEntries))
            {
                if (await paths.TrackOfAsync(path, cancellationToken) is { } trackId)
                {
                    playlist.Entries.Add(new PlaylistEntry
                    {
                        PlaylistId = playlist.Id, TrackId = trackId, Position = playlist.Entries.Count, AddedAt = now, AddedByUserId = userId,
                    });
                }
                else
                    unmatched++;
            }

            db.Playlists.Add(playlist);
            playlists++;
        }

        await db.SaveChangesAsync(cancellationToken);
        return new MstreamImportCounts(playlists, rated, played, unmatched);
    }

    /// <summary>
    /// Finds the track of a vpath path. Paths match exactly or, failing that, ignoring case and Unicode form (mStream
    /// keeps paths as clients sent them).
    /// </summary>
    private sealed class PathResolver(ArcanistDbContext db, IReadOnlyDictionary<string, Guid> vpaths)
    {
        private readonly Dictionary<Guid, (Dictionary<string, Guid> Exact, Dictionary<string, Guid> Loose)> _sources = [];

        public async Task<Guid?> TrackOfAsync(string path, CancellationToken cancellationToken)
        {
            var trimmed = path.Replace('\\', '/').TrimStart('/');
            var slash = trimmed.IndexOf('/');
            if (slash <= 0 || !vpaths.TryGetValue(trimmed[..slash], out var sourceId))
                return null;

            if (!_sources.TryGetValue(sourceId, out var files))
            {
                var rows = await db.MediaFiles.AsNoTracking().Where(f => f.LibrarySourceId == sourceId && f.MissingSince == null)
                    .Select(f => new { f.RelativePath, f.CueTrack, f.TrackId })
                    .ToListAsync(cancellationToken);
                files = (new Dictionary<string, Guid>(StringComparer.Ordinal), new Dictionary<string, Guid>(StringComparer.Ordinal));
                // A file split by a cue sheet is matched to its first track.
                foreach (var row in rows.OrderBy(r => r.CueTrack))
                {
                    files.Exact.TryAdd(row.RelativePath, row.TrackId);
                    files.Loose.TryAdd(Loose(row.RelativePath), row.TrackId);
                }

                _sources[sourceId] = files;
            }

            var relative = trimmed[(slash + 1)..];
            return files.Exact.TryGetValue(relative, out var exact) ? exact
                : files.Loose.TryGetValue(Loose(relative), out var loose) ? loose
                : null;
        }

        private static string Loose(string path) => path.Normalize(NormalizationForm.FormC).ToLowerInvariant();
    }
}
