using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Arcanist.Core.Cloud;
using Arcanist.Core.Data;
using Arcanist.Core.Setup;
using Arcanist.Core.Tests.Scanning;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Arcanist.Core.Tests.Cloud;

/// <summary>Ultimate Cloud as an extension, and its online services: plays, albums heard whole, StarTracks and statistics.</summary>
public sealed class UltimateCloudOnlineTests : IAsyncDisposable
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private readonly FakeUltimateCloud _cloud = new();
    private readonly string _music = Directory.CreateTempSubdirectory("arcanist-cloud-online-").FullName;
    private ArcanistFactory _factory = null!;
    private HttpClient _admin = null!;
    private Dictionary<string, (Guid TrackId, Guid AcoustId)> _tracks = null!;

    private async Task StartAsync()
    {
        AudioFixtures.WriteWav(Path.Combine(_music, "Whole", "1.wav"), 1, new FixtureTags("Opening", "Band", "Whole", "Band", TrackNumber: 1));
        AudioFixtures.WriteWav(Path.Combine(_music, "Whole", "2.wav"), 2, new FixtureTags("Closing", "Band", "Whole", "Band", TrackNumber: 2));
        AudioFixtures.WriteWav(Path.Combine(_music, "Single", "s.wav"), 3, new FixtureTags("Single", "Soloist", "Single", "Soloist", TrackNumber: 1));
        // Long enough to be skipped.
        AudioFixtures.WriteMusic(Path.Combine(_music, "Long", "l.wav"), new FixtureTags("Long", "Soloist", "Long", "Soloist", TrackNumber: 1), seconds: 70);
        _factory = new ArcanistFactory(new Dictionary<string, string?>
        {
            ["Arcanist:UltimateCloud:ApiUrl"] = FakeUltimateCloud.ApiUrl,
            ["Arcanist:UltimateCloud:Authority"] = FakeUltimateCloud.Authority,
        }, services: _cloud.Register);
        await _factory.CompleteSetupAsync(new SetupLibrary("Music", [_music]));
        _admin = await _factory.CreateAuthenticatedClientAsync();
        await _factory.WaitForScansAsync(_admin);

        // Identified, as AcoustID would.
        await using (var db = _factory.Services.GetRequiredService<IArcanistDbContextFactory>().CreateDbContext())
        {
            foreach (var recording in await db.Tracks.Select(t => t.Recording).ToListAsync(Cancel))
                recording.AcoustId = Guid.NewGuid();
            await db.SaveChangesAsync(Cancel);
            _tracks = await db.Tracks.ToDictionaryAsync(t => t.Title, t => (t.Id, t.Recording.AcoustId!.Value), Cancel);
        }

        await _admin.GraphQLAsync("mutation { startUltimateCloudLink { state } }");
        _cloud.Approve = true;
        await WaitAsync(async () => (await StatusAsync()).GetProperty("plan").ValueKind != JsonValueKind.Null, "The account was not linked");
    }

    private async Task<JsonElement> StatusAsync() =>
        (await _admin.GraphQLAsync("query { ultimateCloud { enabled tier plan { name } services { personalPlays albumPlaythroughs starTracks metrics } } }"))
        .GetProperty("data").GetProperty("ultimateCloud");

    private async Task<Guid> AdminIdAsync()
    {
        await using var db = _factory.Services.GetRequiredService<IArcanistDbContextFactory>().CreateDbContext();
        return await db.Users.Where(u => u.UserName == ArcanistFactory.AdminUserName).Select(u => u.Id).SingleAsync(Cancel);
    }

    private UltimateCloudReporter Reporter => _factory.Services.GetRequiredService<UltimateCloudReporter>();

    private async Task PlayAsync(HttpClient client, string title, int minutesAgo)
    {
        var response = await client.PostAsJsonAsync("/rest/v1/plays",
            new { trackId = _tracks[title].TrackId, playedMs = 60_000, playedAt = DateTime.UtcNow.AddMinutes(-minutesAgo), client = "tests" }, ArcanistFactory.Json, Cancel);
        Assert.True((await response.Content.ReadFromJsonAsync<JsonElement>(ArcanistFactory.Json, Cancel)).GetProperty("counted").GetBoolean());
    }

    private static async Task WaitAsync(Func<Task<bool>> done, string message)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (!await done())
        {
            Assert.True(DateTime.UtcNow < deadline, message);
            await Task.Delay(200, Cancel);
        }
    }

    [Fact]
    public async Task Plays_count_for_the_account_or_anonymously_for_the_rankings()
    {
        await StartAsync();
        var status = await StatusAsync();
        Assert.True(status.GetProperty("enabled").GetBoolean());
        // The setup wizard was told not to share anonymous statistics: that is the default.
        Assert.False((await _admin.GraphQLAsync("query { anonymousStatistics }")).GetProperty("data").GetProperty("anonymousStatistics").GetBoolean());
        await _admin.GraphQLAsync("mutation { setAnonymousStatistics(enabled: true) }");

        // Plays made before are never sent.
        await PlayAsync(_admin, "Single", 30);
        await Reporter.RunAsync(Cancel);
        await _factory.CreateUserAsync("listener");
        var listener = await _factory.CreateAuthenticatedClientAsync("listener");
        _cloud.Plays.Clear();

        await PlayAsync(_admin, "Opening", 10);
        await PlayAsync(_admin, "Closing", 5);
        await PlayAsync(listener, "Single", 3);
        await Reporter.RunAsync(Cancel);

        // The administrator who linked the account: theirs. Everyone else: anonymous.
        Assert.Equal([_tracks["Opening"].AcoustId.ToString(), _tracks["Closing"].AcoustId.ToString()], _cloud.Plays.Select(p => p.AcoustId));
        Assert.Equal("Opening", _cloud.Plays.First().Title);
        var shared = Assert.Single(_cloud.SharedPlays);
        Assert.Equal(_tracks["Single"].AcoustId.ToString(), shared.AcoustId);
        // How it was listened to goes along: to the end, not skipped.
        Assert.Equal((false, true), (shared.Skipped, shared.Completed));
        Assert.True(shared.PlayedMs > 0);
        Assert.Equal(("Band", "Whole"), Assert.Single(_cloud.Playthroughs));

        // Skips go anonymously too (the account's own statistics count plays only).
        var skip = await listener.PostAsJsonAsync("/rest/v1/plays",
            new { trackId = _tracks["Long"].TrackId, playedMs = 5_000, playedAt = DateTime.UtcNow.AddMinutes(-2), client = "tests" }, ArcanistFactory.Json, Cancel);
        Assert.True((await skip.Content.ReadFromJsonAsync<JsonElement>(ArcanistFactory.Json, Cancel)).GetProperty("skipped").GetBoolean());
        await Reporter.RunAsync(Cancel);
        var skipped = _cloud.SharedPlays.Last();
        Assert.Equal(_tracks["Long"].AcoustId.ToString(), skipped.AcoustId);
        Assert.Equal((5_000, true, false), (skipped.PlayedMs, skipped.Skipped, skipped.Completed));
        var sent = _cloud.SharedPlays.Count;

        // Anonymous statistics off: nothing for the others.
        await _admin.GraphQLAsync("mutation { setAnonymousStatistics(enabled: false) }");
        await PlayAsync(listener, "Opening", 1);
        await Reporter.RunAsync(Cancel);
        Assert.Equal(sent, _cloud.SharedPlays.Count);
    }

    [Fact]
    public async Task Anonymous_statistics_need_no_account()
    {
        await StartAsync();
        await _admin.GraphQLAsync("mutation { setAnonymousStatistics(enabled: true) }");
        Assert.Equal(HttpStatusCode.OK, (await _admin.PostAsync("/rest/v1/ultimate-cloud/unlink", null, Cancel)).StatusCode);
        await Reporter.RunAsync(Cancel);

        // Unlinked, even the administrator's plays go anonymously, with the albums heard whole.
        await PlayAsync(_admin, "Opening", 10);
        await PlayAsync(_admin, "Closing", 5);
        await Reporter.RunAsync(Cancel);
        Assert.Equal(2, _cloud.SharedPlays.Count);
        Assert.Equal(("Band", "Whole"), Assert.Single(_cloud.AnonymousAlbums));
        Assert.Empty(_cloud.Plays);
    }

    [Fact]
    public async Task Stars_and_StarTracks_follow_each_other()
    {
        var ghost = Guid.NewGuid().ToString();
        await StartAsync();
        var single = _tracks["Single"];
        var opening = _tracks["Opening"];
        _cloud.Stars[single.AcoustId.ToString()] = true;
        _cloud.Stars[ghost] = true;
        await _admin.PutAsJsonAsync($"/rest/v1/tracks/{opening.TrackId}/star", new { starred = true }, ArcanistFactory.Json, Cancel);

        async Task<bool> StarredAsync(Guid trackId) =>
            (await _admin.GetFromJsonAsync<JsonElement>($"/rest/v1/tracks/{trackId}", ArcanistFactory.Json, Cancel)).GetProperty("starred").GetBoolean();

        // The first time, both sides keep what they have.
        await Reporter.SyncStarsAsync(await AdminIdAsync(), Cancel);
        Assert.True(_cloud.Stars.ContainsKey(opening.AcoustId.ToString()));
        Assert.True(await StarredAsync(single.TrackId));
        Assert.True(_cloud.Stars.ContainsKey(ghost));

        // Unstarred here: gone there; removed there: unstarred here. The song this server lacks stays.
        await _admin.PutAsJsonAsync($"/rest/v1/tracks/{opening.TrackId}/star", new { starred = false }, ArcanistFactory.Json, Cancel);
        _cloud.Stars.TryRemove(single.AcoustId.ToString(), out _);
        await Reporter.SyncStarsAsync(await AdminIdAsync(), Cancel);
        Assert.False(_cloud.Stars.ContainsKey(opening.AcoustId.ToString()));
        Assert.False(await StarredAsync(single.TrackId));
        Assert.Equal([ghost], _cloud.Stars.Keys);
    }

    [Fact]
    public async Task The_account_s_statistics_come_from_Ultimate_Cloud()
    {
        await StartAsync();
        var metrics = (await _admin.GraphQLAsync("query { ultimateCloudMetrics { totalPlays topSongAllTime { title playCount } topDecade { decadeStart } topGenreThisMonth { genre } } }"))
            .GetProperty("data").GetProperty("ultimateCloudMetrics");
        Assert.Equal(42, metrics.GetProperty("totalPlays").GetInt64());
        Assert.Equal("Top Song", metrics.GetProperty("topSongAllTime").GetProperty("title").GetString());
        Assert.Equal(1990, metrics.GetProperty("topDecade").GetProperty("decadeStart").GetInt32());
        Assert.Equal(JsonValueKind.Null, metrics.GetProperty("topGenreThisMonth").ValueKind);
    }

    [Fact]
    public async Task Switched_off_it_contacts_nothing_and_hides_the_account_s_libraries()
    {
        await StartAsync();
        _cloud.AddTrack("c1", "From the Cloud", "Cloud Band", "Up There", 1, AudioFixtures.Music(bpm: 100, seconds: 2, chordSeed: 7));
        await _admin.GraphQLAsync("mutation { addUltimateCloudLibrary(input: {collection: LIBRARY}) { state } }");
        await _factory.WaitForScansAsync(_admin);
        async Task<List<string>> TitlesAsync() => (await _admin.GetFromJsonAsync<JsonElement>("/rest/v1/tracks", ArcanistFactory.Json, Cancel))
            .GetProperty("items").EnumerateArray().Select(t => t.GetProperty("title").GetString()!).ToList();
        Assert.Contains("From the Cloud", await TitlesAsync());

        var extensions = await _admin.GetFromJsonAsync<JsonElement>("/rest/v1/extensions", ArcanistFactory.Json, Cancel);
        Assert.True(extensions.EnumerateArray().Single(e => e.GetProperty("id").GetString() == "ultimate-cloud").GetProperty("enabled").GetBoolean());
        var off = await _admin.PutAsJsonAsync("/rest/v1/extensions/ultimate-cloud/enabled", new { enabled = false }, ArcanistFactory.Json, Cancel);
        Assert.Equal(HttpStatusCode.OK, off.StatusCode);

        var status = await StatusAsync();
        Assert.False(status.GetProperty("enabled").GetBoolean());
        Assert.Equal("NONE", status.GetProperty("tier").GetString());
        Assert.DoesNotContain("From the Cloud", await TitlesAsync());
        Assert.Equal("NONE", (await _admin.GetFromJsonAsync<JsonElement>("/rest/v1/server-info", ArcanistFactory.Json, Cancel)).GetProperty("cloudTier").GetString());
        Assert.Equal("cloud_disabled", (await _admin.GraphQLAsync("mutation { startUltimateCloudLink { state } }")).ErrorCode());

        // Its library is disabled: it shows as such and can only be deleted.
        var libraries = await _admin.GetFromJsonAsync<JsonElement>("/rest/v1/libraries", ArcanistFactory.Json, Cancel);
        var cloudLibrary = libraries.EnumerateArray().Single(l => l.GetProperty("sources")[0].GetProperty("kind").GetString() == "ULTIMATE_CLOUD");
        var musicLibrary = libraries.EnumerateArray().Single(l => l.GetProperty("sources")[0].GetProperty("kind").GetString() == "LOCAL");
        Assert.True(cloudLibrary.GetProperty("disabled").GetBoolean());
        Assert.False(musicLibrary.GetProperty("disabled").GetBoolean());
        var cloudId = cloudLibrary.GetProperty("id").GetString();
        var scan = await _admin.PostAsJsonAsync($"/rest/v1/libraries/{cloudId}/scans", new { full = false }, ArcanistFactory.Json, Cancel);
        Assert.Contains("library_disabled", await scan.Content.ReadAsStringAsync(Cancel));
        var rename = await _admin.PutAsJsonAsync($"/rest/v1/libraries/{cloudId}", new { name = "Renamed", visibleToAll = false, scanIntervalMinutes = (int?)null, watchForChanges = false }, ArcanistFactory.Json, Cancel);
        Assert.Contains("library_disabled", await rename.Content.ReadAsStringAsync(Cancel));

        var calls = _cloud.Api.Count;
        await PlayAsync(_admin, "Opening", 1);
        await Reporter.RunAsync(Cancel);
        Assert.Equal(calls, _cloud.Api.Count);

        // Back on, with the account still linked.
        await _admin.PutAsJsonAsync("/rest/v1/extensions/ultimate-cloud/enabled", new { enabled = true }, ArcanistFactory.Json, Cancel);
        Assert.Contains("From the Cloud", await TitlesAsync());
    }

    public async ValueTask DisposeAsync()
    {
        if (_factory is not null)
            await _factory.DisposeAsync();
        try { Directory.Delete(_music, recursive: true); }
        catch (IOException) { }
    }
}
