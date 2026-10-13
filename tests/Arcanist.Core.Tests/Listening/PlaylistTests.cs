using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Arcanist.Core.Application.Libraries;
using Arcanist.Core.Application.Users;
using Arcanist.Core.Setup;
using Arcanist.Core.Tests.Scanning;

namespace Arcanist.Core.Tests.Listening;

/// <summary>Private, public and collaborative playlists.</summary>
public sealed class PlaylistTests : IAsyncLifetime
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private readonly string _root = Directory.CreateTempSubdirectory("arcanist-playlists-").FullName;
    private readonly ArcanistFactory _factory = new();
    private HttpClient _owner = null!;
    private HttpClient _friend = null!;
    private HttpClient _stranger = null!;
    private Guid _friendId;
    private readonly Dictionary<string, Guid> _tracks = [];

    public async ValueTask InitializeAsync()
    {
        foreach (var (title, i) in new[] { "A", "B", "C", "D" }.Select((t, i) => (t, i)))
            AudioFixtures.WriteWav(Path.Combine(_root, "Shared", $"{title}.wav"), i + 1, new FixtureTags(title, "Band", "Album", "Band", TrackNumber: i + 1, WithCover: i == 0));
        AudioFixtures.WriteWav(Path.Combine(_root, "Private", "secret.wav"), 9, new FixtureTags("Secret", "Band", "Hidden", "Band", TrackNumber: 1));

        await _factory.CompleteSetupAsync(
            new SetupLibrary("Shared", [Path.Combine(_root, "Shared")]),
            new SetupLibrary("Private", [Path.Combine(_root, "Private")], VisibleToAll: false));
        _owner = await _factory.CreateAuthenticatedClientAsync();
        await _factory.WaitForScansAsync(_owner);

        _friendId = (await _factory.CreateUserAsync("friend")).Id;
        await _factory.CreateUserAsync("stranger");
        _friend = await _factory.CreateAuthenticatedClientAsync("friend");
        _stranger = await _factory.CreateAuthenticatedClientAsync("stranger");

        var page = await _owner.GetFromJsonAsync<JsonElement>("/rest/v1/tracks", ArcanistFactory.Json, Cancel);
        foreach (var track in page.GetProperty("items").EnumerateArray())
            _tracks[track.GetProperty("title").GetString()!] = track.GetProperty("id").GetGuid();
    }

    private async Task<JsonElement> CreateAsync(HttpClient client, string name, bool isPublic, params string[] titles)
    {
        var response = await client.PostAsJsonAsync("/rest/v1/playlists",
            new { name, description = (string?)null, isPublic, trackIds = titles.Select(t => _tracks[t]) }, ArcanistFactory.Json, Cancel);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>(ArcanistFactory.Json, Cancel);
    }

    private async Task<List<string>> TitlesAsync(HttpClient client, string playlistId) =>
        (await client.GetFromJsonAsync<JsonElement>($"/rest/v1/playlists/{playlistId}/entries", ArcanistFactory.Json, Cancel))
        .GetProperty("items").EnumerateArray().Select(e => e.GetProperty("track").GetProperty("title").GetString()!).ToList();

    private async Task<List<string>> EntryIdsAsync(HttpClient client, string playlistId) =>
        (await client.GetFromJsonAsync<JsonElement>($"/rest/v1/playlists/{playlistId}/entries", ArcanistFactory.Json, Cancel))
        .GetProperty("items").EnumerateArray().Select(e => e.GetProperty("id").GetString()!).ToList();

    [Fact]
    public async Task Tracks_are_added_removed_and_moved_in_order()
    {
        var playlist = await CreateAsync(_owner, "Mix", false, "A", "B");
        var id = playlist.GetProperty("id").GetString()!;
        Assert.Equal(("OWNER", 2), (playlist.GetProperty("role").GetString(), playlist.GetProperty("trackCount").GetInt32()));
        Assert.Single(playlist.GetProperty("artworkIds").EnumerateArray());

        await _owner.PostAsJsonAsync($"/rest/v1/playlists/{id}/entries", new { trackIds = new[] { _tracks["C"], _tracks["A"] }, position = 1 }, ArcanistFactory.Json, Cancel);
        Assert.Equal(["A", "C", "A", "B"], await TitlesAsync(_owner, id));

        var entries = await EntryIdsAsync(_owner, id);
        await _owner.PostAsJsonAsync($"/rest/v1/playlists/{id}/entries/move", new { entryIds = new[] { entries[0], entries[1] }, position = 2 }, ArcanistFactory.Json, Cancel);
        Assert.Equal(["A", "B", "A", "C"], await TitlesAsync(_owner, id));

        entries = await EntryIdsAsync(_owner, id);
        var response = await _owner.PostAsJsonAsync($"/rest/v1/playlists/{id}/entries/remove", new { entryIds = new[] { entries[2] } }, ArcanistFactory.Json, Cancel);
        var updated = await response.Content.ReadFromJsonAsync<JsonElement>(ArcanistFactory.Json, Cancel);
        Assert.Equal(["A", "B", "C"], await TitlesAsync(_owner, id));
        Assert.Equal(3, updated.GetProperty("trackCount").GetInt32());

        // Paging follows the positions.
        var first = await _owner.GetFromJsonAsync<JsonElement>($"/rest/v1/playlists/{id}/entries?first=2", ArcanistFactory.Json, Cancel);
        var rest = await _owner.GetFromJsonAsync<JsonElement>(
            $"/rest/v1/playlists/{id}/entries?first=2&after={first.GetProperty("nextCursor").GetString()}", ArcanistFactory.Json, Cancel);
        Assert.Equal("C", rest.GetProperty("items")[0].GetProperty("track").GetProperty("title").GetString());
    }

    [Fact]
    public async Task Private_playlists_are_hidden_and_public_ones_can_be_copied()
    {
        var playlist = await CreateAsync(_owner, "Mine", false, "A");
        var id = playlist.GetProperty("id").GetString()!;

        Assert.Equal(HttpStatusCode.NotFound, (await _stranger.GetAsync($"/rest/v1/playlists/{id}", Cancel)).StatusCode);
        Assert.Empty((await _stranger.GetFromJsonAsync<List<JsonElement>>("/rest/v1/playlists", ArcanistFactory.Json, Cancel))!);

        await _owner.PutAsJsonAsync($"/rest/v1/playlists/{id}", new { name = "Ours", description = "For all", isPublic = true }, ArcanistFactory.Json, Cancel);
        var seen = await _stranger.GetFromJsonAsync<JsonElement>($"/rest/v1/playlists/{id}", ArcanistFactory.Json, Cancel);
        Assert.Equal(("Ours", "VIEWER"), (seen.GetProperty("name").GetString(), seen.GetProperty("role").GetString()));

        // Viewers listen and copy, but do not change it.
        var add = await _stranger.PostAsJsonAsync($"/rest/v1/playlists/{id}/entries", new { trackIds = new[] { _tracks["B"] } }, ArcanistFactory.Json, Cancel);
        Assert.Equal(HttpStatusCode.Forbidden, add.StatusCode);
        var copy = await (await _stranger.PostAsJsonAsync($"/rest/v1/playlists/{id}/copy", new { name = "My copy" }, ArcanistFactory.Json, Cancel))
            .Content.ReadFromJsonAsync<JsonElement>(ArcanistFactory.Json, Cancel);
        Assert.Equal(("My copy", "OWNER", false), (copy.GetProperty("name").GetString(), copy.GetProperty("role").GetString(), copy.GetProperty("isPublic").GetBoolean()));
        Assert.Equal(["A"], await TitlesAsync(_stranger, copy.GetProperty("id").GetString()!));
    }

    [Fact]
    public async Task Collaborators_edit_tracks_but_only_the_owner_manages_the_playlist()
    {
        var playlist = await CreateAsync(_owner, "Party", false, "A");
        var id = playlist.GetProperty("id").GetString()!;

        var shareable = await _owner.GetFromJsonAsync<List<JsonElement>>("/rest/v1/shareable-users", ArcanistFactory.Json, Cancel);
        Assert.Equal(["friend", "stranger"], shareable!.Select(u => u.GetProperty("name").GetString()));

        var shared = await (await _owner.PutAsJsonAsync($"/rest/v1/playlists/{id}/collaborators", new { userIds = new[] { _friendId } }, ArcanistFactory.Json, Cancel))
            .Content.ReadFromJsonAsync<JsonElement>(ArcanistFactory.Json, Cancel);
        Assert.Equal("friend", shared.GetProperty("collaborators")[0].GetProperty("name").GetString());

        var mine = await _friend.GetFromJsonAsync<List<JsonElement>>("/rest/v1/playlists", ArcanistFactory.Json, Cancel);
        Assert.Equal("COLLABORATOR", Assert.Single(mine!).GetProperty("role").GetString());

        var add = await _friend.PostAsJsonAsync($"/rest/v1/playlists/{id}/entries", new { trackIds = new[] { _tracks["D"] } }, ArcanistFactory.Json, Cancel);
        Assert.Equal(HttpStatusCode.OK, add.StatusCode);
        var entries = await _owner.GetFromJsonAsync<JsonElement>($"/rest/v1/playlists/{id}/entries", ArcanistFactory.Json, Cancel);
        Assert.Equal("friend", entries.GetProperty("items")[1].GetProperty("addedBy").GetProperty("name").GetString());

        Assert.Equal(HttpStatusCode.Forbidden, (await _friend.PutAsJsonAsync($"/rest/v1/playlists/{id}",
            new { name = "Mine now", isPublic = true }, ArcanistFactory.Json, Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _friend.DeleteAsync($"/rest/v1/playlists/{id}", Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await _owner.DeleteAsync($"/rest/v1/playlists/{id}", Cancel)).StatusCode);
        Assert.Empty((await _friend.GetFromJsonAsync<List<JsonElement>>("/rest/v1/playlists", ArcanistFactory.Json, Cancel))!);
    }

    [Fact]
    public async Task Tracks_from_libraries_a_user_cannot_access_stay_but_are_not_shown_to_them()
    {
        var playlist = await CreateAsync(_owner, "Everything", true, "A", "Secret");
        var id = playlist.GetProperty("id").GetString()!;

        Assert.Equal(["A", "Secret"], await TitlesAsync(_owner, id));
        Assert.Equal(["A"], await TitlesAsync(_stranger, id));
        Assert.Equal(1, (await _stranger.GetFromJsonAsync<JsonElement>($"/rest/v1/playlists/{id}", ArcanistFactory.Json, Cancel)).GetProperty("trackCount").GetInt32());

        // Nobody adds what they cannot see.
        var stranger = await CreateAsync(_stranger, "Theirs", false, "A");
        var add = await _stranger.PostAsJsonAsync($"/rest/v1/playlists/{stranger.GetProperty("id").GetString()}/entries",
            new { trackIds = new[] { _tracks["Secret"] } }, ArcanistFactory.Json, Cancel);
        Assert.Equal(HttpStatusCode.BadRequest, add.StatusCode);
    }

    [Fact]
    public async Task Playlists_work_over_graphql_too()
    {
        var created = await _owner.GraphQLAsync("mutation($input: CreatePlaylistInput!) { createPlaylist(input: $input) { id name trackCount } }",
            new { input = new { name = "  Road trip ", isPublic = false, trackIds = new[] { _tracks["A"], _tracks["B"] } } });
        var playlist = created.GetProperty("data").GetProperty("createPlaylist");
        Assert.Equal(("Road trip", 2), (playlist.GetProperty("name").GetString(), playlist.GetProperty("trackCount").GetInt32()));

        var invalid = await _owner.GraphQLAsync("mutation($input: CreatePlaylistInput!) { createPlaylist(input: $input) { id } }",
            new { input = new { name = " ", isPublic = false } });
        Assert.Equal("validation_failed", invalid.ErrorCode());
    }

    public async ValueTask DisposeAsync()
    {
        await _factory.DisposeAsync();
        Directory.Delete(_root, recursive: true);
    }
}
