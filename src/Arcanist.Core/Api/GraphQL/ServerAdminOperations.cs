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
    [Name("reconfigureServer")]
    [Description("The setup wizard's answers again: language, ratings, online services, anonymous statistics and ffmpeg. Administrators only, with a recent authentication.")]
    public static Task<ServerConfiguration> ReconfigureServer(ReconfigureServerInput input, [FromServices] ServerAdminService admin, CancellationToken cancellationToken) =>
        admin.ReconfigureAsync(input, cancellationToken);

    [Name("setPrivacy")]
    [Description("Chooses no telemetry (anonymous statistics off; linked accounts start without sending plays) and which outside services may be used: ALL, ULTIMATE_CLOUD_ONLY (no online services) or NONE (Ultimate Cloud's extension off too). What they leave out is turned off. The music server's own services are not affected. Requires MANAGE_SETTINGS.")]
    public static Task<Privacy> SetPrivacy(SetPrivacyInput input, [FromServices] PrivacyService privacy, CancellationToken cancellationToken) =>
        privacy.SetAsync(input, cancellationToken);

    [Name("setAnonymousStatistics")]
    [Description("Sends anonymous play counts to Ultimate Cloud's global statistics, or stops. Requires MANAGE_SETTINGS.")]
    public static Task<bool> SetAnonymousStatistics(bool enabled, [FromServices] ServerAdminService admin, CancellationToken cancellationToken) =>
        admin.SetAnonymousStatisticsAsync(enabled, cancellationToken);

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
