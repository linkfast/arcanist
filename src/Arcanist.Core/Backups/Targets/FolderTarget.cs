using System.Runtime.CompilerServices;
using Arcanist.Core.Application;
using Arcanist.Core.Storage;

namespace Arcanist.Core.Backups.Targets;

/// <summary>A folder of this server (a disk, a USB drive, a mounted share).</summary>
internal sealed class FolderTarget(string root) : IBackupTarget
{
    private const string PartialPrefix = ".arcanist-partial-";

    private static readonly StringComparison PathComparison =
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private readonly string _root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));

    public Task<string?> ProbeAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (!Directory.Exists(_root))
                return Task.FromResult<string?>(ErrorCodes.BackupDestinationUnavailable);
            using (File.Create(Path.Combine(_root, $".arcanist-write-test-{Guid.NewGuid():N}"), 1, FileOptions.DeleteOnClose))
            {
            }

            return Task.FromResult<string?>(null);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return Task.FromResult<string?>(ErrorCodes.BackupDestinationUnavailable);
        }
    }

    public Task<BackupObject?> StatAsync(string path, CancellationToken cancellationToken)
    {
        var full = Resolve(path);
        return Task.FromResult(File.Exists(full) ? new BackupObject(path, new FileInfo(full).Length) : null);
    }

    public async Task WriteAsync(string path, Stream content, long length, CancellationToken cancellationToken)
    {
        var target = Resolve(path);
        if (File.Exists(target) || Directory.Exists(target))
            throw new StorageConflictException(path);

        var folder = Path.GetDirectoryName(target)!;
        Directory.CreateDirectory(folder);
        var partial = Path.Combine(folder, $"{PartialPrefix}{Guid.NewGuid():N}");
        try
        {
            await using (var output = new FileStream(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true))
                await content.CopyToAsync(output, cancellationToken);
            File.Move(partial, target, overwrite: false);
        }
        catch (IOException) when (File.Exists(target) && File.Exists(partial))
        {
            throw new StorageConflictException(path);
        }
        finally
        {
            if (File.Exists(partial))
                File.Delete(partial);
        }
    }

    public Task MoveAsync(string from, string to, CancellationToken cancellationToken)
    {
        var source = Resolve(from);
        var target = Resolve(to);
        if (File.Exists(target) || Directory.Exists(target))
            throw new StorageConflictException(to);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Move(source, target, overwrite: false);
        return Task.CompletedTask;
    }

    public Task<Stream> OpenReadAsync(string path, CancellationToken cancellationToken) =>
        Task.FromResult<Stream>(new FileStream(Resolve(path), FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, useAsync: true));

    public async IAsyncEnumerable<BackupObject> ListAsync(string folder, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var full = folder.Length == 0 ? _root : Resolve(folder);
        if (!Directory.Exists(full))
            yield break;
        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = 0 };
        foreach (var file in new DirectoryInfo(full).EnumerateFiles("*", options))
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return new BackupObject(Path.GetRelativePath(_root, file.FullName).Replace(Path.DirectorySeparatorChar, '/'), file.Length);
        }

        await Task.CompletedTask;
    }

    public Task<IReadOnlyList<string>> FoldersAsync(string folder, CancellationToken cancellationToken)
    {
        var full = folder.Length == 0 ? _root : Resolve(folder);
        return Task.FromResult<IReadOnlyList<string>>(Directory.Exists(full)
            ? Directory.EnumerateDirectories(full).Select(Path.GetFileName).OfType<string>().Where(n => !n.StartsWith('.')).Order(StringComparer.Ordinal).ToList()
            : []);
    }

    public Task DeleteAsync(string path, CancellationToken cancellationToken)
    {
        var full = Resolve(path);
        if (File.Exists(full))
        {
            File.SetAttributes(full, FileAttributes.Normal);
            File.Delete(full);
        }

        return Task.CompletedTask;
    }

    public Task DeleteFolderAsync(string folder, CancellationToken cancellationToken)
    {
        var full = Resolve(folder);
        if (Directory.Exists(full))
            Directory.Delete(full, recursive: true);
        return Task.CompletedTask;
    }

    /// <summary>The full path of a relative one, refusing anything that would leave the folder.</summary>
    private string Resolve(string path)
    {
        if (!BackupPaths.IsSafe(path))
            throw new ArgumentException("The path is not inside the destination.", nameof(path));
        var full = Path.GetFullPath(Path.Combine(_root, path.Replace('/', Path.DirectorySeparatorChar)));
        if (!full.StartsWith(_root + Path.DirectorySeparatorChar, PathComparison))
            throw new ArgumentException("The path is not inside the destination.", nameof(path));
        return full;
    }
}
