using System.Globalization;
using System.Text.Json;
using Arcanist.Core.Application;
using Microsoft.Data.Sqlite;

namespace Arcanist.Core.Import.Mstream;

/// <summary>A library of an mStream installation: a vpath and the folder it serves.</summary>
internal sealed record MstreamLibraryData(string Name, string Root);

/// <param name="PasswordHash">mStream's PBKDF2 hash and its salt; null when there is none to keep.</param>
internal sealed record MstreamUserData(string Name, bool IsAdmin, IReadOnlyList<string> Vpaths, string? PasswordHash = null, string? Salt = null);

/// <summary>A user's playlist, its songs as vpath paths (<c>vpath/relative/path</c>), in order.</summary>
internal sealed record MstreamPlaylistData(string Name, IReadOnlyList<string> Paths);

/// <summary>What a user did with a file: its rating (0–10), play count and last play.</summary>
internal sealed record MstreamTrackData(string Path, int? Rating, int PlayCount, DateTime? LastPlayedAt);

/// <summary>One user's data, as a vpath path per track.</summary>
internal sealed record MstreamUserContent(IReadOnlyList<MstreamPlaylistData> Playlists, IReadOnlyList<MstreamTrackData> Tracks);

/// <summary>An mStream installation as the importer sees it, whichever version wrote it.</summary>
/// <param name="Version">5 (LokiJS) or 6 (SQLite).</param>
/// <param name="Content">Each user's data, by user name.</param>
/// <param name="Unresolved">User data whose file mStream no longer knows (deleted, or re-tagged under mStream 5).</param>
internal sealed record MstreamData(
    int Version,
    IReadOnlyList<MstreamLibraryData> Libraries,
    IReadOnlyList<MstreamUserData> Users,
    IReadOnlyDictionary<string, MstreamUserContent> Content,
    int Unresolved);

/// <summary>
/// Reads an mStream installation on this server: its configuration file and its database, LokiJS files (mStream 5)
/// or SQLite (mStream 6, where users and libraries live in the database). Written from the formats, not from
/// mStream's code. Nothing is written to it.
/// </summary>
internal static class MstreamFolder
{
    /// <summary>The configuration file of an installation, given it or the folder it is in.</summary>
    public static string? FindConfig(string path)
    {
        if (File.Exists(path))
            return path;
        if (!Directory.Exists(path))
            return null;
        string[] candidates = [Path.Combine("save", "conf", "default.json"), Path.Combine("conf", "default.json"), "config.json", "default.json"];
        return candidates.Select(c => Path.Combine(path, c)).FirstOrDefault(File.Exists);
    }

