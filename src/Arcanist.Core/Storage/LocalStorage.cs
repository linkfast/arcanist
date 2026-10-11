using System.IO.Enumeration;
using System.Runtime.CompilerServices;
using Arcanist.Core.Data.Entities;

namespace Arcanist.Core.Storage;

/// <summary>Folders reachable through the file system, including mounted network shares (SMB, NFS).</summary>
internal sealed class LocalStorage : ILibraryStorage
{
    /// <summary>Folders created by operating systems and NAS software that never contain the user's music.</summary>
    private static readonly HashSet<string> IgnoredDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        "@eaDir",                    // Synology thumbnails and metadata
        "#recycle",                  // Synology recycle bin
        "#snapshot",
        "$RECYCLE.BIN",
        "System Volume Information",
        "lost+found",
    };

    private static readonly StringComparison PathComparison =
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    public LibrarySourceKind Kind => LibrarySourceKind.Local;

    public string NormalizeLocation(string location) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(location.Trim()));

    public Task<StorageProbe> ProbeAsync(StorageLocation location, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(location.Location) || !Path.IsPathFullyQualified(location.Location))
            return Task.FromResult(new StorageProbe(StorageProblem.Invalid));

        try
        {
            if (!Directory.Exists(location.Location))
                return Task.FromResult(new StorageProbe(StorageProblem.NotFound));

            // Listing proves the folder is readable, not just visible.
            using var entries = Directory.EnumerateFileSystemEntries(location.Location).GetEnumerator();
            entries.MoveNext();
            return Task.FromResult(StorageProbe.Available);
        }
        catch (UnauthorizedAccessException)
        {
            return Task.FromResult(new StorageProbe(StorageProblem.AccessDenied));
        }
        catch (IOException)
        {
            // Typically a disconnected network share.
            return Task.FromResult(new StorageProbe(StorageProblem.NotFound));
        }
    }

    public async IAsyncEnumerable<StorageEntry> EnumerateAsync(
        StorageLocation location, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var root = location.Location;
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.System,
            ReturnSpecialDirectories = false,
        };

        var files = new FileSystemEnumerable<StorageEntry>(
            root,
            (ref FileSystemEntry entry) => new StorageEntry(
                ToRelativePath(root, entry.ToFullPath()),
                entry.Length,
                entry.LastWriteTimeUtc.UtcDateTime),
            options)
        {
            ShouldIncludePredicate = (ref FileSystemEntry entry) => !entry.IsDirectory && !IsHidden(entry.FileName),
            ShouldRecursePredicate = (ref FileSystemEntry entry) => !IsHidden(entry.FileName) && !IgnoredDirectories.Contains(entry.FileName.ToString()),
        };

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return file;
        }

        await Task.CompletedTask;
    }

    public Task<Stream> OpenReadAsync(StorageLocation location, string relativePath, CancellationToken cancellationToken)
    {
        var fullPath = ResolveInside(location.Location, relativePath)
                       ?? throw new FileNotFoundException("The path is outside the library source.", relativePath);

        Stream stream = new FileStream(fullPath, new FileStreamOptions
        {
            Mode = FileMode.Open,
            Access = FileAccess.Read,
            Share = FileShare.ReadWrite | FileShare.Delete,
            Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
        });

        return Task.FromResult(stream);
    }

    public bool Contains(string outer, string inner)
    {
        var parent = NormalizeLocation(outer);
        var child = NormalizeLocation(inner);

        return child.Equals(parent, PathComparison)
               || child.StartsWith(Path.EndsInDirectorySeparator(parent) ? parent : parent + Path.DirectorySeparatorChar, PathComparison);
    }

    /// <summary>Maps a stored relative path back to a full path, refusing anything that escapes the root.</summary>
    internal string? ResolveInside(string root, string relativePath)
    {
        if (string.IsNullOrEmpty(relativePath) || Path.IsPathRooted(relativePath))
            return null;

        var fullPath = Path.GetFullPath(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        return Contains(root, fullPath) && !fullPath.Equals(NormalizeLocation(root), PathComparison) ? fullPath : null;
    }

    // Kept byte-for-byte as on disk (no Unicode normalization): on Linux a name stored in NFD, as copied from a
    // Mac, only opens with exactly that spelling. A source is always read through the same mount, so paths are
    // stable between scans.
    private static string ToRelativePath(string root, string fullPath) =>
        Path.GetRelativePath(root, fullPath).Replace(Path.DirectorySeparatorChar, '/');

    private static bool IsHidden(ReadOnlySpan<char> name) => name.StartsWith(".");
}
