namespace Arcanist.Extensibility;

/// <summary>
/// The server's libraries and their songs, for extensions: reading the songs (and their files), and bringing new ones
/// in the way the server does (placed with the library's template, never overwriting, the same audio once). Get it
/// from the services (<c>IMusicLibraries</c>). It does not check permissions: the extension decides who may do what.
/// </summary>
public interface IMusicLibraries
{
    /// <summary>Every library, personal and managed ones too, by creation.</summary>
    Task<IReadOnlyList<MusicLibrary>> ListAsync(CancellationToken cancellationToken);

    Task<MusicLibrary?> GetAsync(Guid libraryId, CancellationToken cancellationToken);

    /// <summary>
    /// The songs a library has in its folders on this server: present files, whole ones (not tracks of a cue sheet),
    /// one per audio (the best copy).
    /// </summary>
    Task<IReadOnlyList<LibrarySong>> SongsAsync(Guid libraryId, CancellationToken cancellationToken);

    /// <summary>One file of a song (any kind of folder), when it is present and whole.</summary>
    Task<LibrarySong?> FileAsync(Guid fileId, CancellationToken cancellationToken);

    /// <summary>A song's best present, whole copy in a folder on this server (none on S3, other servers or Ultimate Cloud).</summary>
    Task<LibrarySong?> BestLocalCopyAsync(Guid trackId, CancellationToken cancellationToken);

    /// <summary>
    /// The file of a song as a file on this server's disk: its own when it is in a local folder, otherwise a temporary
    /// copy (from S3, another server or Ultimate Cloud) removed when disposed.
    /// </summary>
    /// <exception cref="SongUnavailableException">It cannot be read (<c>not_found</c>, <c>download_not_allowed</c>, <c>upload_failed</c>).</exception>
    Task<LocalSongFile> OpenAsync(LibrarySong song, CancellationToken cancellationToken);

    /// <summary>The song's cover (the track's, or its album's) when it is at most <paramref name="maxBytes"/>.</summary>
    Task<SongArtwork?> ArtworkAsync(LibrarySong song, long maxBytes, CancellationToken cancellationToken);

    /// <summary>
    /// Brings an audio file into a library folder (a local one; its first when <paramref name="folderId"/> is null),
    /// placed with the library's template from <paramref name="placement"/>, never overwriting (another name instead),
    /// under the library's lock. The same audio the library has already is not copied (<see cref="SongImportStatus.Duplicate"/>).
    /// The file is moved: it is no longer where it was. A scan takes it into the catalog (<see cref="QueueScanAsync"/>).
    /// </summary>
    Task<SongImport> ImportAsync(Guid libraryId, Guid? folderId, string file, SongPlacement placement, CancellationToken cancellationToken);

    /// <summary>Queues a scan of the library (what changed since the last one).</summary>
    Task QueueScanAsync(Guid libraryId, CancellationToken cancellationToken);
}

public enum LibraryFolderKind
{
    Local,
    S3,

    /// <summary>A library another Arcanist shares through the P2P network.</summary>
    Peer,
    UltimateCloud,
}

public sealed record MusicLibrary(Guid Id, string Name, bool Personal, string? ManagedBy, IReadOnlyList<MusicLibraryFolder> Folders);

public sealed record MusicLibraryFolder(Guid Id, LibraryFolderKind Kind);

/// <summary>A file of a song, as the catalog describes it.</summary>
/// <param name="ContentHash">The hash of its audio (tags left out): the same audio has the same one.</param>
/// <param name="Path">Relative to its folder.</param>
/// <param name="Artist">Its first primary artist.</param>
public sealed record LibrarySong(
    Guid FileId,
    Guid TrackId,
    Guid LibraryId,
    Guid FolderId,
    LibraryFolderKind FolderKind,
    string Path,
    string ContentHash,
    string Title,
    string? Artist,
    string? Album,
    string? Genre,
    int? Year,
    int? TrackNumber,
    int? DiscNumber,
    string? Format,
    int? BitRate,
    int? SampleRate,
    int? BitDepth,
    int? Channels,
    int DurationMs,
    Guid? RecordingMbid,
    Guid? AcoustId,
    bool Lossless);

/// <summary>A song's file on this server's disk; a temporary copy is removed when disposed.</summary>
public sealed class LocalSongFile(string path, bool temporary) : IAsyncDisposable
{
    public string Path { get; } = path;

    public ValueTask DisposeAsync()
    {
        if (temporary)
        {
            try
            {
                File.Delete(Path);
            }
            catch (IOException)
            {
                // Left in the cache folder, which is emptied on its own.
            }
        }
        return ValueTask.CompletedTask;
    }
}

public sealed record SongArtwork(byte[] Bytes, string MimeType);

/// <summary>A song's file cannot be read, with the reason as an error code.</summary>
public sealed class SongUnavailableException(string code) : Exception(code)
{
    public string Code { get; } = code;
}

/// <summary>What places a song in a library's template.</summary>
public sealed record SongPlacement(string? Title, string? Artist, string? Album, int? Year, int? DiscNumber, int? TrackNumber, string? Genre);

public enum SongImportStatus
{
    Imported,

    /// <summary>The library has the same audio already: nothing was copied.</summary>
    Duplicate,

    /// <summary>It is not audio the server can read.</summary>
    Unreadable,
}

/// <param name="ContentHash">The hash of its audio.</param>
/// <param name="Path">Where it went in the folder (relative), when imported.</param>
public sealed record SongImport(SongImportStatus Status, string? ContentHash, string? Path);
