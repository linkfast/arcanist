using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Arcanist.Core.Data;
using Arcanist.Core.Data.Entities;
using Arcanist.Core.Setup;
using Arcanist.Core.Tests.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Arcanist.Core.Tests.Setup;

public sealed class SetupTests : IAsyncLifetime
{
    private readonly ArcanistFactory _factory = new();
    private readonly string _musicFolder = Directory.CreateTempSubdirectory("arcanist-music").FullName;

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Servers_start_rating_with_a_single_star()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/rest/v1/setup/complete", new SetupRequest(
            new SetupDatabase(DatabaseProvider.Sqlite, null),
            new SetupAdmin("admin", "correct horse battery", null),
            [new SetupLibrary("Music", [_musicFolder])],
            ShareAnonymousStatistics: false,
            Language: null), ArcanistFactory.Json, TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        var info = await client.GetFromJsonAsync<System.Text.Json.JsonElement>("/rest/v1/server-info", ArcanistFactory.Json, TestContext.Current.CancellationToken);
        Assert.Equal("STAR", info.GetProperty("ratingMode").GetString());
    }

    [Fact]
    public async Task The_wizard_s_privacy_limits_what_else_it_asks_for()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/rest/v1/setup/complete", new SetupRequest(
            new SetupDatabase(DatabaseProvider.Sqlite, null),
            new SetupAdmin("admin", "correct horse battery", null),
            [new SetupLibrary("Music", [_musicFolder])],
            ShareAnonymousStatistics: true,
            Language: null,
            OnlineServices: new Core.Application.Online.OnlineServicesSettings(true, true, true, true),
            Privacy: new Core.Application.Server.SetPrivacyInput(true, Core.Application.Server.ExternalServices.None)),
            ArcanistFactory.Json, TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();

        // No statistics, no online services, and Ultimate Cloud's extension off.
        var settings = _factory.Services.GetRequiredService<Core.Data.SettingsStore>();
        Assert.False(await settings.GetAsync(Core.Data.SettingKeys.ShareAnonymousStatistics, true, TestContext.Current.CancellationToken));
        Assert.Equal(Core.Application.Online.OnlineServicesSettings.None,
            await _factory.Services.GetRequiredService<Core.Online.OnlineSettings>().GetAsync(TestContext.Current.CancellationToken));
        Assert.False(_factory.Services.GetRequiredService<Core.Extensions.ExtensionHost>().IsEnabled(Core.Extensions.UltimateCloud.UltimateCloudExtension.Id));
        Assert.Equal(new Core.Application.Server.Privacy(true, Core.Application.Server.ExternalServices.None),
            await _factory.Services.GetRequiredService<Core.Application.Server.PrivacySettings>().GetAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Before_setup_the_browser_is_sent_to_the_wizard_and_apis_are_unavailable()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var root = await client.GetAsync("/", Cancel);
        Assert.Equal(HttpStatusCode.Redirect, root.StatusCode);
        Assert.Equal("/admin/setup", root.Headers.Location?.OriginalString);

        var api = await client.GetAsync("/rest/v1/libraries", Cancel);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, api.StatusCode);
        Assert.Equal("setup_required", (await ReadProblem(api)).GetProperty("code").GetString());

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health", Cancel)).StatusCode);
    }

    [Fact]
    public async Task Status_requires_a_code_only_for_other_devices()
    {
        var local = await _factory.CreateClient().GetFromJsonAsync<SetupStatus>("/rest/v1/setup/status", ArcanistFactory.Json, Cancel);
        var remote = await _factory.CreateRemoteClient().GetFromJsonAsync<SetupStatus>("/rest/v1/setup/status", ArcanistFactory.Json, Cancel);

        Assert.False(local!.Completed);
        Assert.False(local.RequiresCode);
        Assert.False(local.Database.Configured);
        Assert.True(remote!.RequiresCode);
    }

    [Fact]
    public async Task Requests_relayed_by_a_local_reverse_proxy_count_as_remote()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Forwarded-For", "198.51.100.7");

        var response = await client.PostAsJsonAsync("/rest/v1/setup/complete", ValidRequest(), ArcanistFactory.Json, Cancel);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Other_devices_need_the_setup_code()
    {
        var client = _factory.CreateRemoteClient();

        var withoutCode = await client.PostAsJsonAsync("/rest/v1/setup/complete", ValidRequest(), ArcanistFactory.Json, Cancel);
        Assert.Equal(HttpStatusCode.Forbidden, withoutCode.StatusCode);
        Assert.Equal("setup_code_invalid", (await ReadProblem(withoutCode)).GetProperty("code").GetString());

        var code = _factory.Services.GetRequiredService<SetupCode>().Value;
        client.DefaultRequestHeaders.Add(SetupCode.HeaderName, code.ToLowerInvariant().Replace("-", ""));

        var withCode = await client.PostAsJsonAsync("/rest/v1/setup/complete", ValidRequest(), ArcanistFactory.Json, Cancel);
        Assert.Equal(HttpStatusCode.NoContent, withCode.StatusCode);
    }

    [Fact]
    public async Task Invalid_requests_return_error_codes_per_field()
    {
        var request = new SetupRequest(
            Database: null,
            Admin: new SetupAdmin("x", "short", null),
            Libraries:
            [
                new SetupLibrary("Music", [Path.Combine(_musicFolder, "missing")]),
                new SetupLibrary("music", []),
                new SetupLibrary("Two", [_musicFolder, _musicFolder + "2"]),
            ],
            ShareAnonymousStatistics: null,
            Language: null);

        var response = await _factory.CreateClient().PostAsJsonAsync("/rest/v1/setup/complete", request, ArcanistFactory.Json, Cancel);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = (await ReadProblem(response)).GetProperty("errors");
        AssertError(errors, "database", "required");
        AssertError(errors, "admin.userName", "too_short");
        AssertError(errors, "admin.password", "too_short");
        AssertError(errors, "libraries[0].folders[0]", "not_found");
        AssertError(errors, "libraries[1].name", "duplicate");
        AssertError(errors, "libraries[1].folders", "required");
        AssertError(errors, "libraries[2].folders", "too_many");
        AssertError(errors, "shareAnonymousStatistics", "required");
    }

    [Fact]
    public async Task Completing_setup_with_sqlite_creates_the_admin_libraries_and_settings()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.PostAsJsonAsync("/rest/v1/setup/complete", ValidRequest(), ArcanistFactory.Json, Cancel);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var status = await client.GetFromJsonAsync<SetupStatus>("/rest/v1/setup/status", ArcanistFactory.Json, Cancel);
        Assert.True(status!.Completed);
        Assert.Equal(DatabaseProvider.Sqlite, status.Database.Provider);
        Assert.True(File.Exists(Path.Combine(_factory.DataPath, "arcanist.db")));
        Assert.False(File.Exists(Path.Combine(_factory.DataPath, SetupCode.FileName)));

        await using var db = _factory.Services.GetRequiredService<IArcanistDbContextFactory>().CreateDbContext();
        var admin = await db.Users.SingleAsync(Cancel);
        Assert.Equal(UserRole.Admin, admin.Role);
        Assert.Equal("Admin", admin.UserName);
        Assert.Equal("admin", admin.NormalizedUserName);
        var hasher = _factory.Services.GetRequiredService<IPasswordHasher<User>>();
        Assert.Equal(PasswordVerificationResult.Success, hasher.VerifyHashedPassword(admin, admin.PasswordHash, "correct horse battery"));

        var library = await db.Libraries.Include(l => l.Sources).SingleAsync(Cancel);
        Assert.Equal("Music", library.Name);
        Assert.True(library.VisibleToAll);
        Assert.Equal(Path.GetFullPath(_musicFolder), Assert.Single(library.Sources).Location);

        var statistics = await db.Settings.SingleAsync(s => s.Key == SettingKeys.ShareAnonymousStatistics, Cancel);
        Assert.Equal("false", statistics.Value);

        // The app is open now, and the wizard cannot run twice.
        Assert.NotEqual(HttpStatusCode.Redirect, (await client.GetAsync("/", Cancel)).StatusCode);
        var again = await client.PostAsJsonAsync("/rest/v1/setup/complete", ValidRequest(), ArcanistFactory.Json, Cancel);
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
    }

    [Fact]
    public async Task The_wizard_can_browse_server_folders()
    {
        Directory.CreateDirectory(Path.Combine(_musicFolder, "Albums"));

        var listing = await _factory.CreateClient().GetFromJsonAsync<JsonElement>(
            $"/rest/v1/setup/directories?path={Uri.EscapeDataString(_musicFolder)}", ArcanistFactory.Json, Cancel);

        Assert.Contains(listing.GetProperty("directories").EnumerateArray(), d => d.GetProperty("name").GetString() == "Albums");
    }

    [Fact]
    public async Task The_wizard_can_create_folders_but_other_devices_need_the_code()
    {
        var input = new { parent = _musicFolder, name = "Singles" };

        var remote = await _factory.CreateRemoteClient().PostAsJsonAsync("/rest/v1/setup/directories", input, ArcanistFactory.Json, Cancel);
        Assert.Equal(HttpStatusCode.Forbidden, remote.StatusCode);
        Assert.False(Directory.Exists(Path.Combine(_musicFolder, "Singles")));

        var response = await _factory.CreateClient().PostAsJsonAsync("/rest/v1/setup/directories", input, ArcanistFactory.Json, Cancel);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.True(Directory.Exists(Path.Combine(_musicFolder, "Singles")));
    }

    [Fact]
    public async Task A_database_set_by_the_environment_is_used_without_asking()
    {
        await using var factory = new ArcanistFactory(new Dictionary<string, string?> { ["Arcanist:Database:Provider"] = "Sqlite" });
        var client = factory.CreateClient();

        var status = await client.GetFromJsonAsync<SetupStatus>("/rest/v1/setup/status", ArcanistFactory.Json, Cancel);
        Assert.True(status!.Database.LockedByEnvironment);

        var response = await client.PostAsJsonAsync("/rest/v1/setup/complete", ValidRequest() with { Database = null }, ArcanistFactory.Json, Cancel);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    private SetupRequest ValidRequest(SetupDatabase? database = null) => new(
        database ?? new SetupDatabase(DatabaseProvider.Sqlite, null),
        new SetupAdmin("Admin", "correct horse battery", "The Admin"),
        [new SetupLibrary("Music", [_musicFolder])],
        ShareAnonymousStatistics: false,
        Language: "es");

    private static async Task<JsonElement> ReadProblem(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>(Cancel);

    private static void AssertError(JsonElement errors, string field, string code)
    {
        Assert.True(errors.TryGetProperty(field, out var codes), $"No errors for '{field}'.");
        Assert.Contains(code, codes.EnumerateArray().Select(c => c.GetString()));
    }

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        await _factory.DisposeAsync();
        Directory.Delete(_musicFolder, recursive: true);
    }
}

