using Arcanist.Core.Application.Online;
using Arcanist.Core.Cloud;
using Arcanist.Core.Data;
using Arcanist.Core.Data.Entities;
using Arcanist.Core.Hosting;
using Arcanist.Core.Online;
using Arcanist.Core.Setup;

namespace Arcanist.Core.Application.Server;

// Types in this file are part of the public API contracts (GraphQL schema and OpenAPI document).

/// <summary>Which outside services the server may use. The music server's own (DLNA, Subsonic, mStream, the P2P network) are not affected.</summary>
public enum ExternalServices
{
    /// <summary>Whatever the administrator turns on: the online services and Ultimate Cloud.</summary>
    All,

    /// <summary>None but Ultimate Cloud: MusicBrainz, AcoustID, Cover Art Archive and LRCLib stay off.</summary>
    UltimateCloudOnly,

    /// <summary>None at all: the online services stay off, and so does the Ultimate Cloud extension.</summary>
    None,
}

/// <summary>What the server shares and with whom, chosen in the setup wizard and in Settings.</summary>
/// <param name="NoTelemetry">No statistics leave the server: anonymous statistics stay off, and linked Ultimate Cloud
/// accounts start with their plays and albums heard whole not sent (each can turn them on).</param>
public sealed record Privacy(bool NoTelemetry, ExternalServices ExternalServices);

public sealed record SetPrivacyInput(bool NoTelemetry, ExternalServices ExternalServices);

/// <summary>The stored privacy choices, read wherever they limit something.</summary>
public sealed class PrivacySettings(SettingsStore store)
{
    public static readonly Privacy Default = new(false, ExternalServices.All);

    public Task<Privacy> GetAsync(CancellationToken cancellationToken) => store.GetAsync(SettingKeys.Privacy, Default, cancellationToken);

    /// <summary>MusicBrainz, AcoustID, Cover Art Archive and LRCLib may run.</summary>
    public async Task<bool> OnlineServicesAllowedAsync(CancellationToken cancellationToken) =>
        (await GetAsync(cancellationToken)).ExternalServices == ExternalServices.All;

    /// <summary>Anonymous statistics may be sent (they go to Ultimate Cloud).</summary>
    public async Task<bool> TelemetryAllowedAsync(CancellationToken cancellationToken) =>
        await GetAsync(cancellationToken) is { NoTelemetry: false, ExternalServices: not ExternalServices.None };
}

/// <summary>Reading and changing the privacy choices. Requires MANAGE_SETTINGS.</summary>
public sealed class PrivacyService(
    AccessGuard guard,
    PrivacySettings privacy,
    SettingsStore settings,
    ISettingsFileWriter settingsFile,
    Core.Extensions.ExtensionHost extensions,
    IServiceProvider services)
{
    public async Task<Privacy> StatusAsync(CancellationToken cancellationToken)
    {
        guard.Permission(Permissions.ManageSettings);
        return await privacy.GetAsync(cancellationToken);
    }

    public async Task<Privacy> SetAsync(SetPrivacyInput input, CancellationToken cancellationToken)
    {
        guard.Permission(Permissions.ManageSettings);
        if (!Enum.IsDefined(input.ExternalServices))
            throw ArcanistException.Field("externalServices", ErrorCodes.InvalidFormat);
        return await ApplyAsync(new Privacy(input.NoTelemetry, input.ExternalServices), cancellationToken);
    }

    /// <summary>Stores the choices and turns off what they leave out: what is turned off stays off when they are relaxed.</summary>
    internal async Task<Privacy> ApplyAsync(Privacy choice, CancellationToken cancellationToken)
    {
        var before = await privacy.GetAsync(cancellationToken);
        await settings.SetAsync(SettingKeys.Privacy, choice, cancellationToken);

        if (choice.NoTelemetry || choice.ExternalServices == ExternalServices.None)
            await settings.SetAsync(SettingKeys.ShareAnonymousStatistics, false, cancellationToken);

        // Linked accounts stop sending plays and albums heard whole (each owner can turn them on again).
        if (choice.NoTelemetry && !before.NoTelemetry)
        {
            var accounts = services.GetRequiredService<UltimateCloudServicesSettings>();
            var users = await services.GetRequiredService<UltimateCloudAuth>().LinkedUsersAsync(cancellationToken);
            foreach (var account in new Guid?[] { null }.Concat(users.Select(u => (Guid?)u)))
            {
                var current = await accounts.GetAsync(cancellationToken, account);
                await accounts.SetAsync(current with { PersonalPlays = false, AlbumPlaythroughs = false }, cancellationToken, account);
            }
        }

        if (choice.ExternalServices != ExternalServices.All)
        {
            await settings.SetAsync(SettingKeys.OnlineServices, OnlineServicesSettings.None, cancellationToken);
            services.GetService<OnlineMonitor>()?.RequestRun();
        }

        if (choice.ExternalServices == ExternalServices.None)
            await TurnOffUltimateCloudAsync(settingsFile, extensions, cancellationToken);

        services.GetService<UltimateCloudReporter>()?.Wake();
        return choice;
    }

    /// <summary>Switches the Ultimate Cloud extension off (unless the environment decides it).</summary>
    internal static async Task TurnOffUltimateCloudAsync(ISettingsFileWriter settingsFile, Core.Extensions.ExtensionHost extensions, CancellationToken cancellationToken)
    {
        var id = Core.Extensions.UltimateCloud.UltimateCloudExtension.Id;
        var key = Core.Extensions.ExtensionHost.EnabledKey(id);
        if (settingsFile.IsSetOutsideSettingsFile(key) || !extensions.IsEnabled(id))
            return;
        await settingsFile.SetAsync(new Dictionary<string, object?> { [key] = false }, cancellationToken);
        extensions.OnEnabledChanged(id);
    }
}
