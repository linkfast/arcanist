using Arcanist.Core.Application.Server;

namespace Arcanist.Core.Api.Rest;

public sealed record SetAnonymousStatisticsInput(bool Enabled);

public static class ServerAdminEndpoints
{
    public static IEndpointRouteBuilder MapServerAdminEndpoints(this IEndpointRouteBuilder rest)
    {
        var server = rest.MapGroup("/server").WithTags("Server");

        rest.MapGet("/storage-usage", (StorageUsageService service, CancellationToken ct, bool refresh = false) => service.UsageAsync(refresh, ct))
            .WithTags("Server")
            .WithName("StorageUsage")
            .WithSummary("How much space Arcanist takes, part by part (the database too), its database's tables, how much is in the " +
                         "catalog and the music of each library. Measured at most every five minutes unless refresh is true. " +
                         "Administrators only.");

        server.MapGet("/configuration", (ServerAdminService service, CancellationToken ct) => service.ConfigurationAsync(ct))
            .WithName("ServerConfiguration")
            .WithSummary("What the setup wizard chose, as it is now, to run it again. Administrators only.");

        server.MapPut("/configuration", (ReconfigureServerInput input, ServerAdminService service, CancellationToken ct) => service.ReconfigureAsync(input, ct))
            .WithName("ReconfigureServer")
            .WithSummary("The setup wizard's answers again: language, ratings, online services, anonymous statistics and ffmpeg. Administrators only, with a recent authentication.");

        server.MapGet("/update", (UpdateService service, CancellationToken ct) => service.GetAsync(ct))
            .WithName("ServerUpdate")
            .WithSummary("Whether there is a newer Arcanist (checked on GitHub every 12 hours), with what changed and how this server is updated. Administrators only.");

        server.MapPost("/update/check", (UpdateService service, CancellationToken ct) => service.CheckAsync(ct))
            .WithName("CheckServerUpdate")
            .WithSummary("Checks now for a newer Arcanist. Administrators only.");

        server.MapPut("/update/settings", (SetServerUpdateSettingsInput input, UpdateService service, CancellationToken ct) => service.SetSettingsAsync(input, ct))
            .WithName("SetServerUpdateSettings")
            .WithSummary("Switches the checks for newer versions on or off. Administrators only.");

        server.MapPost("/restart", (ServerAdminService service) => service.Restart())
            .WithName("RestartServer")
            .WithSummary("Restarts the server (to load extensions installed or switched on since it started). It stops with exit code 75 and its supervisor starts it again; wait for /health. Administrators only, with a recent authentication.");

        server.MapPost("/reset/settings", (ServerAdminService service, CancellationToken ct) => service.ResetSettingsAsync(ct))
            .WithName("ResetSettings")
            .WithSummary("Every server setting back to how a new server starts; libraries, users and their data stay. Administrators only, with a recent authentication.");

        server.MapPost("/reset/user-data", (ServerAdminService service, CancellationToken ct) => service.ResetUserDataAsync(ct))
            .WithName("ResetUserData")
            .WithSummary("Deletes every user's ratings, plays and playlists; users, libraries and settings stay. Administrators only, with a recent authentication.");

        server.MapPost("/reset/factory", (FactoryResetInput input, ServerAdminService service, CancellationToken ct) => service.FactoryResetAsync(input, ct))
            .WithName("FactoryReset")
            .WithSummary("Everything back to a freshly installed server (database, settings, keys, cache; optionally players, extensions and tools); the server restarts into the setup wizard. Music folders are never touched. Administrators only, with a recent authentication.");

        rest.MapGet("/settings/privacy", (PrivacyService service, CancellationToken ct) => service.StatusAsync(ct))
            .WithTags("Settings")
            .WithName("Privacy")
            .WithSummary("No telemetry, and which outside services the server may use. Requires MANAGE_SETTINGS.");

        rest.MapPut("/settings/privacy", (SetPrivacyInput input, PrivacyService service, CancellationToken ct) => service.SetAsync(input, ct))
            .WithTags("Settings")
            .WithName("SetPrivacy")
            .WithSummary("Chooses no telemetry (anonymous statistics off; linked accounts start without sending plays) and which outside services may be used: ALL, ULTIMATE_CLOUD_ONLY (no online services) or NONE (Ultimate Cloud's extension off too). What they leave out is turned off. The music server's own services are not affected. Requires MANAGE_SETTINGS.");

        rest.MapGet("/settings/open-source", (OpenSourceService service, CancellationToken ct) => service.StatusAsync(ct))
            .WithTags("Settings")
            .WithName("OpenSourceMode")
            .WithSummary("The 100% open source mode, the server's edition and the closed source components installed. Administrators only.");

        rest.MapPut("/settings/open-source", (SetOpenSourceModeInput input, OpenSourceService service, CancellationToken ct) => service.SetAsync(input.Enabled, ct))
            .WithTags("Settings")
            .WithName("SetOpenSourceMode")
            .WithSummary("Turns the 100% open source mode on or off: while on, closed source extensions, players and tools are neither installed nor run (closed_source_disabled). Administrators only, with a recent authentication.");

        rest.MapGet("/settings/anonymous-statistics", (ServerAdminService service, CancellationToken ct) => service.AnonymousStatisticsAsync(ct))
            .WithTags("Settings")
            .WithName("AnonymousStatistics")
            .WithSummary("Whether anonymous play counts go to Ultimate Cloud's global statistics. Requires MANAGE_SETTINGS.");

        rest.MapPut("/settings/anonymous-statistics", (SetAnonymousStatisticsInput input, ServerAdminService service, CancellationToken ct) =>
                service.SetAnonymousStatisticsAsync(input.Enabled, ct))
            .WithTags("Settings")
            .WithName("SetAnonymousStatistics")
            .WithSummary("Sends anonymous play counts to Ultimate Cloud's global statistics, or stops. Requires MANAGE_SETTINGS.");

        return rest;
    }
}
