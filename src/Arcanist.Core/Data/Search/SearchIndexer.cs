using Arcanist.Core.Data.Entities;
using Arcanist.Core.Setup;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Arcanist.Core.Data.Search;

/// <summary>
/// Computes the <c>SearchText</c> of catalog rows that lack it: tracks (title, artists and album), their albums
/// (title and album artist) and artists. The scanner clears it on every track it writes, so this runs after each
/// batch; the database triggers or generated columns then update the full-text index.
/// </summary>
public static class SearchIndexer
{
    private const int BatchSize = 1000;

    public static async Task RefreshAsync(ArcanistDbContext db, CancellationToken cancellationToken)
    {
        var albums = new HashSet<Guid>();
        while (true)
        {
            var tracks = await db.Tracks.Where(t => t.SearchText == null).OrderBy(t => t.Id)
                .Select(t => new
                {
                    t.Id,
                    t.Title,
                    t.AlbumId,
                    Album = t.Album.Title,
                    Artists = t.Artists.Where(a => a.Role == ArtistRole.Primary || a.Role == ArtistRole.Featured).Select(a => a.Artist.Name).ToList(),
                })
                .Take(BatchSize)
                .ToListAsync(cancellationToken);
            if (tracks.Count == 0)
                break;

            await InTransactionAsync(db, tracks, track => db.Tracks.Where(t => t.Id == track.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.SearchText, FullTextSearch.Text([track.Title, .. track.Artists, track.Album])), cancellationToken), cancellationToken);
            albums.UnionWith(tracks.Select(t => t.AlbumId));
        }

        // Albums of rewritten tracks may have changed too (a new album artist when found to be a compilation).
        foreach (var chunk in albums.Chunk(BatchSize))
            await IndexAlbumsAsync(db, db.Albums.Where(a => chunk.Contains(a.Id)), cancellationToken);
        while (await IndexAlbumsAsync(db, db.Albums.Where(a => a.SearchText == null), cancellationToken) > 0)
        {
        }

        while (true)
        {
            var artists = await db.Artists.Where(a => a.SearchText == null).OrderBy(a => a.Id).Select(a => new { a.Id, a.Name }).Take(BatchSize).ToListAsync(cancellationToken);
            if (artists.Count == 0)
                break;
            await InTransactionAsync(db, artists, artist => db.Artists.Where(a => a.Id == artist.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(a => a.SearchText, FullTextSearch.Text(artist.Name)), cancellationToken), cancellationToken);
        }

        while (true)
        {
            var playlists = await db.Playlists.Where(p => p.SearchText == null).OrderBy(p => p.Id).Select(p => new { p.Id, p.Name, p.Description }).Take(BatchSize).ToListAsync(cancellationToken);
            if (playlists.Count == 0)
                break;
            await InTransactionAsync(db, playlists, playlist => db.Playlists.Where(p => p.Id == playlist.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.SearchText, FullTextSearch.Text(playlist.Name, playlist.Description)), cancellationToken), cancellationToken);
        }
    }

    private static async Task<int> IndexAlbumsAsync(ArcanistDbContext db, IQueryable<Album> query, CancellationToken cancellationToken)
    {
        var albums = await query.OrderBy(a => a.Id).Select(a => new { a.Id, a.Title, Artist = a.AlbumArtist.Name }).Take(BatchSize).ToListAsync(cancellationToken);
        await InTransactionAsync(db, albums, album => db.Albums.Where(a => a.Id == album.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.SearchText, FullTextSearch.Text(album.Title, album.Artist)), cancellationToken), cancellationToken);
        return albums.Count;
    }

    /// <summary>One transaction per batch: SQLite commits are what make many small updates slow.</summary>
    private static async Task InTransactionAsync<T>(ArcanistDbContext db, IReadOnlyList<T> items, Func<T, Task<int>> update, CancellationToken cancellationToken)
    {
        if (items.Count == 0)
            return;

        await using var transaction = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(cancellationToken) : null;
        foreach (var item in items)
            await update(item);
        if (transaction is not null)
            await transaction.CommitAsync(cancellationToken);
    }
}

/// <summary>
/// At startup, indexes what earlier versions left without search text, in the background: until it finishes,
/// some music may not show up in searches.
/// </summary>
internal sealed class SearchIndexBackfill(
    IArcanistDbContextFactory databases,
    IOptions<SetupOptions> setup,
    ILogger<SearchIndexBackfill> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();
        if (!setup.Value.Completed || !databases.IsConfigured)
            return;

        try
        {
            await using var db = databases.CreateDbContext();
            await SearchIndexer.RefreshAsync(db, stoppingToken);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            logger.LogError(e, "Indexing the catalog for search failed");
        }
    }
}
