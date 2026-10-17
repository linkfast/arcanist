using System.Data.Common;
using Arcanist.Core.Data;
using Arcanist.Core.Data.Entities;
using Arcanist.Core.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Arcanist.Core.Application.Server;

// Types in this file are part of the public API contracts (GraphQL schema and OpenAPI document).

/// <summary>What a part of the server's data is.</summary>
public enum StoragePartKind
{
    /// <summary>The database (SQLite's file, or PostgreSQL's database wherever it runs).</summary>
    Database,

    /// <summary>Covers read from the music and brought from online services.</summary>
    Artwork,

    /// <summary>Smaller copies of the covers, made as they are asked for (they can be deleted; they are made again).</summary>
    Thumbnails,

    /// <summary>Files on their way: imports, uploads, Cloud Drop, synchronization, backups and restores.</summary>
    Temporary,

    Logs,

    /// <summary>ffmpeg and ultimate-audio, when Arcanist installed them.</summary>
    Tools,

    /// <summary>Web players installed from the panel.</summary>
    Players,

    /// <summary>Extensions installed from the panel, and the data extensions keep.</summary>
    Extensions,

    /// <summary>Settings, keys and the rest.</summary>
    Other,
}

public sealed record StoragePart(StoragePartKind Kind, long Bytes);

public sealed record DatabaseTable(string Name, long Rows);

/// <param name="Version">The version of SQLite or of the PostgreSQL server.</param>
/// <param name="SizeBytes">Its size on disk (with SQLite's journal); null if it could not be read.</param>
/// <param name="Tables">The rows of each table, the biggest first.</param>
public sealed record DatabaseUsage(DatabaseProvider Provider, string? Version, long? SizeBytes, IReadOnlyList<DatabaseTable> Tables);

/// <param name="Files">The music files present (an album in one file with its cue sheet counts once).</param>
/// <param name="MissingFiles">Files gone from their folder, kept a few days in case they come back.</param>
/// <param name="Plays">Plays in the history of every user.</param>
public sealed record CatalogCounts(
    int Libraries,
    int Tracks,
    int Albums,
    int Artists,
    int Files,
    int MissingFiles,
    int Playlists,
    int Users,
    long Plays,
    int Ratings);

/// <summary>A library's music: how many files and how much they take where they are.</summary>
/// <param name="Kind">Where its music is (its first location): on this server's disks (or a share it mounts), in S3,
/// Ultimate Cloud or another Arcanist.</param>
public sealed record LibraryUsage(Guid Id, string Name, LibrarySourceKind Kind, int Files, long Bytes);

/// <summary>The disk the data directory is on.</summary>
public sealed record DiskUsage(long TotalBytes, long FreeBytes);

/// <summary>
/// The space Arcanist takes, part by part, and the music it serves. Measured at most every few minutes
/// (<see cref="MeasuredAt"/>) unless asked again.
/// </summary>
/// <param name="ArcanistBytes">Everything Arcanist keeps itself: the parts (with the database), not the music.</param>
/// <param name="MusicBytes">The music of the libraries on this server's disks (local folders).</param>
/// <param name="Libraries">Libraries but the personal ones of users (their own Ultimate Cloud), the biggest first.</param>
public sealed record StorageUsage(
    string DataDirectory,
    long ArcanistBytes,
    IReadOnlyList<StoragePart> Parts,
    DatabaseUsage Database,
    CatalogCounts Catalog,
    long MusicBytes,
    IReadOnlyList<LibraryUsage> Libraries,
    DiskUsage? Disk,
    DateTime MeasuredAt);

/// <summary>The last measurement, kept a few minutes: walking the caches of a big library takes a while.</summary>
public sealed class StorageUsageCache
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);

    internal SemaphoreSlim Lock { get; } = new(1, 1);

    internal StorageUsage? Last { get; set; }
}

