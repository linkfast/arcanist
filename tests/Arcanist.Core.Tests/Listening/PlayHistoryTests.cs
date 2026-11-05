using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Arcanist.Core.Data;
using Arcanist.Core.Setup;
using Arcanist.Core.Tests.Scanning;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Arcanist.Core.Tests.Listening;

/// <summary>Each user reads, searches and empties their own history, and what leaves it stops counting.</summary>
public sealed class PlayHistoryTests : IAsyncLifetime
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private readonly string _music = Directory.CreateTempSubdirectory("arcanist-history-").FullName;
    private readonly ArcanistFactory _factory = new();
    private HttpClient _admin = null!;
    private Guid _alpha;
    private Guid _beta;

    public async ValueTask InitializeAsync()
    {
        AudioFixtures.WriteWav(Path.Combine(_music, "a.wav"), 1, new FixtureTags("Alpha Song", "Band", "Record", "Band", TrackNumber: 1));
        AudioFixtures.WriteWav(Path.Combine(_music, "b.wav"), 2, new FixtureTags("Beta Song", "Band", "Record", "Band", TrackNumber: 2));

        await _factory.CompleteSetupAsync(new SetupLibrary("Music", [_music]));
        _admin = await _factory.CreateAuthenticatedClientAsync();
        await _factory.WaitForScansAsync(_admin);

        var tracks = (await _admin.GetFromJsonAsync<JsonElement>("/rest/v1/tracks", ArcanistFactory.Json, Cancel)).GetProperty("items").EnumerateArray()
            .ToDictionary(t => t.GetProperty("title").GetString()!, t => t.GetProperty("id").GetGuid());
        (_alpha, _beta) = (tracks["Alpha Song"], tracks["Beta Song"]);
    }

    public async ValueTask DisposeAsync()
    {
        await _factory.DisposeAsync();
        Directory.Delete(_music, recursive: true);
    }

    private static async Task<Guid> PlayAsync(HttpClient client, Guid trackId, int minutesAgo, int playedMs = 3_600_000)
    {
        var response = await client.PostAsJsonAsync("/rest/v1/plays",
            new { trackId, playedMs, playedAt = DateTime.UtcNow.AddMinutes(-minutesAgo), client = "Test player" }, ArcanistFactory.Json, Cancel);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>(ArcanistFactory.Json, Cancel)).GetProperty("id").GetGuid();
    }

    private static Task<JsonElement> HistoryAsync(HttpClient client, string query = "") =>
        client.GetFromJsonAsync<JsonElement>("/rest/v1/me/plays" + query, ArcanistFactory.Json, Cancel);

    private static async Task<HttpStatusCode> DeleteAsync(HttpClient client, object input) =>
        (await client.PostAsJsonAsync("/rest/v1/me/plays/delete", input, ArcanistFactory.Json, Cancel)).StatusCode;

    private async Task<(int Plays, DateTime? Last)> CounterAsync(Guid trackId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ArcanistDbContext>();
        var stats = await db.TrackPlayStats.AsNoTracking().SingleOrDefaultAsync(s => s.TrackId == trackId, Cancel);
        return (stats?.PlayCount ?? 0, stats?.LastPlayedAt);
    }

    private static async Task<int> StatsPlaysAsync(HttpClient client)
    {
        var stats = await client.GetFromJsonAsync<JsonElement>("/rest/v1/stats/listening?period=ALL", ArcanistFactory.Json, Cancel);
        return stats.GetProperty("plays").GetInt32();
    }

    [Fact]
    public async Task The_history_says_how_many_and_who_played_and_finds_a_song()
    {
        await PlayAsync(_admin, _alpha, 3);
        await PlayAsync(_admin, _beta, 2);
        await PlayAsync(_admin, _alpha, 1);

        var all = await HistoryAsync(_admin);
        Assert.Equal(3, all.GetProperty("totalCount").GetInt32());
        var first = all.GetProperty("items")[0];
        Assert.Equal(_alpha, first.GetProperty("track").GetProperty("id").GetGuid());
        Assert.Equal("Test player", first.GetProperty("client").GetString());

        var found = await HistoryAsync(_admin, "?search=beta");
        Assert.Equal(1, found.GetProperty("totalCount").GetInt32());
        Assert.Equal(_beta, found.GetProperty("items")[0].GetProperty("track").GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task Removing_a_play_takes_it_from_the_counter_and_the_statistics()
    {
        var older = await PlayAsync(_admin, _alpha, 10);
        var newer = await PlayAsync(_admin, _alpha, 5);
        Assert.Equal(2, (await CounterAsync(_alpha)).Plays);

        Assert.Equal(HttpStatusCode.OK, await DeleteAsync(_admin, new { playIds = new[] { newer } }));

        var (plays, last) = await CounterAsync(_alpha);
        Assert.Equal(1, plays);
        Assert.Equal(DateTime.UtcNow.AddMinutes(-10), last!.Value, TimeSpan.FromMinutes(1));
        Assert.Equal(1, await StatsPlaysAsync(_admin));
        var left = await HistoryAsync(_admin);
        Assert.Equal(older, Assert.Single(left.GetProperty("items").EnumerateArray()).GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task Removing_a_song_takes_its_skips_and_what_an_app_brought()
    {
        await PlayAsync(_admin, _alpha, 10);
        await PlayAsync(_admin, _beta, 9);
        (await _admin.PostAsJsonAsync("/rest/v1/me/listening/import",
            new { playCounts = new[] { new { trackId = _alpha, playCount = 40, lastPlayedAt = (DateTime?)null } } }, ArcanistFactory.Json, Cancel)).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.OK, await DeleteAsync(_admin, new { trackIds = new[] { _alpha } }));

        Assert.Equal(0, (await CounterAsync(_alpha)).Plays);
        Assert.Equal(1, (await CounterAsync(_beta)).Plays);
        var left = await HistoryAsync(_admin);
        Assert.Equal(_beta, Assert.Single(left.GetProperty("items").EnumerateArray()).GetProperty("track").GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task Nobody_touches_the_history_of_someone_else()
    {
        var mine = await PlayAsync(_admin, _alpha, 5);
        await _factory.CreateUserAsync("lena");
        var other = await _factory.CreateAuthenticatedClientAsync("lena");
        await PlayAsync(other, _alpha, 4);

        Assert.Equal(HttpStatusCode.OK, await DeleteAsync(other, new { playIds = new[] { mine } }));
        Assert.Equal(1, (await HistoryAsync(_admin)).GetProperty("totalCount").GetInt32());
        Assert.Equal(1, (await HistoryAsync(other)).GetProperty("totalCount").GetInt32());

        Assert.Equal(HttpStatusCode.OK, (await other.DeleteAsync("/rest/v1/me/plays", Cancel)).StatusCode);
        Assert.Equal(0, (await HistoryAsync(other)).GetProperty("totalCount").GetInt32());
        Assert.Equal(0, await StatsPlaysAsync(other));
        Assert.Equal(1, (await HistoryAsync(_admin)).GetProperty("totalCount").GetInt32());
        Assert.Equal(1, (await CounterAsync(_alpha)).Plays);
    }

    [Fact]
    public async Task Too_many_at_once_are_refused() =>
        Assert.Equal(HttpStatusCode.BadRequest, await DeleteAsync(_admin, new { trackIds = Enumerable.Range(0, 1001).Select(_ => Guid.NewGuid()).ToList() }));
}
