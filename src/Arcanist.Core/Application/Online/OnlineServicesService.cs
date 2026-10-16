using Arcanist.Core.Data;
using Arcanist.Core.Data.Entities;
using Arcanist.Core.Online;
using Microsoft.EntityFrameworkCore;

namespace Arcanist.Core.Application.Online;

public sealed record SetOnlineServicesInput(bool MusicBrainz, bool AcoustId, bool CoverArtArchive, bool Lyrics);

public sealed record SetAcoustIdKeyInput(string? Key);

/// <summary>Turns online services on and off and reports what they did. Requires MANAGE_SETTINGS.</summary>
public sealed class OnlineServicesService(
    AccessGuard guard,
    ArcanistDbContext db,
    OnlineSettings settings,
    SettingsStore store,
    OnlineMonitor monitor,
    TimeProvider time)
{
    public async Task<OnlineServicesStatus> StatusAsync(CancellationToken cancellationToken)
    {
        guard.Permission(Permissions.ManageSettings);
        return await BuildAsync(cancellationToken);
    }

    public async Task<OnlineServicesStatus> SetAsync(SetOnlineServicesInput input, CancellationToken cancellationToken)
    {
        guard.Permission(Permissions.ManageSettings);
        if ((input.MusicBrainz || input.AcoustId || input.CoverArtArchive || input.Lyrics)
            && (await store.GetAsync(SettingKeys.Privacy, Server.PrivacySettings.Default, cancellationToken)).ExternalServices != Server.ExternalServices.All)
            throw new ArcanistException.Conflict(ErrorCodes.ExternalServicesDisabled);
        await store.SetAsync(SettingKeys.OnlineServices,
            new OnlineServicesSettings(input.MusicBrainz, input.AcoustId, input.CoverArtArchive, input.Lyrics), cancellationToken);
        monitor.RequestRun();
        return await BuildAsync(cancellationToken);
    }

    /// <summary>Uses the administrator's own AcoustID application key, or the project's when null or empty.</summary>
    public async Task<OnlineServicesStatus> SetAcoustIdKeyAsync(string? key, CancellationToken cancellationToken)
    {
        guard.Permission(Permissions.ManageSettings);
        key = string.IsNullOrWhiteSpace(key) ? null : key.Trim();
        if (key is { Length: > 100 })
            throw ArcanistException.Field("key", ErrorCodes.InvalidFormat);

        await store.SetAsync(SettingKeys.AcoustIdKey, key, cancellationToken);
        monitor.RequestRun();
        return await BuildAsync(cancellationToken);
    }

    private async Task<OnlineServicesStatus> BuildAsync(CancellationToken cancellationToken)
    {
        var current = await settings.GetAsync(cancellationToken);
        var hasOwnKey = await settings.OwnAcoustIdKeyAsync(cancellationToken) is { Length: > 0 };
        var hasKey = hasOwnKey || settings.ProjectAcoustIdKey is not null;
        var now = time.GetUtcNow().UtcDateTime;

        var outcomes = await db.OnlineLookups.AsNoTracking()
            .GroupBy(l => new { l.Service, l.Outcome })
            .Select(g => new { g.Key.Service, g.Key.Outcome, Count = g.Count() })
            .ToListAsync(cancellationToken);
        int Count(OnlineService service, LookupOutcome outcome) =>
            outcomes.FirstOrDefault(o => o.Service == service && o.Outcome == outcome)?.Count ?? 0;

        var services = new List<OnlineServiceStatus>();
        foreach (var service in Enum.GetValues<OnlineService>())
        {
            var (at, error) = monitor.Last(service);
            var enabled = current.IsEnabled(service);
            services.Add(new OnlineServiceStatus(
                service,
                enabled,
                enabled && (service != OnlineService.AcoustId || hasKey),
                await EnrichmentWorker.Pending(db, service, now).CountAsync(cancellationToken),
                Count(service, LookupOutcome.Found),
                Count(service, LookupOutcome.NotFound),
                Count(service, LookupOutcome.Failed),
                at,
                error));
        }

        return new OnlineServicesStatus(current, hasKey, hasOwnKey, services);
    }
}
