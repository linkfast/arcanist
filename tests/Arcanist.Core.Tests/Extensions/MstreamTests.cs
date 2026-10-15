using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Arcanist.Core.Data;
using Arcanist.Core.Data.Entities;
using Arcanist.Core.Setup;
using Arcanist.Core.Tests.Scanning;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Arcanist.Core.Tests.Extensions;

/// <summary>The built-in mStream extension, called the way the mStream apps call it.</summary>
public sealed class MstreamTests : IAsyncLifetime
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private readonly string _music = Directory.CreateTempSubdirectory("arcanist-mstream-").FullName;
    private readonly ArcanistFactory _factory = new(new Dictionary<string, string?> { ["Arcanist:Extensions:mstream:Enabled"] = "true" });
    private HttpClient _admin = null!;
    private string _secret = null!;
    private string _token = null!;

    public async ValueTask InitializeAsync()
    {
        AudioFixtures.WriteWav(Path.Combine(_music, "The Band", "First", "01.wav"), 1,
            new FixtureTags("Opening", "The Band", "First Album", "The Band", TrackNumber: 1, Year: 2001, Genre: "Rock", WithCover: true,
                Lyrics: "First line"));
        AudioFixtures.WriteWav(Path.Combine(_music, "The Band", "First", "02.wav"), 2,
            new FixtureTags("Closing", "The Band", "First Album", "The Band", TrackNumber: 2, Year: 2001, Genre: "Rock", WithCover: true));
        AudioFixtures.WriteWav(Path.Combine(_music, "Zed", "01.wav"), 3, new FixtureTags("Alone", "Zed", "Solo Record", "Zed", TrackNumber: 1));

        await _factory.CompleteSetupAsync(new SetupLibrary("Music", [_music], VisibleToAll: false));
        _admin = await _factory.CreateAuthenticatedClientAsync();
        await _factory.WaitForScansAsync(_admin);
        _secret = await CreateAppPasswordAsync(_admin);
        _token = await LoginAsync("admin", _secret);
    }

    private static async Task<string> CreateAppPasswordAsync(HttpClient user)
    {
        var response = await user.PostAsJsonAsync("/rest/v1/me/app-passwords", new { name = "Phone" }, ArcanistFactory.Json, Cancel);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>(ArcanistFactory.Json, Cancel)).GetProperty("secret").GetString()!;
    }

    /// <summary>Signs in as the mStream app does: with a form.</summary>
    private async Task<string> LoginAsync(string userName, string password)
    {
        var response = await _factory.CreateClient().PostAsync("/api/v1/auth/login",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["username"] = userName, ["password"] = password }), Cancel);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Cancel)).GetProperty("token").GetString()!;
    }

    private HttpClient Client(string? token = null)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("x-access-token", token ?? _token);
        return client;
    }

    private async Task<JsonElement> PostAsync(string path, object body, string? token = null)
    {
        var response = await Client(token).PostAsJsonAsync(path, body, Cancel);
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{path}: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync(Cancel)}");
        return await response.Content.ReadFromJsonAsync<JsonElement>(Cancel);
    }

    private async Task<JsonElement> GetAsync(string path, string? token = null) =>
        await Client(token).GetFromJsonAsync<JsonElement>(path, Cancel);

    [Fact]
    public async Task Apps_sign_in_with_app_passwords_and_keep_their_token_until_it_is_deleted()
    {
        var anonymous = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/v1/ping", Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/", Cancel)).StatusCode);

        // JSON sign-in works too; the account's own password does not.
        var json = await anonymous.PostAsJsonAsync("/api/v1/auth/login", new { username = "admin", password = _secret }, Cancel);
        var login = await json.Content.ReadFromJsonAsync<JsonElement>(Cancel);
        Assert.Equal(["Music"], login.GetProperty("vpaths").EnumerateArray().Select(v => v.GetString()));
        var refused = await anonymous.PostAsJsonAsync("/api/v1/auth/login", new { username = "admin", password = ArcanistFactory.Password }, Cancel);
        Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
        Assert.Equal("Login Failed", (await refused.Content.ReadFromJsonAsync<JsonElement>(Cancel)).GetProperty("error").GetString());

        // The token goes in a header, the query or the cookie set at sign-in.
        var ping = await GetAsync("/api/v1/ping");
        Assert.Equal(["Music"], ping.GetProperty("vpaths").EnumerateArray().Select(v => v.GetString()));
        Assert.True(ping.GetProperty("noUpload").GetBoolean());
        Assert.True(ping.GetProperty("supportedAudioFiles").GetProperty("flac").GetBoolean());
        Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync($"/api/v1/ping?token={Uri.EscapeDataString(_token)}", Cancel)).StatusCode);
        Assert.Contains(json.Headers.GetValues("Set-Cookie"), c => c.StartsWith("x-access-token=") && c.Contains("httponly"));

        var server = await GetAsync("/api/");
        Assert.Equal(("admin", true), (server.GetProperty("user").GetProperty("username").GetString(), server.GetProperty("user").GetProperty("admin").GetBoolean()));
        Assert.Equal(JsonValueKind.Object, server.GetProperty("features").ValueKind);

        // Deleting the app password signs the app out.
        var id = (await _admin.GetFromJsonAsync<List<JsonElement>>("/rest/v1/me/app-passwords", ArcanistFactory.Json, Cancel))![0].GetProperty("id").GetString();
        await _admin.DeleteAsync($"/rest/v1/me/app-passwords/{id}", Cancel);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Client().GetAsync("/api/v1/ping", Cancel)).StatusCode);
    }

    [Fact]
    public async Task Apps_browse_the_library_and_its_folders()
    {
        // A measured tempo has decimals; mStream apps read it as a whole number.
        await using (var db = _factory.Services.GetRequiredService<IArcanistDbContextFactory>().CreateDbContext())
        {
            var hash = await db.MediaFiles.Where(f => f.RelativePath.EndsWith("01.wav") && f.RelativePath.Contains("First"))
                .Select(f => f.ContentHash).SingleAsync(Cancel);
            db.AudioAnalyses.Add(new AudioAnalysis { ContentHash = hash, State = AnalysisState.Analyzed, AnalyzedAt = DateTime.UtcNow, Bpm = 105.1f, BpmConfidence = 0.9f });
            await db.SaveChangesAsync(Cancel);
        }

        var artists = await GetAsync("/api/v1/db/artists");
        Assert.Equal(["The Band", "Zed"], artists.GetProperty("artists").EnumerateArray().Select(a => a.GetString()));

        var albums = (await PostAsync("/api/v1/db/artists-albums", new { artist = "the band" })).GetProperty("albums");
        var album = Assert.Single(albums.EnumerateArray());
        Assert.Equal(("First Album", 2001), (album.GetProperty("name").GetString(), album.GetProperty("year").GetInt32()));
        Assert.Matches("^[0-9a-f]{32}\\.jpg$", album.GetProperty("album_art_file").GetString()!);
        Assert.Equal(2, (await GetAsync("/api/v1/db/albums")).GetProperty("albums").GetArrayLength());

        var songs = (await PostAsync("/api/v1/db/album-songs", new { album = "First Album", year = 2001 })).EnumerateArray().ToList();
        Assert.Equal(["Music/The Band/First/01.wav", "Music/The Band/First/02.wav"], songs.Select(s => s.GetProperty("filepath").GetString()));
        var metadata = songs[0].GetProperty("metadata");
        Assert.Equal(("Opening", "The Band", 1, "Rock"), (metadata.GetProperty("title").GetString(), metadata.GetProperty("artist").GetString(),
            metadata.GetProperty("track").GetInt32(), metadata.GetProperty("genres")[0].GetString()));
        Assert.Equal(JsonValueKind.Null, metadata.GetProperty("rating").ValueKind);
        Assert.True(metadata.GetProperty("duration").GetDouble() > 0);
        Assert.Equal("105", metadata.GetProperty("bpm").GetRawText());

        var single = await PostAsync("/api/v1/db/metadata", new { filepath = "Music/Zed/01.wav" });
        Assert.Equal("Alone", single.GetProperty("metadata").GetProperty("title").GetString());
        Assert.Equal(JsonValueKind.Null, (await PostAsync("/api/v1/db/metadata", new { filepath = "Music/Zed/nope.wav" })).GetProperty("metadata").ValueKind);

        var search = await PostAsync("/api/v1/db/search", new { search = "zed" });
        Assert.Equal("Zed", search.GetProperty("artists")[0].GetProperty("name").GetString());
        Assert.False(search.GetProperty("artists")[0].GetProperty("filepath").GetBoolean());
        Assert.Equal("Zed - Alone", search.GetProperty("title")[0].GetProperty("name").GetString());
        Assert.Equal("Music/Zed/01.wav", search.GetProperty("files")[0].GetProperty("filepath").GetString());

        // Folders: the libraries, then their folders and files.
        var root = await PostAsync("/api/v1/file-explorer", new { directory = "" });
        Assert.Equal("Music", root.GetProperty("directories")[0].GetProperty("name").GetString());
        var folder = await PostAsync("/api/v1/file-explorer", new { directory = "Music/The Band/First", pullMetadata = true });
        Assert.Equal("/Music/The Band/First/", folder.GetProperty("path").GetString());
        var files = folder.GetProperty("files").EnumerateArray().ToList();
        Assert.Equal(["01.wav", "02.wav"], files.Select(f => f.GetProperty("name").GetString()));
        Assert.Equal(("wav", "Opening"), (files[0].GetProperty("type").GetString(), files[0].GetProperty("metadata").GetProperty("metadata").GetProperty("title").GetString()));
        var top = await PostAsync("/api/v1/file-explorer", new { directory = "~" });
        Assert.Equal(["The Band", "Zed"], top.GetProperty("directories").EnumerateArray().Select(d => d.GetProperty("name").GetString()));
        Assert.Equal(3, (await PostAsync("/api/v1/file-explorer/recursive", new { directory = "Music" })).GetArrayLength());
        Assert.Equal(HttpStatusCode.NotFound, (await Client().PostAsJsonAsync("/api/v1/file-explorer", new { directory = "Music/Nowhere" }, Cancel)).StatusCode);
    }

    [Fact]
    public async Task Apps_play_rate_and_keep_playlists()
    {
        var anonymous = _factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, $"/media/Music/The%20Band/First/01.wav?token={Uri.EscapeDataString(_token)}&app_uuid=x");
        request.Headers.Range = new RangeHeaderValue(0, 9);
        var media = await anonymous.SendAsync(request, Cancel);
        Assert.Equal(HttpStatusCode.PartialContent, media.StatusCode);
        Assert.Equal(10, (await media.Content.ReadAsByteArrayAsync(Cancel)).Length);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/media/Music/The%20Band/First/01.wav", Cancel)).StatusCode);

        var art = (await PostAsync("/api/v1/db/metadata", new { filepath = "Music/The Band/First/01.wav" })).GetProperty("metadata").GetProperty("album-art").GetString();
        var cover = await anonymous.GetAsync($"/album-art/{art}?compress=s&token={Uri.EscapeDataString(_token)}", Cancel);
        Assert.Equal(HttpStatusCode.OK, cover.StatusCode);

        var lyrics = await GetAsync("/api/v1/lyrics?path=Music/The%20Band/First/01.wav");
        Assert.Equal("First line", lyrics.GetProperty("lyrics").GetProperty("lyrics")[0].GetProperty("data").GetString());

        // Ratings are half stars: 7 is four stars, read back as 8.
        await PostAsync("/api/v1/db/rate-song", new { filepath = "Music/Zed/01.wav", rating = 7 });
        var rated = Assert.Single((await GetAsync("/api/v1/db/rated")).EnumerateArray());
        Assert.Equal(8, rated.GetProperty("metadata").GetProperty("rating").GetInt32());
        await PostAsync("/api/v1/db/rate-song", new { filepath = "Music/Zed/01.wav", rating = (int?)null });
        Assert.Equal(0, (await GetAsync("/api/v1/db/rated")).GetArrayLength());

        await PostAsync("/api/v1/lastfm/scrobble-by-filepath", new { filePath = "Music/Zed/01.wav" });
        var played = Assert.Single((await PostAsync("/api/v1/db/stats/most-played", new { limit = 5 })).EnumerateArray());
        Assert.Equal(1, played.GetProperty("metadata").GetProperty("play-count").GetInt32());
        Assert.False((await PostAsync("/api/v1/lastfm/scrobble-by-filepath", new { filePath = "Music/none.wav" })).GetProperty("scrobble").GetBoolean());

        // Playlists by name.
        await PostAsync("/api/v1/playlist/new", new { title = "Road" });
        Assert.Equal(HttpStatusCode.BadRequest, (await Client().PostAsJsonAsync("/api/v1/playlist/new", new { title = "Road" }, Cancel)).StatusCode);
        await PostAsync("/api/v1/playlist/add-song", new { song = "Music/Zed/01.wav", playlist = "Road" });
        await PostAsync("/api/v1/playlist/add-song", new { song = "Music/The Band/First/02.wav", playlist = "Road" });
        var loaded = (await PostAsync("/api/v1/playlist/load", new { playlistname = "Road" })).EnumerateArray().ToList();
        Assert.Equal(["Music/Zed/01.wav", "Music/The Band/First/02.wav"], loaded.Select(e => e.GetProperty("filepath").GetString()));
        await PostAsync("/api/v1/playlist/remove-song", new { lokiid = loaded[0].GetProperty("lokiId").GetInt32() });
        Assert.Single((await PostAsync("/api/v1/playlist/load", new { playlistname = "Road" })).EnumerateArray());

        await PostAsync("/api/v1/playlist/save", new { title = "Road", songs = new[] { "Music/The Band/First/01.wav", "Music/Zed/01.wav" } });
        await PostAsync("/api/v1/playlist/rename", new { oldName = "Road", newName = "Trip" });
        var trip = (await PostAsync("/api/v1/playlist/load", new { playlistname = "Trip" })).EnumerateArray();
        Assert.Equal(["Opening", "Alone"], trip.Select(e => e.GetProperty("metadata").GetProperty("title").GetString()));
        Assert.Equal(["Trip"], (await GetAsync("/api/v1/playlist/getall")).EnumerateArray().Select(p => p.GetProperty("name").GetString()));
        await PostAsync("/api/v1/playlist/delete", new { playlistname = "Trip" });
        Assert.Equal(0, (await GetAsync("/api/v1/playlist/getall")).GetArrayLength());
    }

    [Fact]
    public async Task Users_see_only_their_libraries()
    {
        await _factory.CreateUserAsync("guest");
        var secret = await CreateAppPasswordAsync(await _factory.CreateAuthenticatedClientAsync("guest"));
        var token = await LoginAsync("guest", secret);

        Assert.Equal(0, (await GetAsync("/api/v1/ping", token)).GetProperty("vpaths").GetArrayLength());
        Assert.Equal(0, (await GetAsync("/api/v1/db/artists", token)).GetProperty("artists").GetArrayLength());
        Assert.Equal(HttpStatusCode.NotFound, (await Client(token).GetAsync("/media/Music/Zed/01.wav", Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Client(token).PostAsJsonAsync("/api/v1/db/rate-song", new { filepath = "Music/Zed/01.wav", rating = 10 }, Cancel)).StatusCode);
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
