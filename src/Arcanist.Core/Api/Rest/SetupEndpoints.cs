using Arcanist.Core.Application;
using Arcanist.Core.Application.Libraries;
using Arcanist.Core.Application.Import;
using Arcanist.Core.Auth;
using Arcanist.Core.Dlna;
using Arcanist.Core.Hosting;
using Arcanist.Core.Libraries;
using Arcanist.Core.Setup;

namespace Arcanist.Core.Api.Rest;

public static class SetupEndpoints
{
    public static IEndpointRouteBuilder MapSetupEndpoints(this IEndpointRouteBuilder rest)
    {
        var setup = rest.MapGroup("/setup").WithTags("Setup");

        setup.MapGet("/status", async (HttpContext context, SetupService service, CancellationToken cancellationToken) =>
                service.GetStatus(RequestOrigin.IsLocal(context)) with { Migrating = await service.MigratingAsync(cancellationToken) })
            .WithName("GetSetupStatus");

        // Everything else is only available while setup is pending, and from other devices only with the code.
        var guarded = setup.MapGroup("")
            .AddEndpointFilter(RequireSetupAccess)
            .RequireRateLimiting(AuthServiceCollectionExtensions.AuthRateLimit);

        guarded.MapPost("/database/test", (SetupDatabase request, SetupService service, CancellationToken cancellationToken) =>
                service.TestDatabaseAsync(request, cancellationToken))
            .WithName("TestSetupDatabase");

        guarded.MapGet("/directories", (string? path, DirectoryBrowser browser) => LibraryService.Browse(browser, path))
            .WithName("BrowseSetupDirectories");

        guarded.MapPost("/directories", (CreateServerDirectoryInput input, DirectoryBrowser browser) =>
                Results.Created((string?)null, LibraryService.CreateDirectory(browser, input)))
            .WithName("CreateSetupDirectory")
            .WithSummary("Creates a folder on the server, to pick it as a library location.")
            .Produces<ServerDirectory>(StatusCodes.Status201Created);

        guarded.MapGet("/ffmpeg", (string? path, SetupService service, CancellationToken cancellationToken) =>
                service.CheckFfmpegAsync(path, cancellationToken))
            .WithName("CheckSetupFfmpeg")
            .WithSummary("Checks the ffmpeg at the given path, or which one would be found automatically.");

        guarded.MapPost("/mstream/analyze", (SetupMstreamSource request, SetupService service, CancellationToken cancellationToken) =>
                service.AnalyzeMstreamAsync(request, cancellationToken))
            .WithName("AnalyzeSetupMstream")
            .WithSummary("Reads an mStream installation, from its folder on this server or from its server, to choose what to import.");

        guarded.MapGet("/dlna/servers", (MediaServerClient client, CancellationToken cancellationToken) =>
                DlnaImportService.SearchAsync(client, null, cancellationToken))
            .WithName("SearchSetupDlnaServers")
            .WithSummary("Looks for DLNA/UPnP media servers on the local networks, to copy music from once set up.");

        guarded.MapGet("/dlna/server", (string location, MediaServerClient client, CancellationToken cancellationToken) =>
                DlnaImportService.DescribeAsync(client, location, cancellationToken))
            .WithName("DescribeSetupDlnaServer")
            .WithSummary("The media server at a description URL.");

        guarded.MapGet("/dlna/browse", (string location, string? folderId, MediaServerClient client, CancellationToken cancellationToken) =>
                DlnaImportService.BrowseAsync(client, location, folderId, cancellationToken))
            .WithName("BrowseSetupDlnaServer")
            .WithSummary("A folder of a media server; 0 is the root.");

        guarded.MapPost("/complete", async (SetupRequest request, SetupService service, CancellationToken cancellationToken) =>
                await service.CompleteAsync(request, cancellationToken) switch
                {
                    SetupResult.Completed => Results.NoContent(),
                    SetupResult.Invalid invalid => invalid.Errors.ToResult(),
                    _ => Problems.Create(StatusCodes.Status409Conflict, ErrorCodes.SetupCompleted),
                })
            .WithName("CompleteSetup");

        return rest;
    }

    private static async ValueTask<object?> RequireSetupAccess(EndpointFilterInvocationContext invocation, EndpointFilterDelegate next)
    {
        var context = invocation.HttpContext;
        var services = context.RequestServices;

        if (services.GetRequiredService<SetupService>().IsCompleted)
            return Problems.Create(StatusCodes.Status409Conflict, ErrorCodes.SetupCompleted);

        if (!RequestOrigin.IsLocal(context)
            && !services.GetRequiredService<SetupCode>().Matches(context.Request.Headers[SetupCode.HeaderName]))
        {
            return Problems.Create(StatusCodes.Status403Forbidden, ErrorCodes.SetupCodeInvalid);
        }

        return await next(invocation);
    }
}

public static class SetupServiceCollectionExtensions
{
    public static IServiceCollection AddSetup(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<SetupOptions>(configuration.GetSection(SetupOptions.Section));
        services.AddSingleton<SetupCode>();
        services.AddSingleton<SetupService>();
        services.AddSingleton<ISettingsFileWriter, SettingsFileWriter>();
        services.AddSingleton<DirectoryBrowser>();
        services.AddHostedService<SetupAnnouncer>();
        return services;
    }

    public static IApplicationBuilder UseSetupGate(this IApplicationBuilder app) =>
        app.UseMiddleware<SetupGateMiddleware>();
}
