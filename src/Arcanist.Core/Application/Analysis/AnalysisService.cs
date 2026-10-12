using Arcanist.Core.Analysis;
using Arcanist.Core.Data;
using Arcanist.Core.Data.Entities;

namespace Arcanist.Core.Application.Analysis;

public sealed class AnalysisService(AccessGuard guard, AnalysisMonitor monitor, SettingsStore settings)
{
    /// <summary>Requires MANAGE_LIBRARIES.</summary>
    public AnalysisProgress Progress()
    {
        guard.Permission(Permissions.ManageLibraries);
        return monitor.Current;
    }

    /// <summary>Live progress. Requires MANAGE_LIBRARIES.</summary>
    public IObservable<AnalysisProgress> Watch()
    {
        guard.Permission(Permissions.ManageLibraries);
        return monitor;
    }

    /// <summary>Turns background analysis on or off. Requires MANAGE_SETTINGS.</summary>
    public async Task<AnalysisProgress> SetEnabledAsync(bool enabled, CancellationToken cancellationToken)
    {
        guard.Permission(Permissions.ManageSettings);
        await settings.SetAsync(SettingKeys.AnalysisEnabled, enabled, cancellationToken);
        monitor.Update(p => p with { Enabled = enabled, Activity = enabled ? p.Activity : AnalysisActivity.Disabled });
        monitor.RequestRun();
        return monitor.Current;
    }
}
