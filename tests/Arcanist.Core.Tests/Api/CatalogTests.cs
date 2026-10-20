using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Arcanist.Core.Api.Rest;
using Arcanist.Core.Application.Catalog;
using Arcanist.Core.Application.Libraries;
using Arcanist.Core.Application.Users;
using Arcanist.Core.Setup;
using Arcanist.Core.Tests.Scanning;

namespace Arcanist.Core.Tests.Api;

public sealed class CatalogTests : IAsyncLifetime
{
    private readonly string _root = Directory.CreateTempSubdirectory("arcanist-catalog").FullName;
    private readonly ArcanistFactory _factory = new();
    private HttpClient _admin = null!;
    private HttpClient _guest = null!;

    private string Shared => Path.Combine(_root, "Shared");
    private string Private => Path.Combine(_root, "Private");

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        // Shared: an album by Sigur Rós (tracks out of order on disk) and five singles for paging.
        AudioFixtures.WriteWav(Path.Combine(Shared, "Agaetis", "b.wav"), 1, new FixtureTags("Svefn-g-englar", "Sigur Rós", "Ágætis byrjun", TrackNumber: 2, WithCover: true));
        AudioFixtures.WriteWav(Path.Combine(Shared, "Agaetis", "a.wav"), 2, new FixtureTags("Intro", "Sigur Rós", "Ágætis byrjun", TrackNumber: 1));
        AudioFixtures.WriteWav(Path.Combine(Shared, "Agaetis", "c.wav"), 3, new FixtureTags("Starálfur", "Sigur Rós", "Ágætis byrjun", TrackNumber: 1, DiscNumber: 2));
        for (var i = 0; i < 5; i++)
            AudioFixtures.WriteWav(Path.Combine(Shared, "Singles", $"{i}.wav"), 10 + i, new FixtureTags($"Single {i}", "Various", "Singles", TrackNumber: i + 1));

        // Private: another artist, plus a copy of a shared song.
        AudioFixtures.WriteWav(Path.Combine(Private, "Secret", "1.wav"), 20, new FixtureTags("Hidden", "Nobody", "Secret", TrackNumber: 1));
        Directory.CreateDirectory(Path.Combine(Private, "Copies"));
        File.Copy(Path.Combine(Shared, "Agaetis", "a.wav"), Path.Combine(Private, "Copies", "a.wav"));

        await _factory.CompleteSetupAsync(
            new SetupLibrary("Shared", [Shared], VisibleToAll: false),
            new SetupLibrary("Private", [Private], VisibleToAll: false));
        _admin = await _factory.CreateAuthenticatedClientAsync();
        await _factory.WaitForScansAsync(_admin);