    public static MstreamData Read(string path)
    {
        var config = FindConfig(path.Trim()) ?? throw ArcanistException.Field("folder", ErrorCodes.NotFound);
        JsonElement root;
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(config));
            root = document.RootElement.Clone();
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            throw ArcanistException.Field("folder", ErrorCodes.InvalidFormat);
        }

        if (root.ValueKind != JsonValueKind.Object)
            throw ArcanistException.Field("folder", ErrorCodes.InvalidFormat);

        var database = DatabaseFolder(config, root);
        var sqlite = database is null ? null : Path.Combine(database, "mstream.db");
        if (sqlite is not null && File.Exists(sqlite))
            return ReadSqlite(sqlite);
        return ReadLoki(root, database);
    }

    /// <summary>Where the database is: <c>storage.dbDirectory</c>, relative to the installation, or the defaults.</summary>
    private static string? DatabaseFolder(string config, JsonElement root)
    {
        var folder = Path.GetDirectoryName(Path.GetFullPath(config))!;
        // save/conf/default.json and conf/default.json are relative to the installation; others to their folder.
        var installation = Path.GetFileName(folder) == "conf"
            ? Path.GetFileName(Path.GetDirectoryName(folder)) == "save" ? Path.GetDirectoryName(Path.GetDirectoryName(folder))! : Path.GetDirectoryName(folder)!
            : folder;

        var configured = root.TryGetProperty("storage", out var storage) && storage.ValueKind == JsonValueKind.Object
                         && storage.TryGetProperty("dbDirectory", out var db) && db.ValueKind == JsonValueKind.String
            ? db.GetString()
            : null;
        string[] candidates = configured is { Length: > 0 }
            ? [Path.IsPathRooted(configured) ? configured : Path.Combine(installation, configured)]
            : [Path.Combine(installation, "save", "db"), Path.Combine(installation, "db")];
        return candidates.FirstOrDefault(Directory.Exists);
    }

    private static MstreamData ReadSqlite(string file)
    {
        // A copy, so the live database (and its write-ahead log) is never touched or locked.
        var copy = Directory.CreateTempSubdirectory("arcanist-mstream-");
        try
        {
            var target = Path.Combine(copy.FullName, "mstream.db");
            File.Copy(file, target);
            foreach (var suffix in new[] { "-wal", "-shm" })
            {
                if (File.Exists(file + suffix))
                    File.Copy(file + suffix, target + suffix);
            }

            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = target, Pooling = false }.ToString());
            connection.Open();
            return ReadSqlite(connection);
        }
        catch (SqliteException)
        {
            throw ArcanistException.Field("folder", ErrorCodes.InvalidFormat);
        }
        finally
        {
            try
            {
                copy.Delete(recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    private static MstreamData ReadSqlite(SqliteConnection connection)
    {
        var userColumns = Columns(connection, "users");
        var users = new Dictionary<long, (string Name, bool Admin, string? Hash, string? Salt)>();
        foreach (var row in Rows(connection, "SELECT id, username, password, salt, is_admin" +
                                             (userColumns.Contains("is_anonymous_sentinel") ? ", is_anonymous_sentinel" : ", 0") + " FROM users"))
        {
            // The hidden owner of what was done while mStream had no users.
            if (Convert.ToInt64(row[5], CultureInfo.InvariantCulture) == 1)
                continue;
            users[(long)row[0]!] = ((string)row[1]!, Convert.ToInt64(row[4] ?? 0L, CultureInfo.InvariantCulture) == 1, row[2] as string, row[3] as string);
        }

        var libraries = Rows(connection, "SELECT id, name, root_path FROM libraries").ToDictionary(r => (long)r[0]!, r => ((string)r[1]!, (string)r[2]!));
        var access = Rows(connection, "SELECT user_id, library_id FROM user_libraries").ToLookup(r => (long)r[0]!, r => (long)r[1]!);

        // User data is keyed by a hash of the file: resolved to paths through mStream's own catalog.
        var hasAudioHash = Columns(connection, "tracks").Contains("audio_hash");
        var paths = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var row in Rows(connection, "SELECT library_id, filepath, file_hash" + (hasAudioHash ? ", audio_hash" : ", NULL") + " FROM tracks"))
        {
            if (!libraries.TryGetValue((long)row[0]!, out var library))
                continue;
            var path = $"{library.Item1}/{((string)row[1]!).Replace('\\', '/')}";
            foreach (var hash in new[] { row[3] as string, row[2] as string })
            {
                if (hash is { Length: > 0 })
                    paths.TryAdd(hash, path);
            }
        }

        var tracks = new Dictionary<long, List<MstreamTrackData>>();
        var unresolved = 0;
        foreach (var row in Rows(connection, "SELECT user_id, track_hash, play_count, last_played, rating FROM user_metadata"))
        {
            if (!users.ContainsKey((long)row[0]!))
                continue;
            if (row[1] is not string hash || !paths.TryGetValue(hash, out var path))
            {
                unresolved++;
                continue;
            }

            Add(tracks, (long)row[0]!, new MstreamTrackData(path, row[4] is null ? null : Convert.ToInt32(row[4], CultureInfo.InvariantCulture),
                Convert.ToInt32(row[2] ?? 0L, CultureInfo.InvariantCulture), ParseTime(row[3])));
        }

        var playlists = new Dictionary<long, List<MstreamPlaylistData>>();
        var entries = Rows(connection, "SELECT playlist_id, filepath FROM playlist_tracks WHERE filepath IS NOT NULL ORDER BY playlist_id, position, id")
            .ToLookup(r => (long)r[0]!, r => (string)r[1]!);
        foreach (var row in Rows(connection, "SELECT id, user_id, name FROM playlists ORDER BY id"))
        {
            if (users.ContainsKey((long)row[1]!))
                Add(playlists, (long)row[1]!, new MstreamPlaylistData((string)row[2]!, entries[(long)row[0]!].ToList()));
        }

        return new MstreamData(
            6,
            libraries.Values.Select(l => new MstreamLibraryData(l.Item1, l.Item2)).ToList(),
            users.Select(u => new MstreamUserData(u.Value.Name, u.Value.Admin,
                access[u.Key].Where(libraries.ContainsKey).Select(id => libraries[id].Item1).ToList(), u.Value.Hash, u.Value.Salt)).ToList(),
            users.ToDictionary(u => u.Value.Name, u => new MstreamUserContent(
                playlists.GetValueOrDefault(u.Key) ?? [], tracks.GetValueOrDefault(u.Key) ?? [])),
            unresolved);
    }

    private static HashSet<string> Columns(SqliteConnection connection, string table) =>
        Rows(connection, $"SELECT name FROM pragma_table_info('{table}')").Select(r => (string)r[0]!).ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static List<object?[]> Rows(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        using var reader = command.ExecuteReader();
        var rows = new List<object?[]>();
        while (reader.Read())
        {
            var row = new object?[reader.FieldCount];
            for (var i = 0; i < row.Length; i++)
                row[i] = reader.IsDBNull(i) ? null : reader.GetValue(i);
            rows.Add(row);
        }

        return rows;
    }

    /// <summary>mStream 5: users and folders in the configuration; files, playlists and user data in LokiJS files.</summary>
    private static MstreamData ReadLoki(JsonElement config, string? database)
    {
        var libraries = new List<MstreamLibraryData>();
        if (config.TryGetProperty("folders", out var folders) && folders.ValueKind == JsonValueKind.Object)
        {
            foreach (var folder in folders.EnumerateObject())
            {
                if (folder.Value.ValueKind == JsonValueKind.Object && folder.Value.TryGetProperty("root", out var rootPath) && rootPath.ValueKind == JsonValueKind.String)
                    libraries.Add(new MstreamLibraryData(folder.Name, rootPath.GetString()!));
            }
        }

        var users = new List<MstreamUserData>();
        if (config.TryGetProperty("users", out var configured) && configured.ValueKind == JsonValueKind.Object)
        {
            foreach (var user in configured.EnumerateObject().Where(u => u.Value.ValueKind == JsonValueKind.Object))
            {
                var vpaths = user.Value.TryGetProperty("vpaths", out var list)
                    ? list.ValueKind switch
                    {
                        JsonValueKind.Array => list.EnumerateArray().Where(v => v.ValueKind == JsonValueKind.String).Select(v => v.GetString()!).ToList(),
                        JsonValueKind.String => [list.GetString()!],
                        _ => [],
                    }
                    : [];
                users.Add(new MstreamUserData(user.Name, user.Value.TryGetProperty("admin", out var admin) && admin.ValueKind == JsonValueKind.True,
                    vpaths, String(user.Value, "password"), String(user.Value, "salt")));
            }
        }

        // Paths by the MD5 the user data refers to.
        var paths = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var file in Collection(database, "files.loki-v3.db", "files"))
        {
            if (String(file, "hash") is { } hash && String(file, "vpath") is { } vpath && String(file, "filepath") is { } filepath)
                paths.TryAdd(hash, $"{vpath}/{filepath.Replace('\\', '/')}");
        }

        var tracks = new Dictionary<string, List<MstreamTrackData>>();
        var unresolved = 0;
        foreach (var row in Collection(database, "user-data.loki-v1.db", "user-metadata"))
        {
            if (String(row, "user") is not { } user)
                continue;
            if (String(row, "hash") is not { } hash || !paths.TryGetValue(hash, out var path))
            {
                unresolved++;
                continue;
            }

            Add(tracks, user, new MstreamTrackData(path, Int(row, "rating"), Int(row, "pc") ?? 0,
                row.TryGetProperty("lp", out var lastPlayed) ? ParseTime(lastPlayed.ValueKind == JsonValueKind.Number ? lastPlayed.GetDouble() : null) : null));
        }

        // A playlist is every row with the same user and name, in insertion order; a row without a file marks an empty one.
        var playlists = Collection(database, "user-data.loki-v1.db", "playlists")
            .Where(r => String(r, "user") is not null && String(r, "name") is not null)
            .OrderBy(r => Int(r, "$loki") ?? 0)
            .GroupBy(r => (User: String(r, "user")!, Name: String(r, "name")!))
            .ToLookup(g => g.Key.User, g => new MstreamPlaylistData(g.Key.Name, g.Select(r => String(r, "filepath")).OfType<string>().ToList()));

        var names = users.Select(u => u.Name).ToHashSet();
        return new MstreamData(
            5,
            libraries,
            users,
            names.ToDictionary(n => n, n => new MstreamUserContent(playlists[n].ToList(), tracks.GetValueOrDefault(n) ?? [])),
            unresolved);
    }

    /// <summary>The documents of a LokiJS collection; none when the file is not there.</summary>
    private static List<JsonElement> Collection(string? database, string file, string name)
    {
        var path = database is null ? null : Path.Combine(database, file);
        if (path is null || !File.Exists(path))
            return [];
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            if (!document.RootElement.TryGetProperty("collections", out var collections) || collections.ValueKind != JsonValueKind.Array)
                return [];
            var collection = collections.EnumerateArray().FirstOrDefault(c => String(c, "name") == name);
            return collection.ValueKind == JsonValueKind.Object && collection.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array
                ? data.EnumerateArray().Where(d => d.ValueKind == JsonValueKind.Object).Select(d => d.Clone()).ToList()
                : [];
        }
        catch (JsonException)
        {
            throw ArcanistException.Field("folder", ErrorCodes.InvalidFormat);
        }
    }

    private static string? String(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static int? Int(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? (int)Math.Round(value.GetDouble()) : null;

    private static void Add<TKey, T>(Dictionary<TKey, List<T>> lists, TKey key, T item) where TKey : notnull
    {
        if (!lists.TryGetValue(key, out var list))
            lists[key] = list = [];
        list.Add(item);
    }

    /// <summary>A time as mStream stored it: epoch milliseconds, ISO 8601 or SQLite text, always UTC.</summary>
    internal static DateTime? ParseTime(object? value) => value switch
    {
        long ms => DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime,
        double ms => DateTimeOffset.FromUnixTimeMilliseconds((long)ms).UtcDateTime,
        string text when long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var ms) => DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime,
        string text when DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var time) => time,
        _ => null,
    };
}