/// <summary>How much space Arcanist and its music take, for administrators (the Database page and the Home page).</summary>
public sealed class StorageUsageService(
    AccessGuard guard,
    ArcanistDbContext db,
    ArcanistPaths paths,
    IOptionsMonitor<DatabaseOptions> options,
    StorageUsageCache cache,
    TimeProvider time,
    ILogger<StorageUsageService> logger)
{
    public async Task<StorageUsage> UsageAsync(bool refresh, CancellationToken cancellationToken)
    {
        guard.Admin();
        await cache.Lock.WaitAsync(cancellationToken);
        try
        {
            if (!refresh && cache.Last is { } last && time.GetUtcNow().UtcDateTime - last.MeasuredAt < StorageUsageCache.Lifetime)
                return last;
            return cache.Last = await MeasureAsync(cancellationToken);
        }
        finally
        {
            cache.Lock.Release();
        }
    }

    private async Task<StorageUsage> MeasureAsync(CancellationToken cancellationToken)
    {
        var database = await DatabaseAsync(cancellationToken);
        var sqliteFiles = database.Provider == DatabaseProvider.Sqlite ? SqliteFiles() : [];
        var parts = await Task.Run(() => DataDirectory(sqliteFiles), cancellationToken);
        parts[StoragePartKind.Database] = database.SizeBytes ?? 0;

        var libraries = await LibrariesAsync(cancellationToken);
        var partList = Enum.GetValues<StoragePartKind>().Select(kind => new StoragePart(kind, parts.GetValueOrDefault(kind))).ToList();
        return new StorageUsage(
            paths.Root,
            partList.Sum(p => p.Bytes),
            partList,
            database,
            await CountsAsync(cancellationToken),
            libraries.Where(l => l.Kind == LibrarySourceKind.Local).Sum(l => l.Bytes),
            libraries,
            Disk(),
            time.GetUtcNow().UtcDateTime);
    }

    // ── The data directory ───────────────────────────────────────────────────

    /// <summary>SQLite's database and its journal files.</summary>
    private List<string> SqliteFiles()
    {
        var file = Path.GetFullPath(DatabaseMigrator.SqlitePath(options.CurrentValue, paths));
        return [file, file + "-wal", file + "-shm", file + "-journal"];
    }

    /// <summary>Every file of the data directory, by part; the database's files are counted with the database.</summary>
    private Dictionary<StoragePartKind, long> DataDirectory(IReadOnlyCollection<string> databaseFiles)
    {
        var parts = new Dictionary<StoragePartKind, long>();
        var root = Path.GetFullPath(paths.Root);
        if (!Directory.Exists(root))
            return parts;

        var skip = databaseFiles.ToHashSet(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        var walk = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
        foreach (var file in new DirectoryInfo(root).EnumerateFiles("*", walk))
        {
            if (skip.Contains(file.FullName))
                continue;
            long length;
            try
            {
                length = file.Length;
            }
            catch (IOException)
            {
                // Gone meanwhile (a temporary file).
                continue;
            }

            var kind = Kind(Path.GetRelativePath(root, file.FullName).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            parts[kind] = parts.GetValueOrDefault(kind) + length;
        }

        return parts;
    }

    private static StoragePartKind Kind(string[] segments) => segments switch
    {
        ["cache", "artwork", ..] => StoragePartKind.Artwork,
        ["cache", "thumbnails", ..] => StoragePartKind.Thumbnails,
        ["cache", _, ..] => StoragePartKind.Temporary,
        ["restore-pending", ..] => StoragePartKind.Temporary,
        ["logs", ..] => StoragePartKind.Logs,
        ["tools", ..] => StoragePartKind.Tools,
        ["frontends", ..] => StoragePartKind.Players,
        ["extensions" or "extension-data", ..] => StoragePartKind.Extensions,
        _ => StoragePartKind.Other,
    };

    /// <summary>The disk with the data directory: the mount point that contains it.</summary>
    private DiskUsage? Disk()
    {
        try
        {
            var root = Path.GetFullPath(paths.Root);
            var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            var drive = DriveInfo.GetDrives()
                .Where(d => d.IsReady && root.StartsWith(d.RootDirectory.FullName, comparison))
                .MaxBy(d => d.RootDirectory.FullName.Length);
            return drive is null ? null : new DiskUsage(drive.TotalSize, drive.AvailableFreeSpace);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    // ── The database ─────────────────────────────────────────────────────────

    /// <summary>
    /// The database's version, size and rows per table. The version and size are the only provider-specific queries
    /// outside the search: they describe the database itself.
    /// </summary>
    private async Task<DatabaseUsage> DatabaseAsync(CancellationToken cancellationToken)
    {
        var provider = options.CurrentValue.Provider ?? DatabaseProvider.Sqlite;
        var connection = db.Database.GetDbConnection();
        await db.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            string? version = null;
            long? size = null;
            try
            {
                if (provider == DatabaseProvider.Sqlite)
                {
                    version = (await ScalarAsync(connection, "SELECT sqlite_version()", cancellationToken))?.ToString();
                    size = SqliteFiles().Where(File.Exists).Sum(f => new FileInfo(f).Length);
                }
                else
                {
                    version = (await ScalarAsync(connection, "SHOW server_version", cancellationToken))?.ToString();
                    size = Convert.ToInt64(await ScalarAsync(connection, "SELECT pg_database_size(current_database())", cancellationToken));
                }
            }
            catch (Exception e) when (e is DbException or IOException)
            {
                logger.LogInformation("Could not read the database's version or size: {Error}", e.Message);
            }

            // Standard SQL: the same for both providers.
            var tables = new List<DatabaseTable>();
            foreach (var table in db.Model.GetEntityTypes().Select(t => t.GetTableName()).OfType<string>().Distinct().Order(StringComparer.Ordinal))
                tables.Add(new DatabaseTable(table, Convert.ToInt64(await ScalarAsync(connection, $"SELECT COUNT(*) FROM \"{table}\"", cancellationToken))));

            return new DatabaseUsage(provider, version, size, tables.OrderByDescending(t => t.Rows).ThenBy(t => t.Name, StringComparer.Ordinal).ToList());
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }

    private static async Task<object?> ScalarAsync(DbConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return await command.ExecuteScalarAsync(cancellationToken);
    }

    // ── The catalog and the music ────────────────────────────────────────────

    private async Task<CatalogCounts> CountsAsync(CancellationToken cancellationToken) => new(
        await db.Libraries.CountAsync(cancellationToken),
        await db.Tracks.CountAsync(cancellationToken),
        await db.Albums.CountAsync(cancellationToken),
        await db.Artists.CountAsync(cancellationToken),
        await db.MediaFiles.CountAsync(f => f.MissingSince == null && f.CueTrack <= 1, cancellationToken),
        await db.MediaFiles.CountAsync(f => f.MissingSince != null && f.CueTrack <= 1, cancellationToken),
        await db.Playlists.CountAsync(cancellationToken),
        await db.Users.CountAsync(cancellationToken),
        await db.PlayEvents.LongCountAsync(cancellationToken),
        await db.TrackRatings.CountAsync(cancellationToken));

    /// <summary>
    /// Each library's files and their size (a file split by a cue sheet once: its first track carries it). The
    /// personal libraries of users are only their owners' to see.
    /// </summary>
    private async Task<List<LibraryUsage>> LibrariesAsync(CancellationToken cancellationToken)
    {
        var bySource = await db.MediaFiles.AsNoTracking()
            .Where(f => f.MissingSince == null && f.CueTrack <= 1)
            .GroupBy(f => f.LibrarySourceId)
            .Select(g => new { SourceId = g.Key, Files = g.Count(), Bytes = g.Sum(f => f.Size) })
            .ToListAsync(cancellationToken);
        var libraries = await db.Libraries.AsNoTracking()
            .Where(l => l.OwnerUserId == null)
            .Select(l => new { l.Id, l.Name, Sources = l.Sources.OrderBy(s => s.Kind).Select(s => new { s.Id, s.Kind }).ToList() })
            .ToListAsync(cancellationToken);

        return libraries
            .Select(l =>
            {
                var music = bySource.Where(s => l.Sources.Any(source => source.Id == s.SourceId)).ToList();
                return new LibraryUsage(l.Id, l.Name, l.Sources.FirstOrDefault()?.Kind ?? LibrarySourceKind.Local, music.Sum(s => s.Files), music.Sum(s => s.Bytes));
            })
            .OrderByDescending(l => l.Bytes)
            .ThenBy(l => l.Name, StringComparer.CurrentCulture)
            .ToList();
    }
}
