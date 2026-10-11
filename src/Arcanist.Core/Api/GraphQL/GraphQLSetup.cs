using Arcanist.Core.Application;
using GraphQL;
using GraphQL.Execution;
using GraphQL.Types;
using GraphQL.Utilities;

namespace Arcanist.Core.Api.GraphQL;

public static class GraphQLSetup
{
    public const string Path = "/graphql";

    public static IServiceCollection AddArcanistGraphQL(this IServiceCollection services, IHostEnvironment environment)
    {
        services.AddGraphQL(graphql => graphql
            .AddAutoSchema<Query>(schema => schema.WithMutation<Mutation>().WithSubscription<Subscription>())
            .AddSystemTextJson()
            .AddWebSocketAuthentication<WebSocketAuthentication>()
            .AddErrorInfoProvider(new ArcanistErrorInfoProvider(environment.IsDevelopment())));

        return services;
    }

    public static IEndpointRouteBuilder MapArcanistGraphQL(this IEndpointRouteBuilder app)
    {
        // HTTP and WebSocket transports (graphql-transport-ws and the older graphql-ws subprotocols).
        app.MapGraphQL(Path);

        app.MapGet(Path + "/schema.graphql", (ISchema schema) =>
                Results.Text(schema.Print(new PrintOptions { IncludeDescriptions = true }), "text/plain; charset=utf-8"))
            .ExcludeFromDescription();

        app.MapGet(Path + "/ui", GraphiQL.Page).ExcludeFromDescription();
        return app;
    }
}

/// <summary>Turns expected application failures into GraphQL errors carrying the same codes as the REST API.</summary>
internal sealed class ArcanistErrorInfoProvider(bool exposeExceptionDetails)
    : ErrorInfoProvider(options => options.ExposeExceptionDetails = exposeExceptionDetails)
{
    public override ErrorInfo GetInfo(ExecutionError executionError)
    {
        var info = base.GetInfo(executionError);
        if (executionError.InnerException is not ArcanistException exception)
            return info;

        var extensions = new Dictionary<string, object?> { ["code"] = exception.Code };
        if (exception is ArcanistException.Invalid invalid)
            extensions["errors"] = invalid.Errors.ToDictionary();

        return new ErrorInfo { Message = exception.Code, Extensions = extensions };
    }
}
