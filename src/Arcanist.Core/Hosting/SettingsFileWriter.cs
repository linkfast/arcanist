using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration.Json;

namespace Arcanist.Core.Hosting;

/// <summary>
/// Persists values to <c>arcanist.json</c> in the data directory and reloads configuration immediately, so
/// options read right after a write (for example the database chosen in the setup wizard) see the new values.
/// </summary>
public interface ISettingsFileWriter
{
    /// <summary>Sets configuration keys (colon-separated, e.g. <c>Arcanist:Database:Provider</c>).</summary>
    Task SetAsync(IReadOnlyDictionary<string, object?> values, CancellationToken cancellationToken = default);

    /// <summary>True when the effective value of <paramref name="key"/> comes from somewhere other than
    /// <c>arcanist.json</c> (appsettings, environment variables, command line), so the UI must not edit it.</summary>
    bool IsSetOutsideSettingsFile(string key);
}

internal sealed class SettingsFileWriter(ArcanistPaths paths, IConfiguration configuration) : ISettingsFileWriter
{
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };
    private readonly SemaphoreSlim _lock = new(1, 1);

    public async Task SetAsync(IReadOnlyDictionary<string, object?> values, CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            var root = File.Exists(paths.SettingsFile)
                ? JsonNode.Parse(await File.ReadAllTextAsync(paths.SettingsFile, cancellationToken)) as JsonObject ?? []
                : [];

            foreach (var (key, value) in values)
                SetValue(root, key.Split(':'), value);

            // Write-then-rename so a crash never leaves a truncated settings file behind.
            var temporary = paths.SettingsFile + ".tmp";
            await File.WriteAllTextAsync(temporary, root.ToJsonString(WriteOptions), cancellationToken);
            File.Move(temporary, paths.SettingsFile, overwrite: true);
        }
        finally
        {
            _lock.Release();
        }

        (configuration as IConfigurationRoot)?.Reload();
    }

    public bool IsSetOutsideSettingsFile(string key)
    {
        if (configuration is not IConfigurationRoot root)
            return false;

        // Later providers win; the first one (from the end) that has the key supplies the effective value.
        var provider = root.Providers.Reverse().FirstOrDefault(p => p.TryGet(key, out _));
        return provider is not null && !IsSettingsFile(provider);
    }

    private static bool IsSettingsFile(IConfigurationProvider provider) =>
        provider is JsonConfigurationProvider { Source.Path: ArcanistPaths.SettingsFileName };

    private static void SetValue(JsonObject node, string[] path, object? value)
    {
        for (var i = 0; i < path.Length - 1; i++)
        {
            if (node[path[i]] is not JsonObject child)
            {
                child = [];
                node[path[i]] = child;
            }

            node = child;
        }

        node[path[^1]] = value switch
        {
            null => null,
            Enum e => JsonValue.Create(e.ToString()),
            _ => JsonSerializer.SerializeToNode(value),
        };
    }
}
