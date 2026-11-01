using Arcanist.Core.Data;

namespace Arcanist.Core.Auth.SecondFactor;

/// <param name="Required">Everyone signs in with a second factor; who has none pairs one at their next sign-in.</param>
public sealed record SecondFactorConfig(bool Required)
{
    public static readonly SecondFactorConfig Default = new(false);
}

/// <summary>Whether the server requires a second factor, cached. <c>Arcanist__Auth__RequireSecondFactor</c> wins over the setting.</summary>
public sealed class SecondFactorSettings(SettingsStore store, IConfiguration configuration)
{
    public const string EnvironmentKey = "Arcanist:Auth:RequireSecondFactor";

    private volatile SecondFactorConfig? _current;

    /// <summary>What the environment says, if it says anything.</summary>
    public bool? Environment => configuration.GetValue<bool?>(EnvironmentKey);

    public async Task<bool> RequiredAsync(CancellationToken cancellationToken) =>
        Environment ?? (_current ??= await store.GetAsync(SettingKeys.SecondFactor, SecondFactorConfig.Default, cancellationToken)).Required;

    public async Task SetAsync(bool required, CancellationToken cancellationToken)
    {
        var config = new SecondFactorConfig(required);
        await store.SetAsync(SettingKeys.SecondFactor, config, cancellationToken);
        _current = config;
    }

    public void Invalidate() => _current = null;
}
