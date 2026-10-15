using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Arcanist.Core.Application.Mstream;
using Arcanist.Core.Data;
using Arcanist.Core.Data.Entities;
using Arcanist.Core.Extensions.Mstream;
using Arcanist.Core.Setup;
using Arcanist.Core.Tests.Scanning;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Arcanist.Core.Tests.Extensions;

/// <summary>The mStream page of the panel: the star threshold, signing in with the user's password and the apps' own functions.</summary>
public sealed class MstreamFeatureTests : IAsyncLifetime
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private readonly string _music = Directory.CreateTempSubdirectory("arcanist-mstream-features-").FullName;
    private readonly ArcanistFactory _factory = new(new Dictionary<string, string?> { ["Arcanist:Extensions:mstream:Enabled"] = "true" });
    private HttpClient _admin = null!;
    private string _token = null!;

    public async ValueTask InitializeAsync()
    {
        AudioFixtures.WriteWav(Path.Combine(_music, "The Band", "First", "01.wav"), 1,
            new FixtureTags("Opening", "The Band", "First Album", "The Band", TrackNumber: 1, Genre: "Rock"));
        AudioFixtures.WriteWav(Path.Combine(_music, "The Band", "First", "02.wav"), 2,
            new FixtureTags("Closing", "The Band", "First Album", "The Band", TrackNumber: 2, Genre: "Rock"));
        AudioFixtures.WriteWav(Path.Combine(_music, "Others", "01.wav"), 3, new FixtureTags("Loud", "Others", "Second", "Others", TrackNumber: 1, Genre: "Rock"));
        AudioFixtures.WriteWav(Path.Combine(_music, "Zed", "01.wav"), 4, new FixtureTags("Alone", "Zed", "Solo Record", "Zed", TrackNumber: 1, Genre: "Jazz"));

        await _factory.CompleteSetupAsync(new SetupLibrary("Music", [_music]));
        _admin = await _factory.CreateAuthenticatedClientAsync();
        await _factory.WaitForScansAsync(_admin);

        var created = await _admin.PostAsJsonAsync("/rest/v1/me/app-passwords", new { name = "Phone" }, ArcanistFactory.Json, Cancel);
        var secret = (await created.Content.ReadFromJsonAsync<JsonElement>(ArcanistFactory.Json, Cancel)).GetProperty("secret").GetString()!;
        _token = (await LoginAsync("admin", secret))!;
    }

    private async Task<string?> LoginAsync(string userName, string password)
    {
        var response = await _factory.CreateClient().PostAsJsonAsync("/api/v1/auth/login", new { username = userName, password }, Cancel);
        return response.StatusCode == HttpStatusCode.OK ? (await response.Content.ReadFromJsonAsync<JsonElement>(Cancel)).GetProperty("token").GetString() : null;
    }

    private HttpClient Client(string? token = null)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("x-access-token", token ?? _token);
        return client;
    }

    private async Task<JsonElement> SendAsync(HttpMethod method, string path, object? body = null, HttpStatusCode expected = HttpStatusCode.OK, string? token = null)
    {
        var request = new HttpRequestMessage(method, path) { Content = body is null ? null : JsonContent.Create(body) };
        var response = await Client(token).SendAsync(request, Cancel);
        var text = await response.Content.ReadAsStringAsync(Cancel);
        Assert.True(response.StatusCode == expected, $"{method} {path}: {(int)response.StatusCode} {text}");
        return text.Length > 0 && response.Content.Headers.ContentType?.MediaType == "application/json" ? JsonDocument.Parse(text).RootElement : default;
    }

    private async Task<JsonElement> SettingsAsync(int starThreshold = 1, bool passwordSignIn = false, params MstreamFeature[] features)
    {
        var response = await _admin.PutAsJsonAsync("/rest/v1/mstream/settings",
            new MstreamSettingsInput(starThreshold, passwordSignIn, features), ArcanistFactory.Json, Cancel);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync(Cancel));
        return await response.Content.ReadFromJsonAsync<JsonElement>(ArcanistFactory.Json, Cancel);
    }

    private ArcanistDbContext Db() => _factory.Services.GetRequiredService<IArcanistDbContextFactory>().CreateDbContext();

    /// <summary>Gives a song a tempo and a key, as the audio analysis would.</summary>
    private async Task AnalyzeAsync(string relativePath, float bpm, int tonic, bool minor)
    {
        await using var db = Db();
        var hash = await db.MediaFiles.Where(f => f.RelativePath == relativePath).Select(f => f.ContentHash).SingleAsync(Cancel);
        db.AudioAnalyses.Add(new AudioAnalysis
        {
            ContentHash = hash,
            State = AnalysisState.Analyzed,
            AnalyzedAt = DateTime.UtcNow,
            Bpm = bpm,
            BpmConfidence = 0.9f,
            KeyTonic = tonic,
            KeyMinor = minor,
            Waveform = Enumerable.Range(0, 1000).Select(i => (byte)(i % 256)).ToArray(),
        });
        await db.SaveChangesAsync(Cancel);
    }

    [Fact]
    public async Task Only_administrators_change_the_settings()
    {
        var status = await _admin.GetFromJsonAsync<JsonElement>("/rest/v1/mstream", ArcanistFactory.Json, Cancel);
        // Every function the apps have is on until an administrator turns it off.
        Assert.Equal((true, 1, false, Enum.GetValues<MstreamFeature>().Length), (status.GetProperty("enabled").GetBoolean(), status.GetProperty("starThreshold").GetInt32(),
            status.GetProperty("passwordSignIn").GetBoolean(), status.GetProperty("features").GetArrayLength()));

        var invalid = await _admin.PutAsJsonAsync("/rest/v1/mstream/settings", new MstreamSettingsInput(6, false, []), ArcanistFactory.Json, Cancel);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);

        await _factory.CreateUserAsync("listener");
        var listener = await _factory.CreateAuthenticatedClientAsync("listener");
        Assert.Equal(HttpStatusCode.Forbidden, (await listener.GetAsync("/rest/v1/mstream", Cancel)).StatusCode);
    }

    [Fact]
    public async Task In_single_star_mode_the_threshold_decides_which_ratings_give_the_star()
    {
        await _admin.GraphQLAsync("mutation { setRatingMode(mode: STAR) }");
        await SettingsAsync(starThreshold: 4);

        // Three stars stay a rating, unseen; four give the star (read back as ten).
        await SendAsync(HttpMethod.Post, "/api/v1/db/rate-song", new { filepath = "Music/Zed/01.wav", rating = 6 });
        await SendAsync(HttpMethod.Post, "/api/v1/db/rate-song", new { filepath = "Music/Others/01.wav", rating = 8 });
        await using var db = Db();
        var ratings = await db.TrackRatings.Include(r => r.Track).ToDictionaryAsync(r => r.Track.Title, r => r.Rating, Cancel);
        Assert.Equal((3, 5), (ratings["Alone"], ratings["Loud"]));
        var rated = (await SendAsync(HttpMethod.Get, "/api/v1/db/rated")).EnumerateArray().ToList();
        Assert.Equal(10, Assert.Single(rated).GetProperty("metadata").GetProperty("rating").GetInt32());
    }

    [Fact]
    public async Task Apps_sign_in_with_the_user_s_password_only_while_it_is_allowed()
    {
        Assert.Null(await LoginAsync("admin", ArcanistFactory.Password));

        await SettingsAsync(passwordSignIn: true);
        var token = await LoginAsync("admin", ArcanistFactory.Password);
        Assert.NotNull(token);
        await SendAsync(HttpMethod.Get, "/api/v1/ping", token: token);

        // A new password ends the tokens given with the old one.
        var changed = await _admin.PostAsJsonAsync("/rest/v1/me/password",
            new { currentPassword = ArcanistFactory.Password, newPassword = "another horse battery" }, ArcanistFactory.Json, Cancel);
        Assert.True(changed.IsSuccessStatusCode, await changed.Content.ReadAsStringAsync(Cancel));
        await SendAsync(HttpMethod.Get, "/api/v1/ping", expected: HttpStatusCode.Unauthorized, token: token);

        token = await LoginAsync("admin", "another horse battery");
        Assert.NotNull(token);
        _admin = await _factory.CreateAuthenticatedClientAsync(password: "another horse battery");
        await SettingsAsync(passwordSignIn: false);
        await SendAsync(HttpMethod.Get, "/api/v1/ping", expected: HttpStatusCode.Unauthorized, token: token);
        // App passwords keep working.
        await SendAsync(HttpMethod.Get, "/api/v1/ping");
    }

    [Fact]
    public async Task Functions_that_are_off_are_not_offered()
    {
        await SettingsAsync();
        var ping = await SendAsync(HttpMethod.Get, "/api/v1/ping");
        Assert.False(ping.TryGetProperty("stats", out _));
        Assert.False(ping.GetProperty("discovery").GetBoolean());
        await SendAsync(HttpMethod.Get, "/api/v1/db/waveform?filepath=Music/Zed/01.wav", expected: HttpStatusCode.ServiceUnavailable);
        await SendAsync(HttpMethod.Post, "/api/v1/discovery/local/similar/tracks", new { filePath = "Music/Zed/01.wav" }, HttpStatusCode.Forbidden);
        await SendAsync(HttpMethod.Get, "/api/v1/stats/summary", expected: HttpStatusCode.NotFound);
        await SendAsync(HttpMethod.Get, "/api/v1/scan/status", expected: HttpStatusCode.Forbidden);

        await SettingsAsync(features: [MstreamFeature.Stats, MstreamFeature.Similar, MstreamFeature.Waveform, MstreamFeature.ScanStatus]);
        var info = await SendAsync(HttpMethod.Get, "/api");
        Assert.Equal((2, true), (info.GetProperty("features").GetProperty("stats").GetInt32(), info.GetProperty("features").GetProperty("discovery").GetBoolean()));
        var status = await SendAsync(HttpMethod.Get, "/api/v1/scan/status");
        Assert.Equal(4, status.GetProperty("totals").GetProperty("tracks").GetInt32());
        Assert.Equal(0, (await SendAsync(HttpMethod.Get, "/api/v1/scan/progress")).GetArrayLength());
    }

    [Fact]
    public async Task Waveforms_come_from_the_analysis()
    {
        await SettingsAsync(features: [MstreamFeature.Waveform]);
        await SendAsync(HttpMethod.Get, "/api/v1/db/waveform?filepath=Music/Zed/01.wav", expected: HttpStatusCode.NotFound);

        await AnalyzeAsync("Zed/01.wav", 120, 0, false);
        var waveform = (await SendAsync(HttpMethod.Get, "/api/v1/db/waveform?filepath=Music/Zed/01.wav")).GetProperty("waveform");
        Assert.Equal(800, waveform.GetArrayLength());
        Assert.All(waveform.EnumerateArray(), b => Assert.InRange(b.GetInt32(), 0, 255));
    }

    [Fact]
    public async Task Similar_songs_artists_and_journeys_follow_tempo_key_and_genre()
    {
        await SettingsAsync(features: [MstreamFeature.Similar]);
        await AnalyzeAsync("The Band/First/01.wav", 120, 0, false);
        await AnalyzeAsync("The Band/First/02.wav", 122, 7, false);
        await AnalyzeAsync("Others/01.wav", 121, 2, false);
        await AnalyzeAsync("Zed/01.wav", 70, 6, true);

        var similar = await SendAsync(HttpMethod.Post, "/api/v1/discovery/local/similar/tracks", new { filePath = "Music/The Band/First/01.wav", limit = 5 });
        Assert.False(similar.GetProperty("notAnalyzed").GetBoolean());
        var results = similar.GetProperty("results").EnumerateArray().ToList();
        // Same genre, close tempo and key first; the jazz song is not in the pool.
        Assert.Equal(["Music/The Band/First/02.wav", "Music/Others/01.wav"], results.Select(r => r.GetProperty("filepath").GetString()));
        Assert.True(results[0].GetProperty("similarity").GetDouble() > 0.5);
        Assert.Equal("Closing", results[0].GetProperty("metadata").GetProperty("title").GetString());

        var artists = await SendAsync(HttpMethod.Post, "/api/v1/discovery/local/similar/artists", new { artist = "the band" });
        var artist = Assert.Single(artists.GetProperty("results").EnumerateArray());
        Assert.Equal(("Others", 1), (artist.GetProperty("artist").GetString(), artist.GetProperty("entryPoints").GetArrayLength()));

        var journey = await SendAsync(HttpMethod.Post, "/api/v1/discovery/local/path",
            new { startFilePath = "Music/The Band/First/01.wav", endFilePath = "Music/Zed/01.wav", length = 4 });
        var stops = journey.GetProperty("results").EnumerateArray().ToList();
        Assert.Equal(4, stops.Count);
        Assert.Equal(("Music/The Band/First/01.wav", 0.0), (stops[0].GetProperty("filepath").GetString(), stops[0].GetProperty("t").GetDouble()));
        Assert.Equal(("Music/Zed/01.wav", 1.0), (stops[^1].GetProperty("filepath").GetString(), stops[^1].GetProperty("t").GetDouble()));
    }

    [Fact]
    public async Task The_auto_dj_relaxes_tempo_and_key_and_skips_what_it_just_served()
    {
        // Apps turn the auto-DJ's batches and filters on by mStream version.
        Assert.Equal("6.28.0", (await SendAsync(HttpMethod.Get, "/api")).GetProperty("server").GetString());

        await AnalyzeAsync("The Band/First/01.wav", 120, 0, false);
        await AnalyzeAsync("The Band/First/02.wav", 122, 7, false);
        await AnalyzeAsync("Others/01.wav", 121, 2, false);
        await AnalyzeAsync("Zed/01.wav", 70, 6, true);

        // Tempo and key: only C major (8B) near 120.
        var exact = await SendAsync(HttpMethod.Post, "/api/v1/db/random-songs", new
        {
            limit = 5,
            bpmRanges = new[] { new { min = 115, max = 125 } },
            musicalKeys = new[] { "8B" },
        });
        Assert.Equal("Music/The Band/First/01.wav", Assert.Single(exact.GetProperty("songs").EnumerateArray()).GetProperty("filepath").GetString());

        // No song in that key: the key is dropped, the tempo kept.
        var relaxed = await SendAsync(HttpMethod.Post, "/api/v1/db/random-songs", new
        {
            limit = 5,
            bpmRanges = new[] { new { min = 115, max = 125 } },
            musicalKeys = new[] { "1A" },
        });
        Assert.Equal(3, relaxed.GetProperty("songs").GetArrayLength());

        // Batches are distinct, and what was served goes to the back of the queue next time.
        var first = await SendAsync(HttpMethod.Post, "/api/v1/db/random-songs", new { limit = 2, genres = new[] { "rock" } });
        var served = first.GetProperty("songs").EnumerateArray().Select(s => s.GetProperty("filepath").GetString()).ToList();
        Assert.Equal(2, served.Distinct().Count());
        var ignore = first.GetProperty("ignoreList").EnumerateArray().Select(i => i.GetInt32()).ToArray();
        Assert.Equal(2, ignore.Length);
        var next = await SendAsync(HttpMethod.Post, "/api/v1/db/random-songs", new { genres = new[] { "rock" }, ignoreList = ignore });
        Assert.DoesNotContain(Assert.Single(next.GetProperty("songs").EnumerateArray()).GetProperty("filepath").GetString(), served);

        // Lengths and genres are never relaxed.
        await SendAsync(HttpMethod.Post, "/api/v1/db/random-songs", new { genres = new[] { "polka" } }, HttpStatusCode.BadRequest);
        await SendAsync(HttpMethod.Post, "/api/v1/db/random-songs", new { minDuration = 3600 }, HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Plays_are_reported_with_their_own_ids_and_read_back()
    {
        await SettingsAsync(features: [MstreamFeature.Stats]);
        var started = DateTime.UtcNow.AddHours(-1);
        object Play(string id, string path, int playedMs, DateTime at) =>
            new { id, filePath = path, startedAt = at.ToString("O"), playedMs, outcome = "completed", source = "shuffle" };

        var report = await SendAsync(HttpMethod.Post, "/api/v1/stats/plays", new
        {
            client = new { name = "mstream-player" },
            plays = new[]
            {
                Play("a", "Music/Zed/01.wav", 60_000, started),
                Play("b", "Music/Others/01.wav", 60_000, started.AddMinutes(5)),
                Play("c", "Music/Nowhere.wav", 60_000, started),
                Play("d", "Music/Zed/01.wav", 60_000, started.AddDays(-400)),
            },
        });
        Assert.Equal(["a", "b"], report.GetProperty("accepted").EnumerateArray().Select(a => a.GetString()));
        Assert.Equal([("c", "unknown-track"), ("d", "bad-time")],
            report.GetProperty("rejected").EnumerateArray().Select(r => (r.GetProperty("id").GetString(), r.GetProperty("reason").GetString())));

        // Sent again, nothing moves.
        var again = await SendAsync(HttpMethod.Post, "/api/v1/stats/plays", new { plays = new[] { Play("a", "Music/Zed/01.wav", 60_000, started) } });
        Assert.Equal("a", Assert.Single(again.GetProperty("duplicates").EnumerateArray()).GetString());

        var history = await SendAsync(HttpMethod.Get, "/api/v1/stats/history?limit=1");
        var newest = Assert.Single(history.GetProperty("items").EnumerateArray());
        Assert.Equal(("b", "shuffle", "Music/Others/01.wav"),
            (newest.GetProperty("id").GetString(), newest.GetProperty("source").GetString(), newest.GetProperty("track").GetProperty("filepath").GetString()));
        var older = await SendAsync(HttpMethod.Get, $"/api/v1/stats/history?limit=1&before={history.GetProperty("next").GetString()}");
        Assert.Equal("a", Assert.Single(older.GetProperty("items").EnumerateArray()).GetProperty("id").GetString());

        var summary = await SendAsync(HttpMethod.Get, "/api/v1/stats/summary?period=all");
        // Never more than the song lasts: the fixtures last a few seconds.
        Assert.Equal((2, 2), (summary.GetProperty("plays").GetInt32(), summary.GetProperty("uniqueTracks").GetInt32()));
        Assert.InRange(summary.GetProperty("listenedMs").GetInt64(), 1, 120_000);
        Assert.Equal(1, summary.GetProperty("sessions").GetProperty("count").GetInt32());

        var top = await SendAsync(HttpMethod.Get, "/api/v1/stats/top?entity=genres&period=all");
        Assert.Equal(["Jazz", "Rock"], top.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("name").GetString()).Order());
        Assert.All(top.GetProperty("items").EnumerateArray(), i => Assert.Equal(1, i.GetProperty("plays").GetInt32()));
        var days = await SendAsync(HttpMethod.Get, "/api/v1/stats/timeseries?bucket=hourOfDay&period=all");
        Assert.Equal(24, days.GetProperty("items").GetArrayLength());
        var counters = await SendAsync(HttpMethod.Post, "/api/v1/stats/tracks", new { filePaths = new[] { "Music/Zed/01.wav" } });
        Assert.Equal(1, Assert.Single(counters.GetProperty("items").EnumerateArray()).GetProperty("plays").GetInt32());

        await SendAsync(HttpMethod.Delete, "/api/v1/stats/plays/a");
        Assert.Empty((await SendAsync(HttpMethod.Post, "/api/v1/stats/tracks", new { filePaths = new[] { "Music/Zed/01.wav" } })).GetProperty("items").EnumerateArray());
        await SendAsync(HttpMethod.Delete, "/api/v1/stats/plays/a", expected: HttpStatusCode.NotFound);

        var announced = await SendAsync(HttpMethod.Post, "/api/v1/stats/now-playing", new { filePath = "Music/Zed/01.wav", sessionId = "phone" });
        Assert.True(announced.GetProperty("accepted").GetBoolean());
        var now = Assert.Single((await SendAsync(HttpMethod.Get, "/api/v1/stats/now-playing")).GetProperty("entries").EnumerateArray());
        Assert.Equal("Alone", now.GetProperty("track").GetProperty("title").GetString());
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
