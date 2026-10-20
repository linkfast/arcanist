using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Arcanist.Core.Application.Libraries;
using Arcanist.Core.Data;
using Arcanist.Core.Setup;
using Arcanist.Core.Tests.Scanning;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Permissions = Arcanist.Core.Data.Entities.Permissions;

namespace Arcanist.Core.Tests.Api;

/// <summary>What apps with a library of their own use (Ultimate Desktop adding or migrating to Arcanist): libraries one by one, matching, uploads, listening import.</summary>
public sealed class AppLibraryApiTests : IAsyncLifetime
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private readonly string _rock = Directory.CreateTempSubdirectory("arcanist-app-rock-").FullName;
    private readonly string _jazz = Directory.CreateTempSubdirectory("arcanist-app-jazz-").FullName;
    private readonly string _outside = Directory.CreateTempSubdirectory("arcanist-app-outside-").FullName;
    private readonly ArcanistFactory _factory = new(new Dictionary<string, string?> { ["Arcanist:Uploads:ScanDelay"] = "00:00:00.2" });
    private HttpClient _admin = null!;
    private Guid _rockId;
    private Guid _jazzId;

    public async ValueTask InitializeAsync()
    {
        AudioFixtures.WriteWav(Path.Combine(_rock, "Riff.wav"), 1, new FixtureTags("Riff", "The Band", "Loud", "The Band", TrackNumber: 1));
        AudioFixtures.WriteWav(Path.Combine(_jazz, "Swing.wav"), 2, new FixtureTags("Swing", "Trio", "Smooth", "Trio", TrackNumber: 1));
        await _factory.CompleteSetupAsync(new SetupLibrary("Rock", [_rock]), new SetupLibrary("Jazz", [_jazz]));
        _admin = await _factory.CreateAuthenticatedClientAsync();
        await _factory.WaitForScansAsync(_admin);

        var libraries = await _admin.GetFromJsonAsync<JsonElement>("/rest/v1/libraries", ArcanistFactory.Json, Cancel);
        var ids = libraries.EnumerateArray().ToDictionary(l => l.GetProperty("name").GetString()!, l => l.GetProperty("id").GetGuid());
        (_rockId, _jazzId) = (ids["Rock"], ids["Jazz"]);
    }

    private async Task<List<JsonElement>> TracksAsync(string query = "") =>
        (await _admin.GetFromJsonAsync<JsonElement>("/rest/v1/tracks" + query, ArcanistFactory.Json, Cancel)).GetProperty("items").EnumerateArray().ToList();

    [Fact]
    public async Task Libraries_can_be_read_one_by_one()
    {
        var info = await _admin.GetFromJsonAsync<JsonElement>("/rest/v1/server-info", ArcanistFactory.Json, Cancel);
        Assert.Contains("LIBRARY_FILTER", info.GetProperty("features").EnumerateArray().Select(f => f.GetString()));

        var rock = Assert.Single(await TracksAsync($"?libraryId={_rockId}"));
        Assert.Equal("Riff", rock.GetProperty("title").GetString());
        Assert.Equal([_rockId], rock.GetProperty("libraryIds").EnumerateArray().Select(l => l.GetGuid()));
        Assert.Equal("Smooth", Assert.Single((await _admin.GetFromJsonAsync<JsonElement>($"/rest/v1/albums?libraryId={_jazzId}", ArcanistFactory.Json, Cancel))
            .GetProperty("items").EnumerateArray()).GetProperty("title").GetString());
        Assert.Equal("Trio", Assert.Single((await _admin.GetFromJsonAsync<JsonElement>($"/rest/v1/artists?libraryId={_jazzId}", ArcanistFactory.Json, Cancel))
            .GetProperty("items").EnumerateArray()).GetProperty("name").GetString());

        // A library the user cannot use does not exist for them.
        await _factory.CreateUserAsync("guest");
        var guest = await _factory.CreateAuthenticatedClientAsync("guest");
        Assert.Equal(HttpStatusCode.NotFound, (await guest.GetAsync($"/rest/v1/tracks?libraryId={Guid.NewGuid()}", Cancel)).StatusCode);
    }

    [Fact]
    public async Task All_is_a_virtual_library_of_every_one_the_user_can_access()
    {
        var info = await _admin.GetFromJsonAsync<JsonElement>("/rest/v1/server-info", ArcanistFactory.Json, Cancel);
        Assert.Contains("VIRTUAL_LIBRARIES", info.GetProperty("features").EnumerateArray().Select(f => f.GetString()));

        // Only when asked for, first, and named in the language asked for.
        Assert.DoesNotContain((await _admin.GetFromJsonAsync<JsonElement>("/rest/v1/libraries", ArcanistFactory.Json, Cancel)).EnumerateArray(),
            l => l.GetProperty("virtual").GetBoolean());
        var libraries = (await _admin.GetFromJsonAsync<JsonElement>("/rest/v1/libraries?includeVirtual=true&language=es", ArcanistFactory.Json, Cancel))
            .EnumerateArray().ToList();
        var all = libraries[0];
        Assert.Equal((VirtualLibraries.All, "Todo", true, false), (all.GetProperty("id").GetGuid(), all.GetProperty("name").GetString(),
            all.GetProperty("virtual").GetBoolean(), all.GetProperty("canUpload").GetBoolean()));
        Assert.Equal(JsonValueKind.Null, all.GetProperty("sources").ValueKind);
        Assert.Equal(3, libraries.Count);
        Assert.Equal("All", (await _admin.GetFromJsonAsync<JsonElement>($"/rest/v1/libraries/{VirtualLibraries.All}?language=en", ArcanistFactory.Json, Cancel))
            .GetProperty("name").GetString());

        // As a filter, it is every library at once.
        Assert.Equal(["Riff", "Swing"], (await TracksAsync($"?libraryId={VirtualLibraries.All}")).Select(t => t.GetProperty("title").GetString()).Order());
        Assert.Equal(2, (await _admin.GetFromJsonAsync<JsonElement>($"/rest/v1/albums?libraryId={VirtualLibraries.All}", ArcanistFactory.Json, Cancel))
            .GetProperty("items").GetArrayLength());
        var match = await _admin.PostAsJsonAsync("/rest/v1/tracks/match", new { libraryId = VirtualLibraries.All, candidates = new[] { new { key = "1", title = "Swing", artist = "Trio", album = "Smooth" } } },
            ArcanistFactory.Json, Cancel);
        Assert.NotEqual(JsonValueKind.Null, (await match.Content.ReadFromJsonAsync<JsonElement>(ArcanistFactory.Json, Cancel))[0].GetProperty("trackId").ValueKind);

        // Nothing goes into a view: apps add it as one more library, never migrate to it.
        var upload = await _admin.PostAsync($"/rest/v1/libraries/{VirtualLibraries.All}/uploads?fileName=x.wav", new ByteArrayContent([1, 2, 3]), Cancel);
        Assert.Equal(HttpStatusCode.Conflict, upload.StatusCode);
        Assert.Equal("library_virtual", (await upload.Content.ReadFromJsonAsync<JsonElement>(ArcanistFactory.Json, Cancel)).GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await _admin.DeleteAsync($"/rest/v1/libraries/{VirtualLibraries.All}", Cancel)).StatusCode);
    }

    [Fact]
    public async Task An_app_s_tracks_are_matched_by_identity_or_tags()
    {
        var riff = Assert.Single(await TracksAsync($"?libraryId={_rockId}")).GetProperty("id").GetGuid();
        var mbid = Guid.NewGuid();
        await using (var db = _factory.Services.GetRequiredService<IArcanistDbContextFactory>().CreateDbContext())
        {
            var recording = await db.Tracks.Where(t => t.Title == "Swing").Select(t => t.Recording).SingleAsync(Cancel);
            recording.MusicBrainzId = mbid;
            await db.SaveChangesAsync(Cancel);
        }

        var response = await _admin.PostAsJsonAsync("/rest/v1/tracks/match", new
        {
            candidates = new object[]
            {
                new { key = "1", title = "riff", artist = "The Band; Someone", album = "LOUD", durationMs = 1500 },
                new { key = "2", title = "Other", musicBrainzRecordingId = mbid },
                new { key = "3", title = "Riff", artist = "Nobody" },
            },
        }, ArcanistFactory.Json, Cancel);
        var matches = (await response.Content.ReadFromJsonAsync<JsonElement>(ArcanistFactory.Json, Cancel)).EnumerateArray()
            .ToDictionary(m => m.GetProperty("key").GetString()!);
        Assert.Equal((riff, "TAGS"), (matches["1"].GetProperty("trackId").GetGuid(), matches["1"].GetProperty("matchedBy").GetString()));
        Assert.Equal("MUSIC_BRAINZ", matches["2"].GetProperty("matchedBy").GetString());
        Assert.Equal(JsonValueKind.Null, matches["3"].GetProperty("trackId").ValueKind);

        // Within one library only.
        var scoped = await _admin.PostAsJsonAsync("/rest/v1/tracks/match", new { libraryId = _jazzId, candidates = new[] { new { key = "1", title = "Riff" } } }, ArcanistFactory.Json, Cancel);
        Assert.Equal(JsonValueKind.Null, (await scoped.Content.ReadFromJsonAsync<JsonElement>(ArcanistFactory.Json, Cancel))[0].GetProperty("trackId").ValueKind);
    }

    [Fact]
    public async Task Uploads_enter_the_library_with_its_template_once_and_need_the_permission()
    {
        var file = Path.Combine(_outside, "whatever.wav");
        AudioFixtures.WriteWav(file, 3, new FixtureTags("New Song", "Newcomer", "Debut", "Newcomer", TrackNumber: 2));
        HttpContent Body() => new ByteArrayContent(File.ReadAllBytes(file)) { Headers = { ContentType = new MediaTypeHeaderValue("application/octet-stream") } };

        await _factory.CreateUserAsync("listener");
        var listener = await _factory.CreateAuthenticatedClientAsync("listener");
        Assert.Equal(HttpStatusCode.Forbidden, (await listener.PostAsync($"/rest/v1/libraries/{_rockId}/uploads?fileName=a.wav", Body(), Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _admin.PostAsync($"/rest/v1/libraries/{_rockId}/uploads?fileName=a.txt", Body(), Cancel)).StatusCode);

        var first = await _admin.PostAsync($"/rest/v1/libraries/{_rockId}/uploads?fileName=whatever.wav", Body(), Cancel);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var uploaded = await first.Content.ReadFromJsonAsync<JsonElement>(ArcanistFactory.Json, Cancel);
        Assert.Equal("IMPORTED", uploaded.GetProperty("status").GetString());
        Assert.Equal("Newcomer/Debut/02 New Song.wav", uploaded.GetProperty("path").GetString());
        Assert.True(File.Exists(Path.Combine(_rock, "Newcomer", "Debut", "02 New Song.wav")));

        // The same audio again: nothing written.
        var again = await _admin.PostAsync($"/rest/v1/libraries/{_rockId}/uploads?fileName=copy.wav", Body(), Cancel);
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.Equal("DUPLICATE", (await again.Content.ReadFromJsonAsync<JsonElement>(ArcanistFactory.Json, Cancel)).GetProperty("status").GetString());

        // Catalogued by the scan after the uploads, and found by its hash; until then it is pending.
        var hash = uploaded.GetProperty("contentHash").GetString();
        var early = await _admin.PostAsJsonAsync("/rest/v1/tracks/match", new { candidates = new[] { new { key = "x", contentHash = hash } } }, ArcanistFactory.Json, Cancel);
        var earlyMatch = (await early.Content.ReadFromJsonAsync<JsonElement>(ArcanistFactory.Json, Cancel))[0];
        Assert.True(earlyMatch.GetProperty("pending").GetBoolean() || earlyMatch.GetProperty("trackId").ValueKind != JsonValueKind.Null);
        JsonElement match = default;
        for (var i = 0; i < 100; i++)
        {
            await Task.Delay(200, Cancel);
            await _factory.WaitForScansAsync(_admin);
            var response = await _admin.PostAsJsonAsync("/rest/v1/tracks/match", new { candidates = new[] { new { key = "x", contentHash = hash } } }, ArcanistFactory.Json, Cancel);
            match = (await response.Content.ReadFromJsonAsync<JsonElement>(ArcanistFactory.Json, Cancel))[0];
            if (match.GetProperty("trackId").ValueKind != JsonValueKind.Null)
                break;
        }
        Assert.Equal("CONTENT_HASH", match.GetProperty("matchedBy").GetString());

        // With the permission, a user uploads to the libraries they can use, and is told so.
        var uploader = await _factory.CreateUserAsync("uploader", permissions: Permissions.Upload);
        var client = await _factory.CreateAuthenticatedClientAsync("uploader");
        Assert.True((await client.GetFromJsonAsync<JsonElement>("/rest/v1/libraries", ArcanistFactory.Json, Cancel))[0].GetProperty("canUpload").GetBoolean());
        Assert.False((await listener.GetFromJsonAsync<JsonElement>("/rest/v1/libraries", ArcanistFactory.Json, Cancel))[0].GetProperty("canUpload").GetBoolean());
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync($"/rest/v1/libraries/{_rockId}/uploads?fileName=copy.wav", Body(), Cancel)).StatusCode);
        Assert.NotEqual(Guid.Empty, uploader.Id);
    }

    [Fact]
    public async Task Imported_ratings_fill_gaps_and_play_counts_keep_the_highest()
    {
        var tracks = await TracksAsync();
        var (riff, swing) = (tracks.Single(t => t.GetProperty("title").GetString() == "Riff").GetProperty("id").GetGuid(),
            tracks.Single(t => t.GetProperty("title").GetString() == "Swing").GetProperty("id").GetGuid());
        await _admin.PutAsJsonAsync($"/rest/v1/tracks/{riff}/rating", new { rating = 2 }, ArcanistFactory.Json, Cancel);

        var last = new DateTime(2026, 3, 1, 12, 0, 0, DateTimeKind.Utc);
        async Task<JsonElement> ImportAsync() => await (await _admin.PostAsJsonAsync("/rest/v1/me/listening/import", new
        {
            ratings = new object[] { new { trackId = riff, rating = 5 }, new { trackId = swing, rating = 5 }, new { trackId = Guid.NewGuid(), rating = 5 } },
            playCounts = new object[] { new { trackId = swing, playCount = 7, lastPlayedAt = last } },
        }, ArcanistFactory.Json, Cancel)).Content.ReadFromJsonAsync<JsonElement>(ArcanistFactory.Json, Cancel);

        var result = await ImportAsync();
        Assert.Equal((1, 1, 1), (result.GetProperty("ratingsAdded").GetInt32(), result.GetProperty("playCountsRaised").GetInt32(), result.GetProperty("ignored").GetInt32()));
        var again = await ImportAsync();
        Assert.Equal((0, 0), (again.GetProperty("ratingsAdded").GetInt32(), again.GetProperty("playCountsRaised").GetInt32()));

        var swingNow = await _admin.GetFromJsonAsync<JsonElement>($"/rest/v1/tracks/{swing}", ArcanistFactory.Json, Cancel);
        Assert.Equal((7, true), (swingNow.GetProperty("playCount").GetInt32(), swingNow.GetProperty("starred").GetBoolean()));
        Assert.Equal(2, (await _admin.GetFromJsonAsync<JsonElement>($"/rest/v1/tracks/{riff}", ArcanistFactory.Json, Cancel)).GetProperty("rating").GetInt32());
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
