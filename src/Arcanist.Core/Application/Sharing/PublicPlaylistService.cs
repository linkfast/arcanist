using System.Globalization;
using Arcanist.Core.Application.Catalog;
using Arcanist.Core.Application.Playlists;
using Arcanist.Core.Auth;
using Arcanist.Core.Data;
using Arcanist.Core.Data.Entities;
using Arcanist.Core.Media;
using Microsoft.EntityFrameworkCore;
using PlaylistEntity = Arcanist.Core.Data.Entities.Playlist;

namespace Arcanist.Core.Application.Sharing;

/// <summary>
/// Playlists opened by their public link, with or without an account. Songs are those the owner can listen to (a
/// visitor never sees more than the owner does); a signed-in user also gets those they can play in full themselves.
/// A link works while the server allows public links and its owner may still share them.
/// </summary>
public sealed class PublicPlaylistService(
    ArcanistDbContext db,
    AccessGuard guard,
    ICurrentUser currentUser,
    CatalogService catalog,
    PlaylistService playlists,
    SharingSettings sharing,
    UserAccessCache users,
    FfmpegLocator ffmpeg)
{
    /// <summary>Seconds of a preview.</summary>
    public const int PreviewSeconds = 30;

    /// <summary>The playlist of a public link, page by page; not found when the link does not work.</summary>
    public async Task<SharedPlaylist> GetAsync(string token, string? after, int? first, CancellationToken cancellationToken)
    {
        var (playlist, owner) = await FindAsync(token, cancellationToken);
        var viewer = currentUser.IsAuthenticated ? guard.Authenticated() : null;

        var visible = catalog.Visible(owner);
        var entries = db.PlaylistEntries.AsNoTracking().Where(e => e.PlaylistId == playlist.Id && visible.Any(t => t.Id == e.TrackId));
        var total = await entries.CountAsync(cancellationToken);
        var duration = total == 0 ? 0 : await entries.SumAsync(e => (long)e.Track.DurationMs, cancellationToken);
        var covers = await entries.OrderBy(e => e.Position).Take(50)
            .Select(e => e.Track.ArtworkId ?? e.Track.Album.ArtworkId).Where(a => a != null).Select(a => a!.Value).ToListAsync(cancellationToken);
        if (after is not null)
        {
            if (!int.TryParse(after, NumberStyles.None, CultureInfo.InvariantCulture, out var position))
                throw ArcanistException.Field("after", ErrorCodes.InvalidFormat);
            entries = entries.Where(e => e.Position > position);
        }

        var size = Math.Clamp(first ?? PlaylistService.DefaultPageSize, 1, PlaylistService.MaxPageSize);
        var page = await entries.OrderBy(e => e.Position).Take(size + 1).ToListAsync(cancellationToken);
        var next = page.Count > size ? page[size - 1].Position.ToString(CultureInfo.InvariantCulture) : null;
        page = page.Take(size).ToList();
        var trackIds = page.Select(e => e.TrackId).Distinct().ToList();

        // What the owner has (titles, artists), read as them; never handed out as tracks (their stream links are theirs).
        Dictionary<Guid, Catalog.Track> described;
        using (HttpCurrentUser.ActAs(owner, DateTimeOffset.MinValue))
            described = await catalog.VisibleTracksAsync(owner, trackIds, cancellationToken);
        var previewable = ffmpeg.Current is null
            ? []
            : (await catalog.VisibleFiles(owner)
                .Where(f => trackIds.Contains(f.TrackId) && (f.LibrarySource.Kind == LibrarySourceKind.Local || f.LibrarySource.Kind == LibrarySourceKind.S3))
                .Select(f => f.TrackId).Distinct().ToListAsync(cancellationToken)).ToHashSet();
        var playable = viewer is null ? [] : await catalog.VisibleTracksAsync(viewer, trackIds, cancellationToken);

        var items = page.Where(e => described.ContainsKey(e.TrackId)).Select(e =>
        {
            var track = described[e.TrackId];
            return new SharedPlaylistEntry(
                e.Id,
                e.Position,
                track.Title,
                track.Artists.Where(a => a.Role != ArtistRole.Featured).Select(a => a.Name).DefaultIfEmpty(track.AlbumArtist.Name).ToList(),
                track.Album.Title,
                track.DurationMs,
                track.ArtworkId,
                previewable.Contains(e.TrackId) ? PreviewUrl(token, e.Id) : null,
                playable.GetValueOrDefault(e.TrackId));
        }).ToList();

        var seen = viewer is not null && await playlists.VisibleQuery(viewer).AnyAsync(p => p.Id == playlist.Id, cancellationToken);
        return new SharedPlaylist(token, playlist.Name, playlist.Description, await OwnerNameAsync(playlist.OwnerId, cancellationToken),
            total, duration, covers.Distinct().Take(4).ToList(), playlist.UpdatedAt, seen ? playlist.Id : null,
            new SharedPlaylistPage(items, next, total));
    }

    /// <summary>Makes a private playlist of the signed-in user's own with the songs of a shared one they can play.</summary>
    public async Task<Playlists.Playlist> CopyAsync(string token, string? name, CancellationToken cancellationToken)
    {
        var access = guard.Authenticated();
        var (playlist, _) = await FindAsync(token, cancellationToken);
        return await playlists.CopyFromAsync(access, playlist, name, cancellationToken);
    }

    /// <summary>
    /// What a preview plays: the song as its owner has it, read as them (only from this server's own storage: a local
    /// folder or S3). Not found when the link does not work or the song has no preview.
    /// </summary>
    internal async Task<(UserAccess Owner, Guid TrackId)> PreviewAsync(string token, Guid entryId, CancellationToken cancellationToken)
    {
        var (playlist, owner) = await FindAsync(token, cancellationToken);
        var trackId = await db.PlaylistEntries.Where(e => e.PlaylistId == playlist.Id && e.Id == entryId).Select(e => (Guid?)e.TrackId)
                          .SingleOrDefaultAsync(cancellationToken)
                      ?? throw new ArcanistException.NotFound();
        if (!await catalog.VisibleFiles(owner).AnyAsync(f => f.TrackId == trackId
                && (f.LibrarySource.Kind == LibrarySourceKind.Local || f.LibrarySource.Kind == LibrarySourceKind.S3), cancellationToken))
            throw new ArcanistException.NotFound();
        return (owner, trackId);
    }

    public static string PreviewUrl(string token, Guid entryId) => $"/rest/v1/public/playlists/{token}/preview/{entryId}";

    private async Task<(PlaylistEntity Playlist, UserAccess Owner)> FindAsync(string token, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(token) || token.Length > 64 || !(await sharing.GetAsync(cancellationToken)).PublicLinks)
            throw new ArcanistException.NotFound();
        var playlist = await db.Playlists.AsNoTracking().SingleOrDefaultAsync(p => p.LinkToken == token, cancellationToken)
                       ?? throw new ArcanistException.NotFound();
        // The owner must still be able to share it: a disabled account, or one without the permission, shares nothing.
        var owner = await users.GetUserAsync(playlist.OwnerId, cancellationToken);
        if (owner is null || owner.IsDisabled || !owner.Has(Permissions.ShareLinks))
            throw new ArcanistException.NotFound();
        return (playlist, owner);
    }

    private async Task<string> OwnerNameAsync(Guid ownerId, CancellationToken cancellationToken) =>
        (await playlists.UsersAsync([ownerId], cancellationToken)).GetValueOrDefault(ownerId)?.Name ?? "";
}
