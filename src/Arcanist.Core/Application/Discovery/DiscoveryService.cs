using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Arcanist.Core.Application.Catalog;
using Arcanist.Core.Application.Playlists;
using Arcanist.Core.Auth;
using Arcanist.Core.Data;
using Arcanist.Core.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Album = Arcanist.Core.Application.Catalog.Album;
using Artist = Arcanist.Core.Application.Catalog.Artist;
using Track = Arcanist.Core.Application.Catalog.Track;
using TrackEntity = Arcanist.Core.Data.Entities.Track;

namespace Arcanist.Core.Application.Discovery;

/// <summary>
/// The pages a player builds around the music: the home page, artist pages and mixes. Everything is personal
/// (it follows what the user plays and rates) and limited to what the user can see. Mixes change once a day.
/// </summary>
public sealed class DiscoveryService(
    ArcanistDbContext db,
    AccessGuard guard,
    CatalogService catalog,
    PlaylistService playlists,
    TimeProvider time)
{
    public const int SectionSize = 12;
    public const int MixLength = 50;

    /// <summary>Above this many candidates a mix samples them at random instead of reading them all.</summary>
    private const int MaxPool = 5000;

    public async Task<Home> HomeAsync(CancellationToken cancellationToken)
    {
        var access = guard.Authenticated();
        var user = access.UserId;
        var now = Now();
        var sections = new List<HomeSection>();

        var recent = await db.PlayEvents.AsNoTracking()
            .Where(p => p.UserId == user && p.Counted && p.TrackId != null)
            .OrderByDescending(p => p.PlayedAt)
            .Select(p => p.Track!.AlbumId)
            .Take(300)
            .ToListAsync(cancellationToken);
        await AddAlbumsAsync(HomeSectionKind.RecentlyPlayed, recent.Distinct().Take(SectionSize * 2).ToList());

        var mixes = await MixesAsync(access, cancellationToken);
        if (mixes.Count > 0)
            sections.Add(new HomeSection(HomeSectionKind.Mixes, [], mixes, [], []));

        var added = await catalog.Visible(access)
            .GroupBy(t => t.AlbumId)
            .Select(g => new { AlbumId = g.Key, AddedAt = g.Max(t => t.AddedAt) })
            .OrderByDescending(g => g.AddedAt).ThenBy(g => g.AlbumId)
            .Take(SectionSize)
            .Select(g => g.AlbumId)
            .ToListAsync(cancellationToken);
        await AddAlbumsAsync(HomeSectionKind.RecentlyAdded, added);

        var since = now.AddDays(-90);
        var mostPlayed = await db.PlayEvents.AsNoTracking()
            .Where(p => p.UserId == user && p.Counted && p.TrackId != null && p.PlayedAt >= since)
            .GroupBy(p => p.Track!.AlbumId)
            .Select(g => new { AlbumId = g.Key, Plays = g.Count() })
            .OrderByDescending(g => g.Plays).ThenBy(g => g.AlbumId)
            .Take(SectionSize * 2)
            .Select(g => g.AlbumId)
            .ToListAsync(cancellationToken);
        await AddAlbumsAsync(HomeSectionKind.MostPlayed, mostPlayed);

        var starredIds = await db.TrackRatings.AsNoTracking()
            .Where(r => r.UserId == user && r.Rating == 5)
            .OrderByDescending(r => r.RatedAt)
            .Select(r => r.TrackId)
            .Take(SectionSize * 2)
            .ToListAsync(cancellationToken);
        var starred = await catalog.VisibleTracksAsync(access, starredIds, cancellationToken);
        if (starred.Count > 0)
            sections.Add(new HomeSection(HomeSectionKind.Starred, [], [], starredIds.Where(starred.ContainsKey).Take(SectionSize).Select(id => starred[id]).ToList(), []));

        var forgotten = now.AddDays(-60);
        var rediscover = await db.TrackPlayStats.AsNoTracking()
            .Where(s => s.UserId == user && s.PlayCount > 0)
            .GroupBy(s => s.Track.AlbumId)
            .Select(g => new { AlbumId = g.Key, Plays = g.Sum(s => s.PlayCount), Last = g.Max(s => s.LastPlayedAt) })
            .Where(g => g.Plays >= 3 && g.Last < forgotten)
            .OrderByDescending(g => g.Plays).ThenBy(g => g.AlbumId)
            .Take(SectionSize * 2)
            .Select(g => g.AlbumId)
            .ToListAsync(cancellationToken);
        await AddAlbumsAsync(HomeSectionKind.Rediscover, rediscover);

        var neverPlayed = await catalog.VisibleAlbums(access)
            .Where(a => !db.TrackPlayStats.Any(s => s.UserId == user && s.PlayCount > 0 && s.Track.AlbumId == a.Id))
            .OrderBy(a => a.Id)
            .Select(a => a.Id)
            .Take(MaxPool)
            .ToListAsync(cancellationToken);
        await AddAlbumsAsync(HomeSectionKind.NeverPlayed, Shuffle(neverPlayed, Seed(user, "never-played")).Take(SectionSize).ToList());

        var lists = (await playlists.ListAsync(cancellationToken)).Take(SectionSize).ToList();
        if (lists.Count > 0)
            sections.Add(new HomeSection(HomeSectionKind.Playlists, [], [], [], lists));

        return new Home(sections);

        async Task AddAlbumsAsync(HomeSectionKind kind, IReadOnlyList<Guid> ids)
        {
            var albums = await catalog.VisibleAlbumsAsync(access, ids, cancellationToken);
            var ordered = ids.Where(albums.ContainsKey).Take(SectionSize).Select(id => albums[id]).ToList();
            if (ordered.Count > 0)
                sections.Add(new HomeSection(kind, ordered, [], [], []));
        }
    }

    /// <summary>Mixes made for the user: their favorite artists, genres and decades, favorites and discoveries.</summary>
    public Task<IReadOnlyList<Mix>> MixesAsync(CancellationToken cancellationToken) => MixesAsync(guard.Authenticated(), cancellationToken);

    private async Task<IReadOnlyList<Mix>> MixesAsync(UserAccess access, CancellationToken cancellationToken)
    {
        var user = access.UserId;
        var visible = catalog.Visible(access);
        var since = Now().AddDays(-90);
        var played = db.PlayEvents.Where(p => p.UserId == user && p.Counted && p.TrackId != null && p.PlayedAt >= since);
        var mixes = new List<Mix>();

        // What the user plays most, completed with what the library has most of (for users who just arrived).
        var artists = await played
            .Join(db.TrackArtists.Where(ta => ta.Role == ArtistRole.Primary), p => p.TrackId, ta => ta.TrackId, (p, ta) => new { ta.ArtistId, ta.Artist.Name })
            .GroupBy(a => new { a.ArtistId, a.Name })
            .Select(g => new { g.Key.ArtistId, g.Key.Name, Count = g.Count() })
            .OrderByDescending(g => g.Count).ThenBy(g => g.ArtistId)
            .Take(4)
            .ToListAsync(cancellationToken);
        if (artists.Count < 4)
        {
            var biggest = await db.TrackArtists
                .Where(ta => ta.Role == ArtistRole.Primary && visible.Any(t => t.Id == ta.TrackId))
                .GroupBy(ta => new { ta.ArtistId, ta.Artist.Name })
                .Select(g => new { g.Key.ArtistId, g.Key.Name, Count = g.Count() })
                .Where(g => g.Count >= 3)
                .OrderByDescending(g => g.Count).ThenBy(g => g.ArtistId)
                .Take(4)
                .ToListAsync(cancellationToken);
            artists.AddRange(biggest.Where(b => artists.All(a => a.ArtistId != b.ArtistId)).Take(4 - artists.Count));
        }

        foreach (var artist in artists)
            mixes.Add(await MixOfAsync(access, MixKind.Artist, $"artist:{artist.ArtistId:N}", artist.Name, artist.ArtistId, ArtistTracks(visible, artist.ArtistId), cancellationToken));

        var genres = await played
            .Join(db.TrackGenres, p => p.TrackId, tg => tg.TrackId, (p, tg) => new { tg.GenreId, tg.Genre.Name })
            .GroupBy(g => new { g.GenreId, g.Name })
            .Select(g => new { g.Key.GenreId, g.Key.Name, Count = g.Count() })
            .OrderByDescending(g => g.Count).ThenBy(g => g.GenreId)
            .Take(3)
            .ToListAsync(cancellationToken);
        if (genres.Count < 3)
        {
            var biggest = await db.TrackGenres
                .Where(tg => visible.Any(t => t.Id == tg.TrackId))
                .GroupBy(tg => new { tg.GenreId, tg.Genre.Name })
                .Select(g => new { g.Key.GenreId, g.Key.Name, Count = g.Count() })
                .Where(g => g.Count >= 5)
                .OrderByDescending(g => g.Count).ThenBy(g => g.GenreId)
                .Take(3)
                .ToListAsync(cancellationToken);
            genres.AddRange(biggest.Where(b => genres.All(g => g.GenreId != b.GenreId)).Take(3 - genres.Count));
        }

        foreach (var genre in genres)
            mixes.Add(await MixOfAsync(access, MixKind.Genre, $"genre:{genre.GenreId:N}", genre.Name, genre.GenreId, GenreTracks(visible, genre.GenreId), cancellationToken));

        if (await FavoriteTracks(visible, user).CountAsync(cancellationToken) >= 10)
            mixes.Add(await MixOfAsync(access, MixKind.Favorites, "favorites", null, null, FavoriteTracks(visible, user), cancellationToken));

        if (await UnplayedTracks(visible, user).OrderBy(t => t.Id).Take(20).CountAsync(cancellationToken) >= 20)
            mixes.Add(await MixOfAsync(access, MixKind.Discovery, "discovery", null, null, UnplayedTracks(visible, user), cancellationToken));

        var decades = await visible.Where(t => t.Album.Year != null)
            .GroupBy(t => t.Album.Year!.Value / 10 * 10)
            .Select(g => new
            {
                Decade = g.Key,
                Played = g.Count(t => db.TrackPlayStats.Any(s => s.UserId == user && s.TrackId == t.Id && s.PlayCount > 0)),
                Count = g.Count(),
            })
            .Where(g => g.Count >= 10)
            .OrderByDescending(g => g.Played).ThenByDescending(g => g.Count)
            .Take(2)
            .ToListAsync(cancellationToken);
        foreach (var decade in decades)
        {
            var subject = decade.Decade.ToString(CultureInfo.InvariantCulture);
            mixes.Add(await MixOfAsync(access, MixKind.Decade, $"decade:{subject}", subject, null, DecadeTracks(visible, decade.Decade), cancellationToken));
        }

        return mixes;
    }

    /// <summary>The tracks of a mix, in the order to play them.</summary>
    public async Task<MixTracks> MixAsync(string id, CancellationToken cancellationToken)
    {
        var access = guard.Authenticated();
        var user = access.UserId;
        var visible = catalog.Visible(access);
        var (kind, subject) = ParseId(id);
        var seed = Seed(user, id);

        List<Guid> picked;
        Mix mix;
        switch (kind)
        {
            case MixKind.Artist:
            {
                var name = await NameOfArtistAsync(visible, subject!.Value, cancellationToken);
                var related = await RelatedArtistIdsAsync(visible, subject.Value, 8, cancellationToken);
                picked = await PickAsync(access, ArtistTracks(visible, subject.Value), 20, seed, cancellationToken);
                picked.AddRange(await PickAsync(access, visible.Where(t => t.Artists.Any(a => related.Contains(a.ArtistId) && a.Role == ArtistRole.Primary)),
                    MixLength - picked.Count, seed + 1, cancellationToken, exclude: picked));
                mix = await MixOfAsync(access, kind, id, name, subject, ArtistTracks(visible, subject.Value), cancellationToken);
                break;
            }
            case MixKind.Track:
            {
                var track = await visible.Where(t => t.Id == subject)
                    .Select(t => new
                    {
                        t.Id,
                        t.Title,
                        Artists = t.Artists.Where(a => a.Role == ArtistRole.Primary).Select(a => a.ArtistId).ToList(),
                        Genres = t.Genres.Select(g => g.GenreId).ToList(),
                    })
                    .SingleOrDefaultAsync(cancellationToken) ?? throw new ArcanistException.NotFound();
                var related = new List<Guid>();
                foreach (var artist in track.Artists)
                    related.AddRange(await RelatedArtistIdsAsync(visible, artist, 6, cancellationToken));

                picked = [track.Id];
                picked.AddRange(await PickAsync(access, visible.Where(t => t.Artists.Any(a => track.Artists.Contains(a.ArtistId))), 14, seed, cancellationToken, exclude: picked));
                picked.AddRange(await PickAsync(access, visible.Where(t => t.Artists.Any(a => related.Contains(a.ArtistId) && a.Role == ArtistRole.Primary)), 20, seed + 1, cancellationToken, exclude: picked));
                picked.AddRange(await PickAsync(access, visible.Where(t => t.Genres.Any(g => track.Genres.Contains(g.GenreId))), MixLength - picked.Count, seed + 2, cancellationToken, exclude: picked));
                mix = await MixOfAsync(access, kind, id, track.Title, track.Id, visible.Where(t => t.Id == track.Id), cancellationToken);
                break;
            }
            default:
            {
                IQueryable<TrackEntity> pool;
                string? name = null;
                switch (kind)
                {
                    case MixKind.Genre:
                        pool = GenreTracks(visible, subject!.Value);
                        name = await db.Genres.Where(g => g.Id == subject).Select(g => g.Name).SingleOrDefaultAsync(cancellationToken)
                               ?? throw new ArcanistException.NotFound();
                        break;
                    case MixKind.Decade:
                        var decade = DecadeOf(id);
                        pool = DecadeTracks(visible, decade);
                        name = decade.ToString(CultureInfo.InvariantCulture);
                        break;
                    case MixKind.Favorites:
                        pool = FavoriteTracks(visible, user);
                        break;
                    default:
                        pool = UnplayedTracks(visible, user);
                        break;
                }

                picked = await PickAsync(access, pool, MixLength, seed, cancellationToken);
                mix = await MixOfAsync(access, kind, id, name, kind == MixKind.Genre ? subject : null, pool, cancellationToken);
                break;
            }
        }

        var ordered = await FlowAsync(picked, seed, keepFirst: kind == MixKind.Track, cancellationToken);
        var tracks = await catalog.VisibleTracksAsync(access, ordered, cancellationToken);
        return new MixTracks(mix, ordered.Where(tracks.ContainsKey).Select(t => tracks[t]).ToList());
    }

    public async Task<ArtistOverview> ArtistOverviewAsync(Guid id, CancellationToken cancellationToken)
    {
        var access = guard.Authenticated();
        var user = access.UserId;
        var artist = await catalog.ArtistAsync(id, cancellationToken);
        var visible = catalog.Visible(access);
        var tracks = ArtistTracks(visible, id);

        var top = await tracks
            .Select(t => new
            {
                t.Id,
                Mine = db.TrackPlayStats.Where(s => s.UserId == user && s.TrackId == t.Id).Sum(s => s.PlayCount),
                Everyone = db.TrackPlayStats.Where(s => s.TrackId == t.Id).Sum(s => s.PlayCount),
                t.Album.Year,
                t.DiscNumber,
                t.TrackNumber,
            })
            .OrderByDescending(t => t.Mine).ThenByDescending(t => t.Everyone).ThenByDescending(t => t.Year).ThenBy(t => t.DiscNumber).ThenBy(t => t.TrackNumber)
            .Take(10)
            .Select(t => t.Id)
            .ToListAsync(cancellationToken);
        var topTracks = await catalog.VisibleTracksAsync(access, top, cancellationToken);

        var albums = await catalog.VisibleAlbums(access).AsNoTracking()
            .Where(a => a.AlbumArtistId == id)
            .OrderByDescending(a => a.Year).ThenBy(a => a.NormalizedTitle)
            .ToListAsync(cancellationToken);
        var appearsOn = await catalog.VisibleAlbums(access).AsNoTracking()
            .Where(a => a.AlbumArtistId != id && a.Tracks.Any(t => t.Artists.Any(ta => ta.ArtistId == id && (ta.Role == ArtistRole.Primary || ta.Role == ArtistRole.Featured))))
            .OrderByDescending(a => a.Year).ThenBy(a => a.NormalizedTitle)
            .Take(50)
            .ToListAsync(cancellationToken);

        var relatedIds = await RelatedArtistIdsAsync(visible, id, 10, cancellationToken);
        var related = await db.Artists.AsNoTracking().Where(a => relatedIds.Contains(a.Id)).ToListAsync(cancellationToken);

        var playCount = await db.TrackPlayStats.Where(s => s.UserId == user && tracks.Any(t => t.Id == s.TrackId)).SumAsync(s => s.PlayCount, cancellationToken);
        var albumModels = await catalog.ToModelsAsync(albums, access, cancellationToken);

        return new ArtistOverview(
            artist,
            top.Where(topTracks.ContainsKey).Select(t => topTracks[t]).ToList(),
            albumModels,
            await catalog.ToModelsAsync(appearsOn, access, cancellationToken),
            relatedIds.Select(r => related.FirstOrDefault(a => a.Id == r)).OfType<Data.Entities.Artist>().Select(CatalogService.ToModel).ToList(),
            await MixOfAsync(access, MixKind.Artist, $"artist:{id:N}", artist.Name, id, tracks, cancellationToken),
            playCount,
            albumModels.FirstOrDefault(a => a.ArtworkId is not null)?.ArtworkId);
    }

    private static IQueryable<TrackEntity> ArtistTracks(IQueryable<TrackEntity> visible, Guid artistId) =>
        visible.Where(t => t.Artists.Any(a => a.ArtistId == artistId && (a.Role == ArtistRole.Primary || a.Role == ArtistRole.Featured)));

    private static IQueryable<TrackEntity> GenreTracks(IQueryable<TrackEntity> visible, Guid genreId) =>
        visible.Where(t => t.Genres.Any(g => g.GenreId == genreId));

    private static IQueryable<TrackEntity> DecadeTracks(IQueryable<TrackEntity> visible, int decade) =>
        visible.Where(t => t.Album.Year >= decade && t.Album.Year < decade + 10);

    private IQueryable<TrackEntity> FavoriteTracks(IQueryable<TrackEntity> visible, Guid user) =>
        visible.Where(t => db.TrackRatings.Any(r => r.UserId == user && r.TrackId == t.Id && r.Rating >= 4));

    private IQueryable<TrackEntity> UnplayedTracks(IQueryable<TrackEntity> visible, Guid user) =>
        visible.Where(t => !db.TrackPlayStats.Any(s => s.UserId == user && s.TrackId == t.Id && s.PlayCount > 0));

    /// <summary>
    /// Artists like this one: those sharing its main genres, and those on the same albums (compilations,
    /// collaborations), most related first.
    /// </summary>
    internal Task<List<Guid>> RelatedArtistIdsAsync(UserAccess access, Guid artistId, int count, CancellationToken cancellationToken) =>
        RelatedArtistIdsAsync(catalog.Visible(access), artistId, count, cancellationToken);

    private async Task<List<Guid>> RelatedArtistIdsAsync(IQueryable<TrackEntity> visible, Guid artistId, int count, CancellationToken cancellationToken)
    {
        var own = ArtistTracks(visible, artistId);
        var genres = await db.TrackGenres.Where(tg => own.Any(t => t.Id == tg.TrackId))
            .GroupBy(tg => tg.GenreId)
            .Select(g => new { GenreId = g.Key, Count = g.Count() })
            .OrderByDescending(g => g.Count).ThenBy(g => g.GenreId)
            .Take(3)
            .Select(g => g.GenreId)
            .ToListAsync(cancellationToken);

        var byGenre = await db.TrackArtists
            .Where(ta => ta.Role == ArtistRole.Primary && ta.ArtistId != artistId
                         && visible.Any(t => t.Id == ta.TrackId && t.Genres.Any(g => genres.Contains(g.GenreId))))
            .GroupBy(ta => ta.ArtistId)
            .Select(g => new { ArtistId = g.Key, Count = g.Count() })
            .OrderByDescending(g => g.Count).ThenBy(g => g.ArtistId)
            .Take(count * 2)
            .ToListAsync(cancellationToken);
        var albums = own.Select(t => t.AlbumId);
        var byAlbum = await db.TrackArtists
            .Where(ta => ta.Role == ArtistRole.Primary && ta.ArtistId != artistId && visible.Any(t => t.Id == ta.TrackId && albums.Contains(t.AlbumId)))
            .GroupBy(ta => ta.ArtistId)
            .Select(g => new { ArtistId = g.Key, Count = g.Count() })
            .OrderByDescending(g => g.Count).ThenBy(g => g.ArtistId)
            .Take(count * 2)
            .ToListAsync(cancellationToken);

        // Sharing an album is a stronger tie than sharing a genre.
        return byGenre.Select(a => (a.ArtistId, Score: (double)a.Count))
            .Concat(byAlbum.Select(a => (a.ArtistId, Score: a.Count * 3.0)))
            .GroupBy(a => a.ArtistId)
            .Select(g => (ArtistId: g.Key, Score: g.Sum(a => a.Score)))
            .OrderByDescending(a => a.Score).ThenBy(a => a.ArtistId)
            .Take(count)
            .Select(a => a.ArtistId)
            .ToList();
    }

    /// <summary>
    /// Picks tracks from a pool for the day: favorites and familiar tracks are likelier, tracks the user rated low
    /// or keeps skipping are left out, and no album appears more than three times.
    /// </summary>
    private async Task<List<Guid>> PickAsync(
        UserAccess access, IQueryable<TrackEntity> pool, int count, int seed, CancellationToken cancellationToken, IReadOnlyCollection<Guid>? exclude = null)
    {
        if (count <= 0)
            return [];

        var user = access.UserId;
        var total = await pool.CountAsync(cancellationToken);
        var sample = total <= MaxPool ? pool : pool.OrderBy(_ => EF.Functions.Random()).Take(MaxPool);
        var candidates = await sample.Select(t => new
            {
                t.Id,
                t.AlbumId,
                Plays = db.TrackPlayStats.Where(s => s.UserId == user && s.TrackId == t.Id).Sum(s => s.PlayCount),
                Skips = db.TrackPlayStats.Where(s => s.UserId == user && s.TrackId == t.Id).Sum(s => s.SkipCount),
                Rating = db.TrackRatings.Where(r => r.UserId == user && r.TrackId == t.Id).Select(r => (int?)r.Rating).FirstOrDefault(),
            })
            .ToListAsync(cancellationToken);

        var random = new Random(seed);
        var perAlbum = new Dictionary<Guid, int>();
        var picked = new List<Guid>();
        foreach (var candidate in candidates
                     .OrderBy(c => c.Id)
                     .Where(c => exclude?.Contains(c.Id) != true && c.Rating is not (1 or 2) && !(c.Skips >= 3 && c.Skips > c.Plays))
                     .Select(c => (c.Id, c.AlbumId, Score: random.NextDouble() + (c.Rating >= 4 ? 0.5 : 0) + (c.Plays > 0 ? 0.15 : 0)))
                     .OrderByDescending(c => c.Score))
        {
            if (perAlbum.GetValueOrDefault(candidate.AlbumId) >= 3)
                continue;
            perAlbum[candidate.AlbumId] = perAlbum.GetValueOrDefault(candidate.AlbumId) + 1;
            picked.Add(candidate.Id);
            if (picked.Count == count)
                break;
        }

        return picked;
    }

    /// <summary>
    /// Orders tracks so each leads into the next: close tempos and compatible keys (neighbors on the Camelot
    /// wheel) when analyzed, and never the same artist or album twice in a row when that can be avoided.
    /// </summary>
    private async Task<List<Guid>> FlowAsync(List<Guid> trackIds, int seed, bool keepFirst, CancellationToken cancellationToken)
    {
        if (trackIds.Count <= 2)
            return trackIds;

        var facts = (await db.MediaFiles.AsNoTracking()
                .Where(f => trackIds.Contains(f.TrackId) && f.MissingSince == null)
                .Select(f => new
                {
                    f.TrackId,
                    f.Track.AlbumId,
                    Artist = f.Track.Artists.Where(a => a.Role == ArtistRole.Primary).OrderBy(a => a.Position).Select(a => (Guid?)a.ArtistId).FirstOrDefault(),
                    Analysis = db.AudioAnalyses.Where(a => a.ContentHash == f.ContentHash && a.State == AnalysisState.Analyzed)
                        .Select(a => new { a.Bpm, a.BpmConfidence, a.KeyTonic, a.KeyMinor })
                        .FirstOrDefault(),
                })
                .ToListAsync(cancellationToken))
            .GroupBy(f => f.TrackId)
            .ToDictionary(g => g.Key, g =>
            {
                var best = g.FirstOrDefault(f => f.Analysis is not null) ?? g.First();
                var analysis = best.Analysis;
                return new FlowFacts(
                    best.AlbumId,
                    best.Artist,
                    analysis is { Bpm: { } bpm, BpmConfidence: >= 0.1f } ? bpm : null,
                    analysis is { KeyTonic: { } tonic, KeyMinor: { } minor } ? Camelot(tonic, minor) : null);
            });

        var random = new Random(seed);
        var remaining = trackIds.Where(facts.ContainsKey).ToList();
        var ordered = new List<Guid>();
        var current = keepFirst && remaining.Count > 0 ? remaining[0] : remaining[random.Next(remaining.Count)];
        while (true)
        {
            ordered.Add(current);
            remaining.Remove(current);
            if (remaining.Count == 0)
                break;

            var from = facts[current];
            // A little randomness keeps two mixes from the same tracks from sounding alike.
            current = remaining.MinBy(id => Distance(from, facts[id]) + random.NextDouble() * 0.6)!;
        }

        return ordered;
    }

    private static double Distance(FlowFacts a, FlowFacts b)
    {
        var distance = 0.0;
        if (a.ArtistId is not null && a.ArtistId == b.ArtistId)
            distance += 2;
        if (a.AlbumId == b.AlbumId)
            distance += 1.5;

        // Unknown tempo or key is neither close nor far.
        distance += a.Bpm is { } x && b.Bpm is { } y ? Math.Min(TempoGap(x, y) / 8, 3) : 1;
        distance += a.Key is { } k && b.Key is { } l ? Math.Min(CamelotSteps(k, l), 3) * 0.7 : 1;
        return distance;
    }

    /// <summary>Tempo difference, also comparing at double and half time (90 and 180 BPM mix well).</summary>
    internal static double TempoGap(double a, double b) => new[] { Math.Abs(a - b), Math.Abs(a * 2 - b), Math.Abs(a - b * 2) }.Min();

    internal static (int Number, bool Minor) Camelot(int tonic, bool minor)
    {
        var code = MusicalKeys.Create(tonic, minor, 0).Camelot;
        return (int.Parse(code[..^1], CultureInfo.InvariantCulture), minor);
    }

    /// <summary>Steps on the Camelot wheel: 0 for the same key, 1 for a neighbor or the relative major/minor.</summary>
    internal static int CamelotSteps((int Number, bool Minor) a, (int Number, bool Minor) b)
    {
        var around = Math.Abs(a.Number - b.Number);
        around = Math.Min(around, 12 - around);
        return around + (a.Minor == b.Minor ? 0 : 1);
    }

    /// <summary>A mix's description, with covers of its albums for a collage.</summary>
    private async Task<Mix> MixOfAsync(
        UserAccess access, MixKind kind, string id, string? subject, Guid? subjectId, IQueryable<TrackEntity> tracks, CancellationToken cancellationToken)
    {
        var artwork = await tracks.Select(t => t.ArtworkId ?? t.Album.ArtworkId)
            .Where(a => a != null)
            .Distinct()
            .OrderBy(a => a)
            .Take(40)
            .ToListAsync(cancellationToken);
        return new Mix(id, kind, subject, subjectId, Shuffle(artwork.Select(a => a!.Value).ToList(), Seed(access.UserId, id)).Take(4).ToList());
    }

    private async Task<string> NameOfArtistAsync(IQueryable<TrackEntity> visible, Guid artistId, CancellationToken cancellationToken)
    {
        var tracks = ArtistTracks(visible, artistId);
        return await db.Artists.Where(a => a.Id == artistId && tracks.Any()).Select(a => a.Name).SingleOrDefaultAsync(cancellationToken)
               ?? throw new ArcanistException.NotFound();
    }

    private static (MixKind Kind, Guid? Subject) ParseId(string id)
    {
        var parts = id.Split(':', 2);
        Guid? subject = parts.Length == 2 && Guid.TryParseExact(parts[1], "N", out var guid) ? guid : null;
        return parts[0] switch
        {
            "artist" when subject is not null => (MixKind.Artist, subject),
            "genre" when subject is not null => (MixKind.Genre, subject),
            "track" when subject is not null => (MixKind.Track, subject),
            "decade" when parts.Length == 2 && DecadeOf(id) > 0 => (MixKind.Decade, null),
            "favorites" when parts.Length == 1 => (MixKind.Favorites, null),
            "discovery" when parts.Length == 1 => (MixKind.Discovery, null),
            _ => throw new ArcanistException.NotFound(),
        };
    }

    private static int DecadeOf(string id) =>
        int.TryParse(id.Split(':', 2)[^1], NumberStyles.None, CultureInfo.InvariantCulture, out var year) && year % 10 == 0 && year is >= 1000 and <= 9990
            ? year
            : -1;

    /// <summary>A seed that is the same all day for a user and a subject, and changes the next day.</summary>
    private int Seed(Guid user, string subject)
    {
        var day = Now().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return BitConverter.ToInt32(SHA256.HashData(Encoding.UTF8.GetBytes($"{user:N}:{day}:{subject}")));
    }

    private static List<T> Shuffle<T>(IReadOnlyList<T> items, int seed)
    {
        var list = items.ToList();
        new Random(seed).Shuffle(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(list));
        return list;
    }

    private DateTime Now() => time.GetUtcNow().UtcDateTime;

    private sealed record FlowFacts(Guid AlbumId, Guid? ArtistId, double? Bpm, (int Number, bool Minor)? Key);
}
