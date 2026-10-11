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
/// Access to the files of library sources of one <see cref="LibrarySourceKind"/>. The scanner, streaming and
/// artwork go exclusively through this interface, so remote storage (Ultimate Cloud, S3) only needs a new
/// implementation. Read-only for now; writes arrive with automatic organization.
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

    /// <summary>True when <paramref name="inner"/> is <paramref name="outer"/> or lies inside it.</summary>
    bool Contains(string outer, string inner);
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
