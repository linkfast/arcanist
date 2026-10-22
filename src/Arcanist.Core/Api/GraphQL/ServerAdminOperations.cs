using System.ComponentModel;
using Arcanist.Core.Application.Server;
using GraphQL;

namespace Arcanist.Core.Api.GraphQL;

public sealed partial class Query
{
    [Name("serverConfiguration")]
    [Description("What the setup wizard chose, as it is now, to run it again. Administrators only.")]
    public static Task<ServerConfiguration> ServerConfiguration([FromServices] ServerAdminService admin, CancellationToken cancellationToken) =>
        admin.ConfigurationAsync(cancellationToken);

    [Name("serverUpdate")]
    [Description("Whether there is a newer Arcanist (checked on GitHub every 12 hours), with what changed and how this server is updated. Administrators only.")]
    public static Task<ServerUpdate> ServerUpdate([FromServices] UpdateService updates, CancellationToken cancellationToken) =>
        updates.GetAsync(cancellationToken);

    [Name("serverHost")]
    [Description("Where and how this server runs (how it was installed, system, container, supervisor, user, data folder), so that clients give the steps that fit it when something needs doing outside Arcanist. Administrators only.")]
    public static ServerHost ServerHost([FromServices] HostService host) => host.Get();

    [Name("openSourceMode")]
    [Description("The 100% open source mode, the server's edition and the closed source components installed. Administrators only.")]
    public static Task<OpenSourceMode> OpenSourceMode([FromServices] OpenSourceService service, CancellationToken cancellationToken) =>
        service.StatusAsync(cancellationToken);

    [Name("privacy")]
    [Description("No telemetry, and which outside services the server may use. Requires MANAGE_SETTINGS.")]
    public static Task<Privacy> Privacy([FromServices] PrivacyService privacy, CancellationToken cancellationToken) =>
        privacy.StatusAsync(cancellationToken);

    [Name("anonymousStatistics")]
    [Description("Whether anonymous play counts go to Ultimate Cloud's global statistics. Requires MANAGE_SETTINGS.")]
    public static Task<bool> AnonymousStatistics([FromServices] ServerAdminService admin, CancellationToken cancellationToken) =>
        admin.AnonymousStatisticsAsync(cancellationToken);
}

public sealed partial class Mutation
{
    [Name("checkServerUpdate")]
    [Description("Checks now for a newer Arcanist. Administrators only.")]
    public static Task<ServerUpdate> CheckServerUpdate([FromServices] UpdateService updates, CancellationToken cancellationToken) =>
        updates.CheckAsync(cancellationToken);

    [Name("setServerUpdateSettings")]
    [Description("Switches the checks for newer versions on or off. Administrators only.")]
    public static Task<ServerUpdate> SetServerUpdateSettings(SetServerUpdateSettingsInput input, [FromServices] UpdateService updates, CancellationToken cancellationToken) =>
        updates.SetSettingsAsync(input, cancellationToken);

    [Name("reconfigureServer")]
    [Description("The setup wizard's answers again: language, ratings, online services, anonymous statistics and ffmpeg. Administrators only, with a recent authentication.")]
    public static Task<ServerConfiguration> ReconfigureServer(ReconfigureServerInput input, [FromServices] ServerAdminService admin, CancellationToken cancellationToken) =>
        admin.ReconfigureAsync(input, cancellationToken);

    [Name("setOpenSourceMode")]
    [Description("Turns the 100% open source mode on or off: while on, closed source extensions, players and tools are neither installed nor run (closed_source_disabled). Administrators only, with a recent authentication.")]
    public static Task<OpenSourceMode> SetOpenSourceMode(SetOpenSourceModeInput input, [FromServices] OpenSourceService service, CancellationToken cancellationToken) =>
        service.SetAsync(input.Enabled, cancellationToken);

    [Name("setPrivacy")]
    [Description("Chooses no telemetry (anonymous statistics off; linked accounts start without sending plays) and which outside services may be used: ALL, ULTIMATE_CLOUD_ONLY (no online services) or NONE (Ultimate Cloud's extension off too). What they leave out is turned off. The music server's own services are not affected. Requires MANAGE_SETTINGS.")]
    public static Task<Privacy> SetPrivacy(SetPrivacyInput input, [FromServices] PrivacyService privacy, CancellationToken cancellationToken) =>
        privacy.SetAsync(input, cancellationToken);

    [Name("setAnonymousStatistics")]
    [Description("Sends anonymous play counts to Ultimate Cloud's global statistics, or stops. Requires MANAGE_SETTINGS.")]
    public static Task<bool> SetAnonymousStatistics(bool enabled, [FromServices] ServerAdminService admin, CancellationToken cancellationToken) =>
        admin.SetAnonymousStatisticsAsync(enabled, cancellationToken);

    [Name("restartServer")]
    [Description("Restarts the server (to load extensions installed or switched on since it started). It stops with exit code 75 and its supervisor starts it again; wait for /health. Administrators only, with a recent authentication.")]
    public static ResetResult RestartServer([FromServices] ServerAdminService admin) => admin.Restart();

    [Name("resetSettings")]
    [Description("Every server setting back to how a new server starts; libraries, users and their data stay. Administrators only, with a recent authentication.")]
    public static Task<ResetResult> ResetSettings([FromServices] ServerAdminService admin, CancellationToken cancellationToken) =>
        admin.ResetSettingsAsync(cancellationToken);

    [Name("resetUserData")]
    [Description("Deletes every user's ratings, plays and playlists; users, libraries and settings stay. Administrators only, with a recent authentication.")]
    public static Task<ResetResult> ResetUserData([FromServices] ServerAdminService admin, CancellationToken cancellationToken) =>
        admin.ResetUserDataAsync(cancellationToken);

    [Name("factoryReset")]
    [Description("Everything back to a freshly installed server (database, settings, keys, cache; optionally players, extensions and tools); the server restarts into the setup wizard. Music folders are never touched. Administrators only, with a recent authentication.")]
    public static Task<ResetResult> FactoryReset(FactoryResetInput input, [FromServices] ServerAdminService admin, CancellationToken cancellationToken) =>
        admin.FactoryResetAsync(input, cancellationToken);
}
