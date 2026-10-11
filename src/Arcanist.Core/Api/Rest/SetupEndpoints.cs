using Arcanist.Core.Application;
using Arcanist.Core.Auth;
using Arcanist.Core.Hosting;
using Arcanist.Core.Libraries;
using Arcanist.Core.Setup;

namespace Arcanist.Core.Api.Rest;

public static class SetupEndpoints
{
    public static IEndpointRouteBuilder MapSetupEndpoints(this IEndpointRouteBuilder rest)
    {
        var setup = rest.MapGroup("/setup").WithTags("Setup");

        setup.MapGet("/status", (HttpContext context, SetupService service) =>
                service.GetStatus(RequestOrigin.IsLocal(context)))
            .WithName("GetSetupStatus");

        // Everything else is only available while setup is pending, and from other devices only with the code.
        var guarded = setup.MapGroup("")
            .AddEndpointFilter(RequireSetupAccess)
            .RequireRateLimiting(AuthServiceCollectionExtensions.AuthRateLimit);

        guarded.MapPost("/database/test", (SetupDatabase request, SetupService service, CancellationToken cancellationToken) =>
                service.TestDatabaseAsync(request, cancellationToken))
            .WithName("TestSetupDatabase");

        guarded.MapGet("/directories", (string? path, DirectoryBrowser browser) =>
            {
                var (listing, error) = browser.List(path);
                return error switch
                {
                    null => Results.Ok(listing),
                    DirectoryBrowseError.NotFound => Problems.Create(StatusCodes.Status404NotFound, ErrorCodes.NotFound),
                    _ => Problems.Create(StatusCodes.Status403Forbidden, ErrorCodes.AccessDenied),
                };
            })
            .WithName("BrowseSetupDirectories")
            .Produces<DirectoryListing>();

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
