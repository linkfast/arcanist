using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Arcanist.Core.Api.Rest;
using Arcanist.Core.Data;
using Arcanist.Core.Data.Entities;
using Arcanist.Core.Hosting;
using Arcanist.Core.Setup;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Arcanist.Core.Tests;

/// <summary>Runs the server in-memory against a throwaway data directory.</summary>
public sealed class ArcanistFactory(
    IReadOnlyDictionary<string, string?>? settings = null,
    TimeProvider? time = null) : WebApplicationFactory<Program>
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

        builder.ConfigureServices(services =>
        {
            services.AddSingleton<IStartupFilter, RemoteIpStartupFilter>();
            if (time is not null)
                services.AddSingleton(time);
        });
    }

    /// <summary>A client whose requests look like they come from another device on the network.</summary>
    public HttpClient CreateRemoteClient(string ip = "192.0.2.10")
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add(RemoteIpHeader, ip);
        return client;
    }

    public const string AdminUserName = "admin";
    public const string Password = "correct horse battery";

    /// <summary>Completes setup with SQLite and an administrator (<see cref="AdminUserName"/>/<see cref="Password"/>).</summary>
    public async Task CompleteSetupAsync(params SetupLibrary[] libraries)
    {
        var response = await CreateClient().PostAsJsonAsync("/rest/v1/setup/complete", new SetupRequest(
            new SetupDatabase(DatabaseProvider.Sqlite, null),
            new SetupAdmin(AdminUserName, Password, "Administrator"),
            libraries,
            ShareAnonymousStatistics: false,
            Language: null), Json);

        response.EnsureSuccessStatusCode();
    }

    public async Task<User> CreateUserAsync(string userName, UserRole role = UserRole.User, Permissions permissions = Permissions.None)
    {
        await using var db = Services.GetRequiredService<IArcanistDbContextFactory>().CreateDbContext();
        var user = new User
        {
            UserName = userName,
            NormalizedUserName = TextNormalizer.Normalize(userName),
            Role = role,
            Permissions = permissions,
            PasswordHash = "",
        };
        user.PasswordHash = Services.GetRequiredService<IPasswordHasher<User>>().HashPassword(user, Password);
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    public async Task<TokenResponse> LoginAsync(string userName = AdminUserName, string password = Password, HttpClient? client = null)
    {
        var response = await (client ?? CreateClient()).PostAsJsonAsync("/rest/v1/auth/login", new LoginRequest(userName, password, "tests"), Json);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<TokenResponse>(Json))!;
    }

    public HttpClient CreateClient(string accessToken)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return client;
    }

    /// <summary>Logs in and returns a client that sends the access token.</summary>
    public async Task<HttpClient> CreateAuthenticatedClientAsync(string userName = AdminUserName) =>
        CreateClient((await LoginAsync(userName)).AccessToken);

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
