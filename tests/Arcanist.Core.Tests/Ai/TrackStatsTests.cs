using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Arcanist.Core.Data.Entities;
using Arcanist.Core.Setup;
using Arcanist.Core.Tests.Scanning;

namespace Arcanist.Core.Tests.Ai;

/// <summary>Listening statistics per song: the user's and the server's.</summary>
public sealed class TrackStatsTests : IAsyncDisposable
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private readonly string _music = Directory.CreateTempSubdirectory("arcanist-track-stats-").FullName;
    private readonly ArcanistFactory _factory = new();

    public async ValueTask DisposeAsync()
    {
        await _factory.DisposeAsync();
        try
        {
            Directory.Delete(_music, true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public async Task Counts_skips_completions_and_hours_for_the_user_and_the_server()
    {
        // 70 seconds: long enough to be skipped.
        AudioFixtures.WriteMusic(Path.Combine(_music, "song.wav"), new FixtureTags("Song", "Band", "Album", "Band", TrackNumber: 1), seconds: 70);
        await _factory.CompleteSetupAsync(new SetupLibrary("Music", [_music]));
        var admin = await _factory.CreateAuthenticatedClientAsync();
        await _factory.WaitForScansAsync(admin);
        var track = (await admin.GetFromJsonAsync<JsonElement>("/rest/v1/tracks", ArcanistFactory.Json, Cancel)).GetProperty("items")[0];
        var id = track.GetProperty("id").GetGuid();
        var duration = track.GetProperty("durationMs").GetInt32();

        var day = DateTime.UtcNow.Date.AddDays(-1);
        async Task PlayAsync(HttpClient client, int playedMs, int hour) =>
            Assert.True((await client.PostAsJsonAsync("/rest/v1/plays", new { trackId = id, playedMs, playedAt = day.AddHours(hour), client = "test" },
                ArcanistFactory.Json, Cancel)).IsSuccessStatusCode);
        await PlayAsync(admin, duration, 10);
        await PlayAsync(admin, 5_000, 11);
        await PlayAsync(admin, 40_000, 12);
        await _factory.CreateUserAsync("ana");
        var ana = await _factory.CreateAuthenticatedClientAsync("ana");
        await PlayAsync(ana, duration, 13);

        var mine = (await admin.GetFromJsonAsync<JsonElement>("/rest/v1/stats/tracks?timeZone=America/Lima", ArcanistFactory.Json, Cancel))
            .GetProperty("items")[0];
        Assert.Equal(id, mine.GetProperty("trackId").GetGuid());
        Assert.Equal(2, mine.GetProperty("plays").GetInt32());
        Assert.Equal(1, mine.GetProperty("skips").GetInt32());
        Assert.Equal(5.0, mine.GetProperty("avgSkipPosition").GetDouble());
        Assert.Equal(1, mine.GetProperty("completions").GetInt32());
        Assert.Equal(Math.Round((1 + 5_000.0 / duration + 40_000.0 / duration) / 3, 3), mine.GetProperty("avgListenRatio").GetDouble(), 3);
        var hours = mine.GetProperty("hourHistogram").EnumerateArray().Select(h => h.GetInt32()).ToArray();
        // Lima is five hours behind UTC: the counted plays at 10:00 and 12:00 UTC were at 5 and 7.
        Assert.Equal(1, hours[5]);
        Assert.Equal(1, hours[7]);
        Assert.Equal(2, hours.Sum());

        // The server's: everyone's together.
        var server = (await ana.GetFromJsonAsync<JsonElement>("/rest/v1/stats/tracks?scope=SERVER", ArcanistFactory.Json, Cancel)).GetProperty("items")[0];
        Assert.Equal(3, server.GetProperty("plays").GetInt32());
        Assert.Equal(2, server.GetProperty("completions").GetInt32());

        // Kept up with updatedSince: nothing played since the last play.
        var last = mine.GetProperty("lastPlayedAt").GetDateTime();
        var later = await admin.GetFromJsonAsync<JsonElement>($"/rest/v1/stats/tracks?updatedSince={Uri.EscapeDataString(last.ToString("O"))}",
            ArcanistFactory.Json, Cancel);
        Assert.Empty(later.GetProperty("items").EnumerateArray());

        // Administrators can keep the server's statistics from clients (not from those who may see everyone's).
        Assert.True((await admin.PutAsJsonAsync("/rest/v1/ai/settings",
            new { threads = 1, pauseWhileScanning = true, pauseWhileStreaming = false, onDemandSources = true, shareServerStats = false },
            ArcanistFactory.Json, Cancel)).IsSuccessStatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await ana.GetAsync("/rest/v1/stats/tracks?scope=SERVER", Cancel)).StatusCode);
        Assert.True((await admin.GetAsync("/rest/v1/stats/tracks?scope=SERVER", Cancel)).IsSuccessStatusCode);
        Assert.True((await ana.GetAsync("/rest/v1/stats/tracks", Cancel)).IsSuccessStatusCode);
    }
}
