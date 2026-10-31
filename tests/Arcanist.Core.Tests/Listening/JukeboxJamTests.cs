using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Arcanist.Core.Data.Entities;
using Arcanist.Core.Jams;
using Arcanist.Core.ServerAudio;
using Arcanist.Core.Setup;
using Arcanist.Core.Tests.Scanning;
using Arcanist.Core.Tests.ServerAudio;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;

namespace Arcanist.Core.Tests.Listening;

/// <summary>Jukebox Jams: guests, votes, the host API, the AI DJ and the server's audio.</summary>
public sealed class JukeboxJamTests : IAsyncLifetime
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private readonly string _root = Directory.CreateTempSubdirectory("arcanist-jams-").FullName;
    private readonly FakeTimeProvider _time = new(DateTimeOffset.UtcNow);
    private readonly ArcanistFactory _factory;
    private HttpClient _admin = null!;
    private readonly Dictionary<string, Guid> _tracks = [];
    private Guid _sharedLibrary;

    public JukeboxJamTests()
    {
        JukeboxJamPlayer.FollowInterval = TimeSpan.FromMilliseconds(50);
        _factory = new ArcanistFactory(time: _time);
    }

    public async ValueTask InitializeAsync()
    {
        foreach (var (title, i) in new[] { "Alpha", "Bravo", "Charlie", "Delta" }.Select((t, i) => (t, i)))
            AudioFixtures.WriteWav(Path.Combine(_root, "Shared", $"{title}.wav"), i + 1, new FixtureTags(title, "Band", "Album", "Band", TrackNumber: i + 1));
        AudioFixtures.WriteWav(Path.Combine(_root, "Private", "secret.wav"), 9, new FixtureTags("Secret", "Band", "Hidden", "Band", TrackNumber: 1));

        await _factory.CompleteSetupAsync(
            new SetupLibrary("Shared", [Path.Combine(_root, "Shared")]),
            new SetupLibrary("Private", [Path.Combine(_root, "Private")], VisibleToAll: false));
        _admin = await _factory.CreateAuthenticatedClientAsync();
        await _factory.WaitForScansAsync(_admin);
        await OkAsync(_admin.PutAsJsonAsync("/rest/v1/sharing", new { publicLinks = false, jamGuests = true, guestAddLimit = 2, maxGuests = 100 }, ArcanistFactory.Json, Cancel));

        var page = await _admin.GetFromJsonAsync<JsonElement>("/rest/v1/tracks", ArcanistFactory.Json, Cancel);
        foreach (var track in page.GetProperty("items").EnumerateArray())
            _tracks[track.GetProperty("title").GetString()!] = track.GetProperty("id").GetGuid();
        var libraries = await _admin.GetFromJsonAsync<List<JsonElement>>("/rest/v1/libraries", ArcanistFactory.Json, Cancel);
        _sharedLibrary = libraries!.First(l => l.GetProperty("name").GetString() == "Shared").GetProperty("id").GetGuid();
    }

    public async ValueTask DisposeAsync()
    {
        await _factory.DisposeAsync();
        Directory.Delete(_root, recursive: true);
    }

    private static async Task<JsonElement> OkAsync(Task<HttpResponseMessage> request)
    {
        var response = await request;
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode}: {await response.Content.ReadAsStringAsync(Cancel)}");
        return response.StatusCode == HttpStatusCode.NoContent || response.Content.Headers.ContentLength == 0
            ? default
            : await response.Content.ReadFromJsonAsync<JsonElement>(ArcanistFactory.Json, Cancel);
    }

    private static async Task<(HttpStatusCode Status, string? Code, int? RetryAfter)> FailAsync(Task<HttpResponseMessage> request)
    {
        var response = await request;
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ArcanistFactory.Json, Cancel);
        return (response.StatusCode, body.TryGetProperty("code", out var code) ? code.GetString() : null,
            body.TryGetProperty("retryAfterSeconds", out var retry) ? retry.GetInt32() : null);
    }

    private HttpClient Guest(string token)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Arcanist-Guest", token);
        return client;
    }

    private Task<JsonElement> StartAsync(object input) =>
        OkAsync(_admin.PostAsJsonAsync("/rest/v1/jams", input, ArcanistFactory.Json, Cancel));

    private static List<string> Queue(JsonElement jam) =>
        jam.GetProperty("queue").EnumerateArray().Select(e => e.GetProperty("song").GetProperty("title").GetString()!).ToList();

    private static async Task Until(Func<Task<bool>> condition, string what)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!await condition())
        {
            Assert.True(DateTime.UtcNow < deadline, what);
            await Task.Delay(20, Cancel);
        }
    }

    [Fact]
    public async Task Guests_join_add_songs_within_their_limit_and_vote_every_30_seconds()
    {
        var jam = await StartAsync(new { name = "Friday", playsOn = "CLIENT", voting = true, allowGuests = true, guestLibraryIds = new[] { _sharedLibrary } });
        var code = jam.GetProperty("code").GetString()!;
        Assert.Equal(("HOST", true), (jam.GetProperty("you").GetProperty("participation").GetString(), jam.GetProperty("you").GetProperty("canControl").GetBoolean()));
        Assert.EndsWith($"#/jam/{code}", jam.GetProperty("url").GetString());
        Assert.Equal("JUKEBOX_JAM", (await OkAsync(_admin.GetAsync($"/rest/v1/playlists/{jam.GetProperty("playlistId").GetString()}", Cancel))).GetProperty("type").GetString());

        // Without an account: a visitor sees the Jam, joins by name and searches its libraries.
        var anyone = _factory.CreateClient();
        Assert.Equal("VISITOR", (await OkAsync(anyone.GetAsync($"/rest/v1/public/jams/{code}", Cancel))).GetProperty("you").GetProperty("participation").GetString());
        Assert.Equal((HttpStatusCode.Unauthorized, "unauthorized", null), await FailAsync(anyone.PostAsJsonAsync($"/rest/v1/public/jams/{code}/tracks",
            new { trackIds = new[] { _tracks["Alpha"] } }, ArcanistFactory.Json, Cancel)));
        var session = await OkAsync(anyone.PostAsJsonAsync($"/rest/v1/public/jams/{code}/join", new { name = "Ana" }, ArcanistFactory.Json, Cancel));
        var ana = Guest(session.GetProperty("token").GetString()!);
        var found = await OkAsync(ana.GetAsync($"/rest/v1/public/jams/{code}/search?query=alpha", Cancel));
        Assert.Equal("Alpha", found[0].GetProperty("song").GetProperty("title").GetString());
        Assert.Empty((await OkAsync(ana.GetAsync($"/rest/v1/public/jams/{code}/search?query=secret", Cancel))).EnumerateArray());

        // Songs of the Jam's libraries only, two every 10 minutes.
        Assert.Equal((HttpStatusCode.BadRequest, "validation_failed", null), await FailAsync(ana.PostAsJsonAsync($"/rest/v1/public/jams/{code}/tracks",
            new { trackIds = new[] { _tracks["Secret"] } }, ArcanistFactory.Json, Cancel)));
        var added = await OkAsync(ana.PostAsJsonAsync($"/rest/v1/public/jams/{code}/tracks", new { trackIds = new[] { _tracks["Alpha"], _tracks["Bravo"] } }, ArcanistFactory.Json, Cancel));
        Assert.Equal(["Alpha", "Bravo"], Queue(added));
        Assert.Equal(("Ana", true, 0), (added.GetProperty("queue")[0].GetProperty("addedBy").GetString(), added.GetProperty("queue")[0].GetProperty("addedByYou").GetBoolean(),
            added.GetProperty("you").GetProperty("addsLeft").GetInt32()));
        var limited = await FailAsync(ana.PostAsJsonAsync($"/rest/v1/public/jams/{code}/tracks", new { trackIds = new[] { _tracks["Charlie"] } }, ArcanistFactory.Json, Cancel));
        Assert.Equal((HttpStatusCode.TooManyRequests, "jam_guest_limit"), (limited.Status, limited.Code));
        Assert.InRange(limited.RetryAfter!.Value, 1, 600);

        // A user of the server adds too; votes order the queue, one every 30 seconds.
        await _factory.CreateUserAsync("luis");
        var luis = await _factory.CreateAuthenticatedClientAsync("luis");
        var withCharlie = await OkAsync(luis.PostAsJsonAsync($"/rest/v1/public/jams/{code}/tracks", new { trackIds = new[] { _tracks["Charlie"] } }, ArcanistFactory.Json, Cancel));
        var charlie = withCharlie.GetProperty("queue").EnumerateArray().First(e => e.GetProperty("song").GetProperty("title").GetString() == "Charlie").GetProperty("id").GetString();
        var voted = await OkAsync(ana.PostAsJsonAsync($"/rest/v1/public/jams/{code}/votes", new { entryId = charlie }, ArcanistFactory.Json, Cancel));
        Assert.Equal(["Charlie", "Alpha", "Bravo"], Queue(voted));
        Assert.Equal(2, voted.GetProperty("queue")[0].GetProperty("votes").GetInt32());
        var bravo = voted.GetProperty("queue")[2].GetProperty("id").GetString();
        var tooSoon = await FailAsync(ana.PostAsJsonAsync($"/rest/v1/public/jams/{code}/votes", new { entryId = bravo }, ArcanistFactory.Json, Cancel));
        Assert.Equal((HttpStatusCode.TooManyRequests, "jam_vote_too_soon", 30), tooSoon);
        _time.Advance(TimeSpan.FromSeconds(31));
        await OkAsync(ana.PostAsJsonAsync($"/rest/v1/public/jams/{code}/votes", new { entryId = bravo }, ArcanistFactory.Json, Cancel));
        // Bravo now has as many votes as Charlie, and came first.
        Assert.Equal(["Bravo", "Charlie", "Alpha"], Queue(await OkAsync(luis.GetAsync($"/rest/v1/public/jams/{code}", Cancel))));
        var luisVote = await OkAsync(luis.PostAsJsonAsync($"/rest/v1/public/jams/{code}/votes", new { entryId = charlie }, ArcanistFactory.Json, Cancel));
        Assert.Equal(["Charlie", "Bravo", "Alpha"], Queue(luisVote));
        Assert.Equal("MEMBER", (await OkAsync(luis.GetAsync($"/rest/v1/public/jams/{code}", Cancel))).GetProperty("you").GetProperty("participation").GetString());

        // The host's client plays what the server says, and what plays leaves the queue.
        await OkAsync(_admin.PostAsJsonAsync($"/rest/v1/public/jams/{code}/host", new { deviceId = "tab-1", kind = "WEB", playerId = "webamp", name = "Webamp" }, ArcanistFactory.Json, Cancel));
        var next = await OkAsync(_admin.GetAsync($"/rest/v1/public/jams/{code}/next", Cancel));
        Assert.Equal("Charlie", next.GetProperty("track").GetProperty("title").GetString());
        Assert.False(string.IsNullOrEmpty(next.GetProperty("track").GetProperty("streamUrl").GetString()));
        Assert.Equal((HttpStatusCode.Conflict, "jam_not_host", null), await FailAsync(_admin.PostAsJsonAsync($"/rest/v1/public/jams/{code}/playback",
            new { deviceId = "tab-2", entryId = next.GetProperty("entryId").GetString(), state = "PLAYING", positionMs = 0 }, ArcanistFactory.Json, Cancel)));
        var playing = await OkAsync(_admin.PostAsJsonAsync($"/rest/v1/public/jams/{code}/playback",
            new { deviceId = "tab-1", entryId = next.GetProperty("entryId").GetString(), state = "PLAYING", positionMs = 1200 }, ArcanistFactory.Json, Cancel));
        Assert.Equal("Charlie", playing.GetProperty("nowPlaying").GetProperty("entry").GetProperty("song").GetProperty("title").GetString());
        Assert.Equal(["Bravo", "Alpha"], Queue(playing));
        Assert.True(playing.GetProperty("hostClient").GetProperty("online").GetBoolean());

        // The host takes a guest out, and ends the Jam: what played stays as a playlist.
        await OkAsync(_admin.DeleteAsync($"/rest/v1/public/jams/{code}/guests/{session.GetProperty("guestId").GetString()}", Cancel));
        Assert.Equal((HttpStatusCode.Forbidden, "jam_guest_removed", null), await FailAsync(ana.GetAsync($"/rest/v1/public/jams/{code}", Cancel)));
        var ended = await OkAsync(_admin.PostAsync($"/rest/v1/public/jams/{code}/end", null, Cancel));
        Assert.NotEqual(JsonValueKind.Null, ended.GetProperty("endedAt").ValueKind);
        Assert.Equal((HttpStatusCode.Conflict, "jam_ended", null), await FailAsync(luis.PostAsJsonAsync($"/rest/v1/public/jams/{code}/tracks",
            new { trackIds = new[] { _tracks["Delta"] } }, ArcanistFactory.Json, Cancel)));
        var playlist = await OkAsync(_admin.GetAsync($"/rest/v1/playlists/{jam.GetProperty("playlistId").GetString()}", Cancel));
        Assert.Equal(("NORMAL", 1), (playlist.GetProperty("type").GetString(), playlist.GetProperty("trackCount").GetInt32()));
    }

    [Fact]
    public async Task Guests_browse_the_jams_artists_and_albums_and_replace_what_plays_when_allowed()
    {
        var jam = await StartAsync(new { name = "Party", playsOn = "CLIENT", voting = false, allowGuests = true, guestLibraryIds = new[] { _sharedLibrary } });
        var code = jam.GetProperty("code").GetString()!;
        Assert.False(jam.GetProperty("allowReplace").GetBoolean());
        var session = await OkAsync(_factory.CreateClient().PostAsJsonAsync($"/rest/v1/public/jams/{code}/join", new { name = "Ana" }, ArcanistFactory.Json, Cancel));
        var ana = Guest(session.GetProperty("token").GetString()!);

        // Browsing: only what is in the Jam's libraries (the private album is not).
        var artists = await OkAsync(ana.GetAsync($"/rest/v1/public/jams/{code}/artists", Cancel));
        var band = Assert.Single(artists.GetProperty("items").EnumerateArray());
        Assert.Equal(("Band", 1), (band.GetProperty("name").GetString(), band.GetProperty("albums").GetInt32()));
        var albums = await OkAsync(ana.GetAsync($"/rest/v1/public/jams/{code}/albums?artistId={band.GetProperty("id").GetString()}", Cancel));
        var album = Assert.Single(albums.GetProperty("items").EnumerateArray());
        Assert.Equal(("Album", 4), (album.GetProperty("title").GetString(), album.GetProperty("songs").GetInt32()));
        Assert.Empty((await OkAsync(ana.GetAsync($"/rest/v1/public/jams/{code}/albums?search=hidden", Cancel))).GetProperty("items").EnumerateArray());
        var songs = await OkAsync(ana.GetAsync($"/rest/v1/public/jams/{code}/albums/{album.GetProperty("id").GetString()}/songs", Cancel));
        Assert.Equal(["Alpha", "Bravo", "Charlie", "Delta"], songs.EnumerateArray().Select(s => s.GetProperty("song").GetProperty("title").GetString()));
        Assert.Equal((HttpStatusCode.Unauthorized, "unauthorized", null), await FailAsync(_factory.CreateClient().GetAsync($"/rest/v1/public/jams/{code}/artists", Cancel)));

        // Something plays on the host's tab.
        await OkAsync(_admin.PostAsJsonAsync($"/rest/v1/public/jams/{code}/tracks", new { trackIds = new[] { _tracks["Alpha"], _tracks["Bravo"] } }, ArcanistFactory.Json, Cancel));
        await OkAsync(_admin.PostAsJsonAsync($"/rest/v1/public/jams/{code}/host", new { deviceId = "tab-1", kind = "WEB" }, ArcanistFactory.Json, Cancel));
        var next = await OkAsync(_admin.GetAsync($"/rest/v1/public/jams/{code}/next", Cancel));
        await OkAsync(_admin.PostAsJsonAsync($"/rest/v1/public/jams/{code}/playback",
            new { deviceId = "tab-1", entryId = next.GetProperty("entryId").GetString(), state = "PLAYING", positionMs = 0 }, ArcanistFactory.Json, Cancel));

        // Replacing it is off: the host turns it on.
        var guestView = await OkAsync(ana.GetAsync($"/rest/v1/public/jams/{code}", Cancel));
        Assert.False(guestView.GetProperty("you").GetProperty("canReplace").GetBoolean());
        Assert.Equal((HttpStatusCode.Conflict, "jam_replace_off", null), await FailAsync(ana.PostAsJsonAsync($"/rest/v1/public/jams/{code}/now-playing",
            new { trackId = _tracks["Delta"] }, ArcanistFactory.Json, Cancel)));
        Assert.True((await OkAsync(_admin.PutAsJsonAsync($"/rest/v1/public/jams/{code}", new { allowReplace = true }, ArcanistFactory.Json, Cancel)))
            .GetProperty("allowReplace").GetBoolean());

        // Ana's song plays now: it is the next one the host's client takes, whatever the queue said.
        var replaced = await OkAsync(ana.PostAsJsonAsync($"/rest/v1/public/jams/{code}/now-playing", new { trackId = _tracks["Delta"] }, ArcanistFactory.Json, Cancel));
        Assert.Equal("Delta", Queue(replaced)[0]);
        Assert.True(replaced.GetProperty("queue")[0].GetProperty("pinned").GetBoolean());
        Assert.NotEqual(JsonValueKind.Null, replaced.GetProperty("you").GetProperty("nextReplaceAt").ValueKind);
        Assert.Equal("Delta", (await OkAsync(_admin.GetAsync($"/rest/v1/public/jams/{code}/next", Cancel))).GetProperty("track").GetProperty("title").GetString());

        // Not again for a minute (the host can), and it counts against a guest's songs.
        Assert.Equal((HttpStatusCode.TooManyRequests, "jam_replace_too_soon", 60), await FailAsync(ana.PostAsJsonAsync($"/rest/v1/public/jams/{code}/now-playing",
            new { trackId = _tracks["Charlie"] }, ArcanistFactory.Json, Cancel)));
        await OkAsync(_admin.PostAsJsonAsync($"/rest/v1/public/jams/{code}/now-playing", new { trackId = _tracks["Charlie"] }, ArcanistFactory.Json, Cancel));
        _time.Advance(TimeSpan.FromSeconds(61));
        Assert.Equal(0, (await OkAsync(ana.GetAsync($"/rest/v1/public/jams/{code}", Cancel))).GetProperty("you").GetProperty("addsLeft").GetInt32() - 1);
        await OkAsync(ana.PostAsJsonAsync($"/rest/v1/public/jams/{code}/now-playing", new { trackId = _tracks["Bravo"] }, ArcanistFactory.Json, Cancel));
        var limited = await FailAsync(ana.PostAsJsonAsync($"/rest/v1/public/jams/{code}/tracks", new { trackIds = new[] { _tracks["Alpha"] } }, ArcanistFactory.Json, Cancel));
        Assert.Equal((HttpStatusCode.TooManyRequests, "jam_guest_limit"), (limited.Status, limited.Code));
    }
    [Fact]
    public async Task Guests_need_the_servers_setting_and_the_hosts_permission()
    {
        await _factory.CreateUserAsync("plain");
        var plain = await _factory.CreateAuthenticatedClientAsync("plain");
        Assert.Equal((HttpStatusCode.Forbidden, "jam_guests_disabled", null), await FailAsync(plain.PostAsJsonAsync("/rest/v1/jams",
            new { name = "Mine", playsOn = "CLIENT", voting = false, allowGuests = true }, ArcanistFactory.Json, Cancel)));
        var closed = await OkAsync(plain.PostAsJsonAsync("/rest/v1/jams", new { name = "Mine", playsOn = "CLIENT", voting = false, allowGuests = false }, ArcanistFactory.Json, Cancel));
        Assert.Equal(HttpStatusCode.NotFound, (await _factory.CreateClient().GetAsync($"/rest/v1/public/jams/{closed.GetProperty("code").GetString()}", Cancel)).StatusCode);

        var open = await StartAsync(new { name = "Open", playsOn = "CLIENT", voting = false, allowGuests = true });
        var code = open.GetProperty("code").GetString()!;
        Assert.Equal(HttpStatusCode.OK, (await _factory.CreateClient().GetAsync($"/rest/v1/public/jams/{code}", Cancel)).StatusCode);
        await OkAsync(_admin.PutAsJsonAsync("/rest/v1/sharing", new { publicLinks = false, jamGuests = false, guestAddLimit = 2, maxGuests = 100 }, ArcanistFactory.Json, Cancel));
        Assert.Equal(HttpStatusCode.NotFound, (await _factory.CreateClient().GetAsync($"/rest/v1/public/jams/{code}", Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await plain.GetAsync($"/rest/v1/public/jams/{code}", Cancel)).StatusCode);
    }

    [Fact]
    public async Task An_AI_DJ_session_is_shown_to_everyone_and_chooses_the_order()
    {
        var jam = await StartAsync(new { name = "DJ night", playsOn = "CLIENT", voting = true, allowGuests = true });
        var code = jam.GetProperty("code").GetString()!;
        var guest = Guest((await OkAsync(_factory.CreateClient().PostAsJsonAsync($"/rest/v1/public/jams/{code}/join", new { name = "Eva" }, ArcanistFactory.Json, Cancel)))
            .GetProperty("token").GetString()!);
        await OkAsync(guest.PostAsJsonAsync($"/rest/v1/public/jams/{code}/tracks", new { trackIds = new[] { _tracks["Alpha"] } }, ArcanistFactory.Json, Cancel));

        // Ultimate Desktop hosts it: its AI DJ is the DJ, and everyone sees so.
        await OkAsync(_admin.PostAsJsonAsync($"/rest/v1/public/jams/{code}/host", new { deviceId = "desktop", kind = "DESKTOP", name = "ULTIMATE", version = "3.0" }, ArcanistFactory.Json, Cancel));
        Assert.Equal(HttpStatusCode.Forbidden, (await guest.PutAsJsonAsync($"/rest/v1/public/jams/{code}/dj", new { kind = "ULTIMATE_AI_DJ" }, ArcanistFactory.Json, Cancel)).StatusCode);
        await OkAsync(_admin.PutAsJsonAsync($"/rest/v1/public/jams/{code}/dj", new { kind = "ULTIMATE_AI_DJ", name = "Nova", message = "Warming up" }, ArcanistFactory.Json, Cancel));
        var seen = await OkAsync(guest.GetAsync($"/rest/v1/public/jams/{code}", Cancel));
        Assert.Equal(("ULTIMATE_AI_DJ", "Nova", "Warming up"), (seen.GetProperty("dj").GetProperty("kind").GetString(),
            seen.GetProperty("dj").GetProperty("name").GetString(), seen.GetProperty("dj").GetProperty("message").GetString()));
        Assert.Equal("DESKTOP", seen.GetProperty("hostClient").GetProperty("kind").GetString());

        // The DJ adds its own songs (no limit, no vote) and plays one out of order, or one only Desktop has.
        Assert.Equal(HttpStatusCode.Forbidden, (await guest.PostAsJsonAsync($"/rest/v1/public/jams/{code}/tracks",
            new { trackIds = new[] { _tracks["Bravo"] }, source = "DJ" }, ArcanistFactory.Json, Cancel)).StatusCode);
        var withDj = await OkAsync(_admin.PostAsJsonAsync($"/rest/v1/public/jams/{code}/tracks", new { trackIds = new[] { _tracks["Delta"] }, source = "DJ" }, ArcanistFactory.Json, Cancel));
        var delta = withDj.GetProperty("queue").EnumerateArray().First(e => e.GetProperty("addedByDj").GetBoolean());
        Assert.Equal((0, JsonValueKind.Null), (delta.GetProperty("votes").GetInt32(), delta.GetProperty("addedBy").ValueKind));
        var played = await OkAsync(_admin.PostAsJsonAsync($"/rest/v1/public/jams/{code}/playback",
            new { deviceId = "desktop", entryId = delta.GetProperty("id").GetString(), state = "PLAYING", positionMs = 0 }, ArcanistFactory.Json, Cancel));
        Assert.Equal(["Alpha"], Queue(played));
        var external = await OkAsync(_admin.PostAsJsonAsync($"/rest/v1/public/jams/{code}/playback",
            new { deviceId = "desktop", external = new { title = "Only on my PC", artists = new[] { "Someone" }, durationMs = 180000 }, state = "PLAYING", positionMs = 0 },
            ArcanistFactory.Json, Cancel));
        Assert.Equal("Only on my PC", external.GetProperty("nowPlaying").GetProperty("external").GetProperty("title").GetString());

        // With the host's client gone for 5 minutes, the DJ is taken off.
        _time.Advance(TimeSpan.FromMinutes(6));
        await Until(async () => (await OkAsync(_admin.GetAsync($"/rest/v1/public/jams/{code}", Cancel))).GetProperty("dj").ValueKind == JsonValueKind.Null,
            "the DJ of a host gone is taken off");
    }
}

