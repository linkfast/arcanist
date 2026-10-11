using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Arcanist.Core.Application.Libraries;
using Arcanist.Core.Application.Users;
using Permissions = Arcanist.Core.Data.Entities.Permissions;
using Microsoft.Extensions.Time.Testing;

namespace Arcanist.Core.Tests.Api;

public sealed class LibraryManagementTests : IAsyncLifetime
{
    private readonly FakeTimeProvider _time = new(DateTimeOffset.UtcNow);
    private readonly ArcanistFactory _factory;
    private readonly string _root = Directory.CreateTempSubdirectory("arcanist-libraries").FullName;
    private HttpClient _admin = null!;

    public LibraryManagementTests() => _factory = new ArcanistFactory(time: _time);

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        await _factory.CompleteSetupAsync();
        _admin = await _factory.CreateAuthenticatedClientAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await _factory.DisposeAsync();
        Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task Admin_creates_a_library_with_normalized_sources()
    {
        var folder = Folder("Music");

        var library = await CreateAsync("Music", visibleToAll: false, folder + Path.DirectorySeparatorChar);

        Assert.Equal("Music", library.Name);
        Assert.Equal(folder, Assert.Single(library.Sources!).Location);
        Assert.Contains(await _admin.GetFromJsonAsync<List<Library>>("/rest/v1/libraries", ArcanistFactory.Json, Cancel) ?? [],
            l => l.Id == library.Id);
    }

    [Fact]
    public async Task Users_only_see_libraries_granted_to_them_or_visible_to_all()
    {
        var music = await CreateAsync("Music", visibleToAll: false, Folder("Music"));
        var podcasts = await CreateAsync("Podcasts", visibleToAll: false, Folder("Podcasts"));
        var userA = await _factory.CreateUserAsync("userA");
        var userB = await _factory.CreateUserAsync("userB");

        await Grant(userA.Id, music.Id, podcasts.Id);
        await Grant(userB.Id, podcasts.Id);

        var a = await _factory.CreateAuthenticatedClientAsync("userA");
        var b = await _factory.CreateAuthenticatedClientAsync("userB");

        Assert.Equal(["Music", "Podcasts"], (await ListAsync(a)).Select(l => l.Name));
        Assert.Equal(["Podcasts"], (await ListAsync(b)).Select(l => l.Name));
        Assert.Equal(HttpStatusCode.NotFound, (await b.GetAsync($"/rest/v1/libraries/{music.Id}", Cancel)).StatusCode);

        // Locations are only shown to library managers.
        Assert.All(await ListAsync(a), l => Assert.Null(l.Sources));

        // Making a library visible to all applies immediately.
        await _admin.PutAsJsonAsync($"/rest/v1/libraries/{music.Id}", new UpdateLibraryInput("Music", true, null), ArcanistFactory.Json, Cancel);
        Assert.Equal(["Music", "Podcasts"], (await ListAsync(b)).Select(l => l.Name));

        var graphql = await b.GraphQLAsync("{ libraries { name sources { location } } }");
        Assert.Equal(2, graphql.GetProperty("data").GetProperty("libraries").GetArrayLength());
        Assert.Equal(JsonValueKind.Null, graphql.GetProperty("data").GetProperty("libraries")[0].GetProperty("sources").ValueKind);
    }

    [Fact]
    public async Task Invalid_libraries_are_rejected_per_field()
    {
        var music = Folder("Music");
        await CreateAsync("Music", visibleToAll: true, music);

        var response = await _admin.PostAsJsonAsync("/rest/v1/libraries", new CreateLibraryInput("MUSIC", true, 0,
        [
            new LibrarySourceInput(Path.Combine(_root, "missing")),
            new LibrarySourceInput("relative/folder"),
            new LibrarySourceInput(Folder("Other")),
            new LibrarySourceInput(Folder(Path.Combine("Other", "Nested"))),
        ]), ArcanistFactory.Json, Cancel);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = (await response.Content.ReadFromJsonAsync<JsonElement>(Cancel)).GetProperty("errors");
        Assert.Equal("duplicate", errors.GetProperty("name")[0].GetString());
        Assert.Equal("invalid_format", errors.GetProperty("scanIntervalHours")[0].GetString());
        Assert.Equal("not_found", errors.GetProperty("sources[0].location")[0].GetString());
        Assert.Equal("invalid_format", errors.GetProperty("sources[1].location")[0].GetString());
        Assert.False(errors.TryGetProperty("sources[2].location", out _));
        Assert.Equal("overlapping", errors.GetProperty("sources[3].location")[0].GetString());
    }

    [Fact]
    public async Task Sources_can_be_added_and_removed()
    {
        var library = await CreateAsync("Music", visibleToAll: true, Folder("Music"));

        var overlapping = await _admin.PostAsJsonAsync($"/rest/v1/libraries/{library.Id}/sources",
            new LibrarySourceInput(Folder(Path.Combine("Music", "Rock"))), ArcanistFactory.Json, Cancel);
        Assert.Equal(HttpStatusCode.BadRequest, overlapping.StatusCode);

        var added = await _admin.PostAsJsonAsync($"/rest/v1/libraries/{library.Id}/sources",
            new LibrarySourceInput(Folder("More Music")), ArcanistFactory.Json, Cancel);
        var withTwo = (await added.Content.ReadFromJsonAsync<Library>(ArcanistFactory.Json, Cancel))!;
        Assert.Equal(2, withTwo.Sources!.Count);

        var removed = await _admin.GraphQLAsync("mutation($id: ID!) { removeLibrarySource(id: $id) { sources { location } } }",
            new { id = withTwo.Sources.First(s => s.Location.EndsWith("More Music")).Id });
        Assert.Equal(1, removed.GetProperty("data").GetProperty("removeLibrarySource").GetProperty("sources").GetArrayLength());
    }

