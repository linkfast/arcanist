using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Arcanist.Core.Data;
using Arcanist.Core.Import.Mstream;
using Arcanist.Core.Setup;
using Arcanist.Core.Tests.Online;
using Arcanist.Core.Tests.Scanning;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace Arcanist.Core.Tests.Extensions;

/// <summary>Importing mStream installations: from their files (mStream 5 and 6) and from a server's API.</summary>
public sealed class MstreamImportTests : IAsyncDisposable
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private const string MstreamPassword = "mstream pass 1";

    private readonly string _music = Directory.CreateTempSubdirectory("arcanist-mstream-music-").FullName;
    private readonly string _install = Directory.CreateTempSubdirectory("arcanist-mstream-install-").FullName;
    private readonly FakeOnlineServices _server = new();
    private readonly ArcanistFactory _factory;

    public MstreamImportTests()
    {
        _factory = new ArcanistFactory(services: services =>
            services.AddHttpClient(MstreamServerClient.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => _server));
        AudioFixtures.WriteWav(Path.Combine(_music, "Zed", "01.wav"), 1, new FixtureTags("Alone", "Zed", "Solo", "Zed", TrackNumber: 1));
        AudioFixtures.WriteWav(Path.Combine(_music, "Zed", "02.wav"), 2, new FixtureTags("Again", "Zed", "Solo", "Zed", TrackNumber: 2));
    }

    /// <summary>mStream's password hash: PBKDF2-SHA512, 15000 rounds, 32 bytes, with the base64 salt used as text.</summary>
    private static (string Hash, string Salt) MstreamHash(string password)
    {
        var salt = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));
        var hash = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), Encoding.UTF8.GetBytes(salt), 15000, HashAlgorithmName.SHA512, 32);
        return (Convert.ToBase64String(hash), salt);
    }

    /// <summary>An mStream 6 installation: configuration and SQLite database, with the music folder as library "music".</summary>
    private void WriteVersion6()
    {
        Directory.CreateDirectory(Path.Combine(_install, "save", "conf"));
        File.WriteAllText(Path.Combine(_install, "save", "conf", "default.json"), """{"secret": "s3cret"}""");
        Directory.CreateDirectory(Path.Combine(_install, "save", "db"));
        var (hash, salt) = MstreamHash(MstreamPassword);

        using var connection = new SqliteConnection($"Data Source={Path.Combine(_install, "save", "db", "mstream.db")};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"""
            CREATE TABLE users (id INTEGER PRIMARY KEY, username TEXT, password TEXT, salt TEXT, is_admin INT, is_anonymous_sentinel INT DEFAULT 0);
            CREATE TABLE libraries (id INTEGER PRIMARY KEY, name TEXT, root_path TEXT, type TEXT);
            CREATE TABLE user_libraries (user_id INT, library_id INT);
            CREATE TABLE tracks (id INTEGER PRIMARY KEY, filepath TEXT, library_id INT, file_hash TEXT, audio_hash TEXT);
            CREATE TABLE user_metadata (id INTEGER PRIMARY KEY, user_id INT, track_hash TEXT, play_count INT, last_played TEXT, rating INT);
            CREATE TABLE playlists (id INTEGER PRIMARY KEY, name TEXT, user_id INT);
            CREATE TABLE playlist_tracks (id INTEGER PRIMARY KEY, playlist_id INT, filepath TEXT, position INT);
            INSERT INTO users VALUES (1, 'paul', '{hash}', '{salt}', 0, 0), (2, '__mstream_anonymous__', 'x', 'y', 0, 1);
            INSERT INTO libraries VALUES (1, 'music', '{_music.Replace("'", "''")}', 'music');
            INSERT INTO user_libraries VALUES (1, 1);
            INSERT INTO tracks VALUES (1, 'Zed/01.wav', 1, 'f1', 'a1'), (2, 'Zed/02.wav', 1, 'f2', NULL);
            INSERT INTO user_metadata VALUES (1, 1, 'a1', 3, '2026-10-01 18:22:05.123', 7), (2, 1, 'f2', 0, NULL, 10), (3, 1, 'gone', 1, NULL, 2);
            INSERT INTO playlists VALUES (1, 'Road', 1);
            INSERT INTO playlist_tracks VALUES (1, 1, 'music/Zed/02.wav', 0), (2, 1, 'music/Zed/missing.wav', 1), (3, 1, 'music/Zed/01.wav', 2);
            """;
        command.ExecuteNonQuery();
    }

    /// <summary>An mStream 5 installation: users and folders in the configuration, data in LokiJS files.</summary>
    private void WriteVersion5()
    {
        var (hash, salt) = MstreamHash(MstreamPassword);
        Directory.CreateDirectory(Path.Combine(_install, "save", "conf"));
        File.WriteAllText(Path.Combine(_install, "save", "conf", "default.json"), JsonSerializer.Serialize(new
        {
            folders = new Dictionary<string, object> { ["music"] = new { root = _music, type = "music" } },
            users = new Dictionary<string, object> { ["paul"] = new { password = hash, salt, vpaths = "music", admin = false } },
        }));
        Directory.CreateDirectory(Path.Combine(_install, "save", "db"));
        File.WriteAllText(Path.Combine(_install, "save", "db", "files.loki-v3.db"), JsonSerializer.Serialize(new
        {
            collections = new[]
            {
                new { name = "files", data = new object[] { new { vpath = "music", filepath = "Zed\\01.wav", hash = "h1", __loki = 1 } } },
            },
        }));
        File.WriteAllText(Path.Combine(_install, "save", "db", "user-data.loki-v1.db"), JsonSerializer.Serialize(new
        {
            collections = new object[]
            {
                new
                {
                    name = "playlists",
                    data = new object?[]
                    {
                        new Dictionary<string, object?> { ["name"] = "Old", ["user"] = "paul", ["filepath"] = null, ["$loki"] = 1 },
                        new Dictionary<string, object?> { ["name"] = "Old", ["user"] = "paul", ["filepath"] = "music/Zed/01.wav", ["$loki"] = 2 },
                    },
                },
                new { name = "user-metadata", data = new object[] { new { user = "paul", hash = "h1", rating = 4, pc = 2, lp = 1_760_000_000_000 } } },
            },
        }));
    }

    private async Task<JsonElement> PostAsync(HttpClient client, string path, object body)
    {
        var response = await client.PostAsJsonAsync(path, body, ArcanistFactory.Json, Cancel);
        Assert.True(response.IsSuccessStatusCode, $"{path}: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync(Cancel)}");
        return await response.Content.ReadFromJsonAsync<JsonElement>(ArcanistFactory.Json, Cancel);
    }

    private async Task<JsonElement> WaitForImportAsync(HttpClient admin)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (true)
        {
            var import = (await admin.GetFromJsonAsync<List<JsonElement>>("/rest/v1/mstream/imports", ArcanistFactory.Json, Cancel))![0];
            if (import.GetProperty("status").GetString() is "COMPLETED" or "FAILED")
                return import;
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException("The import did not finish in time.");
            await Task.Delay(50, Cancel);
        }
    }

    private async Task<Dictionary<string, JsonElement>> TracksAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<JsonElement>("/rest/v1/tracks", ArcanistFactory.Json, Cancel)).GetProperty("items").EnumerateArray()
        .ToDictionary(t => t.GetProperty("title").GetString()!);

    [Fact]
    public async Task An_mStream_6_installation_brings_its_library_users_passwords_and_data()
    {
        WriteVersion6();
        await _factory.CompleteSetupAsync();
        var admin = await _factory.CreateAuthenticatedClientAsync();

        var analysis = await PostAsync(admin, "/rest/v1/mstream/analyze-folder", new { folder = _install });
        Assert.Equal(6, analysis.GetProperty("version").GetInt32());
        var library = Assert.Single(analysis.GetProperty("libraries").EnumerateArray());
        Assert.Equal(("music", true), (library.GetProperty("vpath").GetString(), library.GetProperty("rootExists").GetBoolean()));
        var user = Assert.Single(analysis.GetProperty("users").EnumerateArray());
        Assert.Equal(("paul", true, false), (user.GetProperty("userName").GetString(), user.GetProperty("canSignIn").GetBoolean(), user.GetProperty("exists").GetBoolean()));
        Assert.Equal((1, 2, 1), (analysis.GetProperty("playlists").GetInt32(), analysis.GetProperty("ratedTracks").GetInt32(), analysis.GetProperty("playedTracks").GetInt32()));

        var started = await PostAsync(admin, "/rest/v1/mstream/imports", new
        {
            folder = _install,
            libraries = new[] { new { vpath = "music", newLibraryPath = _music } },
            importUsers = true,
        });
        Assert.Equal((1, 1), (started.GetProperty("librariesCreated").GetInt32(), started.GetProperty("usersCreated").GetInt32()));
        var import = await WaitForImportAsync(admin);
        Assert.Equal("COMPLETED", import.GetProperty("status").GetString());
        Assert.Equal((1, 2, 1), (import.GetProperty("playlists").GetInt32(), import.GetProperty("ratings").GetInt32(), import.GetProperty("plays").GetInt32()));
        // The playlist song that is gone and the data of a file mStream no longer knows.
        Assert.Equal(2, import.GetProperty("unmatched").GetInt32());

        // Paul signs in with his mStream password, which then becomes an Arcanist one.
        await _factory.LoginAsync("paul", MstreamPassword);
        await _factory.LoginAsync("paul", MstreamPassword);
        var paul = await _factory.CreateAuthenticatedClientAsync("paul", MstreamPassword);
        var tracks = await TracksAsync(paul);
        Assert.Equal((4, 3), (tracks["Alone"].GetProperty("rating").GetInt32(), tracks["Alone"].GetProperty("playCount").GetInt32()));
        Assert.Equal(5, tracks["Again"].GetProperty("rating").GetInt32());
        var playlist = Assert.Single((await paul.GetFromJsonAsync<List<JsonElement>>("/rest/v1/playlists", ArcanistFactory.Json, Cancel))!);
        Assert.Equal(("Road", 2), (playlist.GetProperty("name").GetString(), playlist.GetProperty("trackCount").GetInt32()));
    }

    [Fact]
    public async Task An_mStream_5_installation_maps_to_an_existing_library()
    {
        WriteVersion5();
        await _factory.CompleteSetupAsync(new SetupLibrary("Music", [_music], VisibleToAll: false));
        var admin = await _factory.CreateAuthenticatedClientAsync();
        await _factory.WaitForScansAsync(admin);

        var analysis = await PostAsync(admin, "/rest/v1/mstream/analyze-folder", new { folder = Path.Combine(_install, "save", "conf", "default.json") });
        Assert.Equal(5, analysis.GetProperty("version").GetInt32());
        var sourceId = analysis.GetProperty("libraries")[0].GetProperty("sourceId").GetString();
        Assert.NotNull(sourceId);

        await PostAsync(admin, "/rest/v1/mstream/imports", new { folder = _install, libraries = new[] { new { vpath = "music", sourceId } }, importUsers = true });
        var import = await WaitForImportAsync(admin);
        Assert.Equal((1, 1, 1, 0), (import.GetProperty("playlists").GetInt32(), import.GetProperty("ratings").GetInt32(),
            import.GetProperty("plays").GetInt32(), import.GetProperty("librariesCreated").GetInt32()));

        // Paul gets the library of his vpath.
        var paul = await _factory.CreateAuthenticatedClientAsync("paul", MstreamPassword);
        var alone = (await TracksAsync(paul))["Alone"];
        Assert.Equal((2, 2), (alone.GetProperty("rating").GetInt32(), alone.GetProperty("playCount").GetInt32()));
    }

    [Fact]
    public async Task Users_of_an_mStream_server_sign_in_with_its_password_and_bring_their_data()
    {
        var online = true;
        _server.Respond = request =>
        {
            if (!online)
                throw new HttpRequestException("offline");
            var path = request.RequestUri!.AbsolutePath;
            var body = _server.Bodies.GetValueOrDefault(request) ?? "";
            return path switch
            {
                "/api/v1/auth/login" when body.Contains("\"admin\"") && body.Contains("\"root pass\"") => FakeOnlineServices.Json("""{"token":"t-admin","vpaths":[]}"""),
                "/api/v1/auth/login" when body.Contains("\"anna\"") && body.Contains("\"anna pass\"") => FakeOnlineServices.Json("""{"token":"t-anna","vpaths":["music"]}"""),
                "/api/v1/auth/login" => new HttpResponseMessage(HttpStatusCode.Unauthorized),
                "/api/v1/admin/directories" => FakeOnlineServices.Json("""{"music":{"id":1,"root":"/srv/music","type":"music"}}"""),
                "/api/v1/admin/users" => FakeOnlineServices.Json("""{"anna":{"admin":false,"vpaths":["music"]}}"""),
                "/api/v1/playlist/getall" => FakeOnlineServices.Json("""[{"name":"Fav"}]"""),
                "/api/v1/playlist/load" => FakeOnlineServices.Json("""[{"id":1,"lokiId":1,"filepath":"music/Zed/01.wav","metadata":{}}]"""),
                "/api/v1/db/rated" => FakeOnlineServices.Json("""[{"filepath":"music/Zed/02.wav","metadata":{"rating":10}}]"""),
                "/api/v1/db/stats/most-played" => FakeOnlineServices.Json("""[{"filepath":"music/Zed/01.wav","metadata":{"play-count":5,"last-played":1760000000000}}]"""),
                _ => new HttpResponseMessage(HttpStatusCode.NotFound),
            };
        };

        await _factory.CompleteSetupAsync(new SetupLibrary("Music", [_music], VisibleToAll: false));
        var admin = await _factory.CreateAuthenticatedClientAsync();
        await _factory.WaitForScansAsync(admin);

        var refused = await admin.PostAsJsonAsync("/rest/v1/mstream/analyze-server",
            new { url = "http://mstream.test:3000", userName = "admin", password = "wrong" }, ArcanistFactory.Json, Cancel);
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        var server = new { url = "http://mstream.test:3000/", userName = "admin", password = "root pass" };
        var analysis = await PostAsync(admin, "/rest/v1/mstream/analyze-server", server);
        Assert.Equal(JsonValueKind.Null, analysis.GetProperty("version").ValueKind);
        Assert.Equal(("music", "/srv/music"), (analysis.GetProperty("libraries")[0].GetProperty("vpath").GetString(), analysis.GetProperty("libraries")[0].GetProperty("root").GetString()));
        Assert.Equal("anna", analysis.GetProperty("users")[0].GetProperty("userName").GetString());

        var sources = (await admin.GetFromJsonAsync<List<JsonElement>>("/rest/v1/libraries", ArcanistFactory.Json, Cancel))![0].GetProperty("sources");
        var import = await PostAsync(admin, "/rest/v1/mstream/imports",
            new { server, libraries = new[] { new { vpath = "music", sourceId = sources[0].GetProperty("id").GetString() } }, importUsers = true });
        Assert.Equal(("COMPLETED", "SERVER", 1), (import.GetProperty("status").GetString(), import.GetProperty("kind").GetString(), import.GetProperty("usersCreated").GetInt32()));

        // Anna's first sign-in asks mStream, then brings her data.
        Assert.Equal(HttpStatusCode.Unauthorized, (await _factory.CreateClient().PostAsJsonAsync("/rest/v1/auth/login",
            new { userName = "anna", password = "nope pass" }, ArcanistFactory.Json, Cancel)).StatusCode);
        await _factory.LoginAsync("anna", "anna pass");
        await ((MstreamServerSignIn)_factory.Services.GetServices<Core.Auth.IExternalSignIn>().Single()).Pending;

        // Her password is Arcanist's now: the mStream server is no longer needed.
        online = false;
        var anna = await _factory.CreateAuthenticatedClientAsync("anna", "anna pass");
        var tracks = await TracksAsync(anna);
        Assert.Equal((5, 1), (tracks["Alone"].GetProperty("playCount").GetInt32(), Assert.Single((await anna.GetFromJsonAsync<List<JsonElement>>("/rest/v1/playlists", ArcanistFactory.Json, Cancel))!).GetProperty("trackCount").GetInt32()));
        Assert.Equal(5, tracks["Again"].GetProperty("rating").GetInt32());
        var report = (await admin.GetFromJsonAsync<List<JsonElement>>("/rest/v1/mstream/imports", ArcanistFactory.Json, Cancel))![0];
        Assert.Equal((1, 1, 1), (report.GetProperty("playlists").GetInt32(), report.GetProperty("ratings").GetInt32(), report.GetProperty("plays").GetInt32()));
    }

    [Fact]
    public async Task The_setup_wizard_reads_and_imports_an_mStream_installation()
    {
        WriteVersion6();
        var client = _factory.CreateClient();

        var analysis = await PostAsync(client, "/rest/v1/setup/mstream/analyze", new { folder = _install });
        Assert.Equal(6, analysis.GetProperty("version").GetInt32());
        var library = Assert.Single(analysis.GetProperty("libraries").EnumerateArray());
        Assert.Equal(("music", true), (library.GetProperty("vpath").GetString(), library.GetProperty("rootExists").GetBoolean()));
        Assert.Equal(JsonValueKind.Null, library.GetProperty("sourceId").ValueKind);

        SetupRequest Request(string folder) => new(
            new SetupDatabase(DatabaseProvider.Sqlite, null),
            new SetupAdmin(ArcanistFactory.AdminUserName, ArcanistFactory.Password, null),
            [new SetupLibrary("Music", [_music], VisibleToAll: false)],
            ShareAnonymousStatistics: false,
            Language: null,
            Mstream: new SetupMstreamImport(_install, null, [new SetupMstreamLibrary("music", folder)], ImportUsers: true));

        // The vpath must go to one of the new libraries' folders; nothing is set up otherwise.
        var refused = await client.PostAsJsonAsync("/rest/v1/setup/complete", Request(Path.Combine(_music, "Zed")), ArcanistFactory.Json, Cancel);
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Contains("mstream.libraries[0].folder", await refused.Content.ReadAsStringAsync(Cancel));

        // The same folder, written another way.
        var sameFolder = _music.Replace(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + "/";
        (await client.PostAsJsonAsync("/rest/v1/setup/complete", Request(sameFolder), ArcanistFactory.Json, Cancel)).EnsureSuccessStatusCode();
        var admin = await _factory.CreateAuthenticatedClientAsync();
        var import = await WaitForImportAsync(admin);
        Assert.Equal("COMPLETED", import.GetProperty("status").GetString());
        Assert.Equal((1, 1), (import.GetProperty("librariesCreated").GetInt32(), import.GetProperty("usersCreated").GetInt32()));
        Assert.Equal((1, 2, 1), (import.GetProperty("playlists").GetInt32(), import.GetProperty("ratings").GetInt32(), import.GetProperty("plays").GetInt32()));

        // Paul keeps his mStream password and gets the library of his vpath, with his data.
        var paul = await _factory.CreateAuthenticatedClientAsync("paul", MstreamPassword);
        var alone = (await TracksAsync(paul))["Alone"];
        Assert.Equal((4, 3), (alone.GetProperty("rating").GetInt32(), alone.GetProperty("playCount").GetInt32()));
    }

    [Fact]
    public async Task Only_administrators_import_and_unknown_folders_are_reported()
    {
        await _factory.CompleteSetupAsync();
        var admin = await _factory.CreateAuthenticatedClientAsync();
        var missing = await admin.PostAsJsonAsync("/rest/v1/mstream/analyze-folder", new { folder = Path.Combine(_install, "nothing") }, ArcanistFactory.Json, Cancel);
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);

        await _factory.CreateUserAsync("guest");
        var guest = await _factory.CreateAuthenticatedClientAsync("guest");
        Assert.Equal(HttpStatusCode.Forbidden, (await guest.PostAsJsonAsync("/rest/v1/mstream/analyze-folder", new { folder = _install }, ArcanistFactory.Json, Cancel)).StatusCode);
    }

    public async ValueTask DisposeAsync()
    {
        await _factory.DisposeAsync();
        foreach (var folder in new[] { _music, _install })
        {
            try
            {
                Directory.Delete(folder, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }
}
