using System.Text.Json;

namespace Arcanist.Core.Hosting;

/// <summary>What a factory reset asked for, left in the data directory for the next start.</summary>
/// <param name="SqliteFile">The SQLite database to delete (PostgreSQL's tables are dropped before the restart).</param>
/// <param name="RemoveAddons">Also installed players, extensions (and their data) and downloaded tools.</param>
public sealed record FactoryResetRequest(string? SqliteFile, bool RemoveAddons, DateTime RequestedAt);

/// <summary>
/// A factory reset ends with a restart, and the files go at the next start, before anything reads them: the database
/// file, <c>arcanist.json</c>, the keys and the cache (and, if asked, players, extensions and tools). The server then
/// starts as freshly installed, in the setup wizard. Music folders are never touched, and logs are kept.
/// </summary>
public static class FactoryReset
{
    public const string MarkerFile = "factory-reset.json";

    /// <summary>The exit code that asks supervisors (the tray, services, systemd, Docker) to start the server again.</summary>
    public const int RestartExitCode = 75;

    public static async Task RequestAsync(ArcanistPaths paths, FactoryResetRequest request, CancellationToken cancellationToken) =>
        await File.WriteAllTextAsync(Path.Combine(paths.Root, MarkerFile), JsonSerializer.Serialize(request), cancellationToken);

    /// <summary>Applies a factory reset left by the previous run. Called at startup, before the settings are read.</summary>
    public static void ApplyIfRequested(ArcanistPaths paths, Action<string>? log = null)
    {
        var marker = Path.Combine(paths.Root, MarkerFile);
        if (!File.Exists(marker))
            return;

        FactoryResetRequest? request;
        try
        {
            request = JsonSerializer.Deserialize<FactoryResetRequest>(File.ReadAllText(marker));
        }
        catch (JsonException)
        {
            request = new FactoryResetRequest(null, false, DateTime.UtcNow);
        }

        if (request?.SqliteFile is { Length: > 0 } database)
        {
            foreach (var file in new[] { database, database + "-wal", database + "-shm" })
                DeleteFile(file);
        }

        DeleteFile(paths.SettingsFile);
        DeleteDirectory(paths.Keys);
        DeleteDirectory(paths.Cache);
        DeleteDirectory(paths.Transcodes);
        if (request?.RemoveAddons == true)
        {
            DeleteDirectory(paths.Frontends);
            DeleteDirectory(paths.Extensions);
            DeleteDirectory(Path.Combine(paths.Root, "extension-data"));
            DeleteDirectory(Path.Combine(paths.Root, "tools"));
        }

        File.Delete(marker);
        log?.Invoke("Factory reset applied: the server starts as freshly installed.");
    }

    private static void DeleteFile(string path)
    {
        if (File.Exists(path))
            File.Delete(path);
    }

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
            Directory.Delete(path, recursive: true);
    }
}
