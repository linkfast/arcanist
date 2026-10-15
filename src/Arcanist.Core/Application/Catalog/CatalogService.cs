using Arcanist.Core.Auth;
using Arcanist.Core.Data;
using Arcanist.Core.Data.Entities;
using Arcanist.Core.Data.Search;
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
public sealed class CatalogService(
    ArcanistDbContext db,
    AccessGuard guard,
    StreamTokens streamTokens,
    Listening.RatingSettings ratings,
    Core.Extensions.ExtensionHost extensions)
{
    public const int DefaultPageSize = 50;
    public const int MaxPageSize = 500;

    /// <param name="libraryId">Only artists with music in this library.</param>
    public async Task<ArtistPage> ArtistsAsync(string? search, string? after, int? first, CancellationToken cancellationToken, Guid? libraryId = null)
    {
        var artists = Matching(VisibleArtists(VisibleTracks(guard.Authenticated(), libraryId)), SearchKind.Artists, search);

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
    /// <param name="libraryId">Only albums with music in this library (their track counts are those of the library).</param>
    public async Task<AlbumPage> AlbumsAsync(string? search, Guid? artistId, string? after, int? first, CancellationToken cancellationToken, Guid? libraryId = null)
    {
        var visible = VisibleTracks(guard.Authenticated(), libraryId);
        var albums = db.Albums.Where(a => visible.Any(t => t.AlbumId == a.Id));

        if (artistId is { } artist)
        {
            albums = albums.Where(a => a.AlbumArtistId == artist
                                       || a.Tracks.Any(t => t.Artists.Any(ta => ta.ArtistId == artist
                                                                              && (ta.Role == ArtistRole.Primary || ta.Role == ArtistRole.Featured))));
        }

        albums = Matching(albums, SearchKind.Albums, search);

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
    /// <param name="libraryId">Only tracks with a copy in this library.</param>
    public async Task<TrackPage> TracksAsync(
        string? search, Guid? albumId, Guid? artistId, string? after, int? first, CancellationToken cancellationToken, Guid? libraryId = null)
    {
        var access = guard.Authenticated();
        var tracks = VisibleTracks(access, libraryId);

        if (artistId is { } artist)
            tracks = tracks.Where(t => t.Artists.Any(ta => ta.ArtistId == artist));

        tracks = Matching(tracks, SearchKind.Tracks, search);

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

    /// <summary>The tracks with these ids that <paramref name="access"/> can see, by id.</summary>
    internal async Task<Dictionary<Guid, Track>> VisibleTracksAsync(UserAccess access, IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
    {
        var tracks = await VisibleTracks(access).AsNoTracking().Where(t => ids.Contains(t.Id)).ToListAsync(cancellationToken);
        return (await ToModelsAsync(tracks, access, cancellationToken)).ToDictionary(t => t.Id);
    }

    /// <summary>The albums with these ids that <paramref name="access"/> can see, by id.</summary>
    internal async Task<Dictionary<Guid, Album>> VisibleAlbumsAsync(UserAccess access, IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
    {
        var visible = VisibleTracks(access);
        var albums = await db.Albums.AsNoTracking().Where(a => ids.Contains(a.Id) && visible.Any(t => t.AlbumId == a.Id)).ToListAsync(cancellationToken);
        return (await ToModelsAsync(albums, visible, cancellationToken)).ToDictionary(a => a.Id);
    }

    /// <summary>
    /// Artists <paramref name="access"/> can see: performers and album artists of visible tracks. Composers and
    /// other credits only appear on tracks.
    /// </summary>
    internal IQueryable<ArtistEntity> VisibleArtists(UserAccess access) => VisibleArtists(VisibleTracks(access));

    private IQueryable<ArtistEntity> VisibleArtists(IQueryable<TrackEntity> visible)
    {
        var visibleAlbums = db.Albums.Where(a => visible.Any(t => t.AlbumId == a.Id));
        return db.Artists.Where(a =>
            visibleAlbums.Any(al => al.AlbumArtistId == a.Id)
            || db.TrackArtists.Any(ta => ta.ArtistId == a.Id
                                         && (ta.Role == ArtistRole.Primary || ta.Role == ArtistRole.Featured)
                                         && visible.Any(t => t.Id == ta.TrackId)));
    }

    /// <summary>Albums <paramref name="access"/> can see: those with a visible track.</summary>
    internal IQueryable<AlbumEntity> VisibleAlbums(UserAccess access)
    {
        var visible = VisibleTracks(access);
        return db.Albums.Where(a => visible.Any(t => t.AlbumId == a.Id));
    }

    /// <summary>Tracks <paramref name="access"/> can play, for queries built elsewhere.</summary>
    internal IQueryable<TrackEntity> Visible(UserAccess access, Guid? libraryId = null) => VisibleTracks(access, libraryId);

    /// <summary>Turns track rows <paramref name="access"/> can see into contract tracks, in the same order.</summary>
    internal Task<IReadOnlyList<Track>> ToModelsAsync(IReadOnlyList<TrackEntity> tracks, CancellationToken cancellationToken) =>
        ToModelsAsync(tracks, guard.Authenticated(), cancellationToken);

    /// <summary>Turns album rows <paramref name="access"/> can see into contract albums, in the same order.</summary>
    internal Task<IReadOnlyList<Album>> ToModelsAsync(IReadOnlyList<AlbumEntity> albums, UserAccess access, CancellationToken cancellationToken) =>
        ToModelsAsync(albums, VisibleTracks(access), cancellationToken);

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
        // Ultimate Cloud's libraries cannot be played while its extension is off: they are not shown either.
        if (!extensions.IsEnabled(Core.Extensions.UltimateCloud.UltimateCloudExtension.Id))
            files = files.Where(f => f.LibrarySource.Kind != LibrarySourceKind.UltimateCloud);
        if (access.IsAdmin)
            return files;

        var libraries = access.LibraryIds.ToList();
        return files.Where(f => libraries.Contains(f.LibrarySource.LibraryId));
    }

    private IQueryable<TrackEntity> VisibleTracks(UserAccess access, Guid? libraryId = null)
    {
        var files = VisibleFiles(access);
        if (libraryId is { } library)
        {
            // A library the caller cannot use does not exist for them.
            if (!access.CanAccessLibrary(library) || !db.Libraries.Any(l => l.Id == library))
                throw new ArcanistException.NotFound();
            files = files.Where(f => f.LibrarySource.LibraryId == library);
        }

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
            .Select(a => new { a.Id, a.Title, a.ArtworkId, a.Loudness, a.TruePeak })
            .ToDictionaryAsync(a => a.Id, cancellationToken);

        var credits = (await db.TrackArtists.AsNoTracking().Where(ta => ids.Contains(ta.TrackId))
                .Select(ta => new { ta.TrackId, ta.ArtistId, ta.Artist.Name, ta.Role, ta.Position })
                .ToListAsync(cancellationToken))
            .ToLookup(c => c.TrackId);

        var genres = (await db.TrackGenres.AsNoTracking().Where(tg => ids.Contains(tg.TrackId))
                .Select(tg => new { tg.TrackId, tg.Genre.Name })
                .ToListAsync(cancellationToken))
            .ToLookup(g => g.TrackId, g => g.Name);

        var visibleFiles = await VisibleFiles(access).AsNoTracking().Where(f => ids.Contains(f.TrackId)).ToListAsync(cancellationToken);
        var files = visibleFiles.ToLookup(f => f.TrackId);
        var sourceIds = visibleFiles.Select(f => f.LibrarySourceId).Distinct().ToList();
        var libraryOf = await db.LibrarySources.AsNoTracking().Where(s => sourceIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, s => s.LibraryId, cancellationToken);

        var withLyrics = (await db.Lyrics.AsNoTracking().Where(l => ids.Contains(l.TrackId)).Select(l => l.TrackId).ToListAsync(cancellationToken)).ToHashSet();

        var hashes = visibleFiles.Select(f => f.ContentHash).Distinct().ToList();
        var analyses = await db.AudioAnalyses.AsNoTracking()
            .Where(a => hashes.Contains(a.ContentHash) && a.State == AnalysisState.Analyzed)
            .Select(a => new AnalysisSummary(a.ContentHash, a.Loudness, a.LoudnessRange, a.TruePeak, a.Bpm, a.BpmConfidence, a.KeyTonic, a.KeyMinor, a.KeyConfidence))
            .ToDictionaryAsync(a => a.ContentHash, cancellationToken);

        var userRatings = await db.TrackRatings.AsNoTracking().Where(r => r.UserId == access.UserId && ids.Contains(r.TrackId))
            .ToDictionaryAsync(r => r.TrackId, r => r.Rating, cancellationToken);
        var userPlays = await db.TrackPlayStats.AsNoTracking().Where(p => p.UserId == access.UserId && ids.Contains(p.TrackId))
            .ToDictionaryAsync(p => p.TrackId, cancellationToken);
        // In single-star mode lower ratings are kept (switching back shows them again) but not reported.
        var singleStar = ratings.Current == RatingMode.Star;

        // One token per response; it is not tied to a track.
        var streamToken = streamTokens.Create(access.UserId, guard.SessionId);

        return tracks.Select(t =>
        {
            var album = albums[t.AlbumId];
            var copies = BestFirst(files[t.Id]).ToList();
            var best = copies.Count > 0 ? analyses.GetValueOrDefault(copies[0].ContentHash) : null;
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
                // Below this, the strongest periodicity is too weak to call it a beat.
                best is { Bpm: { } bpm, BpmConfidence: >= 0.1f } ? Math.Round(bpm, 1) : null,
                best is { KeyTonic: { } tonic, KeyMinor: { } minor } ? MusicalKeys.Create(tonic, minor, best.KeyConfidence ?? 0) : null,
                withLyrics.Contains(t.Id),
                copies
                    .Select(f => new TrackFile(f.Id, f.Format, f.BitRate, f.SampleRate, f.BitDepth, f.Channels, f.Size, f.DurationMs,
                        f.ReplayGainTrackGain, f.ReplayGainAlbumGain,
                        LoudnessOf(analyses.GetValueOrDefault(f.ContentHash), album.Loudness, album.TruePeak),
                        StreamUrls.For(t.Id, streamToken, f.Id)))
                    .ToList(),
                StreamUrls.For(t.Id, streamToken),
                userRatings.TryGetValue(t.Id, out var rating) && (!singleStar || rating == 5) ? rating : null,
                rating == 5,
                userPlays.TryGetValue(t.Id, out var plays) ? plays.PlayCount : 0,
                plays?.LastPlayedAt,
                copies.Select(f => libraryOf[f.LibrarySourceId]).Distinct().ToList());
        }).ToList();
    }

    /// <summary>The overview of a copy's audio (the best one unless <paramref name="fileId"/> says otherwise).</summary>
    public async Task<Waveform> WaveformAsync(Guid trackId, Guid? fileId, CancellationToken cancellationToken)
    {
        var access = guard.Authenticated();
        var copies = await VisibleFiles(access).AsNoTracking()
            .Where(f => f.TrackId == trackId && (fileId == null || f.Id == fileId))
            .ToListAsync(cancellationToken);
        var file = BestFirst(copies).FirstOrDefault() ?? throw new ArcanistException.NotFound();

        var analysis = await db.AudioAnalyses.AsNoTracking()
            .Where(a => a.ContentHash == file.ContentHash && a.Waveform != null)
            .Select(a => new { a.Waveform, a.DurationMs })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new ArcanistException.NotFound();

        return new Waveform(trackId, file.Id, analysis.DurationMs, analysis.Waveform!.Select(b => (int)b).ToList());
    }

    /// <summary>The lyrics of a track the caller can see; not_found when it has none.</summary>
    public async Task<TrackLyrics> LyricsAsync(Guid trackId, CancellationToken cancellationToken)
    {
        var access = guard.Authenticated();
        if (!await VisibleTracks(access).AnyAsync(t => t.Id == trackId, cancellationToken))
            throw new ArcanistException.NotFound();

        var lyrics = await db.Lyrics.AsNoTracking().SingleOrDefaultAsync(l => l.TrackId == trackId, cancellationToken)
                     ?? throw new ArcanistException.NotFound();

        var synced = lyrics.Synced is { } lrc ? Core.Scanning.Lrc.Parse(lrc).Select(l => new LyricsLine(l.TimeMs, l.Text)).ToList() : null;
        var plain = lyrics.Plain ?? (synced is { Count: > 0 } ? string.Join('\n', synced.Select(l => l.Text)) : null);
        return new TrackLyrics(trackId, plain, synced is { Count: > 0 } ? synced : null, lyrics.Source, lyrics.Instrumental);
    }

    /// <summary>Best quality first: the order of <see cref="Track.Files"/> and of playback.</summary>
    private static IEnumerable<MediaFile> BestFirst(IEnumerable<MediaFile> files) =>
        files.OrderByDescending(f => f.BitDepth ?? 0).ThenByDescending(f => f.SampleRate ?? 0).ThenByDescending(f => f.BitRate ?? 0);

    private sealed record AnalysisSummary(
        string ContentHash, double? Loudness, double? Range, double? TruePeak, float? Bpm, float? BpmConfidence, int? KeyTonic, bool? KeyMinor, float? KeyConfidence);

    /// <summary>ReplayGain 2.0 reference level.</summary>
    private const double ReferenceLoudness = -18;

    private static Loudness? LoudnessOf(AnalysisSummary? analysis, double? albumLoudness, double? albumPeak) =>
        analysis is { Loudness: { } integrated, TruePeak: { } peak }
            ? new Loudness(
                Math.Round(integrated, 2),
                Math.Round(analysis.Range ?? 0, 2),
                Math.Round(peak, 4),
                Math.Round(ReferenceLoudness - integrated, 2),
                albumLoudness is { } album ? Math.Round(ReferenceLoudness - album, 2) : null,
                albumPeak is { } albumTruePeak ? Math.Round(albumTruePeak, 4) : null)
            : null;

    internal static Artist ToModel(ArtistEntity artist) => new(artist.Id, artist.Name, artist.SortName, artist.MusicBrainzId);

    /// <summary>
    /// Rows matching a search (every word, as a word prefix, in the name, its artists or its album); null without
    /// one. A search with no words at all matches nothing.
    /// </summary>
    private IQueryable<T> Matching<T>(IQueryable<T> rows, SearchKind kind, string? search) where T : Entity
    {
        if (string.IsNullOrWhiteSpace(search))
            return rows;

        var hits = FullTextSearch.Match(db, kind, search);
        return hits is null ? rows.Where(_ => false) : rows.Where(r => hits.Any(h => h.Id == r.Id));
    }
}

public static class StreamUrls
{
    public static string For(Guid trackId, string token, Guid? fileId = null) =>
        $"/rest/v1/stream/{trackId}?{StreamTokens.QueryParameter}={token}" + (fileId is { } id ? $"&fileId={id}" : "");
}
