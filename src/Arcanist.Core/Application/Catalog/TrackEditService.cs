using Arcanist.Core.Analysis;
using Arcanist.Core.Auth;
using Arcanist.Core.Data;
using Arcanist.Core.Data.Entities;
using Arcanist.Core.Data.Search;
using Arcanist.Core.Hosting;
using Arcanist.Core.Organization;
using Arcanist.Core.Scanning;
using Arcanist.Core.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SkiaSharp;

namespace Arcanist.Core.Application.Catalog;

/// <summary>
/// What a library app does to its own tracks, done to the server's: editing their tags and covers, and deleting them.
/// <para>
/// Edits are written to the files themselves, the one case where Arcanist re-tags library files (asked for by the user,
/// so apps such as Ultimate Desktop can use a server as their only library). Each copy of a track is copied out, tagged,
/// read back and put in place of the original, which goes to the location's trash (<c>.arcanist-trash</c>, purged after
/// 30 days): the file is never seen half-written and nothing is lost. Then the files go through the catalog writer as a
/// scan would read them, so the catalog says what the files say, and the track keeps its id (and what users attached to
/// it) unless the new tags make it the same song as another track of its album, which it then joins.
/// </para>
/// <para>
/// Copies in sources the server cannot write (another server's library, Ultimate Cloud) keep their tags. Deleting moves
/// the files to the trash too; the catalog forgets them at once, and the tracks left without files leave it with their
/// ratings, plays and playlist entries.
/// </para>
/// Needs EDIT_METADATA (tags and covers) or DELETE_TRACKS, and a library the user adds to (not one they only listen to).
/// </summary>
public sealed class TrackEditService(
    ArcanistDbContext db,
    AccessGuard guard,
    IArcanistDbContextFactory databases,
    ILibraryStorageResolver storages,
    LibraryLocks locks,
    ArtworkStore artworkStore,
    ArcanistPaths paths,
    CatalogService catalog,
    CatalogSignals signals,
    IOptions<ScannerOptions> scanner,
    TimeProvider time,
    ILogger<TrackEditService> logger)
{
    public const int MaxTracks = 1000;

    /// <summary>Largest cover accepted (the tag reader ignores bigger pictures).</summary>
    public const int MaxArtworkBytes = 20 * 1024 * 1024;

    /// <summary>How long an edit waits for a scan or an import of the library to let it in, before answering <c>busy</c>.</summary>
    internal static TimeSpan LockTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>The catalog's changes in the libraries the user can access (the <c>catalogChanged</c> subscription).</summary>
    public IObservable<CatalogChange> WatchCatalog() => signals.For(guard.Authenticated());

    /// <summary>A cover sent as base64 (GraphQL), checked as <see cref="SetTrackArtworkAsync"/> does.</summary>
    internal static byte[] FromBase64(string image)
    {
        try
        {
            var comma = image.IndexOf(',');
            // A data URL ("data:image/png;base64,…") works too.
            return Convert.FromBase64String(image.StartsWith("data:", StringComparison.OrdinalIgnoreCase) && comma > 0 ? image[(comma + 1)..] : image);
        }
        catch (FormatException)
        {
            throw ArcanistException.Field("image", ErrorCodes.InvalidFormat);
        }
    }

    // ── Tags ────────────────────────────────────────────────────────────────

    public async Task<Track> EditAsync(Guid trackId, TrackEditInput changes, CancellationToken cancellationToken) =>
        (await EditManyAsync(new EditTracksInput([trackId], changes), cancellationToken))[0];

    /// <summary>The same changes to every track, in one go. Returns the tracks as they are now, in the order asked.</summary>
    public async Task<IReadOnlyList<Track>> EditManyAsync(EditTracksInput input, CancellationToken cancellationToken)
    {
        var access = guard.Permission(Permissions.EditMetadata);
        var ids = Distinct(input.TrackIds);
        var changes = Validate(input.Changes ?? new TrackEditInput());
        var current = await TrackFactsAsync(access, ids, cancellationToken);
        if (changes == new TrackEditInput())
            return await ModelsAsync(ids, cancellationToken);
        if (changes.Album is not null || changes.AlbumArtist is not null)
        {
            // The release would keep the track in its old album: a new album is another release unless the edit says which.
            changes = changes with { MusicBrainzReleaseId = changes.MusicBrainzReleaseId ?? Guid.Empty };
        }

        var moved = await RewriteAsync(access, ids, changes, null, cancellationToken);

        await using (var after = databases.CreateDbContext())
        {
            // Lyrics cleared from the tags leave the catalog too (a .lrc file or ones found online stay).
            var edited = moved.Values.Distinct().ToList();
            if (changes.Lyrics is { Length: 0 })
                await after.Lyrics.Where(l => edited.Contains(l.TrackId) && l.Source == LyricsSource.Embedded).ExecuteDeleteAsync(cancellationToken);

            // A track moved to a new album takes its old album's cover along when the files had none of their own.
            foreach (var (id, now) in moved)
            {
                var before = current[id];
                var album = await after.Tracks.Where(t => t.Id == now).Select(t => t.Album).SingleOrDefaultAsync(cancellationToken);
                if (album is { ArtworkId: null } && album.Id != before.AlbumId && before.AlbumArtworkId is { } artwork)
                    album.ArtworkId = artwork;
            }

            await after.SaveChangesAsync(cancellationToken);
            await CleanAsync(after, cancellationToken);
        }

        return await ModelsAsync(ids.Select(id => moved.GetValueOrDefault(id, id)).ToList(), cancellationToken);
    }

    // ── Covers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Embeds an image as the front cover of the track's files (replacing their pictures) and makes it the track's own
    /// image; an album without a cover takes it too.
    /// </summary>
    public async Task<Track> SetTrackArtworkAsync(Guid trackId, byte[] image, CancellationToken cancellationToken)
    {
        var access = guard.Permission(Permissions.EditMetadata);
        var data = CheckImage(image);
        await TrackFactsAsync(access, [trackId], cancellationToken);
        var moved = await RewriteAsync(access, [trackId], new TrackEditInput(), data, cancellationToken);
        var id = moved.GetValueOrDefault(trackId, trackId);

        await using (var after = databases.CreateDbContext())
        {
            // Stored only once the files have it: a refused edit leaves nothing in the artwork cache.
            var artwork = await ArtworkAsync(after, Store(data), cancellationToken);
            var track = await after.Tracks.Include(t => t.Album).SingleAsync(t => t.Id == id, cancellationToken);
            track.ArtworkId = artwork;
            track.Album.ArtworkId ??= artwork;
            track.UpdatedAt = Now();
            await after.SaveChangesAsync(cancellationToken);
            await CleanAsync(after, cancellationToken);
        }

        return (await ModelsAsync([id], cancellationToken))[0];
    }

    /// <summary>Removes the pictures of the track's files and its own image: it shows its album's cover, if any.</summary>
    public async Task<Track> RemoveTrackArtworkAsync(Guid trackId, CancellationToken cancellationToken)
    {
        var access = guard.Permission(Permissions.EditMetadata);
        await TrackFactsAsync(access, [trackId], cancellationToken);
        var moved = await RewriteAsync(access, [trackId], new TrackEditInput(), [], cancellationToken);
        var id = moved.GetValueOrDefault(trackId, trackId);

        await using (var after = databases.CreateDbContext())
        {
            await after.Tracks.Where(t => t.Id == id)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.ArtworkId, (Guid?)null).SetProperty(t => t.UpdatedAt, Now()), cancellationToken);
            await CleanAsync(after, cancellationToken);
        }

        return (await ModelsAsync([id], cancellationToken))[0];
    }

    /// <summary>Embeds an image as the cover of every file of the album's tracks and makes it the album's cover.</summary>
    public Task<Album> SetAlbumArtworkAsync(Guid albumId, byte[] image, CancellationToken cancellationToken)
    {
        guard.Permission(Permissions.EditMetadata);
        return AlbumArtworkAsync(albumId, CheckImage(image), cancellationToken);
    }

    /// <summary>Removes the pictures of every file of the album's tracks and the album's cover.</summary>
    public Task<Album> RemoveAlbumArtworkAsync(Guid albumId, CancellationToken cancellationToken) =>
        AlbumArtworkAsync(albumId, null, cancellationToken);

    private async Task<Album> AlbumArtworkAsync(Guid albumId, byte[]? image, CancellationToken cancellationToken)
    {
        var access = guard.Permission(Permissions.EditMetadata);
        var visible = catalog.Visible(access);
        var trackIds = await visible.Where(t => t.AlbumId == albumId).Select(t => t.Id).ToListAsync(cancellationToken);
        if (trackIds.Count == 0)
            throw new ArcanistException.NotFound();

        var moved = await RewriteAsync(access, trackIds, new TrackEditInput(), image ?? [], cancellationToken);
        var now = trackIds.Select(id => moved.GetValueOrDefault(id, id)).Distinct().ToList();
        Guid resultAlbum;
        await using (var after = databases.CreateDbContext())
        {
            Guid? artwork = image is { } set ? await ArtworkAsync(after, Store(set), cancellationToken) : null;
            var albumIds = await after.Tracks.Where(t => now.Contains(t.Id)).Select(t => t.AlbumId).Distinct().ToListAsync(cancellationToken);
            await after.Albums.Where(a => albumIds.Contains(a.Id)).ExecuteUpdateAsync(s => s.SetProperty(a => a.ArtworkId, artwork), cancellationToken);
            await after.Tracks.Where(t => now.Contains(t.Id))
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.ArtworkId, (Guid?)null).SetProperty(t => t.UpdatedAt, Now()), cancellationToken);
            resultAlbum = albumIds.Contains(albumId) || albumIds.Count == 0 ? albumId : albumIds[0];
            await CleanAsync(after, cancellationToken);
        }

        return await catalog.AlbumAsync(resultAlbum, cancellationToken);
    }

    /// <summary>
    /// The image as sent, or re-encoded as JPEG when it is not JPEG or PNG (the formats every tag format takes). Nothing
    /// is written yet (see <see cref="Store"/>).
    /// </summary>
    private static byte[] CheckImage(byte[] image)
    {
        if (image.Length == 0)
            throw ArcanistException.Field("image", ErrorCodes.Required);
        if (image.Length > MaxArtworkBytes)
            throw ArcanistException.Field("image", ErrorCodes.TooMany);

        var data = image;
        using (var codec = SKCodec.Create(new SKMemoryStream(image)))
        {
            if (codec is null || codec.Info.Width <= 0 || codec.Info.Height <= 0)
                throw ArcanistException.Field("image", ErrorCodes.InvalidFormat);
            if (codec.EncodedFormat is not (SKEncodedImageFormat.Jpeg or SKEncodedImageFormat.Png))
            {
                using var bitmap = SKBitmap.Decode(image) ?? throw ArcanistException.Field("image", ErrorCodes.InvalidFormat);
                using var decoded = SKImage.FromBitmap(bitmap);
                using var encoded = decoded.Encode(SKEncodedImageFormat.Jpeg, 92);
                data = encoded.ToArray();
            }
        }

        return data;
    }

    /// <summary>The checked image in the artwork cache, once the files have it.</summary>
    private StoredArtwork Store(byte[] data) => artworkStore.Store(data) ?? throw ArcanistException.Field("image", ErrorCodes.InvalidFormat);

    private static async Task<Guid> ArtworkAsync(ArcanistDbContext context, StoredArtwork stored, CancellationToken cancellationToken)
    {
        if (await context.Artworks.Where(a => a.Hash == stored.Hash).Select(a => (Guid?)a.Id).FirstOrDefaultAsync(cancellationToken) is { } id)
            return id;

        var artwork = new Data.Entities.Artwork { Hash = stored.Hash, MimeType = stored.MimeType, Width = stored.Width, Height = stored.Height, Source = ArtworkSource.Embedded };
        context.Artworks.Add(artwork);
        await context.SaveChangesAsync(cancellationToken);
        return artwork.Id;
    }

    // ── Deleting ────────────────────────────────────────────────────────────

    /// <summary>Deletes one track (or its copies in <paramref name="libraryId"/>), answering with the error of its outcome.</summary>
    public async Task DeleteAsync(Guid trackId, Guid? libraryId, CancellationToken cancellationToken)
    {
        var result = (await DeleteManyAsync(new DeleteTracksInput([trackId], libraryId), cancellationToken))[0];
        switch (result.Status)
        {
            case TrackDeletionStatus.Deleted:
                return;
            case TrackDeletionStatus.NotFound:
                throw new ArcanistException.NotFound();
            case TrackDeletionStatus.Forbidden:
                throw new ArcanistException.Forbidden(result.Code ?? ErrorCodes.Forbidden);
            default:
                throw new ArcanistException.Conflict(result.Code ?? ErrorCodes.WriteFailed);
        }
    }

    /// <summary>
    /// Moves the files of the tracks (of every copy, or of those in <c>libraryId</c>) to the trash of their locations and
    /// takes them out of the catalog; a track left without copies goes with its ratings, plays and playlist entries. Each
    /// track has its own outcome: one that cannot be deleted does not stop the others.
    /// </summary>
    public async Task<IReadOnlyList<TrackDeletion>> DeleteManyAsync(DeleteTracksInput input, CancellationToken cancellationToken)
    {
        var access = guard.Permission(Permissions.DeleteTracks);
        var ids = Distinct(input.TrackIds);
        var libraryId = Libraries.VirtualLibraries.Filter(input.LibraryId);
        if (libraryId is { } only && !access.CanAccessLibrary(only))
            throw new ArcanistException.NotFound();

        var visible = await catalog.Visible(access).Where(t => ids.Contains(t.Id)).Select(t => t.Id).ToListAsync(cancellationToken);
        // Only the copies the user can see (one in another user's personal library is not theirs to delete).
        var files = await catalog.VisibleFiles(access).AsNoTracking().Include(f => f.LibrarySource)
            .Where(f => ids.Contains(f.TrackId) && (libraryId == null || f.LibrarySource.LibraryId == libraryId))
            .ToListAsync(cancellationToken);

        var results = new Dictionary<Guid, TrackDeletion>();
        var writable = new Dictionary<Guid, bool>();
        var deletable = new List<(Guid TrackId, List<MediaFile> Files)>();
        foreach (var id in ids)
        {
            var copies = files.Where(f => f.TrackId == id).ToList();
            if (!visible.Contains(id) || copies.Count == 0)
            {
                results[id] = new TrackDeletion(id, TrackDeletionStatus.NotFound, ErrorCodes.NotFound);
                continue;
            }

            if (await RefusalAsync(access, copies, writable, cancellationToken) is { } refusal)
            {
                results[id] = new TrackDeletion(id, refusal.Status, refusal.Code);
                continue;
            }

            deletable.Add((id, copies));
        }

        if (deletable.Count > 0)
        {
            var removed = new List<Guid>();
            using (await LockAsync(deletable.SelectMany(d => d.Files).Select(f => f.LibrarySource.LibraryId), cancellationToken))
            {
                foreach (var (id, copies) in deletable)
                {
                    var failed = false;
                    foreach (var file in copies)
                    {
                        // Only this track's use of a file that stays.
                        if (file.IsSharedCopy)
                        {
                            removed.Add(file.Id);
                            continue;
                        }

                        var storage = storages.For(file.LibrarySource.Kind);
                        var location = StorageLocation.From(file.LibrarySource);
                        try
                        {
                            var leaving = deletable.SelectMany(d => d.Files).Select(f => f.Id).ToList();
                            // Another album's track plays it: the file stays, for that track.
                            if (await HandOverAsync(file, leaving, cancellationToken))
                                continue;
                            await TrashWithLyricsAsync(storage, location, file, leaving, cancellationToken);
                            removed.Add(file.Id);
                            await RemoveFolderAsync(storage, location, file.RelativePath, cancellationToken);
                        }
                        catch (ArcanistException.Conflict e)
                        {
                            logger.LogWarning(e, "Could not hand {Path} over to the track that plays it too", file.RelativePath);
                            failed = true;
                        }
                        catch (Exception e) when (e is IOException or UnauthorizedAccessException or HttpRequestException)
                        {
                            logger.LogWarning(e, "Could not move {Path} to the trash", file.RelativePath);
                            failed = true;
                        }
                    }

                    results[id] = failed
                        ? new TrackDeletion(id, TrackDeletionStatus.Failed, ErrorCodes.WriteFailed)
                        : new TrackDeletion(id, TrackDeletionStatus.Deleted, null);
                }

                await db.MediaFiles.Where(f => removed.Contains(f.Id)).ExecuteDeleteAsync(cancellationToken);
            }

            await using var after = databases.CreateDbContext();
            await CleanAsync(after, cancellationToken);
            var deleted = deletable.Where(d => results[d.TrackId].Status == TrackDeletionStatus.Deleted).Select(d => d.TrackId).ToList();
            var left = await after.Tracks.Where(t => deleted.Contains(t.Id)).Select(t => t.Id).ToListAsync(cancellationToken);
            signals.Publish(deletable.SelectMany(d => d.Files).Select(f => f.LibrarySource.LibraryId), left, deleted.Except(left));
            logger.LogInformation("{User} deleted {Count} tracks", access.UserName, results.Values.Count(r => r.Status == TrackDeletionStatus.Deleted));
        }

        return ids.Select(id => results[id]).ToList();
    }

    /// <summary>Moves a file to the trash. Its .lrc goes along unless another audio file of the folder (song.mp3 beside song.flac) stays and uses it.</summary>
    private async Task TrashWithLyricsAsync(ILibraryStorage storage, StorageLocation location, MediaFile file, List<Guid> leaving, CancellationToken cancellationToken)
    {
        if (await storage.ExistsAsync(location, file.RelativePath, cancellationToken))
            await storage.TrashAsync(location, file.RelativePath, cancellationToken);
        var stem = AudioFormats.WithoutExtension(file.RelativePath);
        var lyrics = stem + ".lrc";
        var staying = await db.MediaFiles.AsNoTracking()
            .Where(f => f.LibrarySourceId == file.LibrarySourceId && !leaving.Contains(f.Id) && f.RelativePath.StartsWith(stem + "."))
            .Select(f => f.RelativePath).ToListAsync(cancellationToken);
        if (!staying.Any(p => AudioFormats.WithoutExtension(p) == stem)
            && await storage.ExistsAsync(location, lyrics, cancellationToken))
            await storage.TrashAsync(location, lyrics, cancellationToken);
    }

    /// <summary>
    /// A file whose track is going, while another track plays it too: that track becomes its owner, and the file's tags
    /// say what the catalog says of it (so scans read it as that track from now on). False when nobody else plays it.
    /// </summary>
    private async Task<bool> HandOverAsync(MediaFile file, IReadOnlyList<Guid> leaving, CancellationToken cancellationToken)
    {
        await using var after = databases.CreateDbContext();
        var heir = await after.MediaFiles
            .Where(f => f.LibrarySourceId == file.LibrarySourceId && f.RelativePath == file.RelativePath && f.CueTrack < 0 && !leaving.Contains(f.Id))
            .OrderBy(f => f.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (heir is null)
            return false;

        var description = await VirtualTracks.DescribeAsync(after, heir.TrackId, cancellationToken);
        var folder = Path.Combine(paths.Cache, "edits");
        Directory.CreateDirectory(folder);
        var scanned = await RewriteFileAsync(file, description.Input with { Artists = [.. description.Input.Artists ?? [], .. description.Featured] }, null, folder, [], cancellationToken);

        await after.MediaFiles.Where(f => f.Id == file.Id).ExecuteDeleteAsync(CancellationToken.None);
        heir.CueTrack = 0;
        heir.TrackLocked = false;
        heir.Size = scanned.Size;
        heir.ModifiedAt = scanned.ModifiedAt;
        heir.ContentHash = scanned.ContentHash;
        await after.SaveChangesAsync(CancellationToken.None);
        await SharedFiles.SyncAsync(after, file.LibrarySourceId, CancellationToken.None);
        logger.LogInformation("{Path} is now the file of track {Track}: its track was deleted and that one played it too", file.RelativePath, heir.TrackId);
        return true;
    }

    /// <summary>
    /// Deletes one copy of a song that has others (a FLAC and an MP3 of it): the file goes to the trash and the track stays.
    /// Tracks of other albums that played that copy follow to another copy in its folder, or become its owner.
    /// </summary>
    public async Task DeleteFileAsync(Guid fileId, CancellationToken cancellationToken)
    {
        var access = guard.Permission(Permissions.DeleteTracks);
        var file = await catalog.VisibleFiles(access).AsNoTracking().Include(f => f.LibrarySource)
                       .SingleOrDefaultAsync(f => f.Id == fileId && f.CueTrack >= 0, cancellationToken)
                   ?? throw new ArcanistException.NotFound();
        if (await RefusalAsync(access, [file], [], cancellationToken) is { } refusal)
        {
            throw refusal.Status == TrackDeletionStatus.Forbidden
                ? new ArcanistException.Forbidden(refusal.Code)
                : new ArcanistException.Conflict(refusal.Code);
        }

        var others = await catalog.VisibleFiles(access).AsNoTracking()
            .Where(f => f.TrackId == file.TrackId && f.Id != file.Id && f.CueTrack >= 0)
            .ToListAsync(cancellationToken);
        if (others.Count == 0)
            throw new ArcanistException.Conflict(ErrorCodes.LastCopy);

        var libraryId = file.LibrarySource.LibraryId;
        var storage = storages.For(file.LibrarySource.Kind);
        var location = StorageLocation.From(file.LibrarySource);
        using (await LockAsync([libraryId], cancellationToken))
        {
            await using var after = databases.CreateDbContext();
            var stand = others.FirstOrDefault(f => f.LibrarySourceId == file.LibrarySourceId && f.CueTrack == 0);
            if (stand is not null)
            {
                var followers = await after.MediaFiles
                    .Where(f => f.LibrarySourceId == file.LibrarySourceId && f.RelativePath == file.RelativePath && f.CueTrack < 0)
                    .ToListAsync(cancellationToken);
                var slots = await after.MediaFiles.Where(f => f.LibrarySourceId == stand.LibrarySourceId && f.RelativePath == stand.RelativePath)
                    .Select(f => f.CueTrack).ToListAsync(cancellationToken);
                foreach (var follower in followers)
                {
                    follower.CueTrack = SharedFiles.NextSlot(slots);
                    slots.Add(follower.CueTrack);
                    follower.RelativePath = stand.RelativePath;
                }

                await after.SaveChangesAsync(CancellationToken.None);
            }

            if (!await HandOverAsync(file, [file.Id], cancellationToken))
            {
                try
                {
                    await TrashWithLyricsAsync(storage, location, file, [file.Id], cancellationToken);
                    await RemoveFolderAsync(storage, location, file.RelativePath, cancellationToken);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or HttpRequestException)
                {
                    logger.LogWarning(e, "Could not move {Path} to the trash", file.RelativePath);
                    throw new ArcanistException.Conflict(ErrorCodes.WriteFailed);
                }

                await after.MediaFiles.Where(f => f.Id == file.Id).ExecuteDeleteAsync(CancellationToken.None);
            }

            await SharedFiles.SyncAsync(after, file.LibrarySourceId, CancellationToken.None);
        }

        await using (var clean = databases.CreateDbContext())
            await CleanAsync(clean, CancellationToken.None);
        signals.Publish([libraryId], [file.TrackId], []);
        logger.LogInformation("{User} deleted the copy {Path} of track {Track}", access.UserName, file.RelativePath, file.TrackId);
    }

    /// <summary>
    /// Makes <paramref name="trackId"/> play <paramref name="fileId"/> (a file of another track, in the same folder of a
    /// library) in place of its own copies there, which go to the trash. The track stays on its album, with its own title,
    /// number and everything users attached to it. Tracks of other albums that played those copies follow to this file.
    /// </summary>
    public async Task ShareFileAsync(Guid fileId, Guid trackId, CancellationToken cancellationToken)
    {
        var access = guard.Permission(Permissions.DeleteTracks);
        var keep = await catalog.VisibleFiles(access).AsNoTracking().Include(f => f.LibrarySource)
                       .SingleOrDefaultAsync(f => f.Id == fileId, cancellationToken)
                   ?? throw new ArcanistException.NotFound();
        var replaced = await catalog.VisibleFiles(access).AsNoTracking().Include(f => f.LibrarySource)
            .Where(f => f.TrackId == trackId && f.LibrarySourceId == keep.LibrarySourceId)
            .ToListAsync(cancellationToken);
        if (keep.CueTrack != 0 || keep.TrackId == trackId || !replaced.Any(f => f.CueTrack == 0))
            throw new ArcanistException.Conflict(ErrorCodes.DuplicateNotShareable);
        if (await RefusalAsync(access, [keep, .. replaced], [], cancellationToken) is { } refusal)
        {
            throw refusal.Status == TrackDeletionStatus.Forbidden
                ? new ArcanistException.Forbidden(refusal.Code)
                : new ArcanistException.Conflict(refusal.Code);
        }

        var storage = storages.For(keep.LibrarySource.Kind);
        var location = StorageLocation.From(keep.LibrarySource);
        var libraryId = keep.LibrarySource.LibraryId;
        var replacedPaths = replaced.Where(f => f.CueTrack == 0).Select(f => f.RelativePath).ToList();
        var now = Now();

        using (await LockAsync([libraryId], cancellationToken))
        {
            var replacedIds = replaced.Select(f => f.Id).ToList();
            foreach (var file in replaced.Where(f => f.CueTrack == 0))
            {
                try
                {
                    await TrashWithLyricsAsync(storage, location, file, replacedIds, cancellationToken);
                    await RemoveFolderAsync(storage, location, file.RelativePath, cancellationToken);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or HttpRequestException)
                {
                    logger.LogWarning(e, "Could not move {Path} to the trash", file.RelativePath);
                    throw new ArcanistException.Conflict(ErrorCodes.WriteFailed);
                }
            }

            // Past the trash there is no turning back: the catalog must say so even if the caller leaves.
            await using var after = databases.CreateDbContext();
            await using var transaction = await after.Database.BeginTransactionAsync(CancellationToken.None);
            var slots = await after.MediaFiles.Where(f => f.LibrarySourceId == keep.LibrarySourceId && f.RelativePath == keep.RelativePath)
                .Select(f => f.CueTrack).ToListAsync(CancellationToken.None);
            var followers = await after.MediaFiles
                .Where(f => f.LibrarySourceId == keep.LibrarySourceId && f.CueTrack < 0 && replacedPaths.Contains(f.RelativePath) && f.TrackId != trackId)
                .ToListAsync(CancellationToken.None);
            foreach (var follower in followers)
            {
                follower.CueTrack = SharedFiles.NextSlot(slots);
                slots.Add(follower.CueTrack);
                follower.RelativePath = keep.RelativePath;
            }

            await after.SaveChangesAsync(CancellationToken.None);
            await after.MediaFiles.Where(f => replacedIds.Contains(f.Id)).ExecuteDeleteAsync(CancellationToken.None);
            after.MediaFiles.Add(SharedFiles.CopyFor(keep, trackId, SharedFiles.NextSlot(slots), now));
            await after.Tracks.Where(t => t.Id == trackId)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.DurationMs, keep.DurationMs).SetProperty(t => t.UpdatedAt, now), CancellationToken.None);
            await after.SaveChangesAsync(CancellationToken.None);
            await SharedFiles.SyncAsync(after, keep.LibrarySourceId, CancellationToken.None);
            await transaction.CommitAsync(CancellationToken.None);
        }

        await using (var clean = databases.CreateDbContext())
        {
            var albums = await clean.Tracks.Where(t => t.Id == trackId || t.Id == keep.TrackId).Select(t => t.AlbumId).Distinct().ToListAsync(CancellationToken.None);
            await AlbumLoudness.UpdateAsync(clean, albums, CancellationToken.None);
            await CleanAsync(clean, CancellationToken.None);
        }

        signals.Publish([libraryId], [trackId, keep.TrackId], []);
        logger.LogInformation("{User} made track {Track} play the file of track {Other}; its copies went to the trash", access.UserName, trackId, keep.TrackId);
    }

    private static async Task RemoveFolderAsync(ILibraryStorage storage, StorageLocation location, string path, CancellationToken cancellationToken)
    {
        var folder = AudioFormats.Folder(path);
        if (folder.Length > 0)
            await storage.RemoveEmptyFoldersAsync(location, folder, cancellationToken);
    }

    // ── The rewrite ─────────────────────────────────────────────────────────

    private sealed record TrackFacts(Guid Id, Guid AlbumId, Guid? AlbumArtworkId);

    /// <summary>The tracks, visible to the user; not_found when one is not.</summary>
    private async Task<Dictionary<Guid, TrackFacts>> TrackFactsAsync(UserAccess access, IReadOnlyList<Guid> ids, CancellationToken cancellationToken)
    {
        var facts = await catalog.Visible(access).Where(t => ids.Contains(t.Id))
            .Select(t => new TrackFacts(t.Id, t.AlbumId, t.Album.ArtworkId))
            .ToDictionaryAsync(t => t.Id, cancellationToken);
        if (facts.Count != ids.Count)
            throw new ArcanistException.NotFound();
        return facts;
    }

    /// <summary>
    /// Writes the changes (and <paramref name="picture"/>: see <see cref="TagWriter.Edit"/>) into every writable copy of
    /// the tracks and reads them into the catalog. Returns, for each track, the track its files are on now.
    /// </summary>
    private async Task<Dictionary<Guid, Guid>> RewriteAsync(
        UserAccess access, IReadOnlyList<Guid> ids, TrackEditInput changes, byte[]? picture, CancellationToken cancellationToken)
    {
        // Only the copies the user can see: one in a library hidden from them (another user's personal one) is neither
        // a reason to refuse nor rewritten.
        var files = await catalog.VisibleFiles(access).AsNoTracking().Include(f => f.LibrarySource)
            .Where(f => ids.Contains(f.TrackId))
            .ToListAsync(cancellationToken);

        // Everything is checked before anything is written.
        var writable = new Dictionary<Guid, bool>();
        var toWrite = new List<MediaFile>();
        var virtualCopies = new List<MediaFile>();
        foreach (var id in ids)
        {
            var copies = files.Where(f => f.TrackId == id && f.LibrarySource.Kind is LibrarySourceKind.Local or LibrarySourceKind.S3).ToList();
            if (copies.Count == 0)
                throw new ArcanistException.Conflict(ErrorCodes.ReadOnly);
            if (await RefusalAsync(access, copies, writable, cancellationToken) is { } refusal)
            {
                throw refusal.Status == TrackDeletionStatus.Forbidden
                    ? new ArcanistException.Forbidden(refusal.Code)
                    : new ArcanistException.Conflict(refusal.Code);
            }

            // A track that only plays another's file has its metadata in the catalog alone: that file's tags are the other's.
            if (copies.Any(f => !f.IsSharedCopy))
                toWrite.AddRange(copies.Where(f => !f.IsSharedCopy));
            else
                virtualCopies.AddRange(copies);
        }

        var folder = Path.Combine(paths.Cache, "edits");
        Directory.CreateDirectory(folder);
        var scanned = new List<ScannedFile>();
        var written = new List<MediaFile>();
        var moved = new Dictionary<Guid, Guid>();
        var sidecarsRemoved = new HashSet<Guid>();
        try
        {
            using (await LockAsync(toWrite.Concat(virtualCopies).Select(f => f.LibrarySource.LibraryId), cancellationToken))
            {
                foreach (var file in toWrite)
                {
                    scanned.Add(await RewriteFileAsync(file, changes, picture, folder, sidecarsRemoved, cancellationToken));
                    written.Add(file);
                }

                if (changes != new TrackEditInput())
                {
                    foreach (var file in virtualCopies)
                    {
                        await VirtualTracks.EditAsync(databases, time, file.Id, changes, cancellationToken);
                        written.Add(file);
                    }
                }
            }
        }
        finally
        {
            // What was written is catalogued, and its tracks follow it, even when a later file failed.
            if (written.Count > 0)
            {
                // A .lrc that went (the edited lyrics have no timing): its lyrics leave the catalog, or they would still win
                // over the tags' (a sidecar is trusted most).
                if (sidecarsRemoved.Count > 0)
                {
                    await using var clean = databases.CreateDbContext();
                    await clean.Lyrics.Where(l => sidecarsRemoved.Contains(l.TrackId) && l.Source == LyricsSource.Sidecar)
                        .ExecuteDeleteAsync(CancellationToken.None);
                }

                if (scanned.Count > 0)
                    await CatalogueAsync(scanned, cancellationToken);
                moved = await FollowAsync(ids, written);
            }
        }

        var result = ids.ToDictionary(id => id, id => moved.GetValueOrDefault(id, id));
        signals.Publish(toWrite.Concat(virtualCopies).Select(f => f.LibrarySource.LibraryId), result.Values.Distinct(), ids.Where(id => !result.ContainsValue(id)));
        return result;
    }

    /// <summary>
    /// Where each track's rewritten files are now. A track whose files went to another one (the new tags make it the same
    /// song as another track of the album, or a new one) is merged into it: its ratings, plays and playlist entries go
    /// along, and so do the copies that were not rewritten (missing ones, another server's), locked there.
    /// </summary>
    private async Task<Dictionary<Guid, Guid>> FollowAsync(IReadOnlyList<Guid> ids, IReadOnlyList<MediaFile> written)
    {
        var writtenIds = written.Select(w => w.Id).ToList();
        await using var merge = databases.CreateDbContext();
        var now = await merge.MediaFiles.AsNoTracking().Where(f => writtenIds.Contains(f.Id))
            .Select(f => new { f.Id, f.TrackId }).ToDictionaryAsync(f => f.Id, f => f.TrackId, CancellationToken.None);
        var moved = new Dictionary<Guid, Guid>();
        foreach (var id in ids)
        {
            if (written.Where(f => f.TrackId == id).Select(f => now.GetValueOrDefault(f.Id, id)).FirstOrDefault(id) is var target && target == id)
                continue;
            moved[id] = target;
            await TrackMerger.MergeAsync(merge, target, id, CancellationToken.None);
        }

        return moved;
    }

    private async Task<ScannedFile> RewriteFileAsync(
        MediaFile file, TrackEditInput changes, byte[]? picture, string folder, HashSet<Guid> sidecarsRemoved, CancellationToken cancellationToken)
    {
        var storage = storages.For(file.LibrarySource.Kind);
        var location = StorageLocation.From(file.LibrarySource);
        var temporary = Path.Combine(folder, $"edit-{Guid.NewGuid():N}{Path.GetExtension(file.RelativePath).ToLowerInvariant()}");
        try
        {
            try
            {
                await using (var input = await storage.OpenReadAsync(location, file.RelativePath, cancellationToken))
                await using (var output = File.Create(temporary))
                    await input.CopyToAsync(output, cancellationToken);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or HttpRequestException)
            {
                logger.LogWarning(e, "Could not read {Path} to edit it", file.RelativePath);
                throw new ArcanistException.Conflict(ErrorCodes.SourceUnavailable);
            }

            try
            {
                TagWriter.Edit(temporary, changes, picture);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                logger.LogInformation(e, "Could not write the tags of {Path}", file.RelativePath);
                throw new ArcanistException.Conflict(ErrorCodes.TagsUnsupported);
            }

            AudioMetadata metadata;
            string hash;
            await using (var stream = File.OpenRead(temporary))
            {
                metadata = TagReader.Read(stream, AudioFormats.MimeType(file.RelativePath));
                hash = await ContentHasher.HashAsync(stream, metadata.AudioDataOffset, metadata.AudioDataSize, cancellationToken);
            }

            var artwork = metadata.EmbeddedPicture is { } embedded ? artworkStore.Store(embedded) : null;
            var size = new FileInfo(temporary).Length;

            var trashed = await storage.TrashAsync(location, file.RelativePath, cancellationToken);
            try
            {
                // With the original in the trash there is no turning back halfway: the caller leaving (or an upload
                // timing out) must not leave the library without the file.
                await storage.ImportAsync(location, file.RelativePath, temporary, CancellationToken.None);
            }
            catch (Exception e)
            {
                logger.LogWarning(e, "Could not put the edited {Path} in place; the original goes back", file.RelativePath);
                try
                {
                    await storage.MoveAsync(location, trashed, file.RelativePath, CancellationToken.None);
                }
                catch (Exception restore)
                {
                    logger.LogError(restore, "The original of {Path} stays in the trash at {Trash}", file.RelativePath, trashed);
                }

                throw new ArcanistException.Conflict(ErrorCodes.WriteFailed);
            }

            var entry = await storage.StatAsync(location, file.RelativePath, CancellationToken.None)
                        ?? new StorageEntry(file.RelativePath, size, Now());
            logger.LogInformation("Edited the tags of {Path}", file.RelativePath);

            if (changes.Lyrics is { } lyrics)
            {
                var (sidecarMetadata, removed) = await EditSidecarLyricsAsync(storage, location, file.RelativePath, lyrics, metadata, folder);
                metadata = sidecarMetadata;
                if (removed)
                    sidecarsRemoved.Add(file.TrackId);
            }

            return new ScannedFile(file.LibrarySourceId, file.Id, file.RelativePath, entry.Size, entry.ModifiedAt, hash,
                metadata with { EmbeddedPicture = null }, artwork);
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    /// <summary>
    /// The .lrc beside an edited file, which a scan reads before its tags: lyrics with timing replace what it says (and
    /// are catalogued from it, as a scan would); lyrics without timing, or none, take it to the trash, so the tags'
    /// are the ones read. Returns the metadata to catalogue, and whether a .lrc went. Without a .lrc, nothing changes.
    /// </summary>
    private async Task<(AudioMetadata Metadata, bool Removed)> EditSidecarLyricsAsync(
        ILibraryStorage storage, StorageLocation location, string audioPath, string lyrics, AudioMetadata metadata, string folder)
    {
        var stem = AudioFormats.WithoutExtension(audioPath);
        string? sidecar = null;
        foreach (var candidate in new[] { stem + ".lrc", stem + ".LRC" })
        {
            if (await storage.ExistsAsync(location, candidate, CancellationToken.None))
            {
                sidecar = candidate;
                break;
            }
        }

        if (sidecar is null)
            return (metadata, false);

        var text = lyrics.Replace("\r\n", "\n").Trim();
        var lines = Lrc.Parse(text);
        var trashed = false;
        try
        {
            // Past the audio file there is no turning back: the caller leaving must not stop this halfway.
            await storage.TrashAsync(location, sidecar, CancellationToken.None);
            trashed = true;
            if (lines.Count == 0)
            {
                logger.LogInformation("Moved {Path} to the trash: the edited lyrics have no timing", sidecar);
                return (metadata, true);
            }

            var temporary = Path.Combine(folder, $"edit-{Guid.NewGuid():N}.lrc");
            try
            {
                await File.WriteAllTextAsync(temporary, text + "\n", new System.Text.UTF8Encoding(false), CancellationToken.None);
                await storage.ImportAsync(location, sidecar, temporary, CancellationToken.None);
            }
            finally
            {
                File.Delete(temporary);
            }

            logger.LogInformation("Wrote the edited lyrics to {Path}", sidecar);
            return (metadata with { SyncedLyrics = text, Lyrics = string.Join('\n', lines.Select(l => l.Text)), LyricsSource = LyricsSource.Sidecar }, false);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or HttpRequestException)
        {
            // The audio file is in place already, with the lyrics in its tags: it is catalogued either way. With the old .lrc
            // in the trash, the tags' are the ones read; when it couldn't even go there, it still wins until fixed by hand.
            logger.LogWarning(e, "Could not write the edited lyrics to {Path}", sidecar);
            return (metadata, trashed);
        }
    }

    /// <summary>Reads the rewritten files into the catalog with the scanner's writer, and makes them searchable.</summary>
    private async Task CatalogueAsync(IReadOnlyList<ScannedFile> scanned, CancellationToken cancellationToken)
    {
        await using var writerDb = databases.CreateDbContext();
        var writer = new CatalogWriter(writerDb, time);
        await writer.InitializeAsync(CancellationToken.None);
        // Once in place, the files must be catalogued even if the caller leaves.
        await writer.ApplyAsync(scanned, CancellationToken.None);
        await SearchIndexer.RefreshAsync(writerDb, CancellationToken.None);
        foreach (var source in scanned.Select(f => f.SourceId).Distinct())
            await SharedFiles.SyncAsync(writerDb, source, CancellationToken.None);
    }

    /// <summary>Why the user cannot change these copies of a track, or null.</summary>
    private async Task<(TrackDeletionStatus Status, string Code)?> RefusalAsync(
        UserAccess access, List<MediaFile> copies, Dictionary<Guid, bool> writable, CancellationToken cancellationToken)
    {
        foreach (var file in copies)
        {
            var libraryId = file.LibrarySource.LibraryId;
            if (!access.CanChange(libraryId))
                return (TrackDeletionStatus.Forbidden, ErrorCodes.LibraryReadOnly);
            if (file.LibrarySource.Kind is not (LibrarySourceKind.Local or LibrarySourceKind.S3))
                return (TrackDeletionStatus.NotDeletable, ErrorCodes.ReadOnly);
            if (file.CueTrack > 0)
                return (TrackDeletionStatus.NotDeletable, ErrorCodes.CueSheetTrack);
            if (!writable.TryGetValue(file.LibrarySourceId, out var canWrite))
            {
                canWrite = await storages.For(file.LibrarySource.Kind).CanWriteAsync(StorageLocation.From(file.LibrarySource), cancellationToken);
                writable[file.LibrarySourceId] = canWrite;
            }

            if (!canWrite)
                return (TrackDeletionStatus.NotDeletable, ErrorCodes.ReadOnly);
        }

        return null;
    }

    /// <summary>The libraries' locks, in a fixed order; <c>busy</c> when a scan or an import keeps one too long.</summary>
    private async Task<IDisposable> LockAsync(IEnumerable<Guid> libraryIds, CancellationToken cancellationToken)
    {
        var held = new List<IDisposable>();
        using var wait = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        wait.CancelAfter(LockTimeout);
        try
        {
            foreach (var id in libraryIds.Distinct().Order())
                held.Add(await locks.AcquireForWritingAsync(id, wait.Token));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            Release(held);
            throw new ArcanistException.Conflict(ErrorCodes.Busy);
        }
        catch
        {
            Release(held);
            throw;
        }

        return new Releaser(() => Release(held));
    }

    private static void Release(List<IDisposable> held)
    {
        for (var i = held.Count - 1; i >= 0; i--)
            held[i].Dispose();
        held.Clear();
    }

    private sealed class Releaser(Action release) : IDisposable
    {
        public void Dispose() => release();
    }

    /// <summary>Takes out what was left without files (tracks, albums, artists, covers…), as a scan does at its end.</summary>
    private async Task CleanAsync(ArcanistDbContext context, CancellationToken cancellationToken) =>
        await new CatalogCleaner(context, artworkStore, time).CleanAsync(TimeSpan.FromDays(scanner.Value.MissingFileRetentionDays), CancellationToken.None);

    private async Task<IReadOnlyList<Track>> ModelsAsync(IReadOnlyList<Guid> ids, CancellationToken cancellationToken)
    {
        db.ChangeTracker.Clear();
        var tracks = await catalog.VisibleTracksAsync(guard.Authenticated(), ids, cancellationToken);
        return ids.Distinct().Where(tracks.ContainsKey).Select(id => tracks[id]).ToList();
    }

    private static IReadOnlyList<Guid> Distinct(IReadOnlyList<Guid>? ids)
    {
        if (ids is not { Count: > 0 })
            throw ArcanistException.Field("trackIds", ErrorCodes.Required);
        if (ids.Count > MaxTracks)
            throw ArcanistException.Field("trackIds", ErrorCodes.TooMany);
        return ids.Distinct().ToList();
    }

    private static TrackEditInput Validate(TrackEditInput changes)
    {
        var errors = new ValidationErrors();
        if (changes.Title is { } title && string.IsNullOrWhiteSpace(title))
            errors.Add("title", ErrorCodes.Required);
        Text(changes.Title, "title");
        Text(changes.Album, "album");
        Text(changes.AlbumArtist, "albumArtist");
        Text(changes.Conductor, "conductor");
        if (changes.Comment is { Length: > 2000 })
            errors.Add("comment", ErrorCodes.TooMany);
        if (changes.Lyrics is { Length: > 100_000 })
            errors.Add("lyrics", ErrorCodes.TooMany);
        List(changes.Artists, "artists");
        List(changes.Genres, "genres");
        List(changes.Composers, "composers");
        List(changes.Lyricists, "lyricists");
        Range(changes.Year, 9999, "year");
        Range(changes.TrackNumber, 9999, "trackNumber");
        Range(changes.DiscNumber, 999, "discNumber");
        if (changes.Bpm is { } bpm && (bpm < 0 || bpm >= 1000 || double.IsNaN(bpm)))
            errors.Add("bpm", ErrorCodes.InvalidFormat);
        errors.ThrowIfAny();
        return changes;

        void Text(string? value, string field)
        {
            if (value is { Length: > 500 })
                errors.Add(field, ErrorCodes.TooMany);
        }

        void List(IReadOnlyList<string>? values, string field)
        {
            if (values is null)
                return;
            if (values.Count > 50 || values.Any(v => v is null || v.Length > 500 || v.Contains(';')))
                errors.Add(field, ErrorCodes.InvalidFormat);
        }

        void Range(int? value, int max, string field)
        {
            if (value is { } number && (number < 0 || number > max))
                errors.Add(field, ErrorCodes.InvalidFormat);
        }
    }

    private DateTime Now() => time.GetUtcNow().UtcDateTime;
}
