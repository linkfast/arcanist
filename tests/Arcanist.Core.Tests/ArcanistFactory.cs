using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Arcanist.Core.Analysis;
using Arcanist.Core.Api.Rest;
using Arcanist.Core.Application.Analysis;
using Arcanist.Core.Data;
using Arcanist.Core.Data.Entities;
using Arcanist.Core.Hosting;
using Arcanist.Core.Organization;
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
    TimeProvider? time = null,
    Action<IServiceCollection>? services = null) : WebApplicationFactory<Program>
{
    /// <summary>Requests carrying this header appear to come from that IP address instead of loopback.</summary>
    public const string RemoteIpHeader = "X-Test-Remote-Ip";

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseUpper) },
    };

    public string DataPath { get; } =
        Path.Combine(Path.GetTempPath(), "arcanist-tests", Guid.NewGuid().ToString("N"));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting(DataDirectory.ConfigurationKey, DataPath);
        // Background analysis would compete with the assertions of unrelated tests; its own tests turn it on.
        builder.UseSetting("Arcanist:Analysis:Enabled", "false");
        // No UDP multicast from tests: DLNA discovery is tested through its messages.
        builder.UseSetting("Arcanist:Dlna:Ssdp", "false");

        foreach (var (key, value) in settings ?? new Dictionary<string, string?>())
            builder.UseSetting(key, value);

        builder.ConfigureServices(collection =>
        {
            collection.AddSingleton<IStartupFilter, RemoteIpStartupFilter>();
            if (time is not null)
                collection.AddSingleton(time);
            services?.Invoke(collection);
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
            Language: null,
            // Five stars, which the tests rate with (servers start with a single star).
            RatingMode: Core.Data.Entities.RatingMode.Stars), Json);

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

    /// <summary>Waits until no scan is queued or running.</summary>
    public async Task WaitForScansAsync(HttpClient admin)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (true)
        {
            var jobs = await admin.GetFromJsonAsync<List<JsonElement>>("/rest/v1/scans?limit=100", Json);
            if (jobs!.All(j => j.GetProperty("status").GetString() is not ("QUEUED" or "RUNNING")))
                return;

            if (DateTime.UtcNow > deadline)
                throw new TimeoutException("Scans did not finish in time.");
            await Task.Delay(50);
        }
    }

    /// <summary>Waits until background analysis has nothing left to do (it must be enabled for this factory).</summary>
    public async Task<AnalysisProgress> WaitForAnalysisAsync()
    {
        var monitor = Services.GetRequiredService<AnalysisMonitor>();
        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (monitor.Current is not { Activity: AnalysisActivity.Idle, Pending: 0 })
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException($"Analysis did not finish: {monitor.Current}");
            monitor.RequestRun();
            await Task.Delay(100);
        }

        return monitor.Current;
    }

    /// <summary>
    /// Waits for a pass over the drop folders that started after this call (including the scan of what it imported),
    /// with no file still changing. Set <c>Arcanist:Organization:StableSeconds</c> to 0 so files settle at once.
    /// </summary>
    public async Task WaitForImportsAsync()
    {
        var monitor = Services.GetRequiredService<ImportMonitor>();
        var deadline = DateTime.UtcNow.AddSeconds(60);
        // The pass running now may have listed the folders before the test changed them.
        var started = monitor.Runs + 1;
        while (monitor.Runs <= started || monitor.Unsettled > 0)
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException("Drop folders were not processed in time.");
            monitor.RequestRun();
            await Task.Delay(50);
        }
    }

    /// <summary>Waits until the library has no organizing run in progress, and returns its latest run.</summary>
    public static async Task<JsonElement> WaitForOrganizeAsync(HttpClient admin, Guid libraryId)
    {
        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (true)
        {
            var runs = await admin.GetFromJsonAsync<List<JsonElement>>($"/rest/v1/libraries/{libraryId}/organize/runs", Json);
            if (runs!.Count > 0 && runs.All(r => r.GetProperty("status").GetString() is not ("RUNNING" or "UNDOING")))
                return runs[0];

            if (DateTime.UtcNow > deadline)
                throw new TimeoutException("Organizing did not finish in time.");
            await Task.Delay(50);
        }
    }

    /// <summary>Logs in and returns a client that sends the access token.</summary>
    public async Task<HttpClient> CreateAuthenticatedClientAsync(string userName = AdminUserName, string password = Password) =>
        CreateClient((await LoginAsync(userName, password)).AccessToken);

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

public static class GraphQLClientExtensions
{
    /// <summary>Posts a GraphQL operation and returns the whole response (<c>data</c> and <c>errors</c>).</summary>
    public static async Task<JsonElement> GraphQLAsync(this HttpClient client, string query, object? variables = null)
    {
        // GraphQL-over-HTTP may answer errors with a non-2xx status; the body still carries data and errors.
        var response = await client.PostAsJsonAsync("/graphql", new { query, variables }, ArcanistFactory.Json);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    /// <summary>The <c>code</c> extension of the first error, or null when the operation succeeded.</summary>
    public static string? ErrorCode(this JsonElement response) =>
        response.TryGetProperty("errors", out var errors) && errors.GetArrayLength() > 0
            ? errors[0].GetProperty("extensions").GetProperty("code").GetString()
            : null;
}
