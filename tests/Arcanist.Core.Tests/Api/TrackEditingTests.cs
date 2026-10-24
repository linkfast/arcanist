using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Arcanist.Core.Setup;
using Arcanist.Core.Tests.Scanning;
using Permissions = Arcanist.Core.Data.Entities.Permissions;

namespace Arcanist.Core.Tests.Api;

/// <summary>
/// What a library app (Ultimate Desktop with a server as its only library) does to tracks: editing tags and covers in
/// the files, deleting them, their properties, renaming libraries, storage, where uploads go and incremental refresh.
/// </summary>
public sealed class TrackEditingTests : IAsyncLifetime
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private readonly string _rock = Directory.CreateTempSubdirectory("arcanist-edit-rock-").FullName;
    private readonly string _jazz = Directory.CreateTempSubdirectory("arcanist-edit-jazz-").FullName;
    private readonly string _outside = Directory.CreateTempSubdirectory("arcanist-edit-outside-").FullName;
    private readonly ArcanistFactory _factory = new(new Dictionary<string, string?> { ["Arcanist:Uploads:ScanDelay"] = "00:00:00.2" });
    private HttpClient _admin = null!;
    private Guid _rockId;
    private Guid _jazzId;

    public async ValueTask InitializeAsync()
    {
        AudioFixtures.WriteWav(Path.Combine(_rock, "Riff.wav"), 1, new FixtureTags("Riff", "The Band", "Loud", "The Band", TrackNumber: 1, Year: 1999));
        AudioFixtures.WriteWav(Path.Combine(_rock, "Ballad.wav"), 2, new FixtureTags("Ballad", "The Band", "Loud", "The Band", TrackNumber: 2, WithCover: true));
        AudioFixtures.WriteWav(Path.Combine(_jazz, "Swing.wav"), 3, new FixtureTags("Swing", "Trio", "Smooth", "Trio", TrackNumber: 1));
        // The same audio as Riff in the other library: one track, two copies.
        File.Copy(Path.Combine(_rock, "Riff.wav"), Path.Combine(_jazz, "Riff copy.wav"));
        await _factory.CompleteSetupAsync(new SetupLibrary("Rock", [_rock]), new SetupLibrary("Jazz", [_jazz]));
        _admin = await _factory.CreateAuthenticatedClientAsync();
        await _factory.WaitForScansAsync(_admin);

        var libraries = await _admin.GetFromJsonAsync<JsonElement>("/rest/v1/libraries", ArcanistFactory.Json, Cancel);
        var ids = libraries.EnumerateArray().ToDictionary(l => l.GetProperty("name").GetString()!, l => l.GetProperty("id").GetGuid());
        (_rockId, _jazzId) = (ids["Rock"], ids["Jazz"]);
    }

    private async Task<List<JsonElement>> TracksAsync(string query = "") =>
        (await _admin.GetFromJsonAsync<JsonElement>("/rest/v1/tracks" + query, ArcanistFactory.Json, Cancel)).GetProperty("items").EnumerateArray().ToList();

    private async Task<JsonElement> TrackAsync(string title) => (await TracksAsync()).Single(t => t.GetProperty("title").GetString() == title);

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>(ArcanistFactory.Json, Cancel);

    private static Task<HttpResponseMessage> PatchAsync(HttpClient client, string url, object body) =>
        client.PatchAsync(url, JsonContent.Create(body, options: ArcanistFactory.Json), Cancel);

    private static ATL.Track Tags(string path) => new(path);

    private static string[] Trashed(string root) =>
        Directory.Exists(Path.Combine(root, ".arcanist-trash")) ? Directory.GetFiles(Path.Combine(root, ".arcanist-trash"), "*", SearchOption.AllDirectories) : [];

    [Fact]
    public async Task The_server_says_it_can_and_a_track_has_what_a_properties_dialog_shows()
    {
        var info = await _admin.GetFromJsonAsync<JsonElement>("/rest/v1/server-info", ArcanistFactory.Json, Cancel);
        var features = info.GetProperty("features").EnumerateArray().Select(f => f.GetString()).ToList();
        foreach (var feature in new[] { "TRACK_EDITING", "TRACK_DELETION", "ARTWORK_EDITING", "TRACK_PROPERTIES", "STORAGE_USAGE", "UPLOAD_TARGET", "CHANGES_SINCE", "LIBRARY_RENAME" })
            Assert.Contains(feature, features);

        var riff = await TrackAsync("Riff");
        var track = await _admin.GetFromJsonAsync<JsonElement>($"/rest/v1/tracks/{riff.GetProperty("id").GetGuid()}", ArcanistFactory.Json, Cancel);
        Assert.Equal("The Band", track.GetProperty("albumArtist").GetProperty("name").GetString());
        Assert.True(track.GetProperty("canEdit").GetBoolean());
        Assert.True(track.GetProperty("canDelete").GetBoolean());
        Assert.NotEqual(default, track.GetProperty("updatedAt").GetDateTime());
        var files = track.GetProperty("files").EnumerateArray().ToList();
        Assert.Equal(2, files.Count);
        Assert.Equal(["Riff copy.wav", "Riff.wav"], files.Select(f => f.GetProperty("path").GetString()).Order());
        Assert.Equal(new[] { _rockId, _jazzId }.Order(), files.Select(f => f.GetProperty("libraryId").GetGuid()).Order());
        Assert.Single(files.Select(f => f.GetProperty("contentHash").GetString()).Distinct());
        Assert.All(files, f => Assert.False(string.IsNullOrEmpty(f.GetProperty("format").GetString())));
        Assert.All(files, f => Assert.True(f.GetProperty("size").GetInt64() > 0 && f.GetProperty("modifiedAt").GetDateTime() > DateTime.MinValue));

        // Without the permissions, the hints say no.
        await _factory.CreateUserAsync("listener");
        var listener = await _factory.CreateAuthenticatedClientAsync("listener");
        var seen = (await listener.GetFromJsonAsync<JsonElement>("/rest/v1/tracks", ArcanistFactory.Json, Cancel)).GetProperty("items")[0];
        Assert.False(seen.GetProperty("canEdit").GetBoolean());
        Assert.False(seen.GetProperty("canDelete").GetBoolean());
    }

    [Fact]
    public async Task Editing_writes_every_copy_s_tags_and_the_catalog_and_keeps_the_track()
    {
        var riff = (await TrackAsync("Riff")).GetProperty("id").GetGuid();
        await _admin.PutAsJsonAsync($"/rest/v1/tracks/{riff}/rating", new { rating = 4 }, ArcanistFactory.Json, Cancel);

        var response = await PatchAsync(_admin, $"/rest/v1/tracks/{riff}", new
        {
            title = "Riff (Live)",
            artists = new[] { "The Band", "Guest" },
            album = "Loud Live",
            genres = new[] { "Rock", "Live" },
            year = 2001,
            comment = "Recorded live",
            bpm = 128.5,
            composers = new[] { "Writer" },
            lyrics = "La la la",
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var edited = await JsonAsync(response);
        Assert.Equal(riff, edited.GetProperty("id").GetGuid());
        Assert.Equal("Riff (Live)", edited.GetProperty("title").GetString());
        Assert.Equal("Loud Live", edited.GetProperty("album").GetProperty("title").GetString());
        Assert.Equal(2001, edited.GetProperty("year").GetInt32());
        Assert.Equal(["Live", "Rock"], edited.GetProperty("genres").EnumerateArray().Select(g => g.GetString()).Order());
        Assert.Equal(["Guest", "The Band", "Writer"], edited.GetProperty("artists").EnumerateArray().Select(a => a.GetProperty("name").GetString()).Order());
        Assert.Equal("Recorded live", edited.GetProperty("comment").GetString());
        Assert.Equal(128.5, edited.GetProperty("taggedBpm").GetDouble());
        Assert.True(edited.GetProperty("hasLyrics").GetBoolean());
        Assert.Equal(4, edited.GetProperty("rating").GetInt32());
        Assert.Equal(2, edited.GetProperty("files").GetArrayLength());

        // Both files say it, and both originals are in their folder's trash.
        foreach (var path in new[] { Path.Combine(_rock, "Riff.wav"), Path.Combine(_jazz, "Riff copy.wav") })
        {
            var tags = Tags(path);
            Assert.Equal(("Riff (Live)", "Loud Live", 2001), (tags.Title, tags.Album, tags.Year));
            Assert.Equal("Recorded live", tags.Comment);
        }
        Assert.Single(Trashed(_rock));
        Assert.Single(Trashed(_jazz));

        // A full scan finds the same: nothing moves, the track and its rating stay.
        await _admin.PostAsJsonAsync($"/rest/v1/libraries/{_rockId}/scans", new { full = true }, ArcanistFactory.Json, Cancel);
        await _admin.PostAsJsonAsync($"/rest/v1/libraries/{_jazzId}/scans", new { full = true }, ArcanistFactory.Json, Cancel);
        await _factory.WaitForScansAsync(_admin);
        var after = await TrackAsync("Riff (Live)");
        Assert.Equal(riff, after.GetProperty("id").GetGuid());
        Assert.Equal(4, after.GetProperty("rating").GetInt32());
        Assert.DoesNotContain(await TracksAsync(), t => t.GetProperty("title").GetString() == "Riff");
        // The old album went with its last track.
        Assert.DoesNotContain((await _admin.GetFromJsonAsync<JsonElement>("/rest/v1/albums", ArcanistFactory.Json, Cancel)).GetProperty("items").EnumerateArray(),
            a => a.GetProperty("title").GetString() == "Loud" && a.GetProperty("trackCount").GetInt32() == 2);

        // Clearing: the comment and the year go.
        var cleared = await JsonAsync(await PatchAsync(_admin, $"/rest/v1/tracks/{riff}", new { comment = "", year = 0 }));
        Assert.Equal(JsonValueKind.Null, cleared.GetProperty("comment").ValueKind);
        Assert.True(string.IsNullOrEmpty(Tags(Path.Combine(_rock, "Riff.wav")).Comment));

        // Validation.
        var invalid = await PatchAsync(_admin, $"/rest/v1/tracks/{riff}", new { title = " " });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
    }

    [Fact]
    public async Task An_edit_that_moves_a_track_takes_its_user_data_and_its_other_copies_along()
    {
        var riff = (await TrackAsync("Riff")).GetProperty("id").GetGuid();
        await _admin.PutAsJsonAsync($"/rest/v1/tracks/{riff}/rating", new { rating = 4 }, ArcanistFactory.Json, Cancel);
        // The copy in Jazz goes missing: it is not rewritten, but it is still one of the track's files.
        File.Move(Path.Combine(_jazz, "Riff copy.wav"), Path.Combine(_outside, "Riff copy.wav"));
        await _admin.PostAsJsonAsync($"/rest/v1/libraries/{_jazzId}/scans", new { full = true }, ArcanistFactory.Json, Cancel);
        await _factory.WaitForScansAsync(_admin);

        var edited = await JsonAsync(await PatchAsync(_admin, $"/rest/v1/tracks/{riff}", new { album = "Somewhere Else" }));
        Assert.Equal("Somewhere Else", edited.GetProperty("album").GetProperty("title").GetString());
        Assert.Equal(4, edited.GetProperty("rating").GetInt32());
        // One track for the song, never two.
        Assert.Single(await TracksAsync(), t => t.GetProperty("title").GetString() == "Riff");
    }

    [Fact]
    public async Task Covers_sent_without_signing_in_or_the_permission_are_never_written()
    {
        var album = (await TrackAsync("Riff")).GetProperty("album").GetProperty("id").GetGuid();
        var cache = Path.Combine(_factory.DataPath, "cache", "artwork");
        int Stored() => Directory.Exists(cache) ? Directory.GetFiles(cache, "*", SearchOption.AllDirectories).Length : 0;
        var before = Stored();
        var image = new ByteArrayContent(AudioFixtures.Png(seed: 42));
        image.Headers.ContentType = new MediaTypeHeaderValue("image/png");

        var anonymous = await _factory.CreateClient().PutAsync($"/rest/v1/albums/{album}/artwork", image, Cancel);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        await _factory.CreateUserAsync("plain");
        var plain = await _factory.CreateAuthenticatedClientAsync("plain");
        Assert.Equal(HttpStatusCode.Forbidden, (await plain.PutAsync($"/rest/v1/albums/{album}/artwork", image, Cancel)).StatusCode);
        Assert.Equal(before, Stored());
    }

    [Fact]
    public async Task Editing_needs_the_permission_and_a_library_the_user_adds_to()
    {
        var swing = (await TrackAsync("Swing")).GetProperty("id").GetGuid();
        await _factory.CreateUserAsync("viewer", permissions: Permissions.Upload);
        var viewer = await _factory.CreateAuthenticatedClientAsync("viewer");
        Assert.Equal(HttpStatusCode.Forbidden, (await PatchAsync(viewer, $"/rest/v1/tracks/{swing}", new { title = "X" })).StatusCode);

        var editor = await _factory.CreateUserAsync("editor", permissions: Permissions.EditMetadata | Permissions.DeleteTracks);
        await _admin.PutAsJsonAsync($"/rest/v1/users/{editor.Id}/libraries",
            new { libraryIds = Array.Empty<Guid>(), readOnlyLibraryIds = new[] { _jazzId } }, ArcanistFactory.Json, Cancel);
        var client = await _factory.CreateAuthenticatedClientAsync("editor");
        var refused = await PatchAsync(client, $"/rest/v1/tracks/{swing}", new { title = "X" });
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        Assert.Equal("library_read_only", (await JsonAsync(refused)).GetProperty("code").GetString());
        Assert.Equal("Swing", Tags(Path.Combine(_jazz, "Swing.wav")).Title);
        var deletion = await JsonAsync(await client.PostAsJsonAsync("/rest/v1/tracks/delete", new { trackIds = new[] { swing } }, ArcanistFactory.Json, Cancel));
        Assert.Equal(("FORBIDDEN", "library_read_only"), (deletion[0].GetProperty("status").GetString(), deletion[0].GetProperty("code").GetString()));

        // In Rock it may.
        var ballad = (await TrackAsync("Ballad")).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await PatchAsync(client, $"/rest/v1/tracks/{ballad}", new { title = "Ballad 2" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await PatchAsync(client, $"/rest/v1/tracks/{Guid.NewGuid()}", new { title = "X" })).StatusCode);
    }

    [Fact]
    public async Task Many_tracks_are_edited_at_once_and_an_album_keeps_its_cover()
    {
        var tracks = await TracksAsync($"?libraryId={_rockId}");
        var ids = tracks.Select(t => t.GetProperty("id").GetGuid()).ToList();
        var cover = (await TrackAsync("Ballad")).GetProperty("artworkId").GetGuid();

        var response = await _admin.PostAsJsonAsync("/rest/v1/tracks/edit", new { trackIds = ids, changes = new { album = "Loud (Remaster)", albumArtist = "The Band" } },
            ArcanistFactory.Json, Cancel);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var edited = (await JsonAsync(response)).EnumerateArray().ToList();
        Assert.Equal(ids, edited.Select(t => t.GetProperty("id").GetGuid()));
        Assert.Single(edited.Select(t => t.GetProperty("album").GetProperty("id").GetGuid()).Distinct());
        var album = await _admin.GetFromJsonAsync<JsonElement>($"/rest/v1/albums/{edited[0].GetProperty("album").GetProperty("id").GetGuid()}", ArcanistFactory.Json, Cancel);
        Assert.Equal("Loud (Remaster)", album.GetProperty("title").GetString());
        Assert.Equal(cover, album.GetProperty("artworkId").GetGuid());
        Assert.Equal(2, album.GetProperty("trackCount").GetInt32());
    }

    [Fact]
    public async Task Covers_are_replaced_and_removed_in_the_files()
    {
        var swing = await TrackAsync("Swing");
        var id = swing.GetProperty("id").GetGuid();
        var png = AudioFixtures.Png(seed: 3, size: 32);
        var put = await _admin.PutAsync($"/rest/v1/tracks/{id}/artwork", new ByteArrayContent(png) { Headers = { ContentType = new MediaTypeHeaderValue("image/png") } }, Cancel);
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        var track = await JsonAsync(put);
        var artwork = track.GetProperty("artworkId").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await _admin.GetAsync($"/rest/v1/artwork/{artwork}", Cancel)).StatusCode);
        Assert.Equal(png, Tags(Path.Combine(_jazz, "Swing.wav")).EmbeddedPictures.Single().PictureData);
        // The album had none: it takes it.
        var albumId = track.GetProperty("album").GetProperty("id").GetGuid();
        Assert.Equal(artwork, (await _admin.GetFromJsonAsync<JsonElement>($"/rest/v1/albums/{albumId}", ArcanistFactory.Json, Cancel)).GetProperty("artworkId").GetGuid());

        // Not an image.
        var bad = await _admin.PutAsync($"/rest/v1/tracks/{id}/artwork", new ByteArrayContent([1, 2, 3]), Cancel);
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);

        // The album's cover goes, from the files too.
        var removed = await _admin.DeleteAsync($"/rest/v1/albums/{albumId}/artwork", Cancel);
        Assert.Equal(HttpStatusCode.OK, removed.StatusCode);
        Assert.Equal(JsonValueKind.Null, (await JsonAsync(removed)).GetProperty("artworkId").ValueKind);
        Assert.Empty(Tags(Path.Combine(_jazz, "Swing.wav")).EmbeddedPictures);
        Assert.Equal(JsonValueKind.Null, (await TrackAsync("Swing")).GetProperty("artworkId").ValueKind);
    }

    [Fact]
    public async Task Deleting_moves_the_files_to_the_trash_and_cleans_up_user_data()
    {
        var riff = (await TrackAsync("Riff")).GetProperty("id").GetGuid();
        var ballad = (await TrackAsync("Ballad")).GetProperty("id").GetGuid();
        await _admin.PutAsJsonAsync($"/rest/v1/tracks/{ballad}/star", new { starred = true }, ArcanistFactory.Json, Cancel);
        var playlist = await JsonAsync(await _admin.PostAsJsonAsync("/rest/v1/playlists", new { name = "Mix", isPublic = false, trackIds = new[] { riff, ballad } },
            ArcanistFactory.Json, Cancel));
        var playlistId = playlist.GetProperty("id").GetGuid();

        // Only the copy in Jazz: the track stays with the other one.
        var result = await JsonAsync(await _admin.PostAsJsonAsync("/rest/v1/tracks/delete", new { trackIds = new[] { riff, Guid.NewGuid() }, libraryId = _jazzId },
            ArcanistFactory.Json, Cancel));
        Assert.Equal(["DELETED", "NOT_FOUND"], result.EnumerateArray().Select(r => r.GetProperty("status").GetString()));
        Assert.False(File.Exists(Path.Combine(_jazz, "Riff copy.wav")));
        Assert.Single(Trashed(_jazz));
        Assert.Equal([_rockId], (await TrackAsync("Riff")).GetProperty("libraryIds").EnumerateArray().Select(l => l.GetGuid()));

        // The whole track: its file, its star and its playlist entry go.
        Assert.Equal(HttpStatusCode.NoContent, (await _admin.DeleteAsync($"/rest/v1/tracks/{ballad}", Cancel)).StatusCode);
        Assert.False(File.Exists(Path.Combine(_rock, "Ballad.wav")));
        Assert.Equal(HttpStatusCode.NotFound, (await _admin.GetAsync($"/rest/v1/tracks/{ballad}", Cancel)).StatusCode);
        Assert.Empty((await _admin.GetFromJsonAsync<JsonElement>("/rest/v1/me/star-tracks", ArcanistFactory.Json, Cancel)).GetProperty("items").EnumerateArray());
        var entries = await _admin.GetFromJsonAsync<JsonElement>($"/rest/v1/playlists/{playlistId}/entries", ArcanistFactory.Json, Cancel);
        Assert.Equal([riff], entries.GetProperty("items").EnumerateArray().Select(e => e.GetProperty("track").GetProperty("id").GetGuid()));
        Assert.Equal(HttpStatusCode.NotFound, (await _admin.DeleteAsync($"/rest/v1/tracks/{ballad}", Cancel)).StatusCode);

        // A scan does not bring it back (the trash is hidden).
        await _admin.PostAsJsonAsync($"/rest/v1/libraries/{_rockId}/scans", new { full = true }, ArcanistFactory.Json, Cancel);
        await _factory.WaitForScansAsync(_admin);
        Assert.DoesNotContain(await TracksAsync(), t => t.GetProperty("title").GetString() == "Ballad");

        // Without the permission, nothing.
        await _factory.CreateUserAsync("user");
        var user = await _factory.CreateAuthenticatedClientAsync("user");
        Assert.Equal(HttpStatusCode.Forbidden, (await user.DeleteAsync($"/rest/v1/tracks/{riff}", Cancel)).StatusCode);
    }

    [Fact]
    public async Task Libraries_are_renamed_by_managers()
    {
        var renamed = await _admin.PutAsJsonAsync($"/rest/v1/libraries/{_rockId}/name", new { name = "Rock & Roll" }, ArcanistFactory.Json, Cancel);
        Assert.Equal("Rock & Roll", (await JsonAsync(renamed)).GetProperty("name").GetString());
        var taken = await _admin.PutAsJsonAsync($"/rest/v1/libraries/{_rockId}/name", new { name = "jazz" }, ArcanistFactory.Json, Cancel);
        Assert.Equal(HttpStatusCode.BadRequest, taken.StatusCode);

        await _factory.CreateUserAsync("guest");
        var guest = await _factory.CreateAuthenticatedClientAsync("guest");
        Assert.Equal(HttpStatusCode.Forbidden, (await guest.PutAsJsonAsync($"/rest/v1/libraries/{_rockId}/name", new { name = "Mine" }, ArcanistFactory.Json, Cancel)).StatusCode);

        // GraphQL too.
        var graph = await _admin.GraphQLAsync("mutation($id: ID!) { renameLibrary(id: $id, input: { name: \"Jazz Club\" }) { name } }", new { id = _jazzId });
        Assert.Null(graph.ErrorCode());
        Assert.Equal("Jazz Club", graph.GetProperty("data").GetProperty("renameLibrary").GetProperty("name").GetString());
    }

    [Fact]
    public async Task The_storage_used_is_told_library_by_library()
    {
        var storage = await _admin.GetFromJsonAsync<JsonElement>("/rest/v1/me/storage", ArcanistFactory.Json, Cancel);
        var libraries = storage.GetProperty("libraries").EnumerateArray().ToDictionary(l => l.GetProperty("libraryId").GetGuid());
        var rockBytes = new[] { "Riff.wav", "Ballad.wav" }.Sum(f => new FileInfo(Path.Combine(_rock, f)).Length);
        Assert.Equal((2, 2, rockBytes), (libraries[_rockId].GetProperty("files").GetInt32(), libraries[_rockId].GetProperty("tracks").GetInt32(),
            libraries[_rockId].GetProperty("bytes").GetInt64()));
        Assert.True(libraries[_rockId].GetProperty("freeBytes").GetInt64() > 0);
        Assert.True(libraries[_rockId].GetProperty("totalBytes").GetInt64() >= libraries[_rockId].GetProperty("freeBytes").GetInt64());
        Assert.Equal(4, storage.GetProperty("files").GetInt32());
        Assert.Equal(libraries.Values.Sum(l => l.GetProperty("bytes").GetInt64()), storage.GetProperty("bytes").GetInt64());
        Assert.Equal(JsonValueKind.Null, storage.GetProperty("uploadLibraryId").ValueKind);

        // A user who sees one library sees only it.
        var user = await _factory.CreateUserAsync("one");
        await _admin.PutAsJsonAsync($"/rest/v1/users/{user.Id}/libraries", new { libraryIds = new[] { _jazzId } }, ArcanistFactory.Json, Cancel);
        await _admin.PutAsJsonAsync($"/rest/v1/libraries/{_rockId}", new { name = "Rock", visibleToAll = false, scanIntervalMinutes = (int?)null, watchForChanges = false },
            ArcanistFactory.Json, Cancel);
        await _admin.PutAsJsonAsync($"/rest/v1/libraries/{_jazzId}", new { name = "Jazz", visibleToAll = false, scanIntervalMinutes = (int?)null, watchForChanges = false },
            ArcanistFactory.Json, Cancel);
        var client = await _factory.CreateAuthenticatedClientAsync("one");
        var theirs = await client.GetFromJsonAsync<JsonElement>("/rest/v1/me/storage", ArcanistFactory.Json, Cancel);
        Assert.Equal([_jazzId], theirs.GetProperty("libraries").EnumerateArray().Select(l => l.GetProperty("libraryId").GetGuid()));
    }

    [Fact]
    public async Task Uploads_go_to_the_library_the_client_or_the_user_chooses()
    {
        var file = Path.Combine(_outside, "new.wav");
        AudioFixtures.WriteWav(file, 9, new FixtureTags("Fresh", "Newcomer", "Debut", "Newcomer", TrackNumber: 1));
        HttpContent Body() => new ByteArrayContent(File.ReadAllBytes(file)) { Headers = { ContentType = new MediaTypeHeaderValue("application/octet-stream") } };

        // Nothing chosen: required.
        var none = await _admin.PostAsync("/rest/v1/uploads?fileName=new.wav", Body(), Cancel);
        Assert.Equal(HttpStatusCode.BadRequest, none.StatusCode);

        // The client says where.
        Assert.Equal(HttpStatusCode.Created, (await _admin.PostAsync($"/rest/v1/uploads?fileName=new.wav&libraryId={_jazzId}", Body(), Cancel)).StatusCode);
        Assert.True(File.Exists(Path.Combine(_jazz, "Newcomer", "Debut", "01 Fresh.wav")));

        // The user's own default.
        var set = await JsonAsync(await _admin.PutAsJsonAsync("/rest/v1/me/upload-settings", new { libraryId = _rockId }, ArcanistFactory.Json, Cancel));
        Assert.Equal((_rockId, "Rock", _rockId), (set.GetProperty("libraryId").GetGuid(), set.GetProperty("libraryName").GetString(), set.GetProperty("effectiveLibraryId").GetGuid()));
        Assert.Equal(HttpStatusCode.Created, (await _admin.PostAsync("/rest/v1/uploads?fileName=new.wav", Body(), Cancel)).StatusCode);
        Assert.True(File.Exists(Path.Combine(_rock, "Newcomer", "Debut", "01 Fresh.wav")));
        Assert.Equal(_rockId, (await _admin.GetFromJsonAsync<JsonElement>("/rest/v1/me/storage", ArcanistFactory.Json, Cancel)).GetProperty("uploadLibraryId").GetGuid());

        // A library the user cannot upload to is refused; null goes back to the server's default.
        var refused = await _admin.PutAsJsonAsync("/rest/v1/me/upload-settings", new { libraryId = Guid.NewGuid() }, ArcanistFactory.Json, Cancel);
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        var cleared = await JsonAsync(await _admin.PutAsJsonAsync("/rest/v1/me/upload-settings", new { libraryId = (Guid?)null }, ArcanistFactory.Json, Cancel));
        Assert.Equal(JsonValueKind.Null, cleared.GetProperty("libraryId").ValueKind);
        Assert.Equal(JsonValueKind.Null, (await _admin.GetFromJsonAsync<JsonElement>("/rest/v1/me/upload-settings", ArcanistFactory.Json, Cancel))
            .GetProperty("effectiveLibraryId").ValueKind);
    }

    [Fact]
    public async Task Clients_refresh_only_what_changed()
    {
        var first = await _admin.GetFromJsonAsync<JsonElement>("/rest/v1/tracks", ArcanistFactory.Json, Cancel);
        var since = first.GetProperty("serverTime").GetDateTime();
        var playlists = await _admin.GetFromJsonAsync<JsonElement>("/rest/v1/playlists", ArcanistFactory.Json, Cancel);
        Assert.Empty(playlists.EnumerateArray());
        await Task.Delay(20, Cancel);

        var ids = await _admin.GetFromJsonAsync<JsonElement>("/rest/v1/tracks/ids", ArcanistFactory.Json, Cancel);
        Assert.Equal(3, ids.GetProperty("ids").GetArrayLength());
        Assert.Equal(2, (await _admin.GetFromJsonAsync<JsonElement>($"/rest/v1/tracks/ids?libraryId={_jazzId}", ArcanistFactory.Json, Cancel)).GetProperty("ids").GetArrayLength());

        string Since(DateTime value) => Uri.EscapeDataString(value.ToString("O"));
        Assert.Empty((await TracksAsync($"?changedSince={Since(since)}")));

        // An edit and a rating show up; nothing else does.
        var swing = (await TrackAsync("Swing")).GetProperty("id").GetGuid();
        var ballad = (await TrackAsync("Ballad")).GetProperty("id").GetGuid();
        await PatchAsync(_admin, $"/rest/v1/tracks/{swing}", new { genres = new[] { "Jazz" } });
        await _admin.PutAsJsonAsync($"/rest/v1/tracks/{ballad}/rating", new { rating = 3 }, ArcanistFactory.Json, Cancel);
        Assert.Equal(["Ballad", "Swing"], (await TracksAsync($"?changedSince={Since(since)}")).Select(t => t.GetProperty("title").GetString()).Order());

        await _admin.PostAsJsonAsync("/rest/v1/playlists", new { name = "New", isPublic = false }, ArcanistFactory.Json, Cancel);
        Assert.Single((await _admin.GetFromJsonAsync<JsonElement>($"/rest/v1/playlists?changedSince={Since(since)}", ArcanistFactory.Json, Cancel)).EnumerateArray());

        // GraphQL has the same.
        var graph = await _admin.GraphQLAsync("query($since: DateTime) { tracks(changedSince: $since) { items { title } serverTime } trackIds { ids } }", new { since });
        Assert.Null(graph.ErrorCode());
        Assert.Equal(2, graph.GetProperty("data").GetProperty("tracks").GetProperty("items").GetArrayLength());
        Assert.Equal(3, graph.GetProperty("data").GetProperty("trackIds").GetProperty("ids").GetArrayLength());

        // A deleted track leaves the ids.
        await _admin.DeleteAsync($"/rest/v1/tracks/{ballad}", Cancel);
        Assert.DoesNotContain(ballad, (await _admin.GetFromJsonAsync<JsonElement>("/rest/v1/tracks/ids", ArcanistFactory.Json, Cancel))
            .GetProperty("ids").EnumerateArray().Select(i => i.GetGuid()));
    }

    [Fact]
    public async Task GraphQL_edits_and_deletes_too()
    {
        var swing = (await TrackAsync("Swing")).GetProperty("id").GetGuid();
        var edit = await _admin.GraphQLAsync("mutation($id: ID!) { editTrack(id: $id, input: { title: \"Swing Low\", trackNumber: 7 }) { id title trackNumber canEdit } }",
            new { id = swing });
        Assert.Null(edit.ErrorCode());
        Assert.Equal(("Swing Low", 7), (edit.GetProperty("data").GetProperty("editTrack").GetProperty("title").GetString(),
            edit.GetProperty("data").GetProperty("editTrack").GetProperty("trackNumber").GetInt32()));
        Assert.Equal(7, Tags(Path.Combine(_jazz, "Swing.wav")).TrackNumber);

        var delete = await _admin.GraphQLAsync("mutation($ids: [ID!]!) { deleteTracks(input: { trackIds: $ids }) { trackId status code } }", new { ids = new[] { swing } });
        Assert.Null(delete.ErrorCode());
        Assert.Equal("DELETED", delete.GetProperty("data").GetProperty("deleteTracks")[0].GetProperty("status").GetString());

        var storage = await _admin.GraphQLAsync("{ myStorage { files bytes libraries { name files freeBytes } } myUploadSettings { effectiveLibraryId } }");
        Assert.Null(storage.ErrorCode());
        Assert.Equal(3, storage.GetProperty("data").GetProperty("myStorage").GetProperty("files").GetInt32());
    }

    [Fact]
    public async Task GraphQL_sets_covers_from_base64()
    {
        var swing = (await TrackAsync("Swing")).GetProperty("id").GetGuid();
        var png = AudioFixtures.Png(seed: 5, size: 24);
        var set = await _admin.GraphQLAsync("mutation($id: ID!, $image: String!) { setTrackArtwork(id: $id, image: $image) { artworkId } }",
            new { id = swing, image = "data:image/png;base64," + Convert.ToBase64String(png) });
        Assert.Null(set.ErrorCode());
        Assert.Equal(png, Tags(Path.Combine(_jazz, "Swing.wav")).EmbeddedPictures.Single().PictureData);
        var bad = await _admin.GraphQLAsync("mutation($id: ID!) { setTrackArtwork(id: $id, image: \"%%%\") { artworkId } }", new { id = swing });
        Assert.Equal("validation_failed", bad.ErrorCode());
    }

    [Fact]
    public async Task A_user_copies_their_own_music_and_data_without_being_an_administrator()
    {
        var riff = (await TrackAsync("Riff")).GetProperty("id").GetGuid();
        var user = await _factory.CreateUserAsync("fan", permissions: Permissions.Download);
        var fan = await _factory.CreateAuthenticatedClientAsync("fan");
        await fan.PutAsJsonAsync($"/rest/v1/tracks/{riff}/rating", new { rating = 5 }, ArcanistFactory.Json, Cancel);
        Assert.NotEqual(Guid.Empty, user.Id);

        // Page by page, every file with its hash and a resumable download.
        var seen = new List<JsonElement>();
        string? after = null;
        do
        {
            var page = await fan.GetFromJsonAsync<JsonElement>("/rest/v1/me/export/files?first=2" + (after is null ? "" : "&after=" + after), ArcanistFactory.Json, Cancel);
            Assert.Equal(4, page.GetProperty("totalCount").GetInt32());
            seen.AddRange(page.GetProperty("items").EnumerateArray());
            after = page.GetProperty("nextCursor").GetString();
        } while (after is not null);
        Assert.Equal(4, seen.Select(f => f.GetProperty("fileId").GetGuid()).Distinct().Count());
        var file = seen.First(f => f.GetProperty("path").GetString() == "Riff.wav");
        Assert.False(string.IsNullOrEmpty(file.GetProperty("contentHash").GetString()));
        var anonymous = _factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, file.GetProperty("downloadUrl").GetString()) { Headers = { Range = new RangeHeaderValue(10, 19) } };
        var part = await anonymous.SendAsync(request, Cancel);
        Assert.Equal(HttpStatusCode.PartialContent, part.StatusCode);
        Assert.Equal(File.ReadAllBytes(Path.Combine(_rock, "Riff.wav"))[10..20], await part.Content.ReadAsByteArrayAsync(Cancel));

        // Their data, in the backups' format.
        var data = await fan.GraphQLAsync("{ myExportData }");
        Assert.Null(data.ErrorCode());
        var document = JsonDocument.Parse(data.GetProperty("data").GetProperty("myExportData").GetString()!).RootElement;
        var mine = Assert.Single(document.GetProperty("userData").GetProperty("users").EnumerateArray());
        Assert.Equal(5, Assert.Single(mine.GetProperty("ratings").EnumerateArray()).GetProperty("rating").GetInt32());
        Assert.NotEmpty(document.GetProperty("catalog").GetProperty("tracks").EnumerateArray());

        // Files need DOWNLOAD.
        await _factory.CreateUserAsync("nodownload");
        var other = await _factory.CreateAuthenticatedClientAsync("nodownload");
        Assert.Equal(HttpStatusCode.Forbidden, (await other.GetAsync("/rest/v1/me/export/files", Cancel)).StatusCode);
    }

    [Fact]
    public async Task The_migration_export_pages_files_with_their_audio_hashes()
    {
        var code = (await _admin.GraphQLAsync("mutation { createMigrationCode { code } }"))
            .GetProperty("data").GetProperty("createMigrationCode").GetProperty("code").GetString()!;
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Arcanist-Migration", code);
        var all = await client.GetFromJsonAsync<JsonElement>($"/rest/v1/export/libraries/{_rockId}/files", ArcanistFactory.Json, Cancel);
        Assert.Equal(["Ballad.wav", "Riff.wav"], all.EnumerateArray().Select(f => f.GetProperty("path").GetString()));
        Assert.All(all.EnumerateArray(), f => Assert.False(string.IsNullOrEmpty(f.GetProperty("contentHash").GetString())));
        var second = await client.GetFromJsonAsync<JsonElement>($"/rest/v1/export/libraries/{_rockId}/files?after=Ballad.wav&first=1", ArcanistFactory.Json, Cancel);
        Assert.Equal("Riff.wav", Assert.Single(second.EnumerateArray()).GetProperty("path").GetString());
    }

    public async ValueTask DisposeAsync()
    {
        await _factory.DisposeAsync();
        foreach (var folder in new[] { _rock, _jazz, _outside })
        {
            try { Directory.Delete(folder, recursive: true); }
            catch (IOException) { }
        }
    }
}
