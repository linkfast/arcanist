namespace Arcanist.Extensibility;

/// <summary>A file in a backup destination; paths are relative to it and use '/'.</summary>
public sealed record BackupStoreFile(string Path, long Size);

/// <summary>The file is already there: backups never overwrite.</summary>
public sealed class BackupFileExistsException(string path) : IOException($"'{path}' exists already.")
{
    public string Path { get; } = path;
}

/// <summary>
/// Where an extension keeps backups (Ultimate Cloud, for Ultimate Cloud Plus). The server writes its backups through it
/// as through its own destinations: writes never overwrite (<see cref="BackupFileExistsException"/>) and land whole or
/// not at all; deleting is only for retention and leftovers of interrupted runs.
/// </summary>
public interface IBackupStore
{
    /// <summary>Null when it can be used now; otherwise why not (an error code).</summary>
    Task<string?> ProbeAsync(CancellationToken cancellationToken);

    Task<BackupStoreFile?> StatAsync(string path, CancellationToken cancellationToken);

    /// <summary>Writes <paramref name="length"/> bytes of <paramref name="content"/> to a new file.</summary>
    Task WriteAsync(string path, Stream content, long length, CancellationToken cancellationToken);

    /// <summary>Moves a file within the store; the target must not exist.</summary>
    Task MoveAsync(string from, string to, CancellationToken cancellationToken);

    Task<Stream> OpenReadAsync(string path, CancellationToken cancellationToken);

    /// <summary>Every file under a folder (recursively); none when it does not exist.</summary>
    IAsyncEnumerable<BackupStoreFile> ListAsync(string folder, CancellationToken cancellationToken);

    /// <summary>The names of the folders directly in a folder ("" for the store's root).</summary>
    Task<IReadOnlyList<string>> FoldersAsync(string folder, CancellationToken cancellationToken);

    Task DeleteAsync(string path, CancellationToken cancellationToken);

    Task DeleteFolderAsync(string folder, CancellationToken cancellationToken);
}

/// <summary>What a new destination of a provider is: where (shown to administrators) and its settings (kept, as JSON).</summary>
public sealed record BackupDestinationSetup(string Location, string? Settings);

/// <summary>A new destination cannot be made (an error code), such as an account without the Cloud plan.</summary>
public sealed class BackupDestinationException(string code) : Exception(code)
{
    public string Code { get; } = code;
}

/// <summary>
/// A kind of backup destination an extension provides (registered as a service; several may be). Administrators add
/// destinations of it in the Backups page; the server keeps them, and opens them through it.
/// </summary>
public interface IBackupDestinationProvider
{
    /// <summary>Its id (a few lowercase letters, digits and dashes), kept with each destination.</summary>
    string Id { get; }

    /// <summary>Its name, in the language asked for (<c>es</c>, <c>en</c>…).</summary>
    string Name(string language);

    /// <summary>A new destination from what the administrator gave (<paramref name="settings"/>, JSON, may be null).</summary>
    /// <exception cref="BackupDestinationException">It cannot be made.</exception>
    Task<BackupDestinationSetup> CreateAsync(string? settings, CancellationToken cancellationToken);

    /// <summary>Opens a destination it made.</summary>
    IBackupStore Open(string location, string? settings);
}
