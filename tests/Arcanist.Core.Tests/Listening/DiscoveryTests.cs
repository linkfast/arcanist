using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Arcanist.Core.Setup;
using Arcanist.Core.Tests.Scanning;

namespace Arcanist.Core.Tests.Listening;

/// <summary>The home page, mixes and artist pages.</summary>
public sealed class DiscoveryTests : IAsyncLifetime
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private readonly string _music = Directory.CreateTempSubdirectory("arcanist-discovery-").FullName;
    private readonly ArcanistFactory _factory = new();
    private HttpClient _admin = null!;
    private Dictionary<string, Guid> _tracks = null!;
    private Dictionary<string, Guid> _artists = null!;

    public async ValueTask InitializeAsync()
    {
        var seed = 0;
        void Album(string folder, string album, string artist, int year, string genre, int count, string? albumArtist = null, string[]? performers = null)
        {
            for (var i = 0; i < count; i++)
            {
                AudioFixtures.WriteWav(Path.Combine(_music, folder, $"{i + 1}.wav"), ++seed, new FixtureTags(
                    $"{album} {i + 1}", performers?[i] ?? artist, album, albumArtist ?? artist, TrackNumber: i + 1, Year: year, Genre: genre, WithCover: i == 0));
            }
        }

        Album("Alpha First", "First", "Alpha", 2001, "Rock", 4);
        Album("Alpha Second", "Second", "Alpha", 2003, "Rock", 3);
        Album("Beta", "Other", "Beta", 1995, "Rock", 3);
        Album("Gamma", "Late Night", "Gamma", 1985, "Jazz", 3);
        Album("Hits", "Hits", "", 2005, "Pop", 2, albumArtist: "Various Artists", performers: ["Alpha", "Delta"]);

        await _factory.CompleteSetupAsync(new SetupLibrary("Music", [_music]));
        _admin = await _factory.CreateAuthenticatedClientAsync();
        await _factory.WaitForScansAsync(_admin);

        var tracks = await _admin.GetFromJsonAsync<JsonElement>("/rest/v1/tracks?first=100", ArcanistFactory.Json, Cancel);
        _tracks = tracks.GetProperty("items").EnumerateArray().ToDictionary(t => t.GetProperty("title").GetString()!, t => t.GetProperty("id").GetGuid());
        var artists = await _admin.GetFromJsonAsync<JsonElement>("/rest/v1/artists?first=100", ArcanistFactory.Json, Cancel);
        _artists = artists.GetProperty("items").EnumerateArray().ToDictionary(a => a.GetProperty("name").GetString()!, a => a.GetProperty("id").GetGuid());
    }

    private Task<JsonElement> GetAsync(string url) => _admin.GetFromJsonAsync<JsonElement>(url, ArcanistFactory.Json, Cancel);

    private static List<string> Kinds(JsonElement home) =>
        home.GetProperty("sections").EnumerateArray().Select(s => s.GetProperty("kind").GetString()!).ToList();

    /// <param name="minutesAgo">When the last of the plays happened; earlier ones are ten minutes apart.</param>
    private async Task PlayAsync(string title, int times = 1, int minutesAgo = 10)
    {
        for (var i = 0; i < times; i++)
        {
            var response = await _admin.PostAsJsonAsync("/rest/v1/plays",
                new { trackId = _tracks[title], playedMs = 60_000, playedAt = DateTime.UtcNow.AddMinutes(-minutesAgo - 10 * i) }, ArcanistFactory.Json, Cancel);
            response.EnsureSuccessStatusCode();
        }
    }

    [Fact]
    public async Task The_home_page_grows_with_what_the_user_listens_to()
    {
        var fresh = await GetAsync("/rest/v1/home");
        Assert.Equal(["MIXES", "RECENTLY_ADDED", "NEVER_PLAYED"], Kinds(fresh));
        Assert.Equal(5, fresh.GetProperty("sections")[2].GetProperty("albums").GetArrayLength());

        await PlayAsync("Second 1", 3, minutesAgo: 5);
        await PlayAsync("First 2", minutesAgo: 60);
        await _admin.PutAsJsonAsync($"/rest/v1/tracks/{_tracks["Late Night 1"]}/star", new { starred = true }, ArcanistFactory.Json, Cancel);
        await _admin.PostAsJsonAsync("/rest/v1/playlists", new { name = "Mine", isPublic = false }, ArcanistFactory.Json, Cancel);

        var home = await GetAsync("/rest/v1/home");
        Assert.Equal(["RECENTLY_PLAYED", "MIXES", "RECENTLY_ADDED", "MOST_PLAYED", "STARRED", "NEVER_PLAYED", "PLAYLISTS"], Kinds(home));
        var sections = home.GetProperty("sections").EnumerateArray().ToDictionary(s => s.GetProperty("kind").GetString()!);
        Assert.Equal(["Second", "First"], sections["RECENTLY_PLAYED"].GetProperty("albums").EnumerateArray().Select(a => a.GetProperty("title").GetString()));
        Assert.Equal("Second", sections["MOST_PLAYED"].GetProperty("albums")[0].GetProperty("title").GetString());
        Assert.Equal("Late Night 1", sections["STARRED"].GetProperty("tracks")[0].GetProperty("title").GetString());
        Assert.DoesNotContain(sections["NEVER_PLAYED"].GetProperty("albums").EnumerateArray(), a => a.GetProperty("title").GetString() is "First" or "Second");

        // The user's favorite artist comes first among the mixes.
        var mix = sections["MIXES"].GetProperty("mixes")[0];
        Assert.Equal(("ARTIST", "Alpha"), (mix.GetProperty("kind").GetString(), mix.GetProperty("subject").GetString()));
        Assert.NotEmpty(mix.GetProperty("artworkIds").EnumerateArray());
    }

    [Fact]
    public async Task An_artist_mix_brings_similar_artists_and_stays_the_same_all_day()
    {
        var id = $"artist:{_artists["Alpha"]:N}";
        await _admin.PutAsJsonAsync($"/rest/v1/tracks/{_tracks["First 3"]}/rating", new { rating = 1 }, ArcanistFactory.Json, Cancel);

        var mix = await GetAsync($"/rest/v1/mixes/{id}");
        var titles = mix.GetProperty("tracks").EnumerateArray().Select(t => t.GetProperty("title").GetString()!).ToList();
        Assert.Equal(("ARTIST", "Alpha"), (mix.GetProperty("mix").GetProperty("kind").GetString(), mix.GetProperty("mix").GetProperty("subject").GetString()));
        Assert.Contains("Second 1", titles);
        // Beta shares Alpha's genre; Delta shares an album with Alpha.
        Assert.Contains(titles, t => t.StartsWith("Other"));
        Assert.Contains("Hits 2", titles);
        Assert.DoesNotContain(titles, t => t.StartsWith("Late Night"));
        // Tracks the user rated low stay out.
        Assert.DoesNotContain("First 3", titles);
        Assert.Equal(titles.Count, titles.Distinct().Count());

        var again = await GetAsync($"/rest/v1/mixes/{id}");
        Assert.Equal(titles, again.GetProperty("tracks").EnumerateArray().Select(t => t.GetProperty("title").GetString()!));
    }

    [Fact]
    public async Task Other_mixes_draw_from_their_subject()
    {
        var radio = await GetAsync($"/rest/v1/mixes/track:{_tracks["Other 2"]:N}");
        Assert.Equal("Other 2", radio.GetProperty("tracks")[0].GetProperty("title").GetString());

        var decade = await GetAsync("/rest/v1/mixes/decade:2000");
        Assert.All(decade.GetProperty("tracks").EnumerateArray(), t => Assert.Matches("^(First|Second|Hits) ", t.GetProperty("title").GetString()!));
        // At most three tracks of an album: 3 + 3 + 2.
        Assert.Equal(8, decade.GetProperty("tracks").GetArrayLength());

        var genres = await _admin.GraphQLAsync("{ mixes { id kind subject } }");
        var rockMix = genres.GetProperty("data").GetProperty("mixes").EnumerateArray().FirstOrDefault(m => m.GetProperty("subject").GetString() == "Rock");
        Assert.Equal("GENRE", rockMix.GetProperty("kind").GetString());
        var rock = await GetAsync($"/rest/v1/mixes/{Uri.EscapeDataString(rockMix.GetProperty("id").GetString()!)}");
        Assert.Equal(9, rock.GetProperty("tracks").GetArrayLength());

        Assert.Equal(HttpStatusCode.NotFound, (await _admin.GetAsync("/rest/v1/mixes/nonsense", Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _admin.GetAsync($"/rest/v1/mixes/artist:{Guid.NewGuid():N}", Cancel)).StatusCode);
    }

    [Fact]
    public async Task An_artist_page_has_top_tracks_albums_and_related_artists()
    {
        await PlayAsync("First 4", 2);

        var page = await GetAsync($"/rest/v1/artists/{_artists["Alpha"]}/overview");
        Assert.Equal("First 4", page.GetProperty("topTracks")[0].GetProperty("title").GetString());
        Assert.Equal(["Second", "First"], page.GetProperty("albums").EnumerateArray().Select(a => a.GetProperty("title").GetString()));
        Assert.Equal(["Hits"], page.GetProperty("appearsOn").EnumerateArray().Select(a => a.GetProperty("title").GetString()));
        var related = page.GetProperty("relatedArtists").EnumerateArray().Select(a => a.GetProperty("name").GetString()).ToList();
        Assert.Contains("Delta", related);
        Assert.Contains("Beta", related);
        Assert.DoesNotContain("Gamma", related);
        Assert.Equal((2, $"artist:{_artists["Alpha"]:N}"), (page.GetProperty("playCount").GetInt32(), page.GetProperty("mix").GetProperty("id").GetString()));
        Assert.NotEqual(JsonValueKind.Null, page.GetProperty("artworkId").ValueKind);
    }

    public async ValueTask DisposeAsync()
    {
        await _factory.DisposeAsync();
        Directory.Delete(_music, recursive: true);
    }
}
