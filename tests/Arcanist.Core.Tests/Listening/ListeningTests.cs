using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Arcanist.Core.Analysis;
using Arcanist.Core.Data;
using Arcanist.Core.Setup;
using Arcanist.Core.Tests.Scanning;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Permissions = Arcanist.Core.Data.Entities.Permissions;

namespace Arcanist.Core.Tests.Listening;

/// <summary>Ratings, plays, history and statistics.</summary>
public sealed class ListeningTests : IAsyncLifetime
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private readonly string _music = Directory.CreateTempSubdirectory("arcanist-listening-").FullName;
    private readonly ArcanistFactory _factory = new();
    private HttpClient _admin = null!;
    private Guid _long;
    private Guid _short;
    private Guid _other;

    public async ValueTask InitializeAsync()
    {
        // 80 seconds: 40 count as a play; under 30 is a skip.
        var path = Path.Combine(_music, "Album", "long.wav");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, AudioFixtures.Music(bpm: 120, seconds: 80, chordSeed: 1));
        AudioFixtures.Tag(path, new FixtureTags("Long Song", "The Band", "Album", "The Band", TrackNumber: 1));
        AudioFixtures.WriteWav(Path.Combine(_music, "Album", "short.wav"), 2, new FixtureTags("Short Song", "The Band", "Album", "The Band", TrackNumber: 2));
        AudioFixtures.WriteWav(Path.Combine(_music, "Album", "other.wav"), 3, new FixtureTags("Other Song", "Guest", "Album", "The Band", TrackNumber: 3));

        await _factory.CompleteSetupAsync(new SetupLibrary("Music", [_music]));
        _admin = await _factory.CreateAuthenticatedClientAsync();
        await _factory.WaitForScansAsync(_admin);

        var tracks = (await _admin.GetFromJsonAsync<JsonElement>("/rest/v1/tracks", ArcanistFactory.Json, Cancel)).GetProperty("items").EnumerateArray()
            .ToDictionary(t => t.GetProperty("title").GetString()!, t => t.GetProperty("id").GetGuid());
        (_long, _short, _other) = (tracks["Long Song"], tracks["Short Song"], tracks["Other Song"]);
    }

    private async Task<JsonElement> TrackAsync(Guid id, HttpClient? client = null) =>
        await (client ?? _admin).GetFromJsonAsync<JsonElement>($"/rest/v1/tracks/{id}", ArcanistFactory.Json, Cancel);

    private async Task<JsonElement> PlayAsync(Guid id, int playedMs, DateTime? playedAt = null, HttpClient? client = null)
    {
        var response = await (client ?? _admin).PostAsJsonAsync("/rest/v1/plays", new { trackId = id, playedMs, playedAt, client = "tests" }, ArcanistFactory.Json, Cancel);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>(ArcanistFactory.Json, Cancel);
    }

    private async Task<HttpResponseMessage> RateAsync(Guid id, int? rating) =>
        await _admin.PutAsJsonAsync($"/rest/v1/tracks/{id}/rating", new { rating }, ArcanistFactory.Json, Cancel);

    private static (JsonElement Rating, bool Starred) RatingOf(JsonElement track) =>
        (track.GetProperty("rating"), track.GetProperty("starred").GetBoolean());

    [Fact]
    public async Task Ratings_follow_the_server_rating_mode_without_losing_anything()
    {
        Assert.Equal(HttpStatusCode.OK, (await RateAsync(_short, 3)).StatusCode);
        await _admin.PutAsJsonAsync($"/rest/v1/tracks/{_long}/star", new { starred = true }, ArcanistFactory.Json, Cancel);

        Assert.Equal(3, RatingOf(await TrackAsync(_short)).Rating.GetInt32());
        Assert.Equal((5, true), (RatingOf(await TrackAsync(_long)).Rating.GetInt32(), RatingOf(await TrackAsync(_long)).Starred));

        // Single star: the starred track is still starred; three stars are kept but not shown, and cannot be given.
        Assert.Equal(HttpStatusCode.OK, (await _admin.PutAsJsonAsync("/rest/v1/settings/rating-mode", new { mode = "STAR" }, ArcanistFactory.Json, Cancel)).StatusCode);
        var info = await _admin.GetFromJsonAsync<JsonElement>("/rest/v1/server-info", ArcanistFactory.Json, Cancel);
        Assert.Equal("STAR", info.GetProperty("ratingMode").GetString());
        Assert.Equal(JsonValueKind.Null, RatingOf(await TrackAsync(_short)).Rating.ValueKind);
        Assert.True(RatingOf(await TrackAsync(_long)).Starred);
        Assert.Equal(HttpStatusCode.BadRequest, (await RateAsync(_other, 3)).StatusCode);

        var starred = await _admin.GetFromJsonAsync<JsonElement>("/rest/v1/me/rated-tracks", ArcanistFactory.Json, Cancel);
        Assert.Equal([_long], starred.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("track").GetProperty("id").GetGuid()));

        await _admin.PutAsJsonAsync("/rest/v1/settings/rating-mode", new { mode = "STARS" }, ArcanistFactory.Json, Cancel);
        Assert.Equal(3, RatingOf(await TrackAsync(_short)).Rating.GetInt32());

        // Unstarring clears five stars; null clears any rating.
        await _admin.PutAsJsonAsync($"/rest/v1/tracks/{_long}/star", new { starred = false }, ArcanistFactory.Json, Cancel);
        Assert.Equal(JsonValueKind.Null, RatingOf(await TrackAsync(_long)).Rating.ValueKind);
        await RateAsync(_short, null);
        Assert.Equal(0, (await _admin.GetFromJsonAsync<JsonElement>("/rest/v1/me/rated-tracks", ArcanistFactory.Json, Cancel)).GetProperty("totalCount").GetInt32());
        Assert.Equal(HttpStatusCode.BadRequest, (await RateAsync(_short, 6)).StatusCode);
    }

    [Fact]
    public async Task Rated_tracks_are_listed_newest_first_page_by_page()
    {
        foreach (var id in new[] { _long, _short, _other })
            await RateAsync(id, 4);

        var first = await _admin.GetFromJsonAsync<JsonElement>("/rest/v1/me/rated-tracks?first=2", ArcanistFactory.Json, Cancel);
        var second = await _admin.GetFromJsonAsync<JsonElement>(
            $"/rest/v1/me/rated-tracks?first=2&after={first.GetProperty("nextCursor").GetString()}", ArcanistFactory.Json, Cancel);

        var ids = first.GetProperty("items").EnumerateArray().Concat(second.GetProperty("items").EnumerateArray())
            .Select(i => i.GetProperty("track").GetProperty("id").GetGuid()).ToList();
        Assert.Equal([_other, _short, _long], ids);
        Assert.Equal(JsonValueKind.Null, second.GetProperty("nextCursor").ValueKind);
        Assert.Equal(3, first.GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task The_server_decides_what_counts_as_a_play()
    {
        var skip = await PlayAsync(_long, 10_000, DateTime.UtcNow.AddMinutes(-10));
        var tooShort = await PlayAsync(_long, 35_000, DateTime.UtcNow.AddMinutes(-8));
        var counted = await PlayAsync(_long, 40_000, DateTime.UtcNow.AddMinutes(-5));
        Assert.Equal((false, true), (skip.GetProperty("counted").GetBoolean(), skip.GetProperty("skipped").GetBoolean()));
        Assert.Equal((false, false), (tooShort.GetProperty("counted").GetBoolean(), tooShort.GetProperty("skipped").GetBoolean()));
        Assert.True(counted.GetProperty("counted").GetBoolean());

        // A player sending the same report again does not count twice.
        var playedAt = DateTime.UtcNow.AddMinutes(-1);
        var once = await PlayAsync(_long, 80_000, playedAt);
        var again = await PlayAsync(_long, 80_000, playedAt.AddMilliseconds(500));
        Assert.Equal(once.GetProperty("id").GetString(), again.GetProperty("id").GetString());

        var track = await TrackAsync(_long);
        Assert.Equal(2, track.GetProperty("playCount").GetInt32());
        Assert.NotEqual(JsonValueKind.Null, track.GetProperty("lastPlayedAt").ValueKind);

        var history = await _admin.GetFromJsonAsync<JsonElement>("/rest/v1/me/plays", ArcanistFactory.Json, Cancel);
        Assert.Equal(2, history.GetProperty("items").GetArrayLength());

        var future = await _admin.PostAsJsonAsync("/rest/v1/plays", new { trackId = _long, playedMs = 1000, playedAt = DateTime.UtcNow.AddHours(1) }, ArcanistFactory.Json, Cancel);
        Assert.Equal(HttpStatusCode.BadRequest, future.StatusCode);
        var unknown = await _admin.PostAsJsonAsync("/rest/v1/plays", new { trackId = Guid.NewGuid(), playedMs = 1000 }, ArcanistFactory.Json, Cancel);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
    }

    [Fact]
    public async Task Statistics_show_the_users_own_listening_and_the_server_to_administrators()
    {
        await PlayAsync(_long, 80_000, DateTime.UtcNow.AddMinutes(-20));
        await PlayAsync(_long, 80_000, DateTime.UtcNow.AddMinutes(-10));
        await PlayAsync(_short, 60_000);

        var listener = await _factory.CreateUserAsync("listener");
        var listenerClient = await _factory.CreateAuthenticatedClientAsync("listener");
        await PlayAsync(_other, 60_000, client: listenerClient);

        var mine = await _admin.GetFromJsonAsync<JsonElement>("/rest/v1/stats/listening?period=week&timeZone=Europe/Madrid", ArcanistFactory.Json, Cancel);
        Assert.Equal(3, mine.GetProperty("plays").GetInt32());
        Assert.Equal(2, mine.GetProperty("distinctTracks").GetInt32());
        var top = mine.GetProperty("topTracks")[0];
        Assert.Equal((_long, 2), (top.GetProperty("track").GetProperty("id").GetGuid(), top.GetProperty("plays").GetInt32()));
        Assert.Equal("The Band", mine.GetProperty("topArtists")[0].GetProperty("artist").GetProperty("name").GetString());
        Assert.Equal(3, mine.GetProperty("topAlbums")[0].GetProperty("plays").GetInt32());
        Assert.Equal("DAY", mine.GetProperty("bucket").GetString());
        Assert.Equal(3, mine.GetProperty("timeline").EnumerateArray().Sum(b => b.GetProperty("plays").GetInt32()));
        // Every day of the week, quiet ones included.
        Assert.Equal(7, mine.GetProperty("timeline").GetArrayLength());

        // Others' statistics and the server's need VIEW_ALL_STATS.
        Assert.Equal(HttpStatusCode.Forbidden, (await listenerClient.GetAsync("/rest/v1/stats/server?period=month", Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await listenerClient.GetAsync($"/rest/v1/stats/listening?period=all&userId={Guid.NewGuid()}", Cancel)).StatusCode);
        var theirs = await _admin.GetFromJsonAsync<JsonElement>($"/rest/v1/stats/listening?period=all&userId={listener.Id}", ArcanistFactory.Json, Cancel);
        Assert.Equal((1, "MONTH"), (theirs.GetProperty("plays").GetInt32(), theirs.GetProperty("bucket").GetString()));

        var server = await _admin.GetFromJsonAsync<JsonElement>("/rest/v1/stats/server?period=month", ArcanistFactory.Json, Cancel);
        Assert.Equal((3, 1, 3), (server.GetProperty("library").GetProperty("tracks").GetInt32(), server.GetProperty("library").GetProperty("albums").GetInt32(),
            server.GetProperty("library").GetProperty("files").GetInt32()));
        Assert.Equal(4, server.GetProperty("listening").GetProperty("plays").GetInt32());
        Assert.Equal(["admin", "listener"], server.GetProperty("users").EnumerateArray().Select(u => u.GetProperty("userName").GetString()).Order());

        var badZone = await _admin.GetAsync("/rest/v1/stats/listening?period=week&timeZone=Mars/Olympus", Cancel);
        Assert.Equal(HttpStatusCode.BadRequest, badZone.StatusCode);
    }

    [Fact]
    public async Task Merging_tracks_keeps_what_users_did_with_both()
    {
        await RateAsync(_short, 2);
        await RateAsync(_other, 4);
        await PlayAsync(_short, 60_000, DateTime.UtcNow.AddMinutes(-3));
        await PlayAsync(_other, 60_000);

        await using (var db = _factory.Services.GetRequiredService<IArcanistDbContextFactory>().CreateDbContext())
            await TrackMerger.MergeAsync(db, _other, _short, Cancel);

        var merged = await TrackAsync(_other);
        Assert.Equal((4, 2), (merged.GetProperty("rating").GetInt32(), merged.GetProperty("playCount").GetInt32()));
        Assert.Equal(HttpStatusCode.NotFound, (await _admin.GetAsync($"/rest/v1/tracks/{_short}", Cancel)).StatusCode);
    }

    [Fact]
    public async Task The_wizard_chooses_the_rating_mode()
    {
        await using var factory = new ArcanistFactory();
        var response = await factory.CreateClient().PostAsJsonAsync("/rest/v1/setup/complete", new
        {
            database = new { provider = "SQLITE" },
            admin = new { userName = "admin", password = ArcanistFactory.Password },
            libraries = Array.Empty<object>(),
            shareAnonymousStatistics = false,
            ratingMode = "STAR",
        }, ArcanistFactory.Json, Cancel);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var info = await factory.CreateClient().GetFromJsonAsync<JsonElement>("/rest/v1/server-info", ArcanistFactory.Json, Cancel);
        Assert.Equal("STAR", info.GetProperty("ratingMode").GetString());

        // Only settings managers change it.
        await factory.CreateUserAsync("listener", permissions: Permissions.None);
        var listener = await factory.CreateAuthenticatedClientAsync("listener");
        Assert.Equal(HttpStatusCode.Forbidden,
            (await listener.PutAsJsonAsync("/rest/v1/settings/rating-mode", new { mode = "STARS" }, ArcanistFactory.Json, Cancel)).StatusCode);
    }

    public async ValueTask DisposeAsync()
    {
        await _factory.DisposeAsync();
        Directory.Delete(_music, recursive: true);
    }
}
