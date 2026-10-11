using Arcanist.Core.Auth;
using Arcanist.Core.Data;
using Arcanist.Core.Data.Entities;
using Microsoft.EntityFrameworkCore;
using AlbumEntity = Arcanist.Core.Data.Entities.Album;
using ArtistEntity = Arcanist.Core.Data.Entities.Artist;
using TrackEntity = Arcanist.Core.Data.Entities.Track;

namespace Arcanist.Core.Application.Catalog;

/// <summary>
/// Browsing the catalog. Everything is filtered to what the caller can play: a track is visible when one of its
/// files is present in a library the caller can access, and albums and artists are visible through such tracks.
/// Lists use keyset pagination, which stays fast and stable while scans add music.
/// </summary>
public sealed class CatalogService(ArcanistDbContext db, AccessGuard guard)
{
    public const int DefaultPageSize = 50;
    public const int MaxPageSize = 500;

    public async Task<ArtistPage> ArtistsAsync(string? search, string? after, int? first, CancellationToken cancellationToken)
    {
        var visible = VisibleTracks(guard.Authenticated());
        var visibleAlbums = db.Albums.Where(a => visible.Any(t => t.AlbumId == a.Id));

        // Performers and album artists; composers and other credits only appear on tracks.
        var artists = db.Artists.Where(a =>
            visibleAlbums.Any(al => al.AlbumArtistId == a.Id)
            || db.TrackArtists.Any(ta => ta.ArtistId == a.Id
                                         && (ta.Role == ArtistRole.Primary || ta.Role == ArtistRole.Featured)
                                         && visible.Any(t => t.Id == ta.TrackId)));

        if (Search(search) is { } term)
            artists = artists.Where(a => a.NormalizedName.Contains(term));

        var (page, next, total) = await Keyset.PageAsync(artists, a => a.NormalizedName, after, first, cancellationToken);
        return new ArtistPage(page.Select(ToModel).ToList(), next, total);
    }

    public async Task<Artist> ArtistAsync(Guid id, CancellationToken cancellationToken)
    {
        var visible = VisibleTracks(guard.Authenticated());
        var artist = await db.Artists.AsNoTracking()
            .Where(a => a.Id == id)
            .Where(a => db.Albums.Any(al => al.AlbumArtistId == a.Id && visible.Any(t => t.AlbumId == al.Id))
                        || db.TrackArtists.Any(ta => ta.ArtistId == a.Id && visible.Any(t => t.Id == ta.TrackId)))
            .SingleOrDefaultAsync(cancellationToken);

        return artist is null ? throw new ArcanistException.NotFound() : ToModel(artist);
    }

    /// <param name="artistId">Albums by this album artist or with tracks performed by them.</param>
    public async Task<AlbumPage> AlbumsAsync(string? search, Guid? artistId, string? after, int? first, CancellationToken cancellationToken)
    {
        var visible = VisibleTracks(guard.Authenticated());
        var albums = db.Albums.Where(a => visible.Any(t => t.AlbumId == a.Id));

        if (artistId is { } artist)
        {
            albums = albums.Where(a => a.AlbumArtistId == artist
                                       || a.Tracks.Any(t => t.Artists.Any(ta => ta.ArtistId == artist
                                                                              && (ta.Role == ArtistRole.Primary || ta.Role == ArtistRole.Featured))));
        }

        if (Search(search) is { } term)
            albums = albums.Where(a => a.NormalizedTitle.Contains(term));

        var (page, next, total) = await Keyset.PageAsync(albums, a => a.NormalizedTitle, after, first, cancellationToken);
        return new AlbumPage(await ToModelsAsync(page, visible, cancellationToken), next, total);
    }

    public async Task<Album> AlbumAsync(Guid id, CancellationToken cancellationToken)
    {
        var visible = VisibleTracks(guard.Authenticated());
        var album = await db.Albums.AsNoTracking()
            .Where(a => a.Id == id && visible.Any(t => t.AlbumId == a.Id))
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new ArcanistException.NotFound();

        return (await ToModelsAsync([album], visible, cancellationToken))[0];
    }

    /// <param name="albumId">When set, all of the album's tracks are returned at once, in disc and track order.</param>
    public async Task<TrackPage> TracksAsync(
        string? search, Guid? albumId, Guid? artistId, string? after, int? first, CancellationToken cancellationToken)
    {
        var access = guard.Authenticated();
        var tracks = VisibleTracks(access);

        if (artistId is { } artist)
            tracks = tracks.Where(t => t.Artists.Any(ta => ta.ArtistId == artist));

        if (Search(search) is { } term)
            tracks = tracks.Where(t => t.NormalizedTitle.Contains(term));

        if (albumId is { } album)
        {
            var albumTracks = await tracks.AsNoTracking()
                .Where(t => t.AlbumId == album)
                .OrderBy(t => t.DiscNumber ?? 1).ThenBy(t => t.TrackNumber).ThenBy(t => t.NormalizedTitle)
                .ToListAsync(cancellationToken);

            return new TrackPage(await ToModelsAsync(albumTracks, access, cancellationToken), null, albumTracks.Count);
        }

        var (page, next, total) = await Keyset.PageAsync(tracks, t => t.NormalizedTitle, after, first, cancellationToken);
        return new TrackPage(await ToModelsAsync(page, access, cancellationToken), next, total);
    }

