namespace Arcanist.Core.Caching;

/// <summary>
/// Files kept in a folder up to a size, the least recently used going first. Entries are written to a hidden
/// partial file and moved into place once complete, so a reader never sees half of one. A file's modification time
/// is when it was last used (touched on every read).
/// </summary>
public sealed class DiskCache(string directory, Func<long> maxBytes, TimeProvider time, ILogger logger)
{
    private const string PartialPrefix = ".partial-";

    private readonly Lock _lock = new();
    private long? _bytes;
    private int _files;

    public string Directory => directory;

    /// <summary>The file of a key, if it is cached; using it makes it the most recent.</summary>
    public string? TryGet(string key)
    {
        var path = PathFor(key);
        if (!File.Exists(path))
            return null;
        try
        {
            File.SetLastWriteTimeUtc(path, time.GetUtcNow().UtcDateTime);
        }
        catch (IOException)
        {
            // Being read elsewhere at the same moment: its age matters little.
        }
        catch (UnauthorizedAccessException)
        {
        }

        return path;
    }

    public bool Contains(string key) => File.Exists(PathFor(key));

    /// <summary>The size of a key's entry; 0 when there is none.</summary>
    public long SizeOf(string key)
    {
        var file = new FileInfo(PathFor(key));
        return file.Exists ? file.Length : 0;
    }

    /// <summary>A file to write an entry to before <see cref="Commit"/>: hidden, so nothing reads it half written.</summary>
    public string NewPartialPath()
    {
        System.IO.Directory.CreateDirectory(directory);
        return Path.Combine(directory, PartialPrefix + Guid.NewGuid().ToString("N"));
    }

    /// <summary>Moves a complete partial file into place as the key's entry, then keeps the cache within its size.</summary>
    public void Commit(string partial, string key)
    {
        var path = PathFor(key);
        try
        {
            var size = new FileInfo(partial).Length;
            System.IO.Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.Move(partial, path, overwrite: false);
            lock (_lock)
            {
                if (_bytes is not null)
                {
                    _bytes += size;
                    _files++;
                }
            }
        }
        catch (IOException)
        {
            // Another request cached the same entry first.
            Discard(partial);
            return;
        }

        Trim();
    }

    public static void Discard(string partial)
    {
        try
        {
            File.Delete(partial);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    /// <summary>What the cache holds: bytes and files.</summary>
    public (long Bytes, int Files) Usage()
    {
        lock (_lock)
        {
            if (_bytes is null)
                Count();
            return (_bytes!.Value, _files);
        }
    }

    /// <summary>Removes the least recently used entries until the cache fits in its size (to 90% of it, to leave room).</summary>
    public void Trim()
    {
        var limit = maxBytes();
        lock (_lock)
        {
            if (_bytes is null)
                Count();
            if (_bytes <= limit)
                return;
        }

        var entries = Entries().OrderBy(f => f.LastWriteTimeUtc).ToList();
        var bytes = entries.Sum(f => f.Length);
        var target = (long)(limit * 0.9);
        foreach (var entry in entries)
        {
            if (bytes <= target)
                break;
            if (Delete(entry))
                bytes -= entry.Length;
        }

        lock (_lock)
            Count();
    }

    /// <summary>Removes entries not used for <paramref name="age"/>, and partial files left by a stopped server.</summary>
    public void Purge(TimeSpan age)
    {
        var now = time.GetUtcNow().UtcDateTime;
        if (!System.IO.Directory.Exists(directory))
            return;
        foreach (var partial in System.IO.Directory.EnumerateFiles(directory, PartialPrefix + "*"))
        {
            if (now - File.GetLastWriteTimeUtc(partial) > TimeSpan.FromHours(6))
                Discard(partial);
        }

        foreach (var entry in Entries().Where(f => now - f.LastWriteTimeUtc > age))
            Delete(entry);
        lock (_lock)
            Count();
    }

    /// <summary>Removes the entries whose keys <paramref name="keep"/> does not want.</summary>
    public void RemoveWhere(Func<string, bool> remove)
    {
        foreach (var entry in Entries().Where(f => remove(f.Name)))
            Delete(entry);
        lock (_lock)
            Count();
    }

    public void Clear()
    {
        foreach (var entry in Entries())
            Delete(entry);
        lock (_lock)
            Count();
    }

    /// <summary>Spread over folders by the key's first two characters, as Git does, so no folder grows too big.</summary>
    private string PathFor(string key)
    {
        if (key.Length < 3 || key.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.')) || key.StartsWith('.'))
            throw new ArgumentException("Cache keys are letters, digits, dashes, dots and underscores.", nameof(key));
        return Path.Combine(directory, key[..2], key);
    }

    private IEnumerable<FileInfo> Entries() =>
        System.IO.Directory.Exists(directory)
            ? new DirectoryInfo(directory).EnumerateDirectories().SelectMany(d => d.EnumerateFiles())
            : [];

    private bool Delete(FileInfo entry)
    {
        try
        {
            entry.Delete();
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // In use (being served): it goes next time.
            logger.LogDebug("Could not remove {File} from the cache: {Error}", entry.FullName, e.Message);
            return false;
        }
    }

    private void Count()
    {
        var entries = Entries().ToList();
        _bytes = entries.Sum(f => f.Length);
        _files = entries.Count;
    }
}
