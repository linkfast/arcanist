using Arcanist.Core.Data;
using Arcanist.Core.Data.Entities;
using Arcanist.Core.Setup;
using Microsoft.Extensions.Options;

namespace Arcanist.Core.Application.Listening;

/// <summary>
/// The server's <see cref="RatingMode"/>, cached: it is part of <c>serverInfo</c>, which is read without touching
/// the database. Loaded at startup (<see cref="RatingSettingsWarmup"/>) and kept current when it changes.
/// </summary>
public sealed class RatingSettings(SettingsStore settings, IArcanistDbContextFactory databases, IOptionsMonitor<SetupOptions> setup)
{
    private volatile object? _mode;

    /// <summary>The mode as last loaded; <see cref="RatingMode.Stars"/> until then.</summary>
    public RatingMode Current => _mode is RatingMode mode ? mode : RatingMode.Stars;

    public async Task<RatingMode> GetAsync(CancellationToken cancellationToken)
    {
        if (_mode is null && setup.CurrentValue.Completed && databases.IsConfigured)
            _mode = await settings.GetAsync(SettingKeys.RatingMode, RatingMode.Stars, cancellationToken);
        return Current;
    }

    public async Task SetAsync(RatingMode mode, CancellationToken cancellationToken)
    {
        await settings.SetAsync(SettingKeys.RatingMode, mode, cancellationToken);
        _mode = mode;
    }

    /// <summary>Forgets the cached mode, e.g. after the setup wizard stored it directly.</summary>
    internal void Reset() => _mode = null;
}

internal sealed class RatingSettingsWarmup(RatingSettings ratings, ILogger<RatingSettingsWarmup> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await ratings.GetAsync(cancellationToken);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            logger.LogWarning(e, "Could not read the rating mode; using stars");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
