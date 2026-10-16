using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Arcanist.Core.Backups;
using Arcanist.Core.Hosting;
using Arcanist.Core.Setup;
using Arcanist.Core.Tests.Scanning;

namespace Arcanist.Core.Tests.Backups;

/// <summary>A lost server comes back from its backup through the setup wizard: accounts, settings, data and music.</summary>
public sealed class ServerRestoreTests : IAsyncDisposable
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private readonly string _music = Directory.CreateTempSubdirectory("arcanist-server-restore-music-").FullName;
    private readonly string _destination = Directory.CreateTempSubdirectory("arcanist-server-restore-destination-").FullName;
    private readonly List<ArcanistFactory> _factories = [];

    private static async Task<JsonElement> PostAsync(HttpClient client, string path, object body, HttpStatusCode expected = HttpStatusCode.OK)
    {
        var response = await client.PostAsJsonAsync(path, body, ArcanistFactory.Json, Cancel);
        var text = await response.Content.ReadAsStringAsync(Cancel);
        Assert.True(response.StatusCode == expected, text);
        return text.Length == 0 ? default : JsonDocument.Parse(text).RootElement;
    }

    private ArcanistFactory Factory(string? dataPath = null, bool keepData = false)
    {
        var settings = new Dictionary<string, string?> { ["Arcanist:Hosting:StopAfterRestore"] = "false" };
        var factory = dataPath is null ? new ArcanistFactory(settings) { KeepData = keepData } : new ArcanistFactory(settings) { DataPath = dataPath, KeepData = keepData };
        _factories.Add(factory);
        return factory;
    }

    [Fact]
    public async Task The_wizard_brings_back_a_server_with_its_accounts_data_and_music()
    {
        AudioFixtures.WriteWav(Path.Combine(_music, "Band", "Pulse", "01 Alpha.wav"), 1, new FixtureTags("Alpha", "Band", "Pulse", TrackNumber: 1));
        AudioFixtures.WriteWav(Path.Combine(_music, "Band", "Pulse", "02 Beta.wav"), 2, new FixtureTags("Beta", "Band", "Pulse", TrackNumber: 2));

        // The server that will be lost: a rating, a playlist, a backup that carries it.
        var lost = Factory();
        await lost.CompleteSetupAsync(new SetupLibrary("Music", [_music]));
        var admin = await lost.CreateAuthenticatedClientAsync();
        await lost.WaitForScansAsync(admin);
        var tracks = (await admin.GraphQLAsync("query { tracks(first: 10) { items { id title } } }"))
            .GetProperty("data").GetProperty("tracks").GetProperty("items").EnumerateArray()
            .ToDictionary(t => t.GetProperty("title").GetString()!, t => t.GetProperty("id").GetGuid());
        await admin.GraphQLAsync("mutation($id: ID!) { rateTrack(trackId: $id, rating: 5) { id } }", new { id = tracks["Beta"] });
        await admin.GraphQLAsync("mutation($input: CreatePlaylistInput!) { createPlaylist(input: $input) { id } }",
            new { input = new { name = "Favourites", isPublic = false, trackIds = new[] { tracks["Beta"] } } });
        var destination = (await PostAsync(admin, "/rest/v1/backups/destinations", new { name = "Disk", kind = "FOLDER", path = _destination })).GetProperty("id").GetGuid();
        var backup = await PostAsync(admin, "/rest/v1/backups", new
        {
            name = "Server", destinationId = destination, scope = "SERVER", includeUserData = true, includeServer = true,
            password = "correct horse battery", schedule = "MANUAL", retentionDays = 30, enabled = true,
        });
        var run = await PostAsync(admin, $"/rest/v1/backups/{backup.GetProperty("id").GetGuid()}/run", new { });
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (run.GetProperty("finishedAt").ValueKind == JsonValueKind.Null)
        {
            Assert.True(DateTime.UtcNow < deadline, "The backup did not end.");
            await Task.Delay(50, Cancel);
            run = await admin.GetFromJsonAsync<JsonElement>($"/rest/v1/backups/runs/{run.GetProperty("id").GetGuid()}", ArcanistFactory.Json, Cancel);
        }

        Assert.Equal("COMPLETED", run.GetProperty("status").GetString());
        var snapshot = run.GetProperty("snapshot").GetString()!;
        var folder = backup.GetProperty("folder").GetString()!;
        await lost.DisposeAsync();
        _factories.Remove(lost);
        // A song is gone with the server.
        File.Delete(Path.Combine(_music, "Band", "Pulse", "01 Alpha.wav"));

        // A new installation, still in its setup wizard, finds the backup and checks the password.
        var fresh = Factory(keepData: true);
        var wizard = fresh.CreateClient();
        var source = new { kind = "FOLDER", path = _destination };
        var found = await PostAsync(wizard, "/rest/v1/setup/restore/browse", source);
        var stored = Assert.Single(found.EnumerateArray());
        Assert.Equal(folder, stored.GetProperty("folder").GetString());
        Assert.Equal(snapshot, Assert.Single(stored.GetProperty("snapshots").EnumerateArray()).GetProperty("name").GetString());
        Assert.False((await PostAsync(wizard, "/rest/v1/setup/restore/check", new { source, folder, snapshot, password = "wrong horse battery" })).GetProperty("success").GetBoolean());
        Assert.True((await PostAsync(wizard, "/rest/v1/setup/restore/check", new { source, folder, snapshot, password = "correct horse battery" })).GetProperty("success").GetBoolean());

        await PostAsync(wizard, "/rest/v1/setup/restore", new { source, folder, snapshot, password = "wrong horse battery", restoreFiles = true }, HttpStatusCode.Conflict);
        await PostAsync(wizard, "/rest/v1/setup/restore", new { source, folder, snapshot, password = "correct horse battery", restoreFiles = true }, HttpStatusCode.Accepted);
        var dataPath = fresh.DataPath;
        Assert.True(File.Exists(Path.Combine(PendingRestore.Folder(new ArcanistPaths(dataPath)), PendingRestore.MarkerFile)));
        await fresh.DisposeAsync();
        _factories.Remove(fresh);

        // It starts again (the supervisor's restart) as the server that was backed up.
        var restored = Factory(dataPath);
        var client = await restored.CreateAuthenticatedClientAsync();
        var beta = (await client.GraphQLAsync("query($id: ID!) { track(id: $id) { rating } }", new { id = tracks["Beta"] })).GetProperty("data").GetProperty("track");
        Assert.Equal(5, beta.GetProperty("rating").GetInt32());
        Assert.Equal("Favourites", Assert.Single((await client.GraphQLAsync("query { playlists { name } }")).GetProperty("data").GetProperty("playlists").EnumerateArray())
            .GetProperty("name").GetString());

        // And the lost song comes back into its library.
        deadline = DateTime.UtcNow.AddSeconds(30);
        while (!File.Exists(Path.Combine(_music, "Band", "Pulse", "01 Alpha.wav")))
        {
            Assert.True(DateTime.UtcNow < deadline, "The song did not come back.");
            await Task.Delay(100, Cancel);
        }

        var jobs = await client.GetFromJsonAsync<JsonElement>("/rest/v1/backups/restores", ArcanistFactory.Json, Cancel);
        // Both songs were looked at; the one still there was the same and stayed.
        Assert.Equal(2, Assert.Single(jobs.EnumerateArray()).GetProperty("filesTotal").GetInt32());
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var factory in _factories)
            await factory.DisposeAsync();
        foreach (var folder in new[] { _music, _destination })
        {
            try
            {
                Directory.Delete(folder, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }
}
