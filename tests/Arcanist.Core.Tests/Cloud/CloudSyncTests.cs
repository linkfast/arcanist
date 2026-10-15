using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Arcanist.Core.Cloud;
using Arcanist.Core.Setup;
using Arcanist.Core.Tests.Scanning;
using Microsoft.Extensions.DependencyInjection;

namespace Arcanist.Core.Tests.Cloud;

/// <summary>
/// Stands in for ultimate-audio: the "audio hash" is the SHA-1 of the whole file, stripping copies it, and tagging
/// writes the tags with ATL. Enough to follow what a sync does with each file.
/// </summary>
public sealed class FakeUltimateAudio : IUltimateAudio
{
    public bool IsInstalled { get; set; } = true;

    public Task<string> HashAsync(string path, CancellationToken cancellationToken) =>
        Task.FromResult(Convert.ToHexStringLower(SHA1.HashData(File.ReadAllBytes(path))));

    public async Task<(string AudioHash, string Sha1, long Size)> StripAsync(string path, string output, CancellationToken cancellationToken)
    {
        File.Copy(path, output, overwrite: true);
        var hash = await HashAsync(path, cancellationToken);
        return (hash, Convert.ToHexStringLower(SHA1.HashData(File.ReadAllBytes(output))), new FileInfo(output).Length);
    }

    public Task TagAsync(string path, UltimateAudioMetadata metadata, CancellationToken cancellationToken)
    {
        var track = new ATL.Track(path) { Title = metadata.Title, Artist = metadata.Artist, Album = metadata.Album, TrackNumber = metadata.TrackNumber };
        track.Save();
        return Task.CompletedTask;
    }
}

