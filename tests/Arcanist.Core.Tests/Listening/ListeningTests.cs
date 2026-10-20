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
    public async Task StarTracks_are_listed_and_changed_by_track_or_by_AcoustID()
    {
        var acoustId = Guid.NewGuid();
        await using (var db = _factory.Services.GetRequiredService<IArcanistDbContextFactory>().CreateDbContext())
        {
            var recording = await db.Tracks.Where(t => t.Id == _other).Select(t => t.Recording).SingleAsync(Cancel);
            recording.AcoustId = acoustId;
            await db.SaveChangesAsync(Cancel);
        }

        var info = await _admin.GetFromJsonAsync<JsonElement>("/rest/v1/server-info", ArcanistFactory.Json, Cancel);
        Assert.Contains("STAR_TRACKS", info.GetProperty("features").EnumerateArray().Select(f => f.GetString()));

        var unknown = Guid.NewGuid().ToString();
        var added = (await _admin.GraphQLAsync(
                "mutation($i: [StarTrackItemInput!]!) { addStarTracks(input: {items: $i}) { changed notFound } }",
                new { i = new object[] { new { trackId = _long }, new { acoustId = acoustId.ToString() }, new { acoustId = unknown } } }))
            .GetProperty("data").GetProperty("addStarTracks");
        Assert.Equal(2, added.GetProperty("changed").GetInt32());
        Assert.Equal([unknown], added.GetProperty("notFound").EnumerateArray().Select(n => n.GetString()));

        var page = await _admin.GetFromJsonAsync<JsonElement>("/rest/v1/me/star-tracks", ArcanistFactory.Json, Cancel);
        Assert.Equal(2, page.GetProperty("totalCount").GetInt32());
        var items = page.GetProperty("items").EnumerateArray().ToDictionary(i => i.GetProperty("track").GetProperty("id").GetGuid());
        Assert.Equal(new[] { _long, _other }.Order(), items.Keys.Order());
        Assert.Equal(acoustId.ToString(), items[_other].GetProperty("acoustId").GetString());
        Assert.True(items[_other].GetProperty("track").GetProperty("starred").GetBoolean());

        // Again: nothing changes.
        var again = await _admin.PostAsJsonAsync("/rest/v1/me/star-tracks", new { items = new[] { new { acoustId = acoustId.ToString() } } }, ArcanistFactory.Json, Cancel);
        Assert.Equal(0, (await again.Content.ReadFromJsonAsync<JsonElement>(ArcanistFactory.Json, Cancel)).GetProperty("changed").GetInt32());

        var removed = await _admin.PostAsJsonAsync("/rest/v1/me/star-tracks/remove", new { items = new[] { new { acoustId = acoustId.ToString() } } }, ArcanistFactory.Json, Cancel);
        Assert.Equal(1, (await removed.Content.ReadFromJsonAsync<JsonElement>(ArcanistFactory.Json, Cancel)).GetProperty("changed").GetInt32());
        page = await _admin.GetFromJsonAsync<JsonElement>("/rest/v1/me/star-tracks", ArcanistFactory.Json, Cancel);
        Assert.Equal([_long], page.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("track").GetProperty("id").GetGuid()));

        Assert.Equal("validation_failed", (await _admin.GraphQLAsync(
            "mutation { addStarTracks(input: {items: [{acoustId: \"not-an-id\"}]}) { changed } }")).ErrorCode());
    }

    [Fact]
    public async Task StarTracks_changes_are_told_to_who_follows_them()
    {
        var signals = _factory.Services.GetRequiredService<Arcanist.Core.Application.Listening.ListeningSignals>();
        Guid adminId;
        await using (var db = _factory.Services.GetRequiredService<IArcanistDbContextFactory>().CreateDbContext())
            adminId = await db.Users.Where(u => u.UserName == ArcanistFactory.AdminUserName).Select(u => u.Id).SingleAsync(Cancel);
        var seen = new List<Arcanist.Core.Application.Listening.StarTracksChange>();
        using var subscription = signals.StarTracksOf(adminId, TimeProvider.System).Subscribe(new Observer(seen.Add));
        using var other = signals.StarTracksOf(Guid.NewGuid(), TimeProvider.System).Subscribe(new Observer(_ => Assert.Fail("Another user's")));

        await _admin.PutAsJsonAsync($"/rest/v1/tracks/{_long}/star", new { starred = true }, ArcanistFactory.Json, Cancel);
        Assert.Single(seen);
    }

    private sealed class Observer(Action<Arcanist.Core.Application.Listening.StarTracksChange> next) : IObserver<Arcanist.Core.Application.Listening.StarTracksChange>
    {
        public void OnNext(Arcanist.Core.Application.Listening.StarTracksChange value) => next(value);

        public void OnError(Exception error)
        {
        }

        public void OnCompleted()
        {
        }
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
        // When in the day and the week the user listens, in their time zone.
        Assert.Equal((24, 3), (mine.GetProperty("playsByHour").GetArrayLength(), mine.GetProperty("playsByHour").EnumerateArray().Sum(h => h.GetInt32())));
        Assert.Equal((7, 3), (mine.GetProperty("playsByWeekday").GetArrayLength(), mine.GetProperty("playsByWeekday").EnumerateArray().Sum(d => d.GetInt32())));
        Assert.Equal(JsonValueKind.Array, mine.GetProperty("topGenres").ValueKind);

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
    public async Task Statistics_can_be_of_one_library_and_each_library_has_a_summary()
    {
        // A second library with a song of its own.
        var jazz = Directory.CreateTempSubdirectory("arcanist-stats-jazz-").FullName;
        File.WriteAllBytes(Path.Combine(jazz, "swing.wav"), AudioFixtures.Music(bpm: 100, seconds: 70, chordSeed: 2));
        AudioFixtures.Tag(Path.Combine(jazz, "swing.wav"), new FixtureTags("Swing", "Trio", "Smooth", "Trio", TrackNumber: 1));
        var created = await _admin.PostAsJsonAsync("/rest/v1/libraries", new { name = "Jazz", sources = new[] { new { location = jazz } }, visibleToAll = true }, ArcanistFactory.Json, Cancel);
        Assert.True(created.IsSuccessStatusCode, await created.Content.ReadAsStringAsync(Cancel));
        var jazzId = (await created.Content.ReadFromJsonAsync<JsonElement>(ArcanistFactory.Json, Cancel)).GetProperty("id").GetGuid();
        await _factory.WaitForScansAsync(_admin);
        var swing = (await _admin.GetFromJsonAsync<JsonElement>($"/rest/v1/tracks?libraryId={jazzId}", ArcanistFactory.Json, Cancel)).GetProperty("items")[0]
            .GetProperty("id").GetGuid();

        await PlayAsync(_long, 40_000);
        await PlayAsync(_long, 5_000, DateTime.UtcNow.AddMinutes(-10));
        await PlayAsync(swing, 40_000);

        var all = await _admin.GetFromJsonAsync<JsonElement>("/rest/v1/stats/server?period=all", ArcanistFactory.Json, Cancel);
        Assert.Equal((2, 1, 1, 4), (all.GetProperty("listening").GetProperty("plays").GetInt32(), all.GetProperty("listening").GetProperty("skips").GetInt32(),
            all.GetProperty("listening").GetProperty("listeners").GetInt32(), all.GetProperty("addedTracks").GetInt32()));

        var one = await _admin.GetFromJsonAsync<JsonElement>($"/rest/v1/stats/server?period=all&libraryId={jazzId}", ArcanistFactory.Json, Cancel);
        Assert.Equal((1, 0, 1), (one.GetProperty("listening").GetProperty("plays").GetInt32(), one.GetProperty("listening").GetProperty("skips").GetInt32(),
            one.GetProperty("library").GetProperty("tracks").GetInt32()));
        Assert.Equal(jazzId, one.GetProperty("listening").GetProperty("libraryId").GetGuid());
        Assert.Equal("Swing", one.GetProperty("listening").GetProperty("topTracks")[0].GetProperty("track").GetProperty("title").GetString());
        var mine = await _admin.GetFromJsonAsync<JsonElement>($"/rest/v1/stats/listening?period=all&libraryId={jazzId}", ArcanistFactory.Json, Cancel);
        Assert.Equal((1, 1), (mine.GetProperty("plays").GetInt32(), mine.GetProperty("activeDays").GetInt32()));
        Assert.Equal(HttpStatusCode.NotFound, (await _admin.GetAsync($"/rest/v1/stats/server?period=all&libraryId={Guid.NewGuid()}", Cancel)).StatusCode);

        var summaries = (await _admin.GetFromJsonAsync<List<JsonElement>>("/rest/v1/stats/libraries", ArcanistFactory.Json, Cancel))!
            .ToDictionary(s => s.GetProperty("libraryId").GetGuid());
        Assert.Equal((1, 1, 1), (summaries[jazzId].GetProperty("tracks").GetInt32(), summaries[jazzId].GetProperty("plays").GetInt32(),
            summaries[jazzId].GetProperty("listeners").GetInt32()));
        Assert.True(summaries[jazzId].GetProperty("sizeBytes").GetInt64() > 0);
        Assert.Equal((3, 1), summaries.Values.Where(s => s.GetProperty("libraryId").GetGuid() != jazzId)
            .Select(s => (s.GetProperty("tracks").GetInt32(), s.GetProperty("plays").GetInt32())).Single());

        // Without VIEW_ALL_STATS, what the libraries hold, not how they are listened to.
        await _factory.CreateUserAsync("guest");
        var guest = await _factory.CreateAuthenticatedClientAsync("guest");
        var theirs = (await guest.GetFromJsonAsync<List<JsonElement>>("/rest/v1/stats/libraries", ArcanistFactory.Json, Cancel))!;
        Assert.All(theirs, s => Assert.Equal(JsonValueKind.Null, s.GetProperty("plays").ValueKind));

        // Libraries keep the order they were made in, whatever their names become.
        await _admin.PutAsJsonAsync($"/rest/v1/libraries/{jazzId}", new { name = "Aaa Jazz", visibleToAll = true }, ArcanistFactory.Json, Cancel);
        Assert.Equal(["Music", "Aaa Jazz"], (await _admin.GetFromJsonAsync<List<JsonElement>>("/rest/v1/libraries", ArcanistFactory.Json, Cancel))!
            .Select(l => l.GetProperty("name").GetString()));
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