    [Fact]
    public async Task Library_managers_only_manage_libraries_they_can_access()
    {
        var hidden = await CreateAsync("Private", visibleToAll: false, Folder("Private"));
        await _factory.CreateUserAsync("curator", permissions: Permissions.ManageLibraries);
        var curator = await _factory.CreateAuthenticatedClientAsync("curator");

        var created = await curator.PostAsJsonAsync("/rest/v1/libraries",
            new CreateLibraryInput("Curated", false, 24, [new LibrarySourceInput(Folder("Curated"))]), ArcanistFactory.Json, Cancel);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal(["Curated"], (await ListAsync(curator)).Select(l => l.Name));

        var update = await curator.PutAsJsonAsync($"/rest/v1/libraries/{hidden.Id}",
            new UpdateLibraryInput("Mine now", true, null), ArcanistFactory.Json, Cancel);
        Assert.Equal(HttpStatusCode.NotFound, update.StatusCode);

        // Only administrators can look at the server's folders.
        Assert.Equal(HttpStatusCode.Forbidden, (await curator.GetAsync("/rest/v1/server/directories", Cancel)).StatusCode);
    }

    [Fact]
    public async Task Users_without_the_permission_cannot_create_libraries()
    {
        await _factory.CreateUserAsync("listener");
        var listener = await _factory.CreateAuthenticatedClientAsync("listener");

        var response = await listener.PostAsJsonAsync("/rest/v1/libraries",
            new CreateLibraryInput("Mine", true, null, [new LibrarySourceInput(Folder("Mine"))]), ArcanistFactory.Json, Cancel);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Deleting_a_library_requires_a_recent_authentication_and_keeps_files()
    {
        var folder = Folder("Music");
        File.WriteAllText(Path.Combine(folder, "song.mp3"), "");
        var library = await CreateAsync("Music", visibleToAll: true, folder);

        _time.Advance(TimeSpan.FromMinutes(11));
        var stale = _factory.CreateClient((await RefreshAsync()).AccessToken);
        Assert.Equal(HttpStatusCode.Forbidden, (await stale.DeleteAsync($"/rest/v1/libraries/{library.Id}", Cancel)).StatusCode);

        var fresh = await _factory.CreateAuthenticatedClientAsync();
        Assert.Equal(HttpStatusCode.NoContent, (await fresh.DeleteAsync($"/rest/v1/libraries/{library.Id}", Cancel)).StatusCode);
        Assert.Empty((await ListAsync(fresh)));
        Assert.True(File.Exists(Path.Combine(folder, "song.mp3")));
    }

    [Fact]
    public async Task Administrators_browse_server_folders()
    {
        Folder(Path.Combine("Browse", "Albums"));

        var listing = await _admin.GetFromJsonAsync<ServerDirectoryListing>(
            $"/rest/v1/server/directories?path={Uri.EscapeDataString(Path.Combine(_root, "Browse"))}", ArcanistFactory.Json, Cancel);
        Assert.Equal("Albums", Assert.Single(listing!.Directories).Name);

        var roots = await _admin.GraphQLAsync("{ serverDirectories { path directories { path } } }");
        Assert.NotEqual(0, roots.GetProperty("data").GetProperty("serverDirectories").GetProperty("directories").GetArrayLength());

        var missing = await _admin.GetAsync($"/rest/v1/server/directories?path={Uri.EscapeDataString(Path.Combine(_root, "nope"))}", Cancel);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    private string Folder(string name) => Directory.CreateDirectory(Path.Combine(_root, name)).FullName;

    private async Task<Library> CreateAsync(string name, bool visibleToAll, params string[] folders)
    {
        var response = await _admin.PostAsJsonAsync("/rest/v1/libraries",
            new CreateLibraryInput(name, visibleToAll, null, folders.Select(f => new LibrarySourceInput(f)).ToList()), ArcanistFactory.Json, Cancel);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<Library>(ArcanistFactory.Json, Cancel))!;
    }

    private async Task Grant(Guid userId, params Guid[] libraryIds)
    {
        var response = await _admin.PutAsJsonAsync($"/rest/v1/users/{userId}/libraries",
            new SetUserLibrariesInput(libraryIds), ArcanistFactory.Json, Cancel);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task<List<Library>> ListAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<List<Library>>("/rest/v1/libraries", ArcanistFactory.Json, Cancel))!;

    private async Task<Arcanist.Core.Api.Rest.TokenResponse> RefreshAsync()
    {
        var tokens = await _factory.LoginAsync();
        _time.Advance(TimeSpan.FromMinutes(11));
        var response = await _factory.CreateClient().PostAsJsonAsync("/rest/v1/auth/refresh",
            new Arcanist.Core.Api.Rest.RefreshRequest(tokens.RefreshToken), Cancel);
        return (await response.Content.ReadFromJsonAsync<Arcanist.Core.Api.Rest.TokenResponse>(ArcanistFactory.Json, Cancel))!;
    }
}
