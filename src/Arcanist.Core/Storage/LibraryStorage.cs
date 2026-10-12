using Arcanist.Core.Data.Entities;

namespace Arcanist.Core.Storage;

/// <summary>Where a library source keeps its files. Mirrors <see cref="LibrarySource"/> without the database.</summary>
public sealed record StorageLocation(LibrarySourceKind Kind, string Location, string? Settings = null)
{
    public static StorageLocation From(LibrarySource source) => new(source.Kind, source.Location, source.Settings);
}

/// <summary>A file found in a source. <see cref="RelativePath"/> is '/'-separated and exactly as stored on disk.</summary>
public sealed record StorageEntry(string RelativePath, long Size, DateTime ModifiedAt);

public enum StorageProblem
{
    /// <summary>The location does not exist or is not mounted.</summary>
    NotFound,

    /// <summary>The server process is not allowed to read it.</summary>
    AccessDenied,

    /// <summary>The location is malformed for its kind (e.g. a relative path).</summary>
    Invalid,
}

/// <summary>Result of checking a source before using it. <see cref="Problem"/> is null when it is usable.</summary>
public sealed record StorageProbe(StorageProblem? Problem)
{
    public bool IsAvailable => Problem is null;

    public static readonly StorageProbe Available = new((StorageProblem?)null);
}

/// <summary>
/// Access to the files of library sources of one <see cref="LibrarySourceKind"/>. The scanner, streaming, artwork
/// and organization go exclusively through this interface, so remote storage (Ultimate Cloud, S3) only needs a new
/// implementation.
/// <para>
/// Writes never overwrite: a move to a path that exists fails with <see cref="StorageConflictException"/>. Files
/// are never deleted either, only moved to the location's trash (<see cref="TrashFolder"/>), which is purged later.
/// </para>
/// </summary>
public interface ILibraryStorage
{
    LibrarySourceKind Kind { get; }

    /// <summary>Normalizes a user-entered location (e.g. to a full path) so equal locations compare equal.</summary>
    string NormalizeLocation(string location);

    Task<StorageProbe> ProbeAsync(StorageLocation location, CancellationToken cancellationToken);

    /// <summary>Lists every file below the location, skipping system and hidden folders.</summary>
    IAsyncEnumerable<StorageEntry> EnumerateAsync(StorageLocation location, CancellationToken cancellationToken);

    /// <summary>Opens a file for reading. The stream is seekable, so it can serve HTTP range requests.</summary>
    Task<Stream> OpenReadAsync(StorageLocation location, string relativePath, CancellationToken cancellationToken);

    /// <summary>
    /// The file's path on this machine when the storage is a local file system, so tools such as ffmpeg can open
    /// (and seek in) it directly; null for remote storage, which is read through <see cref="OpenReadAsync"/>.
    /// </summary>
    string? TryGetLocalPath(StorageLocation location, string relativePath);

    /// <summary>True when <paramref name="inner"/> is <paramref name="outer"/> or lies inside it.</summary>
    bool Contains(string outer, string inner);

    /// <summary>How relative paths compare in this storage (case-insensitive on Windows and macOS file systems).</summary>
    StringComparer PathComparer { get; }

    /// <summary>Hidden folder at the root of a location where removed files wait to be purged.</summary>
    public const string TrashFolder = ".arcanist-trash";

    /// <summary>Whether the server can create, move and remove files in the location.</summary>
    Task<bool> CanWriteAsync(StorageLocation location, CancellationToken cancellationToken);

    Task<bool> ExistsAsync(StorageLocation location, string relativePath, CancellationToken cancellationToken);

    /// <summary>Moves a file within the location, creating the folders it needs.</summary>
    /// <exception cref="StorageConflictException">Something already exists at <paramref name="to"/>.</exception>
    Task MoveAsync(StorageLocation location, string from, string to, CancellationToken cancellationToken);

    /// <summary>
    /// Moves a file of this machine (a converted file, or one from a drop folder) into the location. It appears at
    /// <paramref name="relativePath"/> complete, never half-copied.
    /// </summary>
    /// <exception cref="StorageConflictException">Something already exists at <paramref name="relativePath"/>.</exception>
    Task ImportAsync(StorageLocation location, string relativePath, string localPath, CancellationToken cancellationToken);

    /// <summary>Moves a file to the location's trash. Returns its path there.</summary>
    Task<string> TrashAsync(StorageLocation location, string relativePath, CancellationToken cancellationToken);

    /// <summary>Removes what went to the trash before <paramref name="before"/>.</summary>
    Task PurgeTrashAsync(StorageLocation location, DateTime before, CancellationToken cancellationToken);

    /// <summary>
    /// Removes the folder, then its parents, while they hold nothing but clutter left by operating systems
    /// (Thumbs.db, .DS_Store…). Never removes the location itself.
    /// </summary>
    Task RemoveEmptyFoldersAsync(StorageLocation location, string relativeFolder, CancellationToken cancellationToken);

    /// <summary>
    /// Calls <paramref name="changed"/> when something below the location changes, until disposed. Null when the
    /// storage cannot watch; callers also poll, since watching is unreliable on network shares.
    /// </summary>
    IDisposable? Watch(StorageLocation location, Action changed);
}

/// <summary>A write would overwrite something that already exists.</summary>
public sealed class StorageConflictException(string relativePath) : IOException($"'{relativePath}' already exists.")
{
    public string RelativePath { get; } = relativePath;
}

public interface ILibraryStorageResolver
{
    ILibraryStorage For(LibrarySourceKind kind);
}

internal sealed class LibraryStorageResolver(IEnumerable<ILibraryStorage> storages) : ILibraryStorageResolver
{
    private readonly Dictionary<LibrarySourceKind, ILibraryStorage> _byKind = storages.ToDictionary(s => s.Kind);

    public ILibraryStorage For(LibrarySourceKind kind) =>
        _byKind.TryGetValue(kind, out var storage)
            ? storage
            : throw new NotSupportedException($"No storage is registered for {kind} sources.");
}

public static class StorageServiceCollectionExtensions
{
    public static IServiceCollection AddLibraryStorage(this IServiceCollection services)
    {
        services.AddSingleton<ILibraryStorage, LocalStorage>();
        services.AddSingleton<ILibraryStorageResolver, LibraryStorageResolver>();
        return services;
    }
}
