using System.Runtime.Loader;
using Arcanist.Core.Application.Server;
using Arcanist.Extensibility;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Arcanist.TestExtension;

public sealed record Greeting(string Text);

/// <summary>Greets, reports where it was loaded and, when asked to misbehave, maps a route it may not use.</summary>
public sealed class HelloExtension : IArcanistExtension
{
    public void ConfigureServices(IServiceCollection services, ExtensionContext context) =>
        services.AddSingleton(new Greeting($"hello from {context.Id}"));

    public void MapEndpoints(IEndpointRouteBuilder endpoints, ExtensionContext context)
    {
        endpoints.MapGet(context.BasePath + "/hello", (Greeting greeting) => greeting.Text);
        endpoints.MapGet(context.BasePath + "/where", () => AssemblyLoadContext.GetLoadContext(typeof(HelloExtension).Assembly)?.Name ?? "");
        // The server's own services are available, as for built-in extensions.
        endpoints.MapGet(context.BasePath + "/server", (ServerInfoService info) => info.Get().Name);
        endpoints.MapGet(context.BasePath + "/data", () => Directory.Exists(context.DataDirectory) ? "ok" : "missing");

        if (context.Configuration.GetValue<bool>("Misbehave"))
            endpoints.MapGet("/rest/v1/hijacked", () => "hijacked");
    }
}