/// <summary>A Jukebox Jam on the server's audio (with the real clock: the player waits on it).</summary>
public sealed class JukeboxJamServerAudioTests : IAsyncLifetime
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private readonly string _root = Directory.CreateTempSubdirectory("arcanist-jams-audio-").FullName;
    private readonly FakeServerAudioOutput _output = new();
    private readonly ArcanistFactory _factory;
    private HttpClient _admin = null!;
    private readonly Dictionary<string, Guid> _tracks = [];

    public JukeboxJamServerAudioTests()
    {
        JukeboxJamPlayer.FollowInterval = TimeSpan.FromMilliseconds(50);
        ServerAudioPlayer.FollowInterval = TimeSpan.FromMilliseconds(20);
        _factory = new ArcanistFactory(services: services => services.Replace(ServiceDescriptor.Singleton<IServerAudioOutput>(_output)));
    }

    public async ValueTask InitializeAsync()
    {
        foreach (var (title, i) in new[] { "Alpha", "Bravo", "Charlie" }.Select((t, i) => (t, i)))
            AudioFixtures.WriteWav(Path.Combine(_root, $"{title}.wav"), i + 1, new FixtureTags(title, "Band", "Album", "Band", TrackNumber: i + 1));
        await _factory.CompleteSetupAsync(new SetupLibrary("Music", [_root]));
        _admin = await _factory.CreateAuthenticatedClientAsync();
        await _factory.WaitForScansAsync(_admin);
        var page = await _admin.GetFromJsonAsync<JsonElement>("/rest/v1/tracks", ArcanistFactory.Json, Cancel);
        foreach (var track in page.GetProperty("items").EnumerateArray())
            _tracks[track.GetProperty("title").GetString()!] = track.GetProperty("id").GetGuid();
    }

    public async ValueTask DisposeAsync()
    {
        await _factory.DisposeAsync();
        Directory.Delete(_root, recursive: true);
    }

    private static async Task<JsonElement> OkAsync(Task<HttpResponseMessage> request)
    {
        var response = await request;
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode}: {await response.Content.ReadAsStringAsync(Cancel)}");
        return await response.Content.ReadFromJsonAsync<JsonElement>(ArcanistFactory.Json, Cancel);
    }

    private Task<JsonElement> StartAsync(object input) =>
        OkAsync(_admin.PostAsJsonAsync("/rest/v1/jams", input, ArcanistFactory.Json, Cancel));

    private static async Task Until(Func<Task<bool>> condition, string what)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!await condition())
        {
            Assert.True(DateTime.UtcNow < deadline, what);
            await Task.Delay(20, Cancel);
        }
    }

    [Fact]
    public async Task A_jam_on_the_servers_audio_plays_what_is_added()
    {
        await OkAsync(_admin.PutAsJsonAsync("/rest/v1/extensions/server-audio/enabled", new { enabled = true }, ArcanistFactory.Json, Cancel));
        await Until(() => Task.FromResult(_output.Sink is not null), "the device opens");
        var jam = await StartAsync(new { name = "Living room", playsOn = "SERVER", voting = false, allowGuests = false });
        var code = jam.GetProperty("code").GetString()!;
        Assert.Equal(("SERVER", "server"), (jam.GetProperty("playsOn").GetString(), jam.GetProperty("rendererId").GetString()));

        await OkAsync(_admin.PostAsJsonAsync($"/rest/v1/public/jams/{code}/tracks", new { trackIds = new[] { _tracks["Alpha"], _tracks["Bravo"] } }, ArcanistFactory.Json, Cancel));
        await Until(async () => (await OkAsync(_admin.GetAsync($"/rest/v1/public/jams/{code}", Cancel))).GetProperty("nowPlaying").GetProperty("entry") is
            { ValueKind: JsonValueKind.Object } entry && entry.GetProperty("song").GetProperty("title").GetString() == "Alpha", "the first song plays");
        await Until(() => Task.FromResult(_output.Sink?.Queued is not null), "the next song is handed over ahead");

        _output.Sink!.Finish();
        await Until(async () => (await OkAsync(_admin.GetAsync($"/rest/v1/public/jams/{code}", Cancel))).GetProperty("nowPlaying").GetProperty("entry") is
            { ValueKind: JsonValueKind.Object } entry && entry.GetProperty("song").GetProperty("title").GetString() == "Bravo", "the next song plays");
    }
    private async Task<(string? Entry, string? External)> PlayingAsync(string code)
    {
        var now = (await OkAsync(_admin.GetAsync($"/rest/v1/public/jams/{code}", Cancel))).GetProperty("nowPlaying");
        return (now.GetProperty("entry") is { ValueKind: JsonValueKind.Object } entry ? entry.GetProperty("song").GetProperty("title").GetString() : null,
            now.GetProperty("external") is { ValueKind: JsonValueKind.Object } external ? external.GetProperty("title").GetString() : null);
    }

    [Fact]
    public async Task A_jam_keeps_what_the_host_already_plays_and_its_songs_go_right_after_the_one_playing()
    {
        await OkAsync(_admin.PutAsJsonAsync("/rest/v1/extensions/server-audio/enabled", new { enabled = true }, ArcanistFactory.Json, Cancel));
        await Until(() => Task.FromResult(_output.Sink is not null), "the device opens");
        // The host plays their own queue there: Alpha, then Bravo.
        await OkAsync(_admin.PostAsJsonAsync("/rest/v1/dlna/play", new { rendererId = "server", trackIds = new[] { _tracks["Alpha"], _tracks["Bravo"] }, startIndex = 0 },
            ArcanistFactory.Json, Cancel));
        var code = (await StartAsync(new { name = "Party", playsOn = "SERVER", voting = false, allowGuests = false })).GetProperty("code").GetString()!;

        // What plays is the host's, and keeps playing.
        await Until(async () => await PlayingAsync(code) == (null, "Alpha"), "the host's song is shown as theirs");

        // A song added to the Jam goes right after the one playing, before the host's own next one.
        await OkAsync(_admin.PostAsJsonAsync($"/rest/v1/public/jams/{code}/tracks", new { trackIds = new[] { _tracks["Charlie"] } }, ArcanistFactory.Json, Cancel));
        await Until(async () => (await OkAsync(_admin.GetAsync("/rest/v1/dlna/renderers/server", Cancel))).GetProperty("queue").GetArrayLength() == 3,
            "the Jam's song is put in the renderer's queue");
        var queue = (await OkAsync(_admin.GetAsync("/rest/v1/dlna/renderers/server", Cancel))).GetProperty("queue").EnumerateArray().Select(t => t.GetGuid()).ToList();
        Assert.Equal([_tracks["Alpha"], _tracks["Charlie"], _tracks["Bravo"]], queue);

        _output.Sink!.Finish();
        await Until(async () => await PlayingAsync(code) == ("Charlie", null), "the Jam's song plays next");

        // The Jam has nothing more: the host's own queue goes on.
        _output.Sink!.Finish();
        await Until(async () => await PlayingAsync(code) == (null, "Bravo"), "the host's queue goes on");
    }

    [Fact]
    public async Task A_jam_that_is_not_kept_leaves_no_playlist_when_it_ends()
    {
        var kept = (await StartAsync(new { name = "Kept", playsOn = "CLIENT", voting = false, allowGuests = false })).GetProperty("code").GetString()!;
        var gone = await StartAsync(new { name = "Gone", playsOn = "CLIENT", voting = false, allowGuests = false, saveAsPlaylist = false });
        Assert.False(gone.GetProperty("saveAsPlaylist").GetBoolean());
        var goneCode = gone.GetProperty("code").GetString()!;
        foreach (var code in new[] { kept, goneCode })
        {
            await OkAsync(_admin.PostAsJsonAsync($"/rest/v1/public/jams/{code}/tracks", new { trackIds = new[] { _tracks["Alpha"] } }, ArcanistFactory.Json, Cancel));
            await OkAsync(_admin.PostAsJsonAsync($"/rest/v1/public/jams/{code}/end", new { }, ArcanistFactory.Json, Cancel));
        }

        var names = (await _admin.GraphQLAsync("query { playlists { name } }")).GetProperty("data").GetProperty("playlists")
            .EnumerateArray().Select(p => p.GetProperty("name").GetString()).ToList();
        Assert.Contains("Kept", names);
        Assert.DoesNotContain("Gone", names);
        Assert.Equal(HttpStatusCode.NotFound, (await _admin.GetAsync($"/rest/v1/public/jams/{goneCode}", Cancel)).StatusCode);
    }

    [Fact]
    public async Task A_jams_link_opens_the_servers_own_client_when_no_player_is_one()
    {
        var jam = await StartAsync(new { name = "Link", playsOn = "CLIENT", voting = false, allowGuests = false });
        var url = jam.GetProperty("url").GetString()!;
        Assert.Equal($"/players/jam/#/jam/{jam.GetProperty("code").GetString()}", url);

        var page = await _admin.GetAsync("/players/jam/", Cancel);
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Contains("<base href=\"/players/jam/\"", await page.Content.ReadAsStringAsync(Cancel));
        Assert.Equal(HttpStatusCode.OK, (await _admin.GetAsync("/players/jam/jam.js", Cancel)).StatusCode);
        // Not a player of the list.
        var players = await _admin.GraphQLAsync("query { availablePlayers { id } }");
        Assert.DoesNotContain(players.GetProperty("data").GetProperty("availablePlayers").EnumerateArray(), p => p.GetProperty("id").GetString() == "jam");
    }
}
