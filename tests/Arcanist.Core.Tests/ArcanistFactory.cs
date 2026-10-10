using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Arcanist.Core.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Arcanist.Core.Tests;

/// <summary>Runs the server in-memory against a throwaway data directory.</summary>
public sealed class ArcanistFactory(IReadOnlyDictionary<string, string?>? settings = null) : WebApplicationFactory<Program>
{
    /// <summary>Requests carrying this header appear to come from that IP address instead of loopback.</summary>
    public const string RemoteIpHeader = "X-Test-Remote-Ip";

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public string DataPath { get; } =
        Path.Combine(Path.GetTempPath(), "arcanist-tests", Guid.NewGuid().ToString("N"));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting(DataDirectory.ConfigurationKey, DataPath);

        foreach (var (key, value) in settings ?? new Dictionary<string, string?>())
            builder.UseSetting(key, value);

        builder.ConfigureServices(services => services.AddSingleton<IStartupFilter, RemoteIpStartupFilter>());
    }

    /// <summary>A client whose requests look like they come from another device on the network.</summary>
    public HttpClient CreateRemoteClient(string ip = "192.0.2.10")
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add(RemoteIpHeader, ip);
        return client;
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();

        try
        {
            Directory.Delete(DataPath, recursive: true);
        }
        catch (IOException)
        {
            // A log or database file can still be held briefly on Windows; the OS temp cleanup will take it.
        }
    }

    private sealed class RemoteIpStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((context, nextMiddleware) =>
            {
                context.Connection.RemoteIpAddress =
                    context.Request.Headers.TryGetValue(RemoteIpHeader, out var ip) ? IPAddress.Parse(ip!) : IPAddress.Loopback;
                return nextMiddleware(context);
            });
            next(app);
        };
    }
}
