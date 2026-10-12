using System.Text.Json;
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
        // Process-wide, and tests build many hosts: added once.
        lock (GlobalSwitches.GlobalAttributes)
        {
            if (!GlobalSwitches.GlobalAttributes.OfType<RestEnumNamesAttribute>().Any())
                GlobalSwitches.GlobalAttributes.Add(new RestEnumNamesAttribute());
        }

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

/// <summary>
/// Spells enum values as the REST API does (System.Text.Json's upper snake case), so both APIs agree: GraphQL.NET's
/// own constant case splits digits off ("MP_3" where REST says "MP3").
/// </summary>
internal sealed class RestEnumNamesAttribute : GraphQLAttribute
{
    public override void Modify(EnumValueDefinition enumValueDefinition)
    {
        if (enumValueDefinition.Value is Enum value)
            enumValueDefinition.Name = JsonNamingPolicy.SnakeCaseUpper.ConvertName(value.ToString());
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
