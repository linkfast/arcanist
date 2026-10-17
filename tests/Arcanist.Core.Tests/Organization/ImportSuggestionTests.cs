using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Arcanist.Core.Application.Online;
using Arcanist.Core.Online;
using Arcanist.Core.Organization;
using Arcanist.Core.Scanning;
using Arcanist.Core.Setup;
using Arcanist.Core.Tests.Online;
using Arcanist.Core.Tests.Scanning;
using Microsoft.Extensions.DependencyInjection;

namespace Arcanist.Core.Tests.Organization;

/// <summary>Files dropped without enough tags get suggestions from AcoustID, accepted with a click.</summary>
public sealed class ImportSuggestionTests : IAsyncLifetime
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private static readonly Guid AcoustId = Guid.NewGuid();
    private static readonly Guid Recording = Guid.NewGuid();
    private static readonly Guid Single = Guid.NewGuid();
    private static readonly Guid Album = Guid.NewGuid();
    private static readonly Guid Compilation = Guid.NewGuid();

    private readonly string _root = Directory.CreateTempSubdirectory("arcanist-suggest-").FullName;
    private readonly FakeOnlineServices _online = new();
    private ArcanistFactory _factory = null!;
    private HttpClient _admin = null!;

    private string Music => Path.Combine(_root, "library");

    private string Drop => Path.Combine(_root, "drop");

    private string Staging => Path.Combine(_root, "staging");

    public async ValueTask InitializeAsync()
    {
        ImportSuggester.PollInterval = TimeSpan.FromMilliseconds(100);
        Directory.CreateDirectory(Music);
        Directory.CreateDirectory(Drop);
        Directory.CreateDirectory(Staging);
        _factory = new ArcanistFactory(new Dictionary<string, string?>
        {
            ["Arcanist:Organization:StableSeconds"] = "0",
            ["Arcanist:Online:RequestInterval"] = "00:00:00.010",
            ["Arcanist:Online:AcoustIdKey"] = "test-key",
            ["Arcanist:Ffmpeg:SearchSystem"] = "false",
            ["Arcanist:Ffmpeg:Path"] = Path.Combine(_root, "no-ffmpeg"),
        }, services: _online.Register);
        await _factory.CompleteSetupAsync(new SetupLibrary("Music", [Music]));
        _admin = await _factory.CreateAuthenticatedClientAsync();
        await _factory.WaitForScansAsync(_admin);

        var library = (await _admin.GetFromJsonAsync<List<JsonElement>>("/rest/v1/libraries", ArcanistFactory.Json, Cancel))![0];
        var response = await _admin.PostAsJsonAsync("/rest/v1/drop-folders", new
        {
            path = Drop,
            librarySourceId = library.GetProperty("sources")[0].GetProperty("id").GetString(),
            enabled = true,
            duplicatePolicy = "SKIP",
            convertTo = (string?)null,
            convertBitRate = (int?)null,
            convertLossy = false,
            mode = "MOVE",
        }, ArcanistFactory.Json, Cancel);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    public async ValueTask DisposeAsync()
    {
        await _factory.DisposeAsync();
        Directory.Delete(_root, recursive: true);
    }

    /// <summary>A song is prepared aside and moved into the drop folder at once (it could be read half written).</summary>
    private void Drop_(string name, FixtureTags tags, int seed)
    {
        var path = Path.Combine(Staging, name);
        File.WriteAllBytes(path, AudioFixtures.Music(bpm: 120, seconds: 3, chordSeed: seed));
        AudioFixtures.Tag(path, tags);
        File.Move(path, Path.Combine(Drop, name), overwrite: true);
    }

    private async Task SetAcoustIdAsync(bool on) =>
        (await _admin.PutAsJsonAsync("/rest/v1/online-services", new SetOnlineServicesInput(false, on, false, false), Cancel)).EnsureSuccessStatusCode();

    private async Task<JsonElement> ItemAsync() => Assert.Single((await _admin.GetFromJsonAsync<List<JsonElement>>("/rest/v1/import-items", ArcanistFactory.Json, Cancel))!);

    private async Task<JsonElement> UntilAsync(Func<JsonElement, bool> condition, string what)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (true)
        {
            // The drop folder may not have seen the file yet.
            var items = (await _admin.GetFromJsonAsync<List<JsonElement>>("/rest/v1/import-items", ArcanistFactory.Json, Cancel))!;
            if (items is [var item] && condition(item))
                return item;
            Assert.True(DateTime.UtcNow < deadline, $"{what}: {string.Join(", ", items)}");
            await Task.Delay(50, Cancel);
        }
    }

    private static string? State(JsonElement item) =>
        item.GetProperty("suggestionState").ValueKind == JsonValueKind.Null ? null : item.GetProperty("suggestionState").GetString();

    /// <summary>The recording on a single, on its album (track 3 of disc 2 of two) and on a compilation.</summary>
    private static HttpResponseMessage Known() => FakeOnlineServices.Json($$"""
        {"status":"ok","results":[{"id":"{{AcoustId}}","score":0.93,"recordings":[{"id":"{{Recording}}","title":"Hidden Song","artists":[{"name":"Lost Band"}],
          "releases":[
            {"id":"{{Compilation}}","title":"Hits of the Year","artists":[{"name":"Various Artists"}],"date":{"year":1998},"medium_count":1,
             "mediums":[{"position":1,"tracks":[{"position":12,"title":"Hidden Song"}]}]},
            {"id":"{{Single}}","title":"Hidden Song","date":{"year":1999},"medium_count":1,
             "mediums":[{"position":1,"tracks":[{"position":1,"title":"Hidden Song"}]}]},
            {"id":"{{Album}}","title":"Deep Cuts","artists":[{"name":"Lost Band"}],"date":{"year":2000},"medium_count":2,
             "mediums":[{"position":2,"tracks":[{"position":3,"title":"Hidden Song (album version)"}]}]}]}]}]}
        """);

    [Fact]
    public async Task A_file_without_tags_gets_suggestions_and_accepting_one_identifies_it()
    {
        var bodies = new List<string>();
        _online.Respond = request =>
        {
            Assert.StartsWith("https://api.acoustid.org/v2/lookup", request.RequestUri!.ToString());
            lock (bodies)
                bodies.Add(_online.Bodies[request]);
            return Known();
        };
        await SetAcoustIdAsync(true);

        // It only says who plays it, and that it is on Deep Cuts.
        Drop_("mystery.wav", new FixtureTags(Artist: "Lost Band", Album: "Deep Cuts"), seed: 3);
        var item = await UntilAsync(i => State(i) == "FOUND", "the suggestions arrive");
        Assert.Equal(("NEEDS_REVIEW", "MISSING_TAGS"), (item.GetProperty("status").GetString(), item.GetProperty("reason").GetString()));

        // Only the fingerprint and its duration go to AcoustID, never the file's name or path.
        var body = Assert.Single(bodies);
        Assert.Contains("fingerprint=", body);
        Assert.Contains("meta=recordings+releases+tracks", body);
        Assert.DoesNotContain("mystery", body);

        // Deep Cuts first (it agrees with what the file says), then the earliest; one per release.
        var suggestions = item.GetProperty("suggestions").EnumerateArray().ToList();
        Assert.Equal(["Deep Cuts", "Hits of the Year", "Hidden Song"], suggestions.Select(s => s.GetProperty("album").GetString()));
        var best = suggestions[0];
        Assert.Equal(("Hidden Song (album version)", "Lost Band", "Lost Band", 2000, 3, 2),
            (best.GetProperty("title").GetString(), best.GetProperty("artist").GetString(), best.GetProperty("albumArtist").GetString(),
                best.GetProperty("year").GetInt32(), best.GetProperty("trackNumber").GetInt32(), best.GetProperty("discNumber").GetInt32()));
        Assert.Equal(JsonValueKind.Null, suggestions[1].GetProperty("discNumber").ValueKind);
        Assert.Equal("Lost Band", suggestions[2].GetProperty("albumArtist").GetString());

        // Accepted, the file goes into the library with those tags and the MusicBrainz identifiers.
        var response = await _admin.PostAsJsonAsync($"/rest/v1/import-items/{item.GetProperty("id").GetString()}/accept", new
        {
            tags = new
            {
                title = best.GetProperty("title").GetString(),
                artist = best.GetProperty("artist").GetString(),
                album = best.GetProperty("album").GetString(),
                albumArtist = best.GetProperty("albumArtist").GetString(),
                year = 2000,
                trackNumber = 3,
                discNumber = 2,
                recordingMbid = Recording,
                releaseMbid = Album,
            },
        }, ArcanistFactory.Json, Cancel);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await _factory.WaitForImportsAsync();

        var imported = Directory.EnumerateFiles(Music, "*.wav", SearchOption.AllDirectories).Single();
        await using (var stream = File.OpenRead(imported))
        {
            var tags = TagReader.Read(stream, "audio/wav");
            Assert.Equal(("Hidden Song (album version)", "Deep Cuts", Recording, Album), (tags.Title, tags.Album, tags.RecordingMbid, tags.ReleaseMbid));
        }

        await _factory.WaitForScansAsync(_admin);
        var track = (await _admin.GetFromJsonAsync<JsonElement>("/rest/v1/tracks", ArcanistFactory.Json, Cancel)).GetProperty("items")[0];
        Assert.Equal("Hidden Song (album version)", track.GetProperty("title").GetString());
    }

    [Fact]
    public async Task Nothing_is_looked_up_without_AcoustID_and_a_changed_file_is_looked_up_again()
    {
        var lookups = 0;
        _online.Respond = _ =>
        {
            // Unknown at first; the second file is known.
            return Interlocked.Increment(ref lookups) == 1 ? FakeOnlineServices.Json("""{"status":"ok","results":[]}""") : Known();
        };

        // AcoustID off: the file waits for review with no suggestions, and nothing is sent.
        Drop_("mystery.wav", new FixtureTags(Title: "Untitled"), seed: 4);
        var item = await UntilAsync(i => i.GetProperty("status").GetString() == "NEEDS_REVIEW", "the file waits for review");
        await Task.Delay(500, Cancel);
        item = await ItemAsync();
        Assert.Null(State(item));
        Assert.Empty(item.GetProperty("suggestions").EnumerateArray());
        Assert.Equal(0, lookups);

        // On, it is looked up: AcoustID does not know it, and it is not asked again.
        await SetAcoustIdAsync(true);
        _factory.Services.GetRequiredService<ImportMonitor>().RequestRun();
        await UntilAsync(i => State(i) == "NOT_FOUND", "AcoustID does not know it");
        await Task.Delay(500, Cancel);
        Assert.Equal(1, lookups);

        // Another audio under the same name is looked up again (written later: same size, so the date tells).
        Drop_("mystery.wav", new FixtureTags(Title: "Untitled"), seed: 5);
        File.SetLastWriteTimeUtc(Path.Combine(Drop, "mystery.wav"), DateTime.UtcNow.AddMinutes(1));
        _factory.Services.GetRequiredService<ImportMonitor>().RequestRun();
        item = await UntilAsync(i => State(i) == "FOUND", "the new audio is looked up");
        Assert.Equal(2, lookups);
        Assert.Equal("Hidden Song", item.GetProperty("suggestions")[0].GetProperty("title").GetString());
    }

    [Fact]
    public void Unlikely_matches_and_repeated_tags_are_left_out()
    {
        var recording = Guid.NewGuid();
        var release = new AcoustIdClient.ReleaseDetails(Guid.NewGuid(), "Album", "Band", 2001, 1, 1, 1, null);
        var suggestions = ImportSuggester.Rank(
        [
            new(AcoustId, 0.9, recording, "Song", "Band", [release, release with { ReleaseMbid = Guid.NewGuid() }]),
            new(AcoustId, 0.3, Guid.NewGuid(), "Other song", "Other band", []),
            new(AcoustId, 0.8, Guid.NewGuid(), null, "No title", []),
        ], null);

        var only = Assert.Single(suggestions);
        Assert.Equal(("Song", "Album", (int?)1, (int?)null), (only.Title, only.Album, only.TrackNumber, only.DiscNumber));
    }
}
