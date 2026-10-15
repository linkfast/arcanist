using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Arcanist.Core.Application.Database;
using Arcanist.Core.Application.Playlists;
using Arcanist.Core.Data;
using Arcanist.Core.Data.Entities;
using Arcanist.Core.Setup;
using Arcanist.Core.Tests.Scanning;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Arcanist.Core.Tests.Data;

/// <summary>Moving a server from SQLite to PostgreSQL from the panel, with the server in maintenance meanwhile.</summary>
public sealed class MigrationTests(PostgresContainer postgres) : IClassFixture<PostgresContainer>, IAsyncDisposable
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private readonly string _music = Directory.CreateTempSubdirectory("arcanist-migration-").FullName;
    private readonly ArcanistFactory _factory = new();

    private void RequirePostgres() => Assert.SkipUnless(postgres.IsAvailable, $"PostgreSQL (Docker) is not available: {postgres.UnavailableReason}");

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode} {await response.Content.ReadAsStringAsync(Cancel)}");
        return await response.Content.ReadFromJsonAsync<JsonElement>(ArcanistFactory.Json, Cancel);
    }

    [Fact]
    public async Task A_SQLite_server_moves_to_PostgreSQL_with_its_data_and_keeps_working()
    {
        RequirePostgres();
        AudioFixtures.WriteWav(Path.Combine(_music, "Zed", "01.wav"), 1, new FixtureTags("Alone", "Zed", "Solo", "Zed", TrackNumber: 1));
        AudioFixtures.WriteWav(Path.Combine(_music, "Zed", "02.wav"), 2, new FixtureTags("Again", "Zed", "Solo", "Zed", TrackNumber: 2));
        await _factory.CompleteSetupAsync(new SetupLibrary("Music", [_music]));
        var admin = await _factory.CreateAuthenticatedClientAsync();
        await _factory.WaitForScansAsync(admin);
        var tracks = (await admin.GetFromJsonAsync<JsonElement>("/rest/v1/tracks", ArcanistFactory.Json, Cancel)).GetProperty("items")
            .EnumerateArray().ToDictionary(t => t.GetProperty("title").GetString()!, t => t.GetProperty("id").GetGuid());
        await ReadAsync(await admin.PutAsJsonAsync($"/rest/v1/tracks/{tracks["Alone"]}/rating", new { rating = 4 }, ArcanistFactory.Json, Cancel));
        await ReadAsync(await admin.PostAsJsonAsync("/rest/v1/playlists", new CreatePlaylistInput("Road", null, false, [tracks["Again"], tracks["Alone"]]),
            ArcanistFactory.Json, Cancel));
        var sqlite = Path.Combine(_factory.DataPath, "arcanist.db");

        var connection = postgres.ConnectionStringFor("migrated_" + Guid.NewGuid().ToString("N")[..8]);
        Assert.True((await ReadAsync(await admin.PostAsJsonAsync("/rest/v1/database/test", new { connectionString = connection }, ArcanistFactory.Json, Cancel)))
            .GetProperty("success").GetBoolean());
        var started = await ReadAsync(await admin.PostAsJsonAsync("/rest/v1/database/migrate", new { connectionString = connection }, ArcanistFactory.Json, Cancel));
        Assert.Equal("SQLITE", started.GetProperty("provider").GetString());
        await _factory.Services.GetRequiredService<DatabaseMigrator>().Running;

        var status = await ReadAsync(await admin.GetAsync("/rest/v1/database", Cancel));
        Assert.Equal("POSTGRES", status.GetProperty("provider").GetString());
        var migration = status.GetProperty("migration");
        Assert.Equal("COMPLETED", migration.GetProperty("phase").GetString());
        Assert.Equal(migration.GetProperty("rows").GetInt64(), migration.GetProperty("copied").GetInt64());
        var previous = status.GetProperty("previousSqlite");
        Assert.Equal((sqlite, true), (previous.GetProperty("path").GetString(), previous.GetProperty("exists").GetBoolean()));
        Assert.False((await ReadAsync(await _factory.CreateClient().GetAsync("/rest/v1/maintenance", Cancel))).GetProperty("active").GetBoolean());

        // The same session goes on, against PostgreSQL: catalog, ratings, playlists and search are all there.
        Assert.Equal(DatabaseProvider.Postgres, _factory.Services.GetRequiredService<IOptionsMonitor<DatabaseOptions>>().CurrentValue.Provider);
        var alone = (await admin.GetFromJsonAsync<JsonElement>($"/rest/v1/tracks/{tracks["Alone"]}", ArcanistFactory.Json, Cancel));
        Assert.Equal(4, alone.GetProperty("rating").GetInt32());
        var playlist = Assert.Single((await admin.GetFromJsonAsync<List<JsonElement>>("/rest/v1/playlists", ArcanistFactory.Json, Cancel))!);
        Assert.Equal(2, playlist.GetProperty("trackCount").GetInt32());
        var search = await admin.GetFromJsonAsync<JsonElement>("/rest/v1/search?query=alo", ArcanistFactory.Json, Cancel);
        Assert.Equal("Alone", Assert.Single(search.GetProperty("tracks").EnumerateArray()).GetProperty("title").GetString());
        // New writes go to PostgreSQL; signing in again works.
        await ReadAsync(await admin.PostAsJsonAsync("/rest/v1/playlists", new CreatePlaylistInput("After", null, false, null), ArcanistFactory.Json, Cancel));
        await _factory.LoginAsync();
        await using (var db = ArcanistDbContextFactory.Create(new DatabaseOptions { Provider = DatabaseProvider.Postgres, ConnectionString = connection },
                         _factory.Services.GetRequiredService<Arcanist.Core.Hosting.ArcanistPaths>()))
            Assert.Equal(2, await db.Playlists.CountAsync(Cancel));

        // The old file: the administrator decides.
        var resolved = await ReadAsync(await admin.PostAsJsonAsync("/rest/v1/database/previous-sqlite", new { action = "DELETE" }, ArcanistFactory.Json, Cancel));
        Assert.Equal(JsonValueKind.Null, resolved.GetProperty("previousSqlite").ValueKind);
        Assert.False(File.Exists(sqlite));

        // Once on PostgreSQL there is nothing more to migrate.
        var again = await admin.PostAsJsonAsync("/rest/v1/database/migrate", new { connectionString = connection }, ArcanistFactory.Json, Cancel);
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
    }

    [Fact]
    public async Task A_database_with_data_is_refused_and_nothing_changes()
    {
        RequirePostgres();
        await _factory.CompleteSetupAsync();
        var admin = await _factory.CreateAuthenticatedClientAsync();
        var connection = postgres.ConnectionStringFor("busy_" + Guid.NewGuid().ToString("N")[..8]);
        var target = new DatabaseOptions { Provider = DatabaseProvider.Postgres, ConnectionString = connection };
        await _factory.Services.GetRequiredService<DatabaseInitializer>().MigrateAsync(target, Cancel);
        await using (var db = ArcanistDbContextFactory.Create(target, _factory.Services.GetRequiredService<Arcanist.Core.Hosting.ArcanistPaths>()))
        {
            db.Users.Add(new User { UserName = "someone", NormalizedUserName = "someone", PasswordHash = "" });
            await db.SaveChangesAsync(Cancel);
        }

        var refused = await admin.PostAsJsonAsync("/rest/v1/database/migrate", new { connectionString = connection }, ArcanistFactory.Json, Cancel);
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Contains("database_not_empty", await refused.Content.ReadAsStringAsync(Cancel));
        Assert.Equal("SQLITE", (await ReadAsync(await admin.GetAsync("/rest/v1/database", Cancel))).GetProperty("provider").GetString());
    }

    [Fact]
    public async Task Maintenance_waits_for_running_work_and_holds_new_work_until_it_ends()
    {
        var maintenance = new DatabaseMaintenance();
        var running = await maintenance.WorkAsync(Cancel);

        // Work that does not finish in time: maintenance gives up and nothing waits.
        Assert.False(await maintenance.EnterAsync(TimeSpan.FromMilliseconds(100), Cancel));
        Assert.False(maintenance.IsActive);

        var entering = maintenance.EnterAsync(TimeSpan.FromSeconds(10), Cancel);
        Assert.True(maintenance.IsActive);
        Assert.False(maintenance.IsFrozen);
        var waiting = maintenance.WorkAsync(Cancel).AsTask();
        await Task.Delay(50, Cancel);
        Assert.False(waiting.IsCompleted);

        running.Dispose();
        Assert.True(await entering);
        Assert.True(maintenance.IsFrozen);
        Assert.False(waiting.IsCompleted);

        maintenance.Leave();
        (await waiting).Dispose();
        Assert.False(maintenance.IsActive);
    }

    [Fact]
    public async Task During_maintenance_the_API_answers_503_but_the_state_and_the_panel_do_not()
    {
        await _factory.CompleteSetupAsync();
        var admin = await _factory.CreateAuthenticatedClientAsync();
        var maintenance = _factory.Services.GetRequiredService<DatabaseMaintenance>();
        Assert.True(await maintenance.EnterAsync(TimeSpan.FromSeconds(10), Cancel));
        try
        {
            var refused = await admin.GetAsync("/rest/v1/libraries", Cancel);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, refused.StatusCode);
            Assert.Contains("maintenance", await refused.Content.ReadAsStringAsync(Cancel));
            var state = await ReadAsync(await _factory.CreateClient().GetAsync("/rest/v1/maintenance", Cancel));
            Assert.True(state.GetProperty("active").GetBoolean());
            Assert.Equal(HttpStatusCode.OK, (await _factory.CreateClient().GetAsync("/health", Cancel)).StatusCode);

            // And nothing can write, even from the background.
            await using var db = _factory.Services.GetRequiredService<IArcanistDbContextFactory>().CreateDbContext();
            db.Settings.Add(new Setting { Key = "test", Value = "1" });
            var refusedWrite = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(Cancel));
            Assert.IsType<Application.ArcanistException.Unavailable>(refusedWrite.InnerException);
        }
        finally
        {
            maintenance.Leave();
        }

        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/rest/v1/libraries", Cancel)).StatusCode);
    }

    public async ValueTask DisposeAsync()
    {
        await _factory.DisposeAsync();
        try
        {
            Directory.Delete(_music, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
