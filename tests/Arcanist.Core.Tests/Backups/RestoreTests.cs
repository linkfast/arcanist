using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Arcanist.Core.Setup;
using Arcanist.Core.Tests.Scanning;

namespace Arcanist.Core.Tests.Backups;

/// <summary>Restoring from backups: files without overwriting anything, and user data matched with the catalog.</summary>
public sealed class RestoreTests : IAsyncDisposable
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private readonly string _music = Directory.CreateTempSubdirectory("arcanist-restore-music-").FullName;
    private readonly string _destination = Directory.CreateTempSubdirectory("arcanist-restore-destination-").FullName;
    private readonly List<ArcanistFactory> _factories = [];
    private readonly List<string> _folders = [];

    private async Task<(ArcanistFactory Factory, HttpClient Admin)> StartAsync(string music)
    {
        var factory = new ArcanistFactory();
        _factories.Add(factory);
        await factory.CompleteSetupAsync(new SetupLibrary("Music", [music]));
        var admin = await factory.CreateAuthenticatedClientAsync();
        await factory.WaitForScansAsync(admin);
        return (factory, admin);
    }

    private void Fixtures()
    {
        AudioFixtures.WriteWav(Path.Combine(_music, "Band", "Pulse", "01 Alpha.wav"), 1, new FixtureTags("Alpha", "Band", "Pulse", TrackNumber: 1));
        AudioFixtures.WriteWav(Path.Combine(_music, "Band", "Pulse", "02 Beta.wav"), 2, new FixtureTags("Beta", "Band", "Pulse", TrackNumber: 2));
        AudioFixtures.WriteWav(Path.Combine(_music, "Other", "Solo", "01 Gamma.wav"), 3, new FixtureTags("Gamma", "Other", "Solo", TrackNumber: 1));
    }

    private static async Task<JsonElement> PostAsync(HttpClient admin, string path, object body, HttpStatusCode expected = HttpStatusCode.OK)
    {
        var response = await admin.PostAsJsonAsync(path, body, ArcanistFactory.Json, Cancel);
        var text = await response.Content.ReadAsStringAsync(Cancel);
        Assert.True(response.StatusCode == expected, text);
        return JsonDocument.Parse(text).RootElement;
    }

    private async Task<Guid> DestinationAsync(HttpClient admin) =>
        (await PostAsync(admin, "/rest/v1/backups/destinations", new { name = "Disk", kind = "FOLDER", path = _destination })).GetProperty("id").GetGuid();

    /// <summary>A server backup with its users' data, run once; returns its folder and snapshot.</summary>
    private static async Task<(string Folder, string Snapshot)> BackUpAsync(HttpClient admin, Guid destination)
    {
        var backup = await PostAsync(admin, "/rest/v1/backups", new
        {
            name = "Everything", destinationId = destination, scope = "SERVER", includeUserData = true, includeServer = false,
            schedule = "MANUAL", retentionDays = 30, enabled = true,
        });
        var run = await PostAsync(admin, $"/rest/v1/backups/{backup.GetProperty("id").GetGuid()}/run", new { });
        var id = run.GetProperty("id").GetGuid();
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (run.GetProperty("finishedAt").ValueKind == JsonValueKind.Null)
        {
            Assert.True(DateTime.UtcNow < deadline, $"The backup did not end: {run}");
            await Task.Delay(50, Cancel);
            run = await admin.GetFromJsonAsync<JsonElement>($"/rest/v1/backups/runs/{id}", ArcanistFactory.Json, Cancel);
        }

        Assert.Equal("COMPLETED", run.GetProperty("status").GetString());
        return (backup.GetProperty("folder").GetString()!, run.GetProperty("snapshot").GetString()!);
    }

    private static async Task<JsonElement> RestoreAsync(HttpClient admin, object input)
    {
        var job = await PostAsync(admin, "/rest/v1/backups/restores", input);
        var id = job.GetProperty("id").GetGuid();
        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (job.GetProperty("finishedAt").ValueKind == JsonValueKind.Null)
        {
            Assert.True(DateTime.UtcNow < deadline, $"The restore did not end: {job}");
            await Task.Delay(100, Cancel);
            job = await admin.GetFromJsonAsync<JsonElement>($"/rest/v1/backups/restores/{id}", ArcanistFactory.Json, Cancel);
        }

        return job;
    }

    private static async Task<Dictionary<string, Guid>> TracksAsync(HttpClient admin) =>
        (await admin.GraphQLAsync("query { tracks(first: 50) { items { id title } } }"))
        .GetProperty("data").GetProperty("tracks").GetProperty("items").EnumerateArray()
        .ToDictionary(t => t.GetProperty("title").GetString()!, t => t.GetProperty("id").GetGuid());

    [Fact]
    public async Task Files_come_back_without_overwriting_what_is_there()
    {
        Fixtures();
        var (_, admin) = await StartAsync(_music);
        var destination = await DestinationAsync(admin);
        var (folder, snapshot) = await BackUpAsync(admin, destination);

        var stored = await admin.GetFromJsonAsync<JsonElement>($"/rest/v1/backups/destinations/{destination}/stored", ArcanistFactory.Json, Cancel);
        var backup = Assert.Single(stored.EnumerateArray());
        Assert.Equal(folder, backup.GetProperty("folder").GetString());
        Assert.NotEqual(JsonValueKind.Null, backup.GetProperty("libraries")[0].GetProperty("sourceId").ValueKind);

        // One file is lost, another changed since.
        var alpha = Path.Combine(_music, "Band", "Pulse", "01 Alpha.wav");
        var beta = Path.Combine(_music, "Band", "Pulse", "02 Beta.wav");
        var backedUpBeta = File.ReadAllBytes(beta);
        File.Delete(alpha);
        AudioFixtures.WriteWav(beta, 8, new FixtureTags("Beta", "Band", "Pulse", TrackNumber: 2));
        var changedBeta = File.ReadAllBytes(beta);

        // The album, back where it came from.
        var contents = await admin.GetFromJsonAsync<JsonElement>($"/rest/v1/backups/destinations/{destination}/stored/{folder}/{snapshot}", ArcanistFactory.Json, Cancel);
        var pulse = contents.GetProperty("albums").EnumerateArray().Single(a => a.GetProperty("title").GetString() == "Pulse");
        var job = await RestoreAsync(admin, new
        {
            destinationId = destination, folder, snapshot, files = true, userData = false,
            paths = pulse.GetProperty("paths").EnumerateArray().Select(p => p.GetString()).ToArray(),
        });
        Assert.Equal("COMPLETED", job.GetProperty("status").GetString());
        Assert.Equal((2, 2, 1), (job.GetProperty("filesTotal").GetInt32(), job.GetProperty("restored").GetInt32(), job.GetProperty("renamed").GetInt32()));

        Assert.True(File.Exists(alpha));
        // The changed file stays; the backup's comes beside it.
        Assert.Equal(changedBeta, File.ReadAllBytes(beta));
        var restoredBeta = Assert.Single(Directory.GetFiles(Path.GetDirectoryName(beta)!, "02 Beta (restored *).wav"));
        Assert.Equal(backedUpBeta, File.ReadAllBytes(restoredBeta));

        // Again: everything is there and the same.
        var again = await RestoreAsync(admin, new { destinationId = destination, folder, snapshot, files = true, userData = false, paths = new[] { "Music/Band/Pulse/01 Alpha.wav" } });
        Assert.Equal((0, 1), (again.GetProperty("restored").GetInt32(), again.GetProperty("identical").GetInt32()));
    }

    [Fact]
    public async Task Another_server_gets_the_music_and_its_users_data_once()
    {
        Fixtures();
        var (_, owner) = await StartAsync(_music);
        var tracks = await TracksAsync(owner);
        await owner.GraphQLAsync("mutation($id: ID!) { rateTrack(trackId: $id, rating: 4) { id } }", new { id = tracks["Alpha"] });
        await owner.GraphQLAsync("mutation($input: CreatePlaylistInput!) { createPlaylist(input: $input) { id } }",
            new { input = new { name = "Road trip", isPublic = false, trackIds = new[] { tracks["Gamma"], tracks["Alpha"] } } });
        var (folder, snapshot) = await BackUpAsync(owner, await DestinationAsync(owner));

        // A new server, with an empty library, reads the same destination.
        var empty = Directory.CreateTempSubdirectory("arcanist-restore-new-").FullName;
        _folders.Add(empty);
        var (_, admin) = await StartAsync(empty);
        var destination = await DestinationAsync(admin);
        var libraries = await admin.GetFromJsonAsync<JsonElement>("/rest/v1/libraries", ArcanistFactory.Json, Cancel);
        var source = libraries[0].GetProperty("sources")[0].GetProperty("id").GetGuid();
        var contents = await admin.GetFromJsonAsync<JsonElement>($"/rest/v1/backups/destinations/{destination}/stored/{folder}/{snapshot}", ArcanistFactory.Json, Cancel);
        var user = Assert.Single(contents.GetProperty("users").EnumerateArray());
        Assert.Equal((1, 1), (user.GetProperty("ratings").GetInt32(), user.GetProperty("playlists").GetInt32()));
        Assert.NotEqual(JsonValueKind.Null, user.GetProperty("userId").ValueKind);

        var job = await RestoreAsync(admin, new { destinationId = destination, folder, snapshot, files = true, userData = true, targetSourceId = source });
        Assert.Equal("COMPLETED", job.GetProperty("status").GetString());
        Assert.Equal((3, 1, 1, 0), (job.GetProperty("restored").GetInt32(), job.GetProperty("ratings").GetInt32(), job.GetProperty("playlists").GetInt32(),
            job.GetProperty("unmatched").GetInt32()));
        Assert.True(File.Exists(Path.Combine(empty, "Band", "Pulse", "01 Alpha.wav")));

        var restored = await TracksAsync(admin);
        var alpha = await admin.GraphQLAsync("query($id: ID!) { track(id: $id) { rating } }", new { id = restored["Alpha"] });
        Assert.Equal(4, alpha.GetProperty("data").GetProperty("track").GetProperty("rating").GetInt32());
        var playlists = (await admin.GraphQLAsync("query { playlists { name trackCount } }")).GetProperty("data").GetProperty("playlists");
        var playlist = Assert.Single(playlists.EnumerateArray());
        Assert.Equal(("Road trip", 2), (playlist.GetProperty("name").GetString(), playlist.GetProperty("trackCount").GetInt32()));

        // Restoring twice adds nothing.
        var again = await RestoreAsync(admin, new { destinationId = destination, folder, snapshot, files = false, userData = true });
        Assert.Equal((0, 0), (again.GetProperty("ratings").GetInt32(), again.GetProperty("playlists").GetInt32()));
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var factory in _factories)
            await factory.DisposeAsync();
        foreach (var folder in _folders.Append(_music).Append(_destination))
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
