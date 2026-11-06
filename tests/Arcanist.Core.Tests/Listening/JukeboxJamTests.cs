using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Arcanist.Core.Application.Jams;
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
        // A new server deletes a Jam's playlist when it ends (see the test about it); these keep it to look at it after.
        Assert.True((await OkAsync(_admin.GetAsync("/rest/v1/sharing", Cancel))).GetProperty("jamDeletePlaylist").GetBoolean());
        await OkAsync(_admin.PutAsJsonAsync("/rest/v1/sharing", new { publicLinks = false, jamGuests = true, guestAddLimit = 2, maxGuests = 100, jamDeletePlaylist = false },
            ArcanistFactory.Json, Cancel));

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
    public async Task Guests_join_add_songs_within_their_limit_and_vote_once_per_song()
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

        // A user of the server adds too. Votes order what comes after the next song, which stays: it may be on its way.
        await _factory.CreateUserAsync("luis");
        var luis = await _factory.CreateAuthenticatedClientAsync("luis");
        var withCharlie = await OkAsync(luis.PostAsJsonAsync($"/rest/v1/public/jams/{code}/tracks", new { trackIds = new[] { _tracks["Charlie"] } }, ArcanistFactory.Json, Cancel));
        Assert.Equal(["Alpha", "Bravo", "Charlie"], Queue(withCharlie));
        Assert.True(withCharlie.GetProperty("queue")[0].GetProperty("pinned").GetBoolean());
        var charlie = withCharlie.GetProperty("queue")[2].GetProperty("id").GetString();
        var voted = await OkAsync(ana.PostAsJsonAsync($"/rest/v1/public/jams/{code}/votes", new { entryId = charlie }, ArcanistFactory.Json, Cancel));
        Assert.Equal(["Alpha", "Charlie", "Bravo"], Queue(voted));
        Assert.Equal((2, true), (voted.GetProperty("queue")[1].GetProperty("votes").GetInt32(), voted.GetProperty("queue")[1].GetProperty("votedByYou").GetBoolean()));

        // One vote per person and song, without waiting: voting again takes it back.
        var charlieBack = await OkAsync(ana.PostAsJsonAsync($"/rest/v1/public/jams/{code}/votes", new { entryId = charlie }, ArcanistFactory.Json, Cancel));
        var charlieEntry = charlieBack.GetProperty("queue").EnumerateArray().First(e => e.GetProperty("id").GetString() == charlie);
        Assert.Equal((1, false), (charlieEntry.GetProperty("votes").GetInt32(), charlieEntry.GetProperty("votedByYou").GetBoolean()));
        var again = await OkAsync(ana.PostAsJsonAsync($"/rest/v1/public/jams/{code}/votes", new { entryId = charlie }, ArcanistFactory.Json, Cancel));
        Assert.Equal(["Alpha", "Charlie", "Bravo"], Queue(again));
        Assert.Equal("MEMBER", (await OkAsync(luis.GetAsync($"/rest/v1/public/jams/{code}", Cancel))).GetProperty("you").GetProperty("participation").GetString());

        // The host's client plays what the server says, and what plays leaves the queue.
        await OkAsync(_admin.PostAsJsonAsync($"/rest/v1/public/jams/{code}/host", new { deviceId = "tab-1", kind = "WEB", playerId = "webamp", name = "Webamp" }, ArcanistFactory.Json, Cancel));
        var next = await OkAsync(_admin.GetAsync($"/rest/v1/public/jams/{code}/next", Cancel));
        Assert.Equal("Alpha", next.GetProperty("track").GetProperty("title").GetString());
        Assert.False(string.IsNullOrEmpty(next.GetProperty("track").GetProperty("streamUrl").GetString()));
        Assert.Equal((HttpStatusCode.Conflict, "jam_not_host", null), await FailAsync(_admin.PostAsJsonAsync($"/rest/v1/public/jams/{code}/playback",
            new { deviceId = "tab-2", entryId = next.GetProperty("entryId").GetString(), state = "PLAYING", positionMs = 0 }, ArcanistFactory.Json, Cancel)));
        var playing = await OkAsync(_admin.PostAsJsonAsync($"/rest/v1/public/jams/{code}/playback",
            new { deviceId = "tab-1", entryId = next.GetProperty("entryId").GetString(), state = "PLAYING", positionMs = 1200 }, ArcanistFactory.Json, Cancel));
        Assert.Equal("Alpha", playing.GetProperty("nowPlaying").GetProperty("entry").GetProperty("song").GetProperty("title").GetString());
        Assert.Equal(["Charlie", "Bravo"], Queue(playing));
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
        var jam = await StartAsync(new { name = "Party", playsOn = "CLIENT", voting = false, allowGuests = true, allowReplace = false, guestLibraryIds = new[] { _sharedLibrary } });
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
        Assert.Equal("Delta", (await OkAsync(_admin.GetAsync($"/rest/v1/public/jams/{code}/next", Cancel))).GetProperty("track").GetProperty("title").GetString());

        // Without waiting; for a guest it counts as one of their songs.
        Assert.Equal(1, replaced.GetProperty("you").GetProperty("addsLeft").GetInt32());
        await OkAsync(ana.PostAsJsonAsync($"/rest/v1/public/jams/{code}/now-playing", new { trackId = _tracks["Charlie"] }, ArcanistFactory.Json, Cancel));
        var limited = await FailAsync(ana.PostAsJsonAsync($"/rest/v1/public/jams/{code}/tracks", new { trackIds = new[] { _tracks["Alpha"] } }, ArcanistFactory.Json, Cancel));
        Assert.Equal((HttpStatusCode.TooManyRequests, "jam_guest_limit"), (limited.Status, limited.Code));
    }

    [Fact]
    public async Task Anyone_taking_part_changes_what_plays_until_the_host_says_otherwise()
    {
        // Only a name: shared, without voting or guests.
        var jam = await StartAsync(new { name = "Sunday", playsOn = "CLIENT" });
        var code = jam.GetProperty("code").GetString()!;
        Assert.Equal((true, false, false, true), (jam.GetProperty("allowReplace").GetBoolean(), jam.GetProperty("voting").GetBoolean(),
            jam.GetProperty("allowGuests").GetBoolean(), jam.GetProperty("suggestions").GetBoolean()));
        await _factory.CreateUserAsync("luis");
        var luis = await _factory.CreateAuthenticatedClientAsync("luis");

        // Before joining it, they are nobody; once in, they can do what the host does with the music.
        Assert.False((await OkAsync(luis.GetAsync($"/rest/v1/public/jams/{code}", Cancel))).GetProperty("you").GetProperty("canControl").GetBoolean());
        await OkAsync(luis.PostAsJsonAsync($"/rest/v1/public/jams/{code}/join", new { }, ArcanistFactory.Json, Cancel));
        var added = await OkAsync(luis.PostAsJsonAsync($"/rest/v1/public/jams/{code}/tracks", new { trackIds = new[] { _tracks["Alpha"], _tracks["Bravo"], _tracks["Charlie"] } }, ArcanistFactory.Json, Cancel));
        Assert.Equal((true, false), (added.GetProperty("you").GetProperty("canControl").GetBoolean(), added.GetProperty("you").GetProperty("canManage").GetBoolean()));
        var moved = await OkAsync(luis.PostAsJsonAsync($"/rest/v1/public/jams/{code}/control",
            new { action = "MOVE", entryId = added.GetProperty("queue")[2].GetProperty("id").GetString(), position = 0 }, ArcanistFactory.Json, Cancel));
        Assert.Equal(["Charlie", "Alpha", "Bravo"], Queue(moved));
        var removed = await OkAsync(luis.PostAsJsonAsync($"/rest/v1/public/jams/{code}/control",
            new { action = "REMOVE", entryId = moved.GetProperty("queue")[2].GetProperty("id").GetString() }, ArcanistFactory.Json, Cancel));
        Assert.Equal(["Charlie", "Alpha"], Queue(removed));
        await OkAsync(luis.PostAsJsonAsync($"/rest/v1/public/jams/{code}/control", new { action = "SKIP" }, ArcanistFactory.Json, Cancel));

        // Guests too, but managing the Jam stays with the host.
        await OkAsync(_admin.PutAsJsonAsync($"/rest/v1/public/jams/{code}", new { allowGuests = true }, ArcanistFactory.Json, Cancel));
        var session = await OkAsync(_factory.CreateClient().PostAsJsonAsync($"/rest/v1/public/jams/{code}/join", new { name = "Ana" }, ArcanistFactory.Json, Cancel));
        var ana = Guest(session.GetProperty("token").GetString()!);
        await OkAsync(ana.PostAsJsonAsync($"/rest/v1/public/jams/{code}/control", new { action = "PAUSE" }, ArcanistFactory.Json, Cancel));
        var guestId = session.GetProperty("guestId").GetString();
        Assert.Equal(HttpStatusCode.Forbidden, (await luis.DeleteAsync($"/rest/v1/public/jams/{code}/guests/{guestId}", Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await luis.PutAsJsonAsync($"/rest/v1/public/jams/{code}", new { allowReplace = false }, ArcanistFactory.Json, Cancel)).StatusCode);
        Assert.Equal(JsonValueKind.Null, (await OkAsync(luis.GetAsync($"/rest/v1/public/jams/{code}", Cancel))).GetProperty("guests").ValueKind);

        // With the host's switch off, they only add songs.
        await OkAsync(_admin.PutAsJsonAsync($"/rest/v1/public/jams/{code}", new { allowReplace = false }, ArcanistFactory.Json, Cancel));
        Assert.False((await OkAsync(luis.GetAsync($"/rest/v1/public/jams/{code}", Cancel))).GetProperty("you").GetProperty("canControl").GetBoolean());
        Assert.Equal(HttpStatusCode.Forbidden, (await luis.PostAsJsonAsync($"/rest/v1/public/jams/{code}/control", new { action = "SKIP" }, ArcanistFactory.Json, Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await ana.PostAsJsonAsync($"/rest/v1/public/jams/{code}/control", new { action = "RESUME" }, ArcanistFactory.Json, Cancel)).StatusCode);
    }

    [Fact]
    public async Task A_jam_starts_from_what_plays_and_the_queue_runs_on_with_suggestions_for_the_group()
    {
        // What the host was playing becomes the queue; a song it cannot have stays out.
        var jam = await StartAsync(new { name = "Now", playsOn = "CLIENT", guestLibraryIds = new[] { _sharedLibrary },
            queueTrackIds = new[] { _tracks["Alpha"], _tracks["Secret"], _tracks["Bravo"] } });
        var code = jam.GetProperty("code").GetString()!;
        Assert.Equal(["Alpha", "Bravo"], Queue(jam));
        await OkAsync(_admin.PostAsJsonAsync($"/rest/v1/public/jams/{code}/host", new { deviceId = "tab", kind = "WEB" }, ArcanistFactory.Json, Cancel));
        await _factory.CreateUserAsync("luis");
        var luis = await _factory.CreateAuthenticatedClientAsync("luis");
        await OkAsync(luis.PostAsJsonAsync($"/rest/v1/public/jams/{code}/join", new { }, ArcanistFactory.Json, Cancel));
        await OkAsync(luis.PutAsJsonAsync($"/rest/v1/tracks/{_tracks["Charlie"]}/rating", new { rating = 5 }, ArcanistFactory.Json, Cancel));

        async Task<JsonElement> PlayAsync(string title, int? ownLeft) => await OkAsync(_admin.PostAsJsonAsync($"/rest/v1/public/jams/{code}/playback", new
        {
            deviceId = "tab",
            entryId = (await OkAsync(_admin.GetAsync($"/rest/v1/public/jams/{code}", Cancel))).GetProperty("queue").EnumerateArray()
                .First(e => e.GetProperty("song").GetProperty("title").GetString() == title).GetProperty("id").GetString(),
            state = "PLAYING",
            positionMs = 0,
            ownSongsLeft = ownLeft,
        }, ArcanistFactory.Json, Cancel));

        // While songs wait, none are suggested; nor while the host has songs of their own coming, or does not say.
        Assert.Equal(["Bravo"], Queue(await PlayAsync("Alpha", 0)));
        var last = await PlayAsync("Bravo", 2);
        Assert.Empty(Queue(last));
        var report = new { deviceId = "tab", entryId = last.GetProperty("nowPlaying").GetProperty("entry").GetProperty("id").GetString(), state = "PLAYING", positionMs = 1000 };
        Assert.Empty(Queue(await OkAsync(_admin.PostAsJsonAsync($"/rest/v1/public/jams/{code}/playback", report, ArcanistFactory.Json, Cancel))));

        // The queue ran out: the two songs not heard, of this Jam's libraries, marked as suggestions.
        var suggested = await OkAsync(_admin.PostAsJsonAsync($"/rest/v1/public/jams/{code}/playback",
            new { report.deviceId, report.entryId, report.state, report.positionMs, ownSongsLeft = 0 }, ArcanistFactory.Json, Cancel));
        Assert.Equal(["Charlie", "Delta"], Queue(suggested).Order());
        Assert.All(suggested.GetProperty("queue").EnumerateArray(), e =>
            Assert.Equal((true, JsonValueKind.Null), (e.GetProperty("suggested").GetBoolean(), e.GetProperty("addedBy").ValueKind)));
        var charlie = suggested.GetProperty("queue").EnumerateArray().First(e => e.GetProperty("song").GetProperty("title").GetString() == "Charlie");
        Assert.Equal(["luis"], charlie.GetProperty("likedBy").EnumerateArray().Select(n => n.GetString()));

        // What someone asks for goes before them.
        var asked = await OkAsync(luis.PostAsJsonAsync($"/rest/v1/public/jams/{code}/tracks", new { trackIds = new[] { _tracks["Alpha"] } }, ArcanistFactory.Json, Cancel));
        Assert.Equal("Alpha", Queue(asked)[0]);
        Assert.False(asked.GetProperty("queue")[0].GetProperty("suggested").GetBoolean());

        // Guests are not told about the tastes of the server's users.
        await OkAsync(_admin.PutAsJsonAsync($"/rest/v1/public/jams/{code}", new { allowGuests = true }, ArcanistFactory.Json, Cancel));
        var ana = Guest((await OkAsync(_factory.CreateClient().PostAsJsonAsync($"/rest/v1/public/jams/{code}/join", new { name = "Ana" }, ArcanistFactory.Json, Cancel))).GetProperty("token").GetString()!);
        Assert.All((await OkAsync(ana.GetAsync($"/rest/v1/public/jams/{code}", Cancel))).GetProperty("queue").EnumerateArray(), e => Assert.Equal(0, e.GetProperty("likedBy").GetArrayLength()));

        // A suggestion taken out does not come back.
        var delta = asked.GetProperty("queue").EnumerateArray().First(e => e.GetProperty("song").GetProperty("title").GetString() == "Delta").GetProperty("id").GetString();
        await OkAsync(luis.PostAsJsonAsync($"/rest/v1/public/jams/{code}/control", new { action = "REMOVE", entryId = delta }, ArcanistFactory.Json, Cancel));
        await PlayAsync("Alpha", 0);
        var charlieLast = await PlayAsync("Charlie", 0);
        Assert.DoesNotContain("Delta", Queue(charlieLast));

        // Off, the queue stays empty; and a new Jam nothing played on does not start by itself.
        var quiet = (await StartAsync(new { name = "Quiet", playsOn = "CLIENT" })).GetProperty("code").GetString()!;
        await OkAsync(_admin.PostAsJsonAsync($"/rest/v1/public/jams/{quiet}/host", new { deviceId = "tab", kind = "WEB" }, ArcanistFactory.Json, Cancel));
        Assert.Empty(Queue(await OkAsync(_admin.PostAsJsonAsync($"/rest/v1/public/jams/{quiet}/playback", new { deviceId = "tab", state = "STOPPED", positionMs = 0, ownSongsLeft = 0 }, ArcanistFactory.Json, Cancel))));
    }

    [Fact]
    public async Task When_the_hosts_client_is_gone_someone_else_plays_the_jam_or_it_ends()
    {
        var jam = await StartAsync(new { name = "Handover", playsOn = "CLIENT" });
        var code = jam.GetProperty("code").GetString()!;
        await OkAsync(_admin.PostAsJsonAsync($"/rest/v1/public/jams/{code}/host", new { deviceId = "tab", kind = "WEB" }, ArcanistFactory.Json, Cancel));
        await _factory.CreateUserAsync("luis");
        var luis = await _factory.CreateAuthenticatedClientAsync("luis");
        await OkAsync(luis.PostAsJsonAsync($"/rest/v1/public/jams/{code}/join", new { }, ArcanistFactory.Json, Cancel));

        // While it reports, nobody can.
        Assert.Equal((HttpStatusCode.Conflict, "jam_host_online", null), await FailAsync(luis.PostAsync($"/rest/v1/public/jams/{code}/take-over", null, Cancel)));
        _time.Advance(TimeSpan.FromMinutes(3));
        var seen = await OkAsync(luis.GetAsync($"/rest/v1/public/jams/{code}", Cancel));
        Assert.Equal((true, true), (seen.GetProperty("hostGone").GetBoolean(), seen.GetProperty("you").GetProperty("canTakeOver").GetBoolean()));

        // Luis plays it from now on: the one who started it can no longer report, and keeps managing it.
        var taken = await OkAsync(luis.PostAsync($"/rest/v1/public/jams/{code}/take-over", null, Cancel));
        Assert.Equal(("HOST", "luis", false), (taken.GetProperty("you").GetProperty("participation").GetString(), taken.GetProperty("host").GetString(), taken.GetProperty("hostGone").GetBoolean()));
        await OkAsync(luis.PostAsJsonAsync($"/rest/v1/public/jams/{code}/host", new { deviceId = "phone", kind = "MOBILE" }, ArcanistFactory.Json, Cancel));
        Assert.Equal((HttpStatusCode.Forbidden, "jam_not_host", null), await FailAsync(_admin.PostAsJsonAsync($"/rest/v1/public/jams/{code}/playback",
            new { deviceId = "tab", state = "PLAYING", positionMs = 0 }, ArcanistFactory.Json, Cancel)));
        Assert.Equal("MANAGER", (await OkAsync(_admin.GetAsync($"/rest/v1/public/jams/{code}", Cancel))).GetProperty("you").GetProperty("participation").GetString());

        // Those who manage it end it once its client is gone; and after an hour like this, it ends by itself.
        Assert.Equal(HttpStatusCode.Forbidden, (await _admin.PostAsync($"/rest/v1/public/jams/{code}/end", null, Cancel)).StatusCode);
        var other = (await StartAsync(new { name = "Other", playsOn = "CLIENT" })).GetProperty("code").GetString()!;
        await OkAsync(_admin.PostAsJsonAsync($"/rest/v1/public/jams/{other}/host", new { deviceId = "tab", kind = "WEB" }, ArcanistFactory.Json, Cancel));
        _time.Advance(TimeSpan.FromMinutes(3));
        Assert.NotEqual(JsonValueKind.Null, (await OkAsync(_admin.PostAsync($"/rest/v1/public/jams/{code}/end", null, Cancel))).GetProperty("endedAt").ValueKind);
        _time.Advance(TimeSpan.FromMinutes(61));
        var later = await _factory.CreateAuthenticatedClientAsync();
        await Until(async () => (await OkAsync(later.GetAsync($"/rest/v1/public/jams/{other}", Cancel))).GetProperty("endedAt").ValueKind != JsonValueKind.Null,
            "a Jam whose host is gone for an hour ends");
    }

    [Fact]
    public async Task With_an_account_one_hears_the_jam_from_elsewhere_in_step_and_only_what_they_can_listen_to()
    {
        var jam = await StartAsync(new { name = "Far away", playsOn = "CLIENT", voting = false, allowGuests = true });
        var code = jam.GetProperty("code").GetString()!;
        await OkAsync(_admin.PostAsJsonAsync($"/rest/v1/public/jams/{code}/host", new { deviceId = "tab", kind = "WEB" }, ArcanistFactory.Json, Cancel));
        var added = await OkAsync(_admin.PostAsJsonAsync($"/rest/v1/public/jams/{code}/tracks",
            new { trackIds = new[] { _tracks["Alpha"], _tracks["Secret"], _tracks["Bravo"] } }, ArcanistFactory.Json, Cancel));
        var first = added.GetProperty("queue")[0].GetProperty("id").GetString();
        var secret = added.GetProperty("queue")[1].GetProperty("id").GetString();

        await _factory.CreateUserAsync("luis");
        var luis = await _factory.CreateAuthenticatedClientAsync("luis");
        var idle = await OkAsync(luis.PostAsync($"/rest/v1/public/jams/{code}/listen", null, Cancel));
        Assert.Equal("NOTHING", idle.GetProperty("state").GetString());
        var seen = await OkAsync(_admin.GetAsync($"/rest/v1/public/jams/{code}", Cancel));
        Assert.Equal(["luis"], seen.GetProperty("listeners").EnumerateArray().Select(l => l.GetString()));

        // A guest follows it without hearing anything.
        var guest = _factory.CreateClient();
        var token = (await OkAsync(guest.PostAsJsonAsync($"/rest/v1/public/jams/{code}/join", new { name = "Eva" }, ArcanistFactory.Json, Cancel))).GetProperty("token").GetString();
        guest.DefaultRequestHeaders.Add("X-Arcanist-Guest", token);
        Assert.Equal((HttpStatusCode.Forbidden, "jam_listen_account", null), await FailAsync(guest.PostAsync($"/rest/v1/public/jams/{code}/listen", null, Cancel)));
        Assert.False((await OkAsync(guest.GetAsync($"/rest/v1/public/jams/{code}", Cancel))).GetProperty("you").GetProperty("canListen").GetBoolean());

        // It plays: where it is, by the server's clock, with the next one they can hear ready; a stream of their own that works.
        await OkAsync(_admin.PostAsJsonAsync($"/rest/v1/public/jams/{code}/playback",
            new { deviceId = "tab", entryId = first, state = "PLAYING", positionMs = 10_000 }, ArcanistFactory.Json, Cancel));
        _time.Advance(TimeSpan.FromSeconds(3));
        var what = await OkAsync(luis.PostAsync($"/rest/v1/public/jams/{code}/listen", null, Cancel));
        Assert.Equal(("PLAYING", _tracks["Alpha"], 10_000), (what.GetProperty("state").GetString(), what.GetProperty("trackId").GetGuid(), what.GetProperty("positionMs").GetInt32()));
        Assert.Equal(TimeSpan.FromSeconds(3), what.GetProperty("serverTime").GetDateTime() - what.GetProperty("at").GetDateTime());
        Assert.Equal(_tracks["Bravo"], what.GetProperty("nextTrackId").GetGuid());
        var stream = await _factory.CreateClient().GetAsync(what.GetProperty("streamUrl").GetString(), HttpCompletionOption.ResponseHeadersRead, Cancel);
        Assert.Equal(HttpStatusCode.OK, stream.StatusCode);

        // The host moving within the song is told at once, unlike the same place reported on time.
        var told = new List<string>();
        var signals = _factory.Services.GetRequiredService<JukeboxJamSignals>();
        Action<Guid, JukeboxJamChange> onChange = (_, change) => { if (change.Code == code) lock (told) told.Add(change.Kind.ToString()); };
        signals.Changed += onChange;
        try
        {
            await OkAsync(_admin.PostAsJsonAsync($"/rest/v1/public/jams/{code}/playback",
                new { deviceId = "tab", entryId = first, state = "PLAYING", positionMs = 13_000 }, ArcanistFactory.Json, Cancel));
            Assert.DoesNotContain("NowPlaying", told);
            await OkAsync(_admin.PostAsJsonAsync($"/rest/v1/public/jams/{code}/playback",
                new { deviceId = "tab", entryId = first, state = "PLAYING", positionMs = 60_000 }, ArcanistFactory.Json, Cancel));
            Assert.Contains("NowPlaying", told);
        }
        finally
        {
            signals.Changed -= onChange;
        }

        // Paused, it waits; with the host's client gone too, so the music does not run ahead of nobody.
        await OkAsync(_admin.PostAsJsonAsync($"/rest/v1/public/jams/{code}/playback",
            new { deviceId = "tab", entryId = first, state = "PAUSED", positionMs = 60_000 }, ArcanistFactory.Json, Cancel));
        Assert.Equal("PAUSED", (await OkAsync(luis.PostAsync($"/rest/v1/public/jams/{code}/listen", null, Cancel))).GetProperty("state").GetString());
        await OkAsync(_admin.PostAsJsonAsync($"/rest/v1/public/jams/{code}/playback",
            new { deviceId = "tab", entryId = first, state = "PLAYING", positionMs = 60_000 }, ArcanistFactory.Json, Cancel));
        _time.Advance(TimeSpan.FromMinutes(3));
        var later = await _factory.CreateAuthenticatedClientAsync("luis");
        Assert.Equal("PAUSED", (await OkAsync(later.PostAsync($"/rest/v1/public/jams/{code}/listen", null, Cancel))).GetProperty("state").GetString());

        // A Jam does not widen who hears a library: a song of one they have no access to is silence, and no stream.
        var host = await _factory.CreateAuthenticatedClientAsync();
        await OkAsync(host.PostAsJsonAsync($"/rest/v1/public/jams/{code}/playback",
            new { deviceId = "tab", entryId = secret, state = "PLAYING", positionMs = 0 }, ArcanistFactory.Json, Cancel));
        var silence = await OkAsync(later.PostAsync($"/rest/v1/public/jams/{code}/listen", null, Cancel));
        Assert.Equal(("UNAVAILABLE", JsonValueKind.Null, JsonValueKind.Null), (silence.GetProperty("state").GetString(), silence.GetProperty("trackId").ValueKind, silence.GetProperty("streamUrl").ValueKind));

        // Stopping takes them off the list, and so does going away.
        var stopped = await OkAsync(later.DeleteAsync($"/rest/v1/public/jams/{code}/listen", Cancel));
        Assert.DoesNotContain("luis", stopped.GetProperty("listeners").EnumerateArray().Select(l => l.GetString()));
        _time.Advance(TimeSpan.FromMinutes(1));
        var afterwards = await _factory.CreateAuthenticatedClientAsync();
        Assert.Empty((await OkAsync(afterwards.GetAsync($"/rest/v1/public/jams/{code}", Cancel))).GetProperty("listeners").EnumerateArray());
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
        // The server's choice for those who do not say: delete it. Whoever starts one can still keep it.
        await OkAsync(_admin.PutAsJsonAsync("/rest/v1/sharing", new { publicLinks = false, jamGuests = true, guestAddLimit = 2, maxGuests = 100, jamDeletePlaylist = true },
            ArcanistFactory.Json, Cancel));
        var kept = (await StartAsync(new { name = "Kept", playsOn = "CLIENT", voting = false, allowGuests = false, saveAsPlaylist = true })).GetProperty("code").GetString()!;
        var gone = await StartAsync(new { name = "Gone", playsOn = "CLIENT", voting = false, allowGuests = false });
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
