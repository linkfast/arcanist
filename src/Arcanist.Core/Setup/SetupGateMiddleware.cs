using Arcanist.Core.Api;
using Arcanist.Core.Application;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Arcanist.Core.Setup;

/// <summary>
/// Until setup completes, only the wizard and what it needs are reachable: the browser is sent to the wizard
/// and API calls get <c>503 setup_required</c>.
/// </summary>
internal sealed class SetupGateMiddleware(RequestDelegate next, IOptionsMonitor<SetupOptions> options, IProblemDetailsService problems)
{
    public const string WizardPath = "/admin/setup";

    private static readonly PathString[] AllowedDuringSetup =
    [
        "/rest/v1/setup",
        "/rest/v1/server-info",
        "/rest/v1/legal",
        "/rest/v1/openapi.json",
        "/rest/docs",
        "/admin",
        "/health",
    ];

    public async Task InvokeAsync(HttpContext context)
    {
        if (options.CurrentValue.Completed || AllowedDuringSetup.Any(p => context.Request.Path.StartsWithSegments(p)))
        {
            await next(context);
            return;
        }

        if (context.Request.Path == "/" && (HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method)))
        {
            context.Response.Redirect(WizardPath);
            return;
        }

        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        await problems.WriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status503ServiceUnavailable,
                Title = "Setup required",
                Detail = $"Complete the setup wizard at {WizardPath}.",
                Extensions = { ["code"] = ErrorCodes.SetupRequired },
            },
        });
    }
}

/// <summary>Logs how to reach the wizard on startup while setup is pending.</summary>
internal sealed class SetupAnnouncer(IOptions<SetupOptions> options, SetupCode code, ILogger<SetupAnnouncer> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!options.Value.Completed)
        {
            logger.LogWarning(
                "Arcanist is not set up yet. Open {WizardPath} in a browser on this machine, or from another device using setup code {SetupCode}",
                SetupGateMiddleware.WizardPath, code.Value);
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
