using System.Runtime.Loader;
using Arcanist.Core.Application.Libraries;
using Arcanist.Core.Application.Server;
using Arcanist.Core.Auth;
using Arcanist.Extensibility;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Arcanist.TestExtension;

public sealed record Greeting(string Text);

/// <summary>Greets, reports where it was loaded and, when asked to misbehave, maps a route it may not use.</summary>
public sealed class HelloExtension : IArcanistExtension
{
    public void ConfigureServices(IServiceCollection services, ExtensionContext context)
    {
        services.AddSingleton(new Greeting($"hello from {context.Id}"));
        services.AddKeyedSingleton<IExtensionAdminPage, HelloAdminPage>(context.Id);
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints, ExtensionContext context)
    {
        endpoints.MapGet(context.BasePath + "/hello", (Greeting greeting) => greeting.Text);
        endpoints.MapGet(context.BasePath + "/where", () => AssemblyLoadContext.GetLoadContext(typeof(HelloExtension).Assembly)?.Name ?? "");
        // The server's own services are available, as for built-in extensions.
        endpoints.MapGet(context.BasePath + "/server", (ServerInfoService info) => info.Get().Name);
        endpoints.MapGet(context.BasePath + "/data", () => Directory.Exists(context.DataDirectory) ? "ok" : "missing");
        endpoints.MapGet(context.BasePath + "/package", () =>
            context.PackageDirectory is { } package && File.Exists(Path.Combine(package, "arcanist-extension.json")) ? "ok" : "missing");

        // Puts the body into a library for a user, from background work (no request user), as an extension that
        // fetches files would.
        endpoints.MapPost(context.BasePath + "/import", async (Guid user, Guid library, string name, HttpRequest request,
            UserAccessCache users, IServiceScopeFactory scopes) =>
        {
            var body = new MemoryStream();
            await request.Body.CopyToAsync(body);
            body.Position = 0;
            return await Task.Run(async () =>
            {
                var access = await users.GetUserAsync(user, CancellationToken.None);
                if (access is null)
                    return Results.NotFound();
                await using var scope = scopes.CreateAsyncScope();
                var result = await scope.ServiceProvider.GetRequiredService<UploadService>()
                    .UploadAsync(access, library, name, body, context.Id, CancellationToken.None);
                return Results.Ok(result);
            });
        });

        if (context.Configuration.GetValue<bool>("Misbehave"))
            endpoints.MapGet("/rest/v1/hijacked", () => "hijacked");
    }
}

/// <summary>A settings page: a greeting to edit, a switch, and an action that fails on purpose when asked to.</summary>
public sealed class HelloAdminPage : IExtensionAdminPage
{
    private string _greeting = "hi";
    private bool _loud;

    public Task<AdminPage> GetAsync(AdminPageContext context, CancellationToken cancellationToken) =>
        Task.FromResult(new AdminPage([
            new AdminSection("main", context.Language == "es" ? "Saludo" : "Greeting", Blocks:
            [
                AdminBlock.Field("greeting", "Text", AdminFieldType.Text, _greeting),
                AdminBlock.Field("loud", "Loud", AdminFieldType.Switch, _loud),
                AdminBlock.Status("State", _loud ? "LOUD" : "quiet", _loud ? AdminTone.Warning : AdminTone.Good),
            ], Actions: [new AdminAction("shout", "Shout", AdminActionStyle.Primary), new AdminAction("break", "Break")]),
        ]));

    public Task<AdminPageResult> SaveAsync(AdminPageContext context, IReadOnlyDictionary<string, System.Text.Json.JsonElement> values,
        CancellationToken cancellationToken)
    {
        if (values.TryGetValue("greeting", out var greeting))
        {
            if (string.IsNullOrWhiteSpace(greeting.GetString()))
                return Task.FromResult(AdminPageResult.Failed("Say something", new Dictionary<string, string> { ["greeting"] = "required" }));
            _greeting = greeting.GetString()!;
        }

        if (values.TryGetValue("loud", out var loud))
            _loud = loud.GetBoolean();
        return Task.FromResult(AdminPageResult.Done("Saved"));
    }

    public Task<AdminPageResult> RunAsync(AdminPageContext context, string action, IReadOnlyDictionary<string, System.Text.Json.JsonElement> values,
        CancellationToken cancellationToken) =>
        action == "break" ? throw new InvalidOperationException("broken on purpose") : Task.FromResult(AdminPageResult.Done(_greeting.ToUpperInvariant()));
}
