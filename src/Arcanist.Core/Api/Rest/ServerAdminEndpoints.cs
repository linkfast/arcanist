using Arcanist.Core.Application.Server;

namespace Arcanist.Core.Api.Rest;

public sealed record SetAnonymousStatisticsInput(bool Enabled);

public static class ServerAdminEndpoints
{
    public static IEndpointRouteBuilder MapServerAdminEndpoints(this IEndpointRouteBuilder rest)
    {
        var server = rest.MapGroup("/server").WithTags("Server");

        server.MapGet("/configuration", (ServerAdminService service, CancellationToken ct) => service.ConfigurationAsync(ct))
            .WithName("ServerConfiguration")
            .WithSummary("What the setup wizard chose, as it is now, to run it again. Administrators only.");

        server.MapPut("/configuration", (ReconfigureServerInput input, ServerAdminService service, CancellationToken ct) => service.ReconfigureAsync(input, ct))
            .WithName("ReconfigureServer")
            .WithSummary("The setup wizard's answers again: language, ratings, online services, anonymous statistics and ffmpeg. Administrators only, with a recent authentication.");

        server.MapPost("/reset/settings", (ServerAdminService service, CancellationToken ct) => service.ResetSettingsAsync(ct))
            .WithName("ResetSettings")
            .WithSummary("Every server setting back to how a new server starts; libraries, users and their data stay. Administrators only, with a recent authentication.");

        server.MapPost("/reset/user-data", (ServerAdminService service, CancellationToken ct) => service.ResetUserDataAsync(ct))
            .WithName("ResetUserData")
            .WithSummary("Deletes every user's ratings, plays and playlists; users, libraries and settings stay. Administrators only, with a recent authentication.");

        server.MapPost("/reset/factory", (FactoryResetInput input, ServerAdminService service, CancellationToken ct) => service.FactoryResetAsync(input, ct))
            .WithName("FactoryReset")
            .WithSummary("Everything back to a freshly installed server (database, settings, keys, cache; optionally players, extensions and tools); the server restarts into the setup wizard. Music folders are never touched. Administrators only, with a recent authentication.");

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
