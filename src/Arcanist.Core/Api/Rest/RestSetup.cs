using Arcanist.Core.Application;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.OpenApi;
using Scalar.AspNetCore;

namespace Arcanist.Core.Api.Rest;

public static class RestSetup
{
    public const string Prefix = "/rest/v1";
    public const string DocumentName = "v1";

    public static IServiceCollection AddArcanistRest(this IServiceCollection services)
    {
        services.AddExceptionHandler<ArcanistExceptionHandler>();
        services.AddOpenApi(DocumentName, options =>
        {
            options.AddDocumentTransformer((document, _, _) =>
            {
                document.Info = new OpenApiInfo
                {
                    Title = "Arcanist REST API",
                    Version = "1",
                    Description = "REST equivalent of the Arcanist GraphQL API (/graphql), without subscriptions. " +
                                  "Authenticate with POST /rest/v1/auth/login and send the access token as a Bearer token.",
                    License = new OpenApiLicense { Name = "AGPL-3.0-or-later", Identifier = "AGPL-3.0-or-later" },
                };

                // Relative to whichever address the client used to reach the server.
                document.Servers = [new OpenApiServer { Url = "/" }];

                document.Components ??= new OpenApiComponents();
                document.Components.SecuritySchemes = new Dictionary<string, IOpenApiSecurityScheme>
                {
                    ["bearer"] = new OpenApiSecurityScheme
                    {
                        Type = SecuritySchemeType.Http,
                        Scheme = "bearer",
                        BearerFormat = "JWT",
                    },
                };
                document.Security = [new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference("bearer", document)] = [] }];
                return Task.CompletedTask;
            });
        });

        return services;
    }

    public static IEndpointRouteBuilder MapArcanistRest(this IEndpointRouteBuilder app)
    {
        app.MapOpenApi(Prefix + "/openapi.json");
        app.MapScalarApiReference("/rest/docs", options => options
            .WithTitle("Arcanist REST API")
            .WithOpenApiRoutePattern(Prefix + "/openapi.json"));

        var rest = app.MapGroup(Prefix);
        rest.MapSetupEndpoints();
        rest.MapAuthEndpoints();
        rest.MapUserEndpoints();
        rest.MapLibraryEndpoints();
        rest.MapScanEndpoints();
        rest.MapAnalysisEndpoints();
        rest.MapFfmpegEndpoints();
        rest.MapDuplicateEndpoints();
        rest.MapOrganizationEndpoints();
        rest.MapOnlineEndpoints();
        rest.MapCatalogEndpoints();
        rest.MapMediaEndpoints();
        return app;
    }
}

/// <summary>Turns expected application failures into problem details with their status and code.</summary>
internal sealed class ArcanistExceptionHandler(IProblemDetailsService problems) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not ArcanistException error)
            return false;

        ProblemDetails details = error is ArcanistException.Invalid invalid
            ? new ValidationProblemDetails(invalid.Errors.ToDictionary())
            : new ProblemDetails();

        details.Status = error.StatusCode;
        details.Extensions["code"] = error.Code;

        context.Response.StatusCode = error.StatusCode;
        return await problems.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = details,
            Exception = exception,
        });
    }
}
