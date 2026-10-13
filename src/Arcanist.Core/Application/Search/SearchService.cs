using Arcanist.Core.Application.Catalog;
using Arcanist.Core.Application.Playlists;
using Arcanist.Core.Data;
using Arcanist.Core.Data.Search;
using Microsoft.EntityFrameworkCore;

namespace Arcanist.Core.Application.Search;

/// <summary>The best matches of each kind for a search. Part of the API contract.</summary>
public sealed record SearchResults(
    IReadOnlyList<Artist> Artists,
    IReadOnlyList<Album> Albums,
    IReadOnlyList<Track> Tracks,
    IReadOnlyList<Playlist> Playlists);

/// <summary>
/// Searches artists, albums, tracks and playlists at once, best matches first. Every word must appear as the start
/// of a word in the name, its artists or its album; accents and case do not matter. Only what the user can see
/// is returned.
/// </summary>
public sealed class SearchService(ArcanistDbContext db, AccessGuard guard, CatalogService catalog, PlaylistService playlists)
{
    public const int DefaultCount = 10;
    public const int MaxCount = 50;

    public async Task<SearchResults> SearchAsync(string query, int? first, CancellationToken cancellationToken)
    {
        var access = guard.Authenticated();
        var count = Math.Clamp(first ?? DefaultCount, 1, MaxCount);
        if (FullTextSearch.Match(db, SearchKind.Tracks, query) is null)
            return new SearchResults([], [], [], []);

        var artists = await (
                from hit in FullTextSearch.Match(db, SearchKind.Artists, query)!
                join artist in catalog.VisibleArtists(access) on hit.Id equals artist.Id
                orderby hit.Score descending, artist.NormalizedName
                select artist)
            .AsNoTracking().Take(count).ToListAsync(cancellationToken);

        var albums = await (
                from hit in FullTextSearch.Match(db, SearchKind.Albums, query)!
                join album in catalog.VisibleAlbums(access) on hit.Id equals album.Id
                orderby hit.Score descending, album.NormalizedTitle
                select album)
            .AsNoTracking().Take(count).ToListAsync(cancellationToken);

        var tracks = await (
                from hit in FullTextSearch.Match(db, SearchKind.Tracks, query)!
                join track in catalog.Visible(access) on hit.Id equals track.Id
                orderby hit.Score descending, track.NormalizedTitle
                select track)
            .AsNoTracking().Take(count).ToListAsync(cancellationToken);

        var lists = await (
                from hit in FullTextSearch.Match(db, SearchKind.Playlists, query)!
                join playlist in playlists.VisibleQuery(access) on hit.Id equals playlist.Id
                orderby hit.Score descending, playlist.NormalizedName
                select playlist)
            .AsNoTracking().Take(count).ToListAsync(cancellationToken);

        return new SearchResults(
            artists.Select(CatalogService.ToModel).ToList(),
            await catalog.ToModelsAsync(albums, access, cancellationToken),
            await catalog.ToModelsAsync(tracks, cancellationToken),
            await playlists.ModelsAsync(access, lists, cancellationToken));
    }
}