        var guest = await _factory.CreateUserAsync("guest");
        var libraries = await _admin.GetFromJsonAsync<List<Library>>("/rest/v1/libraries", ArcanistFactory.Json, Cancel);
        await _admin.PutAsJsonAsync($"/rest/v1/users/{guest.Id}/libraries",
            new SetUserLibrariesInput([libraries!.Single(l => l.Name == "Shared").Id]), ArcanistFactory.Json, Cancel);
        _guest = await _factory.CreateAuthenticatedClientAsync("guest");
    }

    [Fact]
    public async Task One_search_finds_artists_albums_tracks_and_playlists_the_user_can_see()
    {
        await _guest.PostAsJsonAsync("/rest/v1/playlists", new { name = "Sigur Rós favourites", isPublic = false }, ArcanistFactory.Json, Cancel);

        var results = await _guest.GetFromJsonAsync<JsonElement>("/rest/v1/search?query=sigur", ArcanistFactory.Json, Cancel);
        Assert.Equal(["Sigur Rós"], results.GetProperty("artists").EnumerateArray().Select(a => a.GetProperty("name").GetString()));
        Assert.Equal(["Ágætis byrjun"], results.GetProperty("albums").EnumerateArray().Select(a => a.GetProperty("title").GetString()));
        Assert.Equal(3, results.GetProperty("tracks").GetArrayLength());
        Assert.Equal(["Sigur Rós favourites"], results.GetProperty("playlists").EnumerateArray().Select(p => p.GetProperty("name").GetString()));

        // Words combine across title, artist and album, and match word starts.
        var combined = await _guest.GetFromJsonAsync<JsonElement>("/rest/v1/search?query=ros%20staral", ArcanistFactory.Json, Cancel);
        Assert.Equal(["Starálfur"], combined.GetProperty("tracks").EnumerateArray().Select(t => t.GetProperty("title").GetString()));

        // Nothing from libraries the user cannot access, nor other users' private playlists.
        var hidden = await _guest.GetFromJsonAsync<JsonElement>("/rest/v1/search?query=hidden", ArcanistFactory.Json, Cancel);
        Assert.Equal(0, hidden.GetProperty("tracks").GetArrayLength());
        var admin = await _admin.GraphQLAsync("{ search(query: \"favourites\") { playlists { name } tracks { title } } }");
        Assert.Equal(0, admin.GetProperty("data").GetProperty("search").GetProperty("playlists").GetArrayLength());

        var empty = await _guest.GetFromJsonAsync<JsonElement>("/rest/v1/search?query=%3F%3F", ArcanistFactory.Json, Cancel);
        Assert.Equal(0, empty.GetProperty("artists").GetArrayLength());
    }

    public async ValueTask DisposeAsync()
    {
        await _factory.DisposeAsync();
        Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task Users_only_see_music_from_their_libraries()
    {
        var adminAlbums = await GetAsync<AlbumPage>(_admin, "/rest/v1/albums");
        var guestAlbums = await GetAsync<AlbumPage>(_guest, "/rest/v1/albums");

        // The copy in Private/Copies carries the same album tags, so it joins "Ágætis byrjun" instead of a "Copies" album.
        Assert.Equal(new[] { "Secret", "Singles", "Ágætis byrjun" }.Order(), adminAlbums.Items.Select(a => a.Title).Order());
        Assert.Equal(new[] { "Singles", "Ágætis byrjun" }.Order(), guestAlbums.Items.Select(a => a.Title).Order());

        var hidden = adminAlbums.Items.Single(a => a.Title == "Secret");
        Assert.Equal(HttpStatusCode.NotFound, (await _guest.GetAsync($"/rest/v1/albums/{hidden.Id}", Cancel)).StatusCode);
        Assert.DoesNotContain((await GetAsync<ArtistPage>(_guest, "/rest/v1/artists")).Items, a => a.Name == "Nobody");

        var graphql = await _guest.GraphQLAsync("{ tracks(search: \"hidden\") { totalCount } }");
        Assert.Equal(0, graphql.GetProperty("data").GetProperty("tracks").GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task A_song_in_two_libraries_shows_the_files_each_user_can_reach()
    {
        var adminIntro = (await GetAsync<TrackPage>(_admin, "/rest/v1/tracks?search=intro")).Items;
        var guestIntro = (await GetAsync<TrackPage>(_guest, "/rest/v1/tracks?search=intro")).Items;

        // Same audio and tags in two libraries: one track with a file in each.
        Assert.Equal(2, Assert.Single(adminIntro).Files.Count);
        Assert.Single(Assert.Single(guestIntro).Files);
    }

    [Fact]
    public async Task Album_tracks_come_in_disc_and_track_order_with_album_details()
    {
        // Accents and letters like "æ" are folded for search.
        var bySearch = Assert.Single((await GetAsync<AlbumPage>(_guest, "/rest/v1/albums?search=agaetis")).Items);
        Assert.Equal("Ágætis byrjun", bySearch.Title);

        var byArtist = await GetAsync<ArtistPage>(_guest, "/rest/v1/artists?search=sigur%20ros");
        var sigurRos = Assert.Single(byArtist.Items);
        var albums = await GetAsync<AlbumPage>(_guest, $"/rest/v1/albums?artistId={sigurRos.Id}");
        var agaetis = Assert.Single(albums.Items);
        Assert.Equal("Sigur Rós", agaetis.Artist.Name);
        Assert.NotNull(agaetis.ArtworkId);
        Assert.True(agaetis.TrackCount >= 2);

        var tracks = await GetAsync<TrackPage>(_guest, $"/rest/v1/tracks?albumId={agaetis.Id}");
        Assert.Null(tracks.NextCursor);
        Assert.Equal("Starálfur", tracks.Items[^1].Title);
        Assert.True(tracks.Items.Select(t => (t.DiscNumber ?? 1, t.TrackNumber)).SequenceEqual(
            tracks.Items.Select(t => (t.DiscNumber ?? 1, t.TrackNumber)).Order()));
        Assert.All(tracks.Items, t => Assert.Equal(agaetis.ArtworkId, t.ArtworkId));
        Assert.Equal("Sigur Rós", tracks.Items[0].Artists[0].Name);
    }

    [Fact]
    public async Task Pages_cover_every_item_once()
    {
        var titles = new List<string>();
        string? cursor = null;
        var pages = 0;
        do
        {
            var url = "/rest/v1/tracks?search=single&first=2" + (cursor is null ? "" : "&after=" + Uri.EscapeDataString(cursor));
            var page = await GetAsync<TrackPage>(_guest, url);
            Assert.Equal(5, page.TotalCount);
            titles.AddRange(page.Items.Select(t => t.Title));
            cursor = page.NextCursor;
            pages++;
        }
        while (cursor is not null);

        Assert.Equal(3, pages);
        Assert.Equal(Enumerable.Range(0, 5).Select(i => $"Single {i}"), titles);

        var graphql = await _guest.GraphQLAsync("""{ albums(first: 1) { items { title } nextCursor totalCount } }""");
        Assert.NotEqual(JsonValueKind.Null, graphql.GetProperty("data").GetProperty("albums").GetProperty("nextCursor").ValueKind);
    }

    [Fact]
    public async Task Albums_come_newest_first_or_at_random_and_pages_cover_each_once()
    {
        AudioFixtures.WriteWav(Path.Combine(Shared, "Fresh", "1.wav"), 30, new FixtureTags("New", "Newcomer", "Fresh", TrackNumber: 1));
        var library = (await _admin.GetFromJsonAsync<List<Library>>("/rest/v1/libraries", ArcanistFactory.Json, Cancel))!.Single(l => l.Name == "Shared");
        await _admin.PostAsJsonAsync($"/rest/v1/libraries/{library.Id}/scans", new StartScanInput(), Cancel);
        await _factory.WaitForScansAsync(_admin);

        var newest = await GetAsync<AlbumPage>(_admin, "/rest/v1/albums?sort=RECENTLY_ADDED&first=1");
        Assert.Equal(("Fresh", 4), (newest.Items.Single().Title, newest.TotalCount));
        var graphql = await _guest.GraphQLAsync("""{ albums(sort: RECENTLY_ADDED, first: 1) { items { title } } }""");
        Assert.Equal("Fresh", graphql.GetProperty("data").GetProperty("albums").GetProperty("items")[0].GetProperty("title").GetString());

        async Task<List<string>> AllAsync(string query)
        {
            var titles = new List<string>();
            string? cursor = null;
            do
            {
                var page = await GetAsync<AlbumPage>(_admin, $"/rest/v1/albums?{query}&first=1" + (cursor is null ? "" : "&after=" + Uri.EscapeDataString(cursor)));
                titles.AddRange(page.Items.Select(a => a.Title));
                cursor = page.NextCursor;
            }
            while (cursor is not null);
            return titles;
        }

        var added = await AllAsync("sort=RECENTLY_ADDED");
        Assert.Equal("Fresh", added[0]);
        Assert.Equal(["Fresh", "Secret", "Singles", "Ágætis byrjun"], added.Order(StringComparer.Ordinal));
        var random = await AllAsync("sort=RANDOM&seed=7");
        Assert.Equal(added.Order(StringComparer.Ordinal), random.Order(StringComparer.Ordinal));
        // The same all day.
        Assert.Equal(random, await AllAsync("sort=RANDOM&seed=7"));

        Assert.Equal(HttpStatusCode.BadRequest, (await _admin.GetAsync("/rest/v1/albums?sort=sideways", Cancel)).StatusCode);
    }

    [Fact]
    public async Task Malformed_cursors_are_rejected()
    {
        var response = await _guest.GetAsync("/rest/v1/tracks?after=not-a-cursor", Cancel);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = (await response.Content.ReadFromJsonAsync<JsonElement>(Cancel)).GetProperty("errors");
        Assert.Equal("invalid_format", errors.GetProperty("after")[0].GetString());
    }

    [Fact]
    public async Task Missing_files_hide_their_tracks()
    {
        File.Delete(Path.Combine(Shared, "Singles", "4.wav"));
        var library = (await _admin.GetFromJsonAsync<List<Library>>("/rest/v1/libraries", ArcanistFactory.Json, Cancel))!.Single(l => l.Name == "Shared");
        await _admin.PostAsJsonAsync($"/rest/v1/libraries/{library.Id}/scans", new StartScanInput(), Cancel);
        await _factory.WaitForScansAsync(_admin);

        var singles = await GetAsync<TrackPage>(_guest, "/rest/v1/tracks?search=single");
        Assert.Equal(4, singles.TotalCount);
    }

    private static async Task<T> GetAsync<T>(HttpClient client, string url)
    {
        var response = await client.GetAsync(url, Cancel);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<T>(ArcanistFactory.Json, Cancel))!;
    }
}
