using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Arcanist.Core.Data.Entities;
using Arcanist.Core.Setup;
using Arcanist.Core.Tests.Media;
using Arcanist.Core.Tests.Scanning;

namespace Arcanist.Core.Tests.Listening;

/// <summary>Playlists shared with users by role, and public links that anyone opens to hear previews.</summary>
public sealed class PlaylistSharingTests : IAsyncLifetime
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private readonly string _root = Directory.CreateTempSubdirectory("arcanist-sharing-").FullName;
    private readonly ArcanistFactory _factory = new();
    private HttpClient _admin = null!;
    private readonly Dictionary<string, Guid> _tracks = [];
    private readonly Dictionary<string, Guid> _users = [];

    public async ValueTask InitializeAsync()
    {
        foreach (var (title, i) in new[] { "A", "B", "C" }.Select((t, i) => (t, i)))
            AudioFixtures.WriteWav(Path.Combine(_root, "Shared", $"{title}.wav"), i + 1, new FixtureTags(title, "Band", "Album", "Band", TrackNumber: i + 1));
        AudioFixtures.WriteWav(Path.Combine(_root, "Private", "secret.wav"), 9, new FixtureTags("Secret", "Band", "Hidden", "Band", TrackNumber: 1));

        await _factory.CompleteSetupAsync(
            new SetupLibrary("Shared", [Path.Combine(_root, "Shared")]),
            new SetupLibrary("Private", [Path.Combine(_root, "Private")], VisibleToAll: false));
        _admin = await _factory.CreateAuthenticatedClientAsync();
        await _factory.WaitForScansAsync(_admin);
        foreach (var name in new[] { "listener", "editor", "manager", "stranger" })
            _users[name] = (await _factory.CreateUserAsync(name)).Id;
        _users["sharer"] = (await _factory.CreateUserAsync("sharer", permissions: Permissions.ShareLinks)).Id;

        var page = await _admin.GetFromJsonAsync<JsonElement>("/rest/v1/tracks", ArcanistFactory.Json, Cancel);
        foreach (var track in page.GetProperty("items").EnumerateArray())
            _tracks[track.GetProperty("title").GetString()!] = track.GetProperty("id").GetGuid();
    }

    public async ValueTask DisposeAsync()
    {
        await _factory.DisposeAsync();
        Directory.Delete(_root, recursive: true);
    }

    private async Task<JsonElement> CreateAsync(HttpClient client, string name, params string[] titles) =>
        await OkAsync(client.PostAsJsonAsync("/rest/v1/playlists", new { name, isPublic = false, trackIds = titles.Select(t => _tracks[t]) }, ArcanistFactory.Json, Cancel));

    private static async Task<JsonElement> OkAsync(Task<HttpResponseMessage> request)
    {
        var response = await request;
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode}: {await response.Content.ReadAsStringAsync(Cancel)}");
        return await response.Content.ReadFromJsonAsync<JsonElement>(ArcanistFactory.Json, Cancel);
    }

    private static async Task<string?> CodeAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>(ArcanistFactory.Json, Cancel)).GetProperty("code").GetString();

    private Task<JsonElement> ShareAsync(HttpClient client, string id, params (string User, string Role)[] members) =>
        OkAsync(client.PutAsJsonAsync($"/rest/v1/playlists/{id}/members",
            new { members = members.Select(m => new { userId = _users[m.User], role = m.Role }) }, ArcanistFactory.Json, Cancel));

    [Fact]
    public async Task Members_listen_edit_or_manage_and_only_the_owner_deletes()
    {
        var owner = await _factory.CreateAuthenticatedClientAsync("sharer");
        var id = (await CreateAsync(owner, "Ours", "A")).GetProperty("id").GetString()!;
        var shared = await ShareAsync(owner, id, ("listener", "LISTEN"), ("editor", "EDIT"), ("manager", "MANAGE"));
        Assert.Equal(["editor:EDIT", "listener:LISTEN", "manager:MANAGE"],
            shared.GetProperty("members").EnumerateArray().Select(m => $"{m.GetProperty("user").GetProperty("name").GetString()}:{m.GetProperty("role").GetString()}"));
        Assert.Equal(["editor", "manager"], shared.GetProperty("collaborators").EnumerateArray().Select(u => u.GetProperty("name").GetString()));

        // Listeners see it and play it; they change nothing.
        var listener = await _factory.CreateAuthenticatedClientAsync("listener");
        Assert.Equal("VIEWER", (await listener.GetFromJsonAsync<JsonElement>($"/rest/v1/playlists/{id}", ArcanistFactory.Json, Cancel)).GetProperty("role").GetString());
        Assert.Equal(HttpStatusCode.Forbidden, (await listener.PostAsJsonAsync($"/rest/v1/playlists/{id}/entries",
            new { trackIds = new[] { _tracks["B"] } }, ArcanistFactory.Json, Cancel)).StatusCode);
        var stranger = await _factory.CreateAuthenticatedClientAsync("stranger");
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync($"/rest/v1/playlists/{id}", Cancel)).StatusCode);

        // Editors change its songs, not its name or who it is shared with.
        var editor = await _factory.CreateAuthenticatedClientAsync("editor");
        await OkAsync(editor.PostAsJsonAsync($"/rest/v1/playlists/{id}/entries", new { trackIds = new[] { _tracks["B"] } }, ArcanistFactory.Json, Cancel));
        Assert.Equal(HttpStatusCode.Forbidden, (await editor.PutAsJsonAsync($"/rest/v1/playlists/{id}",
            new { name = "Mine", isPublic = false }, ArcanistFactory.Json, Cancel)).StatusCode);

        // Managers rename it and share it; only the owner deletes it.
        var manager = await _factory.CreateAuthenticatedClientAsync("manager");
        var renamed = await OkAsync(manager.PutAsJsonAsync($"/rest/v1/playlists/{id}", new { name = "Renamed", isPublic = false }, ArcanistFactory.Json, Cancel));
        Assert.Equal(("Renamed", "MANAGER"), (renamed.GetProperty("name").GetString(), renamed.GetProperty("role").GetString()));
        await ShareAsync(manager, id, ("listener", "LISTEN"), ("manager", "MANAGE"), ("stranger", "EDIT"));
        Assert.Equal(HttpStatusCode.OK, (await stranger.GetAsync($"/rest/v1/playlists/{id}", Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await editor.GetAsync($"/rest/v1/playlists/{id}", Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.DeleteAsync($"/rest/v1/playlists/{id}", Cancel)).StatusCode);

        // The old call keeps working: those listed edit (a manager stays one), listeners stay.
        var collaborators = await OkAsync(owner.PutAsJsonAsync($"/rest/v1/playlists/{id}/collaborators",
            new { userIds = new[] { _users["manager"], _users["editor"] } }, ArcanistFactory.Json, Cancel));
        Assert.Equal(["editor:EDIT", "listener:LISTEN", "manager:MANAGE"],
            collaborators.GetProperty("members").EnumerateArray().Select(m => $"{m.GetProperty("user").GetProperty("name").GetString()}:{m.GetProperty("role").GetString()}"));
        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync($"/rest/v1/playlists/{id}", Cancel)).StatusCode);
    }

    [Fact]
    public async Task A_public_link_shows_the_owners_songs_with_previews_to_anyone_while_allowed()
    {
        var owner = await _factory.CreateAuthenticatedClientAsync("sharer");
        var id = (await CreateAsync(owner, "Party", "A", "B")).GetProperty("id").GetString()!;

        // Off until an administrator allows it, and only for those with SHARE_LINKS.
        var off = await owner.PostAsync($"/rest/v1/playlists/{id}/link", null, Cancel);
        Assert.Equal((HttpStatusCode.Conflict, "public_links_disabled"), (off.StatusCode, await CodeAsync(off)));
        await OkAsync(_admin.PutAsJsonAsync("/rest/v1/sharing", new { publicLinks = true, jamGuests = false, guestAddLimit = 5, maxGuests = 100 }, ArcanistFactory.Json, Cancel));
        var plain = await _factory.CreateAuthenticatedClientAsync("listener");
        var mine = (await CreateAsync(plain, "Mine", "A")).GetProperty("id").GetString()!;
        Assert.Equal(HttpStatusCode.Forbidden, (await plain.PostAsync($"/rest/v1/playlists/{mine}/link", null, Cancel)).StatusCode);

        var linked = await OkAsync(owner.PostAsync($"/rest/v1/playlists/{id}/link", null, Cancel));
        var token = linked.GetProperty("link").GetProperty("token").GetString()!;
        Assert.EndsWith($"#/s/{token}", linked.GetProperty("link").GetProperty("url").GetString());
        Assert.Equal(token, (await OkAsync(owner.PostAsync($"/rest/v1/playlists/{id}/link", null, Cancel))).GetProperty("link").GetProperty("token").GetString());

        // Anyone sees its songs as the owner has them, never as tracks to play.
        var anyone = _factory.CreateClient();
        var shared = await OkAsync(anyone.GetAsync($"/rest/v1/public/playlists/{token}", Cancel));
        Assert.Equal(("Party", "sharer", 2), (shared.GetProperty("name").GetString(), shared.GetProperty("owner").GetString(), shared.GetProperty("trackCount").GetInt32()));
        var entries = shared.GetProperty("entries").GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(["A", "B"], entries.Select(e => e.GetProperty("title").GetString()));
        Assert.All(entries, e => Assert.Equal(JsonValueKind.Null, e.GetProperty("track").ValueKind));
        Assert.Equal(JsonValueKind.Null, shared.GetProperty("playlistId").ValueKind);

        // A signed-in user also gets the songs they can play, and saves a copy.
        var stranger = await _factory.CreateAuthenticatedClientAsync("stranger");
        var signedIn = await OkAsync(stranger.GetAsync($"/rest/v1/public/playlists/{token}", Cancel));
        Assert.All(signedIn.GetProperty("entries").GetProperty("items").EnumerateArray(),
            e => Assert.False(string.IsNullOrEmpty(e.GetProperty("track").GetProperty("streamUrl").GetString())));
        var copy = await OkAsync(stranger.PostAsJsonAsync($"/rest/v1/public/playlists/{token}/copy", new { name = "Saved" }, ArcanistFactory.Json, Cancel));
        Assert.Equal(("Saved", "OWNER", 2), (copy.GetProperty("name").GetString(), copy.GetProperty("role").GetString(), copy.GetProperty("trackCount").GetInt32()));

        // It stops working when revoked, when the owner loses the permission, or when public links are turned off.
        await OkAsync(owner.DeleteAsync($"/rest/v1/playlists/{id}/link", Cancel));
        Assert.Equal(HttpStatusCode.NotFound, (await anyone.GetAsync($"/rest/v1/public/playlists/{token}", Cancel)).StatusCode);
        token = (await OkAsync(owner.PostAsync($"/rest/v1/playlists/{id}/link", null, Cancel))).GetProperty("link").GetProperty("token").GetString()!;
        Assert.Equal(HttpStatusCode.OK, (await anyone.GetAsync($"/rest/v1/public/playlists/{token}", Cancel)).StatusCode);
        await OkAsync(_admin.PutAsJsonAsync("/rest/v1/sharing", new { publicLinks = false, jamGuests = false, guestAddLimit = 5, maxGuests = 100 }, ArcanistFactory.Json, Cancel));
        Assert.Equal(HttpStatusCode.NotFound, (await anyone.GetAsync($"/rest/v1/public/playlists/{token}", Cancel)).StatusCode);
        Assert.Equal(JsonValueKind.Null, (await OkAsync(owner.GetAsync($"/rest/v1/playlists/{id}", Cancel))).GetProperty("link").ValueKind);
    }

    [Fact]
    public async Task A_preview_is_a_short_mp3_anyone_can_hear()
    {
        TestFfmpeg.Require();
        await OkAsync(_admin.PutAsJsonAsync("/rest/v1/sharing", new { publicLinks = true, jamGuests = false, guestAddLimit = 5, maxGuests = 100 }, ArcanistFactory.Json, Cancel));
        var id = (await CreateAsync(_admin, "Mine", "A", "Secret")).GetProperty("id").GetString()!;
        var token = (await OkAsync(_admin.PostAsync($"/rest/v1/playlists/{id}/link", null, Cancel))).GetProperty("link").GetProperty("token").GetString()!;

        var anyone = _factory.CreateClient();
        var shared = await OkAsync(anyone.GetAsync($"/rest/v1/public/playlists/{token}", Cancel));
        var preview = shared.GetProperty("entries").GetProperty("items")[0].GetProperty("previewUrl").GetString()!;
        var response = await anyone.GetAsync(preview, Cancel);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("audio/mpeg", response.Content.Headers.ContentType?.MediaType);
        Assert.True((await response.Content.ReadAsByteArrayAsync(Cancel)).Length > 1000);
        Assert.Equal(HttpStatusCode.NotFound, (await anyone.GetAsync($"/rest/v1/public/playlists/{token}/preview/{Guid.NewGuid()}", Cancel)).StatusCode);
    }
}