public sealed class PostgresSetupTests(PostgresContainer postgres) : IClassFixture<PostgresContainer>, IAsyncLifetime
{
    private readonly ArcanistFactory _factory = new();
    private readonly string _musicFolder = Directory.CreateTempSubdirectory("arcanist-music").FullName;

    [Fact]
    public async Task Completing_setup_with_postgres_creates_and_migrates_the_database()
    {
        Assert.SkipUnless(postgres.IsAvailable, $"PostgreSQL tests need Docker: {postgres.UnavailableReason}");
        var connectionString = postgres.ConnectionStringFor($"arcanist_{Guid.NewGuid():N}");
        var client = _factory.CreateClient();

        var test = await client.PostAsJsonAsync("/rest/v1/setup/database/test",
            new SetupDatabase(DatabaseProvider.Postgres, connectionString), ArcanistFactory.Json, TestContext.Current.CancellationToken);
        Assert.True((await test.Content.ReadFromJsonAsync<DatabaseTestResult>(ArcanistFactory.Json, TestContext.Current.CancellationToken))!.Success);

        var response = await client.PostAsJsonAsync("/rest/v1/setup/complete", new SetupRequest(
            new SetupDatabase(DatabaseProvider.Postgres, connectionString),
            new SetupAdmin("admin", "correct horse battery", null),
            [new SetupLibrary("Music", [_musicFolder])],
            ShareAnonymousStatistics: true,
            Language: null), ArcanistFactory.Json, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        await using var db = _factory.Services.GetRequiredService<IArcanistDbContextFactory>().CreateDbContext();
        Assert.IsType<PostgresArcanistDbContext>(db);
        Assert.Equal("admin", (await db.Users.SingleAsync(TestContext.Current.CancellationToken)).UserName);
    }

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        await _factory.DisposeAsync();
        Directory.Delete(_musicFolder, recursive: true);
    }
}
