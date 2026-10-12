using System.Text.Json;
using Arcanist.Core.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Arcanist.Core.Data;

/// <summary>
/// Runtime settings edited from the admin panel, stored as JSON values in the <c>Settings</c> table (see
/// <see cref="SettingKeys"/>). Only usable once the database is configured.
/// </summary>
public sealed class SettingsStore(IArcanistDbContextFactory databases, TimeProvider time)
{
    public async Task<T> GetAsync<T>(string key, T defaultValue, CancellationToken cancellationToken)
    {
        await using var db = databases.CreateDbContext();
        var value = await db.Settings.AsNoTracking().Where(s => s.Key == key).Select(s => s.Value).FirstOrDefaultAsync(cancellationToken);
        if (value is null)
            return defaultValue;

        try
        {
            return JsonSerializer.Deserialize<T>(value, JsonSerializerOptions.Web) ?? defaultValue;
        }
        catch (JsonException)
        {
            return defaultValue;
        }
    }

    public async Task SetAsync<T>(string key, T value, CancellationToken cancellationToken)
    {
        await using var db = databases.CreateDbContext();
        var json = JsonSerializer.Serialize(value, JsonSerializerOptions.Web);
        var setting = await db.Settings.FirstOrDefaultAsync(s => s.Key == key, cancellationToken);
        if (setting is null)
            db.Settings.Add(new Setting { Key = key, Value = json, UpdatedAt = time.GetUtcNow().UtcDateTime });
        else
        {
            setting.Value = json;
            setting.UpdatedAt = time.GetUtcNow().UtcDateTime;
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