    public async Task<Track> TrackAsync(Guid id, CancellationToken cancellationToken)
    {
        var access = guard.Authenticated();
        var track = await VisibleTracks(access).AsNoTracking().SingleOrDefaultAsync(t => t.Id == id, cancellationToken)
                    ?? throw new ArcanistException.NotFound();

        return (await ToModelsAsync([track], access, cancellationToken))[0];
    }

    /// <summary>Files the caller may play: present (not missing) and in an accessible library.</summary>
    internal IQueryable<MediaFile> VisibleFiles(UserAccess access)
    {
        var files = db.MediaFiles.Where(f => f.MissingSince == null);
        if (access.IsAdmin)
            return files;

        var libraries = access.LibraryIds.ToList();
        return files.Where(f => libraries.Contains(f.LibrarySource.LibraryId));
    }

    private IQueryable<TrackEntity> VisibleTracks(UserAccess access)
    {
        var files = VisibleFiles(access);
        return db.Tracks.Where(t => files.Any(f => f.TrackId == t.Id));
    }

    private async Task<IReadOnlyList<Album>> ToModelsAsync(IReadOnlyList<AlbumEntity> albums, IQueryable<TrackEntity> visible, CancellationToken cancellationToken)
    {
        var ids = albums.Select(a => a.Id).ToList();
        var artistIds = albums.Select(a => a.AlbumArtistId).Distinct().ToList();

        var artists = await db.Artists.AsNoTracking().Where(a => artistIds.Contains(a.Id))
            .ToDictionaryAsync(a => a.Id, a => a.Name, cancellationToken);
        var stats = await visible.Where(t => ids.Contains(t.AlbumId))
            .GroupBy(t => t.AlbumId)
            .Select(g => new { AlbumId = g.Key, Count = g.Count(), Duration = g.Sum(t => (long)t.DurationMs) })
            .ToDictionaryAsync(s => s.AlbumId, cancellationToken);

        return albums.Select(a => new Album(
            a.Id,
            a.Title,
            new ArtistReference(a.AlbumArtistId, artists.GetValueOrDefault(a.AlbumArtistId, "")),
            a.Year,
            a.IsCompilation,
            a.MusicBrainzReleaseId,
            a.ArtworkId,
            stats.TryGetValue(a.Id, out var s) ? s.Count : 0,
            stats.TryGetValue(a.Id, out var d) ? d.Duration : 0)).ToList();
    }

    private async Task<IReadOnlyList<Track>> ToModelsAsync(IReadOnlyList<TrackEntity> tracks, UserAccess access, CancellationToken cancellationToken)
    {
        var ids = tracks.Select(t => t.Id).ToList();
        var albumIds = tracks.Select(t => t.AlbumId).Distinct().ToList();

        var albums = await db.Albums.AsNoTracking().Where(a => albumIds.Contains(a.Id))
            .Select(a => new { a.Id, a.Title, a.ArtworkId })
            .ToDictionaryAsync(a => a.Id, cancellationToken);

        var credits = (await db.TrackArtists.AsNoTracking().Where(ta => ids.Contains(ta.TrackId))
                .Select(ta => new { ta.TrackId, ta.ArtistId, ta.Artist.Name, ta.Role, ta.Position })
                .ToListAsync(cancellationToken))
            .ToLookup(c => c.TrackId);

        var genres = (await db.TrackGenres.AsNoTracking().Where(tg => ids.Contains(tg.TrackId))
                .Select(tg => new { tg.TrackId, tg.Genre.Name })
                .ToListAsync(cancellationToken))
            .ToLookup(g => g.TrackId, g => g.Name);

        var files = (await VisibleFiles(access).AsNoTracking().Where(f => ids.Contains(f.TrackId)).ToListAsync(cancellationToken))
            .ToLookup(f => f.TrackId);

        return tracks.Select(t =>
        {
            var album = albums[t.AlbumId];
            return new Track(
                t.Id,
                t.Title,
                new AlbumReference(album.Id, album.Title),
                credits[t.Id].OrderBy(c => c.Position).Select(c => new ArtistCredit(c.ArtistId, c.Name, c.Role)).ToList(),
                t.DiscNumber,
                t.TrackNumber,
                t.DurationMs,
                t.ArtworkId ?? album.ArtworkId,
                genres[t.Id].Order().ToList(),
                t.AddedAt,
                files[t.Id]
                    .OrderByDescending(f => f.BitDepth ?? 0).ThenByDescending(f => f.SampleRate ?? 0).ThenByDescending(f => f.BitRate ?? 0)
                    .Select(f => new TrackFile(f.Id, f.Format, f.BitRate, f.SampleRate, f.BitDepth, f.Channels, f.Size, f.DurationMs,
                        f.ReplayGainTrackGain, f.ReplayGainAlbumGain))
                    .ToList());
        }).ToList();
    }

    private static Artist ToModel(ArtistEntity artist) => new(artist.Id, artist.Name, artist.SortName, artist.MusicBrainzId);

    private static string? Search(string? search) =>
        string.IsNullOrWhiteSpace(search) ? null : TextNormalizer.Normalize(search);
}