public sealed class CloudSyncTests : IAsyncDisposable
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private const string Syncs = "query { cloudSyncs { libraryId mode lastRunAt requested running { phase } matched uploaded downloaded localOnly failed errorCode failures { title reason } } }";

    private readonly FakeUltimateCloud _cloud = new();
    private readonly FakeUltimateAudio _audio = new();
    private readonly string _music = Directory.CreateTempSubdirectory("arcanist-sync-").FullName;
    private readonly string _staging = Directory.CreateTempSubdirectory("arcanist-sync-staging-").FullName;
    private ArcanistFactory _factory = null!;
    private HttpClient _admin = null!;
    private string _libraryId = "";

    private async Task StartAsync(string plan = "Cloud")
    {
        // The library: Alpha (also in the account) and Beta (only here).
        var alpha = AudioFixtures.WriteWav(Path.Combine(_music, "Band", "Alpha.wav"), 1, new FixtureTags("Alpha", "Band", "Pulse", "Band", TrackNumber: 1));
        AudioFixtures.WriteWav(Path.Combine(_music, "Band", "Beta.wav"), 2, new FixtureTags("Beta", "Band", "Pulse", "Band", TrackNumber: 2));
        // The account: Alpha (the same file), and Gamma (only there, without tags: Ultimate Cloud keeps none).
        _cloud.AddTrack("c-alpha", "Alpha", "Band", "Pulse", 1, File.ReadAllBytes(alpha), format: "WAV");
        _cloud.AddTrack("c-gamma", "Gamma", "Echo", "Deep", 3, File.ReadAllBytes(AudioFixtures.WriteWav(Path.Combine(_staging, "gamma.wav"), 3, new FixtureTags())), format: "WAV");
        _cloud.Plan = plan;

        _factory = new ArcanistFactory(new Dictionary<string, string?>
        {
            ["Arcanist:UltimateCloud:ApiUrl"] = FakeUltimateCloud.ApiUrl,
            ["Arcanist:UltimateCloud:Authority"] = FakeUltimateCloud.Authority,
        }, services: services =>
        {
            _cloud.Register(services);
            services.AddSingleton<IUltimateAudio>(_audio);
        });
        await _factory.CompleteSetupAsync(new SetupLibrary("Music", [_music]));
        _admin = await _factory.CreateAuthenticatedClientAsync();
        await _factory.WaitForScansAsync(_admin);
        _libraryId = (await _admin.GraphQLAsync("query { libraries { id } }")).GetProperty("data").GetProperty("libraries")[0].GetProperty("id").GetString()!;

        await _admin.GraphQLAsync("mutation { startUltimateCloudLink { state } }");
        _cloud.Approve = true;
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while ((await _admin.GraphQLAsync("query { ultimateCloud { plan { name } } }")).GetProperty("data").GetProperty("ultimateCloud").GetProperty("plan").ValueKind == JsonValueKind.Null)
        {
            Assert.True(DateTime.UtcNow < deadline, "The account was not linked.");
            await Task.Delay(50, Cancel);
        }
    }

    private async Task<JsonElement> SetAsync(string mode, string? libraryId = null) =>
        await _admin.GraphQLAsync("mutation($id: ID!, $m: CloudSyncMode!) { setLibraryCloudSync(libraryId: $id, input: {mode: $m}) { mode } }",
            new { id = libraryId ?? _libraryId, m = mode });

    /// <summary>Waits for the run asked for to finish.</summary>
    private async Task<JsonElement> WaitForSyncAsync()
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (true)
        {
            var sync = (await _admin.GraphQLAsync(Syncs)).GetProperty("data").GetProperty("cloudSyncs").EnumerateArray()
                .Single(s => s.GetProperty("libraryId").GetString() == _libraryId);
            if (!sync.GetProperty("requested").GetBoolean() && sync.GetProperty("running").ValueKind == JsonValueKind.Null
                                                          && sync.GetProperty("lastRunAt").ValueKind != JsonValueKind.Null)
                return sync;
            if (DateTime.UtcNow > deadline)
                Assert.Fail($"Still {sync}");
            await Task.Delay(50, Cancel);
        }
    }

    [Fact]
    public async Task Both_ways_pairs_what_is_on_both_sides_uploads_and_downloads_the_rest()
    {
        await StartAsync();
        Assert.Equal("BOTH", (await SetAsync("BOTH")).GetProperty("data").GetProperty("setLibraryCloudSync").GetProperty("mode").GetString());

        var sync = await WaitForSyncAsync();
        Assert.Equal(JsonValueKind.Null, sync.GetProperty("errorCode").ValueKind);
        Assert.Equal((1, 1, 1, 0), (sync.GetProperty("matched").GetInt32(), sync.GetProperty("uploaded").GetInt32(),
            sync.GetProperty("downloaded").GetInt32(), sync.GetProperty("failed").GetInt32()));

        // Beta went up as Ultimate clients upload: a blob by its audio hash, then a track with its tags.
        var beta = Assert.Single(_cloud.Uploaded);
        Assert.Equal(File.ReadAllBytes(Path.Combine(_music, "Band", "Beta.wav")), beta.Value);
        Assert.Contains(_cloud.Tracks, t => t.Id.StartsWith("up-", StringComparison.Ordinal));

        // Gamma came down, tagged with what Ultimate Cloud knows, and placed with the library's template.
        var gamma = Path.Combine(_music, "Echo", "2020 - Deep", "03 Gamma.wav");
        Assert.True(File.Exists(gamma));
        Assert.Equal("Gamma", new ATL.Track(gamma).Title);
        await _factory.WaitForScansAsync(_admin);
        var titles = (await _admin.GraphQLAsync("query { tracks(first: 10) { items { title } } }"))
            .GetProperty("data").GetProperty("tracks").GetProperty("items").EnumerateArray().Select(t => t.GetProperty("title").GetString()).Order();
        Assert.Equal(["Alpha", "Beta", "Gamma"], titles);

        // A second run finds nothing to do: nothing goes up or comes down twice.
        await _admin.GraphQLAsync("mutation($id: ID!) { syncLibraryWithCloud(libraryId: $id) { requested } }", new { id = _libraryId });
        sync = await WaitForSyncAsync();
        Assert.Single(_cloud.Uploaded);
        Assert.Equal((1, 1, 1), (sync.GetProperty("matched").GetInt32(), sync.GetProperty("uploaded").GetInt32(), sync.GetProperty("downloaded").GetInt32()));
        Assert.Single(Directory.EnumerateFiles(Path.Combine(_music, "Echo"), "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Uploading_only_leaves_the_library_alone_and_stops_when_the_account_is_full()
    {
        await StartAsync();
        _cloud.Full = true;
        await SetAsync("UPLOAD");

        var sync = await WaitForSyncAsync();
        Assert.Equal("cloud_quota_exceeded", sync.GetProperty("errorCode").GetString());
        Assert.Equal((1, 0, 1), (sync.GetProperty("matched").GetInt32(), sync.GetProperty("uploaded").GetInt32(), sync.GetProperty("localOnly").GetInt32()));
        Assert.Empty(_cloud.Uploaded);
        Assert.False(Directory.Exists(Path.Combine(_music, "Echo")));

        // With room again, the next run uploads what was left.
        _cloud.Full = false;
        await _admin.GraphQLAsync("mutation($id: ID!) { syncLibraryWithCloud(libraryId: $id) { requested } }", new { id = _libraryId });
        sync = await WaitForSyncAsync();
        Assert.Equal((1, 1, 0), (sync.GetProperty("matched").GetInt32(), sync.GetProperty("uploaded").GetInt32(), sync.GetProperty("localOnly").GetInt32()));
    }

    [Fact]
    public async Task Syncing_needs_the_Cloud_plan_and_one_library_downloading()
    {
        await StartAsync(plan: "Standard");
        Assert.Equal("cloud_sync_unavailable", (await SetAsync("BOTH")).ErrorCode());

        _cloud.Plan = "Cloud";
        await _admin.GraphQLAsync("mutation { refreshUltimateCloud { tier } }");
        Assert.Null((await SetAsync("DOWNLOAD")).ErrorCode());

        var other = Directory.CreateTempSubdirectory("arcanist-sync-other-").FullName;
        var created = await _admin.PostAsJsonAsync("/rest/v1/libraries", new { name = "Other", visibleToAll = false, sources = new[] { new { location = other } } }, Cancel);
        var otherId = (await created.Content.ReadFromJsonAsync<JsonElement>(Cancel)).GetProperty("id").GetString();
        Assert.Equal("validation_failed", (await SetAsync("BOTH", otherId)).ErrorCode());
        Assert.Null((await SetAsync("UPLOAD", otherId)).ErrorCode());
    }

    public async ValueTask DisposeAsync()
    {
        if (_factory is not null)
            await _factory.DisposeAsync();
    }
}
