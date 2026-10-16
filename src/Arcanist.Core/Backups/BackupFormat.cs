using System.Globalization;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using Arcanist.Core.Backups.Targets;

namespace Arcanist.Core.Backups;

/// <summary>
/// The layout of a backup in its destination (format 1), described in <c>docs/backups.md</c>. Everything needed to
/// restore is there, so another server (or a fresh installation) can read it:
/// <code>
/// &lt;backup folder&gt;/
///   arcanist-backup.json                 what it is, its library folders and its snapshots
///   current/&lt;library&gt;/&lt;path&gt;             the mirror
///   versions/&lt;snapshot&gt;/&lt;library&gt;/&lt;path&gt;  what that run replaced or removed from current/
///   snapshots/&lt;snapshot&gt;/index.json.gz   every file of that run
///   in-progress/&lt;snapshot&gt;/&lt;n&gt;.json      what an interrupted run did already
/// </code>
/// </summary>
internal static class BackupFormat
{
    public const int Version = 1;
    public const string ManifestFile = "arcanist-backup.json";
    public const string Current = "current";
    public const string Versions = "versions";
    public const string Snapshots = "snapshots";
    public const string InProgress = "in-progress";
    public const string IndexFile = "index.json.gz";

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseUpper) },
    };

    private static readonly JsonSerializerOptions JsonIndented = new(Json) { WriteIndented = true };

    /// <summary>A snapshot is named after its run's start, in UTC, to the second.</summary>
    public static string SnapshotName(DateTime utc) => utc.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);

    public static bool TryParseSnapshot(string name, out DateTime utc) =>
        DateTime.TryParseExact(name, "yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out utc);

    public static string CurrentPath(string folder, string path) => BackupPaths.Join(folder, Current, path);

    public static string VersionPath(string folder, string snapshot, string path) => BackupPaths.Join(folder, Versions, snapshot, path);

    public static async Task<T?> ReadAsync<T>(IBackupTarget target, string path, CancellationToken cancellationToken) where T : class
    {
        if (await target.StatAsync(path, cancellationToken) is null)
            return null;
        await using var stream = await target.OpenReadAsync(path, cancellationToken);
        await using var content = path.EndsWith(".gz", StringComparison.Ordinal) ? new GZipStream(stream, CompressionMode.Decompress) : stream;
        try
        {
            return await JsonSerializer.DeserializeAsync<T>(content, Json, cancellationToken);
        }
        catch (JsonException)
        {
            throw new Application.ArcanistException.Conflict(Application.ErrorCodes.BackupFormatUnsupported);
        }
    }

    /// <summary>Writes a new JSON file (gzipped when its name ends in <c>.gz</c>); it must not exist.</summary>
    public static async Task WriteAsync<T>(IBackupTarget target, string path, T value, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        if (path.EndsWith(".gz", StringComparison.Ordinal))
        {
            await using var zip = new GZipStream(buffer, CompressionLevel.Optimal, leaveOpen: true);
            await JsonSerializer.SerializeAsync(zip, value, Json, cancellationToken);
        }
        else
        {
            await JsonSerializer.SerializeAsync(buffer, value, JsonIndented, cancellationToken);
        }

        buffer.Position = 0;
        await target.WriteAsync(path, buffer, buffer.Length, cancellationToken);
    }

    /// <summary>
    /// Replaces a JSON file that changes between runs (the manifest): the new one is written beside it, the old one
    /// deleted, and the new one moved into place. A stop in between leaves the new one, which <see cref="ReadReplacedAsync"/> finds.
    /// </summary>
    public static async Task ReplaceAsync<T>(IBackupTarget target, string path, T value, CancellationToken cancellationToken)
    {
        var next = path + ".next";
        await target.DeleteAsync(next, cancellationToken);
        await WriteAsync(target, next, value, cancellationToken);
        await target.DeleteAsync(path, cancellationToken);
        await target.MoveAsync(next, path, cancellationToken);
    }

    public static async Task<T?> ReadReplacedAsync<T>(IBackupTarget target, string path, CancellationToken cancellationToken) where T : class =>
        await ReadAsync<T>(target, path, cancellationToken) ?? await ReadAsync<T>(target, path + ".next", cancellationToken);
}

/// <summary><c>arcanist-backup.json</c>: what the backup is, so it can be found and restored without this server.</summary>
internal sealed record BackupManifest
{
    public int Format { get; init; } = BackupFormat.Version;

    public Guid Id { get; init; }

    public required string Name { get; set; }

    public required string Scope { get; set; }

    /// <summary>The Arcanist version and machine that made it.</summary>
    public required string ArcanistVersion { get; set; }

    public required string Machine { get; set; }

    public DateTime CreatedAt { get; init; }

    public DateTime UpdatedAt { get; set; }

    /// <summary>Which folder of <c>current/</c> holds each library folder (chosen once, so it never moves).</summary>
    public List<BackupSourceFolder> Sources { get; init; } = [];

    /// <summary>The snapshots that can be restored, oldest first.</summary>
    public List<BackupSnapshotInfo> Snapshots { get; init; } = [];
}

internal sealed record BackupSourceFolder(Guid SourceId, Guid LibraryId, string Library, string Folder);

/// <param name="Server">It holds the server's encrypted database, settings and keys.</param>
internal sealed record BackupSnapshotInfo(string Name, DateTime CreatedAt, int Files, long Bytes, bool UserData, bool Server);

/// <summary>Every file of a snapshot, and what its run moved from <c>current/</c> into <c>versions/&lt;snapshot&gt;/</c>.</summary>
internal sealed record BackupIndex(int Format, string Snapshot, List<BackupIndexEntry> Files, List<string> Moved);

/// <param name="Path">Relative to <c>current/</c>: the library's folder, then the file's path in the library.</param>
/// <param name="ContentHash">Arcanist's hash of its audio, when the catalog knows the file (to match it on restore).</param>
internal sealed record BackupIndexEntry(
    [property: JsonPropertyName("p")] string Path,
    [property: JsonPropertyName("s")] long Size,
    [property: JsonPropertyName("m")] DateTime Modified,
    [property: JsonPropertyName("h")] string Sha256,
    [property: JsonPropertyName("c")] string? ContentHash = null);

/// <summary>A piece of an interrupted run's work, so resuming neither copies nor versions anything twice.</summary>
internal sealed record BackupJournal(List<BackupIndexEntry> Files, List<string> Moved);
