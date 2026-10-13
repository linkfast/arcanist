using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Arcanist.Core.Setup;
using Arcanist.Core.Tests.Scanning;

namespace Arcanist.Core.Tests.Extensions;

/// <summary>The built-in Subsonic extension, called the way Subsonic apps call it.</summary>
public sealed class SubsonicTests : IAsyncLifetime
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private readonly string _music = Directory.CreateTempSubdirectory("arcanist-subsonic-").FullName;
    private readonly ArcanistFactory _factory = new(new Dictionary<string, string?> { ["Arcanist:Extensions:subsonic:Enabled"] = "true" });
    private HttpClient _client = null!;
    private HttpClient _admin = null!;
    private string _secret = null!;

    public async ValueTask InitializeAsync()
    {
        AudioFixtures.WriteWav(Path.Combine(_music, "The Band", "First", "01.wav"), 1,
            new FixtureTags("Opening", "The Band", "First Album", "The Band", TrackNumber: 1, Year: 2001, Genre: "Rock", WithCover: true,
                Lyrics: "First line\nSecond line"));
        AudioFixtures.WriteWav(Path.Combine(_music, "The Band", "First", "02.wav"), 2,
            new FixtureTags("Closing", "The Band", "First Album", "The Band", TrackNumber: 2, Year: 2001, Genre: "Rock", WithCover: true));
        AudioFixtures.WriteWav(Path.Combine(_music, "Zed", "Solo", "01.wav"), 3,
            new FixtureTags("Alone", "Zed", "Solo Record", "Zed", TrackNumber: 1, Year: 1999, Genre: "Jazz"));

        // Not visible to everyone: other users get no access until given it.
        await _factory.CompleteSetupAsync(new SetupLibrary("Music", [_music], VisibleToAll: false));
        _admin = await _factory.CreateAuthenticatedClientAsync();
        await _factory.WaitForScansAsync(_admin);
        _secret = await CreateAppPasswordAsync(_admin);
        _client = _factory.CreateClient();
    }

    private static async Task<string> CreateAppPasswordAsync(HttpClient user)
    {
        var response = await user.PostAsJsonAsync("/rest/v1/me/app-passwords", new { name = "Phone" }, ArcanistFactory.Json, Cancel);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>(ArcanistFactory.Json, Cancel)).GetProperty("secret").GetString()!;
    }

    private string Auth(string user = "admin", string? secret = null) => $"u={user}&p={Uri.EscapeDataString(secret ?? _secret)}";

    /// <summary>Calls a method with JSON responses and returns <c>subsonic-response</c>.</summary>
    private async Task<JsonElement> CallAsync(string method, string query = "", string? auth = null)
    {
        var response = await _client.GetAsync($"/rest/{method}.view?{auth ?? Auth()}&v=1.16.1&c=tests&f=json&{query}", Cancel);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.StartsWith("application/json", response.Content.Headers.ContentType!.ToString());
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Cancel)).GetProperty("subsonic-response");
    }

    private async Task<JsonElement> OkAsync(string method, string query = "", string? auth = null)
    {
        var response = await CallAsync(method, query, auth);
        Assert.True(response.GetProperty("status").GetString() == "ok", response.ToString());
        return response;
    }

    private static int ErrorOf(JsonElement response)
    {
        Assert.Equal("failed", response.GetProperty("status").GetString());
        return response.GetProperty("error").GetProperty("code").GetInt32();
    }

    private async Task<Dictionary<string, string>> SongIdsAsync() =>
        (await OkAsync("search3", "query=&songCount=50")).GetProperty("searchResult3").GetProperty("song").EnumerateArray()
        .ToDictionary(s => s.GetProperty("title").GetString()!, s => s.GetProperty("id").GetString()!);

    [Fact]
    public async Task Apps_sign_in_with_app_passwords_only()
    {
        var ping = await OkAsync("ping");
        Assert.Equal(("1.16.1", "arcanist", true), (ping.GetProperty("version").GetString(), ping.GetProperty("type").GetString(), ping.GetProperty("openSubsonic").GetBoolean()));

        // Hex-encoded password, salted token and OpenSubsonic API key.
        await OkAsync("ping", auth: $"u=admin&p=enc:{Convert.ToHexStringLower(Encoding.UTF8.GetBytes(_secret))}");
        var token = Convert.ToHexStringLower(MD5.HashData(Encoding.UTF8.GetBytes(_secret + "c19b2d")));
        await OkAsync("ping", auth: $"u=admin&t={token}&s=c19b2d");
        var info = await OkAsync("tokenInfo", auth: $"apiKey={Uri.EscapeDataString(_secret)}");
        Assert.Equal("admin", info.GetProperty("tokenInfo").GetProperty("username").GetString());

        // The account's own password never works here.
        Assert.Equal(40, ErrorOf(await CallAsync("ping", auth: $"u=admin&p={Uri.EscapeDataString(ArcanistFactory.Password)}")));
        Assert.Equal(43, ErrorOf(await CallAsync("ping", auth: $"apiKey={Uri.EscapeDataString(_secret)}&u=admin")));
        Assert.Equal(44, ErrorOf(await CallAsync("ping", auth: "apiKey=nope")));
        Assert.Equal(10, ErrorOf(await CallAsync("ping", auth: "x=1")));

        // Extensions are listed without signing in.
        var extensions = (await OkAsync("getOpenSubsonicExtensions", auth: "x=1")).GetProperty("openSubsonicExtensions").EnumerateArray()
            .Select(e => e.GetProperty("name").GetString()).ToList();
        Assert.Contains("apiKeyAuthentication", extensions);
        Assert.Contains("formPost", extensions);

        // Form posts, and XML by default.
        var form = new FormUrlEncodedContent(new Dictionary<string, string> { ["u"] = "admin", ["p"] = _secret, ["v"] = "1.16.1", ["c"] = "tests" });
        var posted = await _client.PostAsync("/rest/getLicense", form, Cancel);
        Assert.StartsWith("text/xml", posted.Content.Headers.ContentType!.ToString());
        var xml = XDocument.Parse(await posted.Content.ReadAsStringAsync(Cancel));
        XNamespace ns = "http://subsonic.org/restapi";
        Assert.Equal("ok", xml.Root!.Attribute("status")!.Value);
        Assert.Equal("true", xml.Root.Element(ns + "license")!.Attribute("valid")!.Value);

        Assert.Equal(0, ErrorOf(await CallAsync("noSuchMethod")));
    }

    [Fact]
    public async Task Apps_browse_by_tags_and_by_folders()
    {
        var folder = Assert.Single((await OkAsync("getMusicFolders")).GetProperty("musicFolders").GetProperty("musicFolder").EnumerateArray());
        Assert.Equal((1, "Music"), (folder.GetProperty("id").GetInt32(), folder.GetProperty("name").GetString()));

        // "The Band" is indexed under B; ids are strings.
        var artists = (await OkAsync("getArtists")).GetProperty("artists");
        Assert.Equal("The El La Los Las Le Les", artists.GetProperty("ignoredArticles").GetString());
        var index = artists.GetProperty("index").EnumerateArray().ToDictionary(i => i.GetProperty("name").GetString()!, i => i.GetProperty("artist"));
        Assert.Equal(["B", "Z"], index.Keys);
        var band = Assert.Single(index["B"].EnumerateArray());
        Assert.Equal(("The Band", 1, JsonValueKind.String), (band.GetProperty("name").GetString(), band.GetProperty("albumCount").GetInt32(), band.GetProperty("id").ValueKind));

        var artist = (await OkAsync("getArtist", $"id={band.GetProperty("id").GetString()}")).GetProperty("artist");
        var albumId = Assert.Single(artist.GetProperty("album").EnumerateArray()).GetProperty("id").GetString();

        var album = (await OkAsync("getAlbum", $"id={albumId}")).GetProperty("album");
        Assert.Equal(("First Album", 2, 2001, "Rock"), (album.GetProperty("name").GetString(), album.GetProperty("songCount").GetInt32(),
            album.GetProperty("year").GetInt32(), album.GetProperty("genre").GetString()));
        var songs = album.GetProperty("song").EnumerateArray().ToList();
        Assert.Equal(["Opening", "Closing"], songs.Select(s => s.GetProperty("title").GetString()));
        var song = songs[0];
        Assert.False(song.GetProperty("isDir").GetBoolean());
        Assert.Equal(("wav", "audio/wav", albumId), (song.GetProperty("suffix").GetString(), song.GetProperty("contentType").GetString(), song.GetProperty("albumId").GetString()));
        Assert.Equal("The Band/First Album/01 - Opening.wav", song.GetProperty("path").GetString());
        Assert.Equal(JsonValueKind.Object, song.GetProperty("replayGain").ValueKind);
        Assert.Equal("Rock", Assert.Single(song.GetProperty("genres").EnumerateArray()).GetProperty("name").GetString());
        Assert.False(song.TryGetProperty("starred", out _));
        Assert.Equal("Opening", (await OkAsync("getSong", $"id={song.GetProperty("id").GetString()}")).GetProperty("song").GetProperty("title").GetString());

        // The same in XML: OpenSubsonic lists are repeated elements.
        XNamespace ns = "http://subsonic.org/restapi";
        var xml = XDocument.Parse(await _client.GetStringAsync($"/rest/getAlbum?{Auth()}&v=1.16.1&c=tests&id={albumId}", Cancel));
        var xmlAlbum = xml.Root!.Element(ns + "album")!;
        Assert.Equal("First Album", xmlAlbum.Attribute("name")!.Value);
        Assert.Equal(2, xmlAlbum.Elements(ns + "song").Count());
        Assert.Equal("Rock", xmlAlbum.Elements(ns + "song").First().Element(ns + "genres")!.Attribute("name")!.Value);

        // The folder view: artist, then album, then songs.
        var indexes = (await OkAsync("getIndexes")).GetProperty("indexes");
        Assert.True(indexes.GetProperty("lastModified").GetInt64() > 0);
        var directory = (await OkAsync("getMusicDirectory", $"id={band.GetProperty("id").GetString()}")).GetProperty("directory");
        var child = Assert.Single(directory.GetProperty("child").EnumerateArray());
        Assert.Equal((true, "First Album"), (child.GetProperty("isDir").GetBoolean(), child.GetProperty("title").GetString()));
        var albumDirectory = (await OkAsync("getMusicDirectory", $"id={albumId}")).GetProperty("directory");
        Assert.Equal(2, albumDirectory.GetProperty("child").GetArrayLength());

        var genres = (await OkAsync("getGenres")).GetProperty("genres").GetProperty("genre").EnumerateArray()
            .ToDictionary(g => g.GetProperty("value").GetString()!, g => (g.GetProperty("songCount").GetInt32(), g.GetProperty("albumCount").GetInt32()));
        Assert.Equal((2, 1), genres["Rock"]);

        // Album lists page in a stable order.
        var first = (await OkAsync("getAlbumList2", "type=alphabeticalByName&size=1")).GetProperty("albumList2").GetProperty("album");
        var second = (await OkAsync("getAlbumList2", "type=alphabeticalByName&size=1&offset=1")).GetProperty("albumList2").GetProperty("album");
        Assert.Equal(["First Album", "Solo Record"], [first[0].GetProperty("name").GetString(), second[0].GetProperty("name").GetString()]);
        var byYear = (await OkAsync("getAlbumList2", "type=byYear&fromYear=2010&toYear=1990")).GetProperty("albumList2").GetProperty("album");
        Assert.Equal(["First Album", "Solo Record"], byYear.EnumerateArray().Select(a => a.GetProperty("name").GetString()));
        Assert.Equal(10, ErrorOf(await CallAsync("getAlbumList2", "type=sideways")));
        Assert.Equal(2, (await OkAsync("getAlbumList", "type=newest")).GetProperty("albumList").GetProperty("album").GetArrayLength());

        // An empty search matches everything, page by page; a real one searches words.
        var all = (await OkAsync("search3", "query=%22%22&songCount=2&songOffset=1")).GetProperty("searchResult3");
        Assert.Equal(2, all.GetProperty("song").GetArrayLength());
        Assert.Equal(2, all.GetProperty("artist").GetArrayLength());
        var found = (await OkAsync("search3", "query=solo")).GetProperty("searchResult3");
        Assert.Equal("Solo Record", Assert.Single(found.GetProperty("album").EnumerateArray()).GetProperty("name").GetString());
        // Songs match their album's words too.
        Assert.Equal("Alone", Assert.Single(found.GetProperty("song").EnumerateArray()).GetProperty("title").GetString());

        Assert.Equal(2, (await OkAsync("getSongsByGenre", "genre=rock")).GetProperty("songsByGenre").GetProperty("song").GetArrayLength());
        Assert.Equal(3, (await OkAsync("getRandomSongs", "size=10")).GetProperty("randomSongs").GetProperty("song").GetArrayLength());
        Assert.Equal(70, ErrorOf(await CallAsync("getAlbum", $"id={Guid.NewGuid()}")));
        Assert.Equal(70, ErrorOf(await CallAsync("getArtists", "musicFolderId=9")));
    }

    [Fact]
    public async Task Stars_ratings_plays_and_playlists_are_the_users_own()
    {
        var songs = await SongIdsAsync();
        await OkAsync("star", $"id={songs["Opening"]}");
        var starred = (await OkAsync("getStarred2")).GetProperty("starred2");
        Assert.Equal("Opening", Assert.Single(starred.GetProperty("song").EnumerateArray()).GetProperty("title").GetString());
        Assert.Equal(0, starred.GetProperty("album").GetArrayLength());
        await OkAsync("unstar", $"id={songs["Opening"]}");
        Assert.Equal(0, (await OkAsync("getStarred2")).GetProperty("starred2").GetProperty("song").GetArrayLength());

        await OkAsync("setRating", $"id={songs["Closing"]}&rating=3");
        Assert.Equal(3, (await OkAsync("getSong", $"id={songs["Closing"]}")).GetProperty("song").GetProperty("userRating").GetInt32());
        Assert.Equal(10, ErrorOf(await CallAsync("setRating", $"id={songs["Closing"]}&rating=9")));

        // Now playing, then a scrobble that counts.
        await OkAsync("scrobble", $"id={songs["Alone"]}&submission=false");
        var playing = Assert.Single((await OkAsync("getNowPlaying")).GetProperty("nowPlaying").GetProperty("entry").EnumerateArray());
        Assert.Equal(("Alone", "admin"), (playing.GetProperty("title").GetString(), playing.GetProperty("username").GetString()));
        await OkAsync("scrobble", $"id={songs["Alone"]}&time={DateTimeOffset.UtcNow.AddMinutes(-5).ToUnixTimeMilliseconds()}");
        Assert.Equal(1, (await OkAsync("getSong", $"id={songs["Alone"]}")).GetProperty("song").GetProperty("playCount").GetInt32());
        Assert.Equal(0, (await OkAsync("getNowPlaying")).GetProperty("nowPlaying").GetProperty("entry").GetArrayLength());

        // Playlists: create, edit by position, read, delete.
        var created = (await OkAsync("createPlaylist", $"name=Mix&songId={songs["Opening"]}&songId={songs["Closing"]}")).GetProperty("playlist");
        var id = created.GetProperty("id").GetString();
        Assert.Equal(("admin", 2, false), (created.GetProperty("owner").GetString(), created.GetProperty("songCount").GetInt32(), created.GetProperty("readonly").GetBoolean()));
        await OkAsync("updatePlaylist", $"playlistId={id}&songIndexToRemove=0&songIdToAdd={songs["Alone"]}&name=Renamed&public=true");
        var playlist = (await OkAsync("getPlaylist", $"id={id}")).GetProperty("playlist");
        Assert.Equal(("Renamed", true), (playlist.GetProperty("name").GetString(), playlist.GetProperty("public").GetBoolean()));
        Assert.Equal(["Closing", "Alone"], playlist.GetProperty("entry").EnumerateArray().Select(e => e.GetProperty("title").GetString()));
        await OkAsync("createPlaylist", $"playlistId={id}&songId={songs["Opening"]}");
        Assert.Equal(["Opening"], (await OkAsync("getPlaylist", $"id={id}")).GetProperty("playlist").GetProperty("entry").EnumerateArray()
            .Select(e => e.GetProperty("title").GetString()));
        Assert.Single((await OkAsync("getPlaylists")).GetProperty("playlists").GetProperty("playlist").EnumerateArray());
        await OkAsync("deletePlaylist", $"id={id}");
        Assert.Equal(70, ErrorOf(await CallAsync("getPlaylist", $"id={id}")));
    }

    [Fact]
    public async Task Apps_stream_download_and_show_covers_and_lyrics()
    {
        var songs = await SongIdsAsync();
        var request = new HttpRequestMessage(HttpMethod.Get, $"/rest/stream?{Auth()}&v=1.16.1&c=tests&id={songs["Opening"]}");
        request.Headers.Range = new RangeHeaderValue(0, 99);
        var partial = await _client.SendAsync(request, Cancel);
        Assert.Equal(HttpStatusCode.PartialContent, partial.StatusCode);
        Assert.Equal(100, (await partial.Content.ReadAsByteArrayAsync(Cancel)).Length);

        var download = await _client.GetAsync($"/rest/download?{Auth()}&v=1.16.1&c=tests&id={songs["Opening"]}", Cancel);
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal("01.wav", download.Content.Headers.ContentDisposition?.FileNameStar ?? download.Content.Headers.ContentDisposition?.FileName);

        var song = (await OkAsync("getSong", $"id={songs["Opening"]}")).GetProperty("song");
        var cover = await _client.GetAsync($"/rest/getCoverArt?{Auth()}&v=1.16.1&c=tests&id={song.GetProperty("coverArt").GetString()}&size=100", Cancel);
        Assert.Equal(HttpStatusCode.OK, cover.StatusCode);
        Assert.StartsWith("image/", cover.Content.Headers.ContentType!.MediaType);
        var byAlbum = await _client.GetAsync($"/rest/getCoverArt?{Auth()}&v=1.16.1&c=tests&id=al-{song.GetProperty("albumId").GetString()}", Cancel);
        Assert.Equal(HttpStatusCode.OK, byAlbum.StatusCode);

        var lyrics = Assert.Single((await OkAsync("getLyricsBySongId", $"id={songs["Opening"]}")).GetProperty("lyricsList").GetProperty("structuredLyrics").EnumerateArray());
        Assert.False(lyrics.GetProperty("synced").GetBoolean());
        Assert.Equal(["First line", "Second line"], lyrics.GetProperty("line").EnumerateArray().Select(l => l.GetProperty("value").GetString()));
        Assert.Equal(0, (await OkAsync("getLyricsBySongId", $"id={songs["Closing"]}")).GetProperty("lyricsList").GetProperty("structuredLyrics").GetArrayLength());
        Assert.Equal("First line\nSecond line",
            (await OkAsync("getLyrics", "artist=the%20band&title=opening")).GetProperty("lyrics").GetProperty("value").GetString());
    }

    [Fact]
    public async Task Users_see_only_their_libraries_and_roles()
    {
        await _factory.CreateUserAsync("guest");
        var guest = await _factory.CreateAuthenticatedClientAsync("guest");
        var secret = await CreateAppPasswordAsync(guest);
        var auth = Auth("guest", secret);

        Assert.Equal(0, (await OkAsync("getMusicFolders", auth: auth)).GetProperty("musicFolders").GetProperty("musicFolder").GetArrayLength());
        Assert.Equal(0, (await OkAsync("search3", "query=", auth)).GetProperty("searchResult3").GetProperty("song").GetArrayLength());
        var songs = await SongIdsAsync();
        Assert.Equal(70, ErrorOf(await CallAsync("getSong", $"id={songs["Opening"]}", auth)));
        Assert.Equal(70, ErrorOf(await CallAsync("star", $"id={songs["Opening"]}", auth)));

        var user = (await OkAsync("getUser", "username=guest", auth)).GetProperty("user");
        Assert.Equal((false, false, true), (user.GetProperty("adminRole").GetBoolean(), user.GetProperty("downloadRole").GetBoolean(), user.GetProperty("streamRole").GetBoolean()));
        Assert.Equal(50, ErrorOf(await CallAsync("getUser", "username=admin", auth)));
        Assert.Equal(50, ErrorOf(await CallAsync("getUsers", auth: auth)));
        Assert.Equal(50, ErrorOf(await CallAsync("startScan", auth: auth)));

        var admin = (await OkAsync("getUser", "username=admin")).GetProperty("user");
        Assert.Equal([1], admin.GetProperty("folder").EnumerateArray().Select(f => f.GetInt32()));
        Assert.Equal(2, (await OkAsync("getUsers")).GetProperty("users").GetProperty("user").GetArrayLength());
        Assert.Equal(JsonValueKind.True, (await OkAsync("startScan")).GetProperty("scanStatus").GetProperty("scanning").ValueKind);
        await _factory.WaitForScansAsync(_admin);
        Assert.False((await OkAsync("getScanStatus")).GetProperty("scanStatus").GetProperty("scanning").GetBoolean());
    }

    [Fact]
    public async Task The_extension_is_off_until_an_administrator_enables_it()
    {
        await using var factory = new ArcanistFactory();
        await factory.CompleteSetupAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await factory.CreateClient().GetAsync("/rest/ping.view?u=admin&p=x", Cancel)).StatusCode);

        var admin = await factory.CreateAuthenticatedClientAsync();
        var subsonic = Assert.Single((await admin.GetFromJsonAsync<List<JsonElement>>("/rest/v1/extensions", ArcanistFactory.Json, Cancel))!,
            e => e.GetProperty("id").GetString() == "subsonic");
        Assert.Equal((true, false, "RUNNING"), (subsonic.GetProperty("builtIn").GetBoolean(), subsonic.GetProperty("enabled").GetBoolean(), subsonic.GetProperty("status").GetString()));

        // Built in: it switches on at once.
        var on = await admin.PutAsJsonAsync("/rest/v1/extensions/subsonic/enabled", new { enabled = true }, ArcanistFactory.Json, Cancel);
        Assert.Equal(HttpStatusCode.OK, on.StatusCode);
        Assert.False((await on.Content.ReadFromJsonAsync<JsonElement>(ArcanistFactory.Json, Cancel)).GetProperty("restartRequired").GetBoolean());
        var ping = await factory.CreateClient().GetAsync("/rest/ping.view?u=admin&p=x&f=json", Cancel);
        Assert.Equal(HttpStatusCode.OK, ping.StatusCode);
        Assert.Equal(40, (await ping.Content.ReadFromJsonAsync<JsonElement>(Cancel)).GetProperty("subsonic-response").GetProperty("error").GetProperty("code").GetInt32());

        // The core API is not affected.
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/rest/v1/server-info", Cancel)).StatusCode);
    }

    public async ValueTask DisposeAsync()
    {
        await _factory.DisposeAsync();
        try
        {
            Directory.Delete(_music, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
