using System.Text.Json.Serialization;
using Arcanist.Core.Application.Mstream;
using Arcanist.Core.Data;
using Arcanist.Core.Setup;
using Microsoft.Extensions.Options;

namespace Arcanist.Core.Extensions.Mstream;

/// <summary>The mStream settings, kept in the database (<see cref="SettingKeys.Mstream"/>).</summary>
/// <param name="Off">
/// The apps' functions an administrator turned off. Kept this way round so every function is on until turned off,
/// also those a later version of Arcanist adds.
/// </param>
public sealed record MstreamConfig(int StarThreshold, bool PasswordSignIn, IReadOnlyList<MstreamFeature> Off)
{
    public static readonly MstreamConfig Default = new(1, false, []);

    public bool Has(MstreamFeature feature) => !Off.Contains(feature);

    [JsonIgnore]
    public IReadOnlyList<MstreamFeature> Features => Enum.GetValues<MstreamFeature>().Where(Has).ToList();
}

/// <summary>The mStream settings, cached: every mStream call reads them.</summary>
public sealed class MstreamSettings(SettingsStore settings, IArcanistDbContextFactory databases, IOptionsMonitor<SetupOptions> setup)
{
    private volatile MstreamConfig? _config;

    public async Task<MstreamConfig> GetAsync(CancellationToken cancellationToken)
    {
        if (_config is null && setup.CurrentValue.Completed && databases.IsConfigured)
            _config = await settings.GetAsync(SettingKeys.Mstream, MstreamConfig.Default, cancellationToken);
        return _config ?? MstreamConfig.Default;
    }

    public async Task SetAsync(MstreamConfig config, CancellationToken cancellationToken)
    {
        await settings.SetAsync(SettingKeys.Mstream, config, cancellationToken);
        _config = config;
    }
}
