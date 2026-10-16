using Arcanist.Core.Storage;

namespace Arcanist.Core.Backups.Targets;

/// <summary>A file in a backup destination; paths are relative to the destination and use '/'.</summary>
internal sealed record BackupObject(string Path, long Size);

/// <summary>
/// Where backups are written: a folder, an S3 bucket or another Arcanist server. Writes never overwrite (a file
/// already there is a <see cref="StorageConflictException"/>) and land whole or not at all; deleting is only for
/// retention and leftovers of interrupted runs.
/// </summary>
internal interface IBackupTarget
{
    /// <summary>Null when the destination can be used; otherwise an <c>ErrorCodes</c> value.</summary>
    Task<string?> ProbeAsync(CancellationToken cancellationToken);

    Task<BackupObject?> StatAsync(string path, CancellationToken cancellationToken);

    /// <summary>Writes <paramref name="length"/> bytes of <paramref name="content"/> to a new file.</summary>
    Task WriteAsync(string path, Stream content, long length, CancellationToken cancellationToken);

    /// <summary>Moves a file within the destination; the target must not exist.</summary>
    Task MoveAsync(string from, string to, CancellationToken cancellationToken);

    Task<Stream> OpenReadAsync(string path, CancellationToken cancellationToken);

    /// <summary>Every file under a folder (recursively); none when it does not exist.</summary>
    IAsyncEnumerable<BackupObject> ListAsync(string folder, CancellationToken cancellationToken);

    Task DeleteAsync(string path, CancellationToken cancellationToken);

    Task DeleteFolderAsync(string folder, CancellationToken cancellationToken);
}

internal static class BackupPaths
{
    public static string Join(params string[] parts) =>
        string.Join('/', parts.Where(p => !string.IsNullOrEmpty(p)).Select(p => p.Trim('/')));

    /// <summary>
    /// Whether a relative path stays inside its folder (no "..", no absolute paths, no empty segments). Other
    /// characters are the library's own file names, kept as they are; folders also check the resolved full path.
    /// </summary>
    public static bool IsSafe(string path) =>
        path.Length > 0 && !path.StartsWith('/') && path.Split('/').All(segment => segment.Length > 0 && segment != "." && segment != "..");

    public static string Folder(string path)
    {
        var slash = path.LastIndexOf('/');
        return slash < 0 ? "" : path[..slash];
    }
}
