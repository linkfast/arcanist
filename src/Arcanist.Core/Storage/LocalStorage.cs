using System.Globalization;
using System.IO.Enumeration;
using System.Runtime.CompilerServices;
using Arcanist.Core.Data.Entities;

namespace Arcanist.Core.Storage;

/// <summary>Folders reachable through the file system, including mounted network shares (SMB, NFS).</summary>
internal sealed class LocalStorage(TimeProvider? time = null) : ILibraryStorage
{
    private readonly TimeProvider _time = time ?? TimeProvider.System;

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

    /// <summary>What operating systems leave in folders by themselves; a folder holding only this counts as empty.</summary>
    private static readonly HashSet<string> Clutter = new(StringComparer.OrdinalIgnoreCase) { "Thumbs.db", "desktop.ini", ".DS_Store", "@eaDir" };

    private static readonly StringComparison PathComparison =
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    public LibrarySourceKind Kind => LibrarySourceKind.Local;

    public StringComparer PathComparer { get; } =
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

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

    public Task<bool> CanWriteAsync(StorageLocation location, CancellationToken cancellationToken)
    {
        try
        {
            // Creating a file is the only reliable test: permissions, read-only mounts and ACLs all show up here.
            using (File.Create(Path.Combine(location.Location, $".arcanist-write-test-{Guid.NewGuid():N}"), 1, FileOptions.DeleteOnClose))
            {
            }

            return Task.FromResult(true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return Task.FromResult(false);
        }
    }

    public Task<bool> ExistsAsync(StorageLocation location, string relativePath, CancellationToken cancellationToken)
    {
        var path = Resolve(location, relativePath);
        return Task.FromResult(File.Exists(path) || Directory.Exists(path));
    }

    public Task MoveAsync(StorageLocation location, string from, string to, CancellationToken cancellationToken)
    {
        var source = Resolve(location, from);
        var target = Resolve(location, to);

        // Changing only the case of a name moves a file onto "itself" on case-insensitive file systems.
        var caseOnly = PathComparer.Equals(source, target);
        if (!caseOnly && (File.Exists(target) || Directory.Exists(target)))
            throw new StorageConflictException(to);

        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        try
        {
            File.Move(source, target, overwrite: false);
        }
        catch (IOException) when (!caseOnly && File.Exists(target) && File.Exists(source))
        {
            throw new StorageConflictException(to);
        }

        return Task.CompletedTask;
    }

    public Task ImportAsync(StorageLocation location, string relativePath, string localPath, CancellationToken cancellationToken)
    {
        var target = Resolve(location, relativePath);
        if (File.Exists(target) || Directory.Exists(target))
            throw new StorageConflictException(relativePath);

        var folder = Path.GetDirectoryName(target)!;
        Directory.CreateDirectory(folder);

        // Across volumes File.Move copies, so the file first lands under a hidden name that scans skip, then is
        // renamed into place, which is atomic.
        var partial = Path.Combine(folder, $".arcanist-partial-{Guid.NewGuid():N}");
        File.Move(localPath, partial);
        try
        {
            File.Move(partial, target, overwrite: false);
        }
        catch (IOException)
        {
            File.Move(partial, localPath);
            if (File.Exists(target))
                throw new StorageConflictException(relativePath);
            throw;
        }

        return Task.CompletedTask;
    }

    public async Task<string> TrashAsync(StorageLocation location, string relativePath, CancellationToken cancellationToken)
    {
        // One folder per day, so purging removes whole folders.
        var day = $"{ILibraryStorage.TrashFolder}/{_time.GetUtcNow():yyyy-MM-dd}";
        var name = WithoutExtension(relativePath);
        var extension = relativePath[name.Length..];
        var target = $"{day}/{relativePath}";
        for (var copy = 2; await ExistsAsync(location, target, cancellationToken); copy++)
            target = $"{day}/{name} ({copy}){extension}";

        await MoveAsync(location, relativePath, target, cancellationToken);
        return target;
    }

    public Task PurgeTrashAsync(StorageLocation location, DateTime before, CancellationToken cancellationToken)
    {
        var trash = Path.Combine(location.Location, ILibraryStorage.TrashFolder);
        if (!Directory.Exists(trash))
            return Task.CompletedTask;

        foreach (var day in Directory.EnumerateDirectories(trash))
        {
            // A day's folder goes once all of that day is before the limit.
            if (DateTime.TryParseExact(Path.GetFileName(day), "yyyy-MM-dd", CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var date)
                && date.AddDays(1) <= before)
            {
                Directory.Delete(day, recursive: true);
            }
        }

        if (!Directory.EnumerateFileSystemEntries(trash).Any())
            Directory.Delete(trash);

        return Task.CompletedTask;
    }

    public Task RemoveEmptyFoldersAsync(StorageLocation location, string relativeFolder, CancellationToken cancellationToken)
    {
        var root = NormalizeLocation(location.Location);
        var folder = string.IsNullOrEmpty(relativeFolder) ? null : ResolveInside(root, relativeFolder);

        while (folder is not null && !PathComparer.Equals(folder, root) && Contains(root, folder))
        {
            if (Directory.Exists(folder))
            {
                var entries = Directory.EnumerateFileSystemEntries(folder).ToList();
                if (!entries.All(IsClutter))
                    break;

                foreach (var entry in entries)
                {
                    if (Directory.Exists(entry))
                        Directory.Delete(entry, recursive: true);
                    else
                        File.Delete(entry);
                }

                Directory.Delete(folder);
            }

            folder = Path.GetDirectoryName(folder);
        }

        return Task.CompletedTask;

        // AppleDouble files ("._song.flac") hold a Mac's metadata about the file of the same name.
        static bool IsClutter(string path) =>
            Clutter.Contains(Path.GetFileName(path)) || Path.GetFileName(path).StartsWith("._", StringComparison.Ordinal);
    }

    public IDisposable? Watch(StorageLocation location, Action changed)
    {
        try
        {
            var root = NormalizeLocation(location.Location);
            var watcher = new FileSystemWatcher(root)
            {
                IncludeSubdirectories = true,
                InternalBufferSize = 64 * 1024,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.Size | NotifyFilters.LastWrite,
            };

            // Hidden paths are Arcanist's own (partial copies, the trash) or skipped by scans anyway.
            void OnChange(string path)
            {
                if (!Path.GetRelativePath(root, path).Split(Path.DirectorySeparatorChar).Any(segment => IsHidden(segment)))
                    changed();
            }

            watcher.Created += (_, e) => OnChange(e.FullPath);
            watcher.Changed += (_, e) => OnChange(e.FullPath);
            watcher.Deleted += (_, e) => OnChange(e.FullPath);
            watcher.Renamed += (_, e) => OnChange(e.FullPath);
            // The buffer overflowed: something changed, and since callers list everything, knowing what is not needed.
            watcher.Error += (_, _) => changed();
            watcher.EnableRaisingEvents = true;
            return watcher;
        }
        catch (Exception e) when (e is IOException or ArgumentException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            // Too many watches (Linux inotify limits) or a file system that cannot be watched: polling covers it.
            return null;
        }
    }

    /// <summary>Maps a stored relative path back to a full path, refusing anything that escapes the root.</summary>
    public string? TryGetLocalPath(StorageLocation location, string relativePath) => ResolveInside(location.Location, relativePath);

    internal string? ResolveInside(string root, string relativePath)
    {
        if (string.IsNullOrEmpty(relativePath) || Path.IsPathRooted(relativePath))
            return null;

        var fullPath = Path.GetFullPath(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        return Contains(root, fullPath) && !fullPath.Equals(NormalizeLocation(root), PathComparison) ? fullPath : null;
    }

    private string Resolve(StorageLocation location, string relativePath) =>
        ResolveInside(location.Location, relativePath)
        ?? throw new FileNotFoundException("The path is outside the location.", relativePath);

    // Kept byte-for-byte as on disk (no Unicode normalization): on Linux a name stored in NFD, as copied from a
    // Mac, only opens with exactly that spelling. A source is always read through the same mount, so paths are
    // stable between scans.
    private static string ToRelativePath(string root, string fullPath) =>
        Path.GetRelativePath(root, fullPath).Replace(Path.DirectorySeparatorChar, '/');

    private static bool IsHidden(ReadOnlySpan<char> name) => name.StartsWith(".");

    private static string WithoutExtension(string path)
    {
        var dot = path.LastIndexOf('.');
        return dot > path.LastIndexOf('/') ? path[..dot] : path;
    }
}
