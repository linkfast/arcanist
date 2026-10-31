using System.Linq.Expressions;
using Arcanist.Core.Application;
using Arcanist.Core.Data;
using Arcanist.Core.Data.Entities;
using Arcanist.Core.Hosting;
using Arcanist.Core.Organization;
using Arcanist.Core.Scanning;
using Arcanist.Core.Storage;
using Arcanist.Extensibility;
using Microsoft.EntityFrameworkCore;

namespace Arcanist.Core.Extensions.Sdk;

/// <summary>The server's libraries and songs, for extensions (<see cref="IMusicLibraries"/>).</summary>
internal sealed class CoreMusicLibraries(
    IArcanistDbContextFactory databases,
    ILibraryStorageResolver storages,
    LibraryLocks locks,
    ScanCoordinator scans,
    ArtworkStore artwork,
    DatabaseMaintenance maintenance,
    ArcanistPaths paths,
    ILogger<CoreMusicLibraries> logger) : IMusicLibraries
{
    public async Task<IReadOnlyList<MusicLibrary>> ListAsync(CancellationToken cancellationToken)
    {
        await using var db = databases.CreateDbContext();
        var libraries = await db.Libraries.AsNoTracking().Include(l => l.Sources).OrderBy(l => l.Number).ToListAsync(cancellationToken);
        return libraries.Select(ToModel).ToList();
    }

    public async Task<MusicLibrary?> GetAsync(Guid libraryId, CancellationToken cancellationToken)
    {
        await using var db = databases.CreateDbContext();
        var library = await db.Libraries.AsNoTracking().Include(l => l.Sources).SingleOrDefaultAsync(l => l.Id == libraryId, cancellationToken);
        return library is null ? null : ToModel(library);
    }

    public async Task<IReadOnlyList<LibrarySong>> SongsAsync(Guid libraryId, CancellationToken cancellationToken)
    {
        await using var db = databases.CreateDbContext();
        var rows = await db.MediaFiles.AsNoTracking()
            .Where(f => f.LibrarySource.LibraryId == libraryId && f.LibrarySource.Kind == LibrarySourceKind.Local && f.MissingSince == null && f.CueTrack == 0)
            .OrderByDescending(f => f.BitDepth).ThenByDescending(f => f.SampleRate).ThenByDescending(f => f.BitRate)
            .Select(ToSong)
            .ToListAsync(cancellationToken);
        return rows.DistinctBy(r => r.ContentHash).Select(r => Fill(r)!).ToList();
    }

    public async Task<LibrarySong?> FileAsync(Guid fileId, CancellationToken cancellationToken)
    {
        await using var db = databases.CreateDbContext();
        return Fill(await db.MediaFiles.AsNoTracking().Where(f => f.Id == fileId && f.MissingSince == null && f.CueTrack == 0).Select(ToSong).FirstOrDefaultAsync(cancellationToken));
    }

    public async Task<LibrarySong?> BestLocalCopyAsync(Guid trackId, CancellationToken cancellationToken)
    {
        await using var db = databases.CreateDbContext();
        return Fill(await db.MediaFiles.AsNoTracking()
            .Where(f => f.TrackId == trackId && f.MissingSince == null && f.CueTrack == 0 && f.LibrarySource.Kind == LibrarySourceKind.Local)
            .OrderByDescending(f => f.BitDepth).ThenByDescending(f => f.SampleRate).ThenByDescending(f => f.BitRate)
            .Select(ToSong)
            .FirstOrDefaultAsync(cancellationToken));
    }

    public async Task<LocalSongFile> OpenAsync(LibrarySong song, CancellationToken cancellationToken)
    {
        LibrarySource source;
        await using (var db = databases.CreateDbContext())
            source = await db.LibrarySources.AsNoTracking().SingleOrDefaultAsync(s => s.Id == song.FolderId, cancellationToken)
                     ?? throw new SongUnavailableException(ErrorCodes.NotFound);
        var storage = storages.For(source.Kind);
        var location = StorageLocation.From(source);
        if (storage.TryGetLocalPath(location, song.Path) is { } local)
            return File.Exists(local) ? new LocalSongFile(local, false) : throw new SongUnavailableException(ErrorCodes.NotFound);

        // Not on this server's disks (S3, another server, Ultimate Cloud): a temporary copy.
        var folder = Path.Combine(paths.Cache, "extensions");
        Directory.CreateDirectory(folder);
        var copy = Path.Combine(folder, $"{Guid.NewGuid():N}{Path.GetExtension(song.Path)}");
        try
        {
            await using (var input = await storage.OpenReadAsync(location, song.Path, cancellationToken))
            await using (var output = File.Create(copy))
                await input.CopyToAsync(output, cancellationToken);
            return new LocalSongFile(copy, true);
        }
        catch (Exception e) when (e is IOException or HttpRequestException or UnauthorizedAccessException)
        {
            File.Delete(copy);
            logger.LogWarning("Reading {Path} for an extension failed: {Error}", song.Path, e.Message);
            // Another server refusing the download says so in the error (PeerStorage).
            throw new SongUnavailableException(e.Message == ErrorCodes.DownloadNotAllowed ? ErrorCodes.DownloadNotAllowed
                : e is FileNotFoundException ? ErrorCodes.NotFound : ErrorCodes.UploadFailed);
        }
    }

    public async Task<SongArtwork?> ArtworkAsync(LibrarySong song, long maxBytes, CancellationToken cancellationToken)
    {
        await using var db = databases.CreateDbContext();
        var cover = await db.Tracks.AsNoTracking().Where(t => t.Id == song.TrackId)
            .Select(t => t.Artwork != null ? new { t.Artwork.Hash, t.Artwork.MimeType } : t.Album.Artwork != null ? new { t.Album.Artwork.Hash, t.Album.Artwork.MimeType } : null)
            .FirstOrDefaultAsync(cancellationToken);
        if (cover is null)
            return null;
        var file = artwork.PathFor(cover.Hash, cover.MimeType);
        return File.Exists(file) && new FileInfo(file).Length <= maxBytes ? new SongArtwork(await File.ReadAllBytesAsync(file, cancellationToken), cover.MimeType) : null;
    }

    public async Task<SongImport> ImportAsync(Guid libraryId, Guid? folderId, string file, SongPlacement placement, CancellationToken cancellationToken)
    {
        Library library;
        await using (var db = databases.CreateDbContext())
            library = await db.Libraries.AsNoTracking().Include(l => l.Sources).SingleOrDefaultAsync(l => l.Id == libraryId, cancellationToken)
                      ?? throw new ArcanistException.NotFound();
        var target = library.Sources.FirstOrDefault(s => s.Id == folderId && s.Kind == LibrarySourceKind.Local)
                     ?? library.Sources.FirstOrDefault(s => s.Kind == LibrarySourceKind.Local)
                     ?? throw new ArcanistException.Conflict(ErrorCodes.ReadOnly);

        string contentHash;
        try
        {
            await using var stream = File.OpenRead(file);
            var metadata = TagReader.Read(stream, AudioFormats.MimeType(file));
            contentHash = await ContentHasher.HashAsync(stream, metadata.AudioDataOffset, metadata.AudioDataSize, cancellationToken);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            logger.LogWarning("{File} cannot be read as audio: {Error}", Path.GetFileName(file), e.Message);
            return new SongImport(SongImportStatus.Unreadable, null, null);
        }

        var template = PathTemplate.TryParse(library.OrganizeTemplate, out var parsed, out _) ? parsed : PathTemplate.DefaultTemplate;
        var wanted = template.Render(new PathValues(null, placement.Artist, placement.Album, placement.Year, placement.DiscNumber, placement.DiscNumber > 1,
            placement.TrackNumber, placement.Title, placement.Genre));
        var storage = storages.For(target.Kind);
        var location = StorageLocation.From(target);
        using (await locks.AcquireForWritingAsync(libraryId, cancellationToken))
        {
            // The library has the same audio already (a scan may not have paired it yet): nothing to copy.
            await using (var db = databases.CreateDbContext())
                if (await db.MediaFiles.AnyAsync(f => f.ContentHash == contentHash && f.MissingSince == null && f.LibrarySource.LibraryId == libraryId, cancellationToken))
                    return new SongImport(SongImportStatus.Duplicate, contentHash, null);

            var extension = Path.GetExtension(file);
            var destination = wanted + extension;
            for (var copy = 2; await storage.ExistsAsync(location, destination, cancellationToken); copy++)
                destination = $"{wanted} ({copy}){extension}";
            await storage.ImportAsync(location, destination, file, cancellationToken);
            return new SongImport(SongImportStatus.Imported, contentHash, destination);
        }
    }

    public async Task QueueScanAsync(Guid libraryId, CancellationToken cancellationToken)
    {
        using (await maintenance.WorkAsync(cancellationToken))
            await scans.EnqueueAsync(libraryId, ScanKind.Incremental, null, cancellationToken);
    }

    private static MusicLibrary ToModel(Library library) => new(
        library.Id, library.Name, library.OwnerUserId is not null, library.ManagedBy,
        library.Sources.Select(s => new MusicLibraryFolder(s.Id, ToKind(s.Kind))).ToList());

    internal static LibraryFolderKind ToKind(LibrarySourceKind kind) => kind switch
    {
        LibrarySourceKind.S3 => LibraryFolderKind.S3,
        LibrarySourceKind.Peer => LibraryFolderKind.Peer,
        LibrarySourceKind.UltimateCloud => LibraryFolderKind.UltimateCloud,
        _ => LibraryFolderKind.Local,
    };

    private static readonly Expression<Func<MediaFile, LibrarySong>> ToSong = f => new LibrarySong(
        f.Id, f.TrackId, f.LibrarySource.LibraryId, f.LibrarySourceId,
        f.LibrarySource.Kind == LibrarySourceKind.S3 ? LibraryFolderKind.S3
            : f.LibrarySource.Kind == LibrarySourceKind.Peer ? LibraryFolderKind.Peer
            : f.LibrarySource.Kind == LibrarySourceKind.UltimateCloud ? LibraryFolderKind.UltimateCloud
            : LibraryFolderKind.Local,
        f.RelativePath, f.ContentHash, f.Track.Title,
        f.Track.Artists.Where(a => a.Role == ArtistRole.Primary).OrderBy(a => a.Position).Select(a => a.Artist.Name).FirstOrDefault(),
        f.Track.Album.Title,
        f.Track.Genres.Select(g => g.Genre.Name).FirstOrDefault(),
        f.Track.Album.Year, f.Track.TrackNumber, f.Track.DiscNumber, f.Format, f.BitRate, f.SampleRate, f.BitDepth, f.Channels, f.DurationMs,
        f.Track.Recording.MusicBrainzId, f.Track.Recording.AcoustId, false);

    /// <summary>What the catalog does not keep: whether the format is lossless (by the file's name).</summary>
    private static LibrarySong? Fill(LibrarySong? song) => song is null ? null : song with { Lossless = AudioFormats.IsLossless(song.Path) };
}
