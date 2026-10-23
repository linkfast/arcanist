using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Arcanist.Core.Application.Online;
using Arcanist.Core.Data;
using Arcanist.Core.Data.Entities;
using Arcanist.Core.Online;
using Arcanist.Core.Setup;
using Arcanist.Core.Tests.Scanning;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Arcanist.Core.Tests.Online;

/// <summary>Answers the online services' requests from canned responses and records them. Nothing reaches the network.</summary>
public sealed class FakeOnlineServices : HttpMessageHandler
{
    public ConcurrentQueue<HttpRequestMessage> Requests { get; } = new();

    /// <summary>Request bodies, read when sent: clients dispose them afterwards.</summary>
    public ConcurrentDictionary<HttpRequestMessage, string> Bodies { get; } = new();

    public Func<HttpRequestMessage, HttpResponseMessage> Respond { get; set; } = _ => new HttpResponseMessage(HttpStatusCode.NotFound);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Enqueue(request);
        if (request.Content is not null)
            Bodies[request] = await request.Content.ReadAsStringAsync(cancellationToken);
        return Respond(request);
    }

    public static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    /// <summary>Uses this handler for every online service client.</summary>
    public void Register(IServiceCollection services)
    {
        foreach (var name in new[] { OnlineHttp.MusicBrainz, OnlineHttp.AcoustId, OnlineHttp.CoverArtArchive, OnlineHttp.LrcLib, Application.WishList.WishLinkResolver.HttpClientName })
            services.AddHttpClient(name).ConfigurePrimaryHttpMessageHandler(() => this);
    }
}

public sealed class OnlineServicesTests : IAsyncLifetime
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private static readonly Guid AcoustIdA = Guid.NewGuid();
    private static readonly Guid RecordingA = Guid.NewGuid();
    private static readonly Guid RecordingB = Guid.NewGuid();
    private static readonly Guid Release = Guid.NewGuid();
    private static readonly Guid ArtistMbid = Guid.NewGuid();

    private readonly FakeOnlineServices _online = new();
    private readonly string _music = Directory.CreateTempSubdirectory("arcanist-online-").FullName;
    private ArcanistFactory _factory = null!;
    private HttpClient _admin = null!;
    private int _acoustIdCalls;

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    private async Task StartAsync(string? acoustIdKey = "test-key")
    {
        var album = new FixtureTags(Album: "Pulse", AlbumArtist: "Metronome", Artist: "Metronome");
        AudioFixtures.WriteMusic(Path.Combine(_music, "01.wav"), album with { Title = "Slow", TrackNumber = 1 }, bpm: 96, chordSeed: 2);
        AudioFixtures.WriteMusic(Path.Combine(_music, "02.wav"), album with { Title = "Fast", TrackNumber = 2 }, bpm: 140, chordSeed: 7);

        _factory = new ArcanistFactory(new Dictionary<string, string?>
        {
            ["Arcanist:Analysis:Enabled"] = "true",
            ["Arcanist:Online:RequestInterval"] = "00:00:00.010",
            ["Arcanist:Online:AcoustIdKey"] = acoustIdKey,
        }, services: _online.Register);

        await _factory.CompleteSetupAsync(new SetupLibrary("Music", [_music]));
        _admin = await _factory.CreateAuthenticatedClientAsync();
        await _factory.WaitForScansAsync(_admin);
        await _factory.WaitForAnalysisAsync();
    }

    private HttpResponseMessage Canned(HttpRequestMessage request)
    {
        var url = request.RequestUri!.ToString();
        if (url.StartsWith("https://api.acoustid.org/"))
        {
            // One recording per call, as two different songs would be.
            var recording = Interlocked.Increment(ref _acoustIdCalls) == 1 ? RecordingA : RecordingB;
            return FakeOnlineServices.Json($$"""{"status":"ok","results":[{"id":"{{AcoustIdA}}","score":0.97,"recordings":[{"id":"{{recording}}"}]}]}""");
        }

        if (url.StartsWith("https://musicbrainz.org/ws/2/release/"))
        {
            return FakeOnlineServices.Json($$$"""
                {"releases":[
                  {"id":"{{{Guid.NewGuid()}}}","score":100,"title":"Pulse","track-count":14,"date":"2010","artist-credit":[{"name":"Metronome","artist":{"id":"{{{Guid.NewGuid()}}}","name":"Metronome"}}]},
                  {"id":"{{{Release}}}","score":100,"title":"Pulse","track-count":2,"date":"2001-05-02","artist-credit":[{"name":"Metronome","artist":{"id":"{{{ArtistMbid}}}","name":"Metronome"}}]}
                ]}
                """);
        }

        if (url == $"https://coverartarchive.org/release/{Release}/front-1200")
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(AudioFixtures.Png(seed: 5, size: 64)) };

        if (url.StartsWith("https://lrclib.net/api/get") && url.Contains("track_name=Slow"))
            return FakeOnlineServices.Json("""{"plainLyrics":"La la","syncedLyrics":"[00:01.00]La la","instrumental":false}""");

        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    private async Task<JsonElement> WaitForOnlineServicesAsync()
    {
        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (true)
        {
            var status = await _admin.GetFromJsonAsync<JsonElement>("/rest/v1/online-services", ArcanistFactory.Json, Cancel);
            var services = status.GetProperty("services").EnumerateArray().ToList();
            if (services.Where(s => s.GetProperty("ready").GetBoolean()).All(s => s.GetProperty("pending").GetInt32() == 0))
                return status;
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException(status.ToString());
            _factory.Services.GetRequiredService<OnlineMonitor>().RequestRun();
            await Task.Delay(100, Cancel);
        }
    }

    private ArcanistDbContext Db() => _factory.Services.GetRequiredService<IArcanistDbContextFactory>().CreateDbContext();

    [Fact]
    public async Task Each_service_completes_the_catalog()
    {
        _online.Respond = Canned;
        await StartAsync();

        var set = await _admin.PutAsJsonAsync("/rest/v1/online-services", new SetOnlineServicesInput(true, true, true, true), Cancel);
        set.EnsureSuccessStatusCode();
        var status = await WaitForOnlineServicesAsync();

        await using var db = Db();
        // AcoustID: both recordings identified.
        var recordings = await db.Recordings.ToListAsync(Cancel);
        Assert.Equal(new[] { RecordingA, RecordingB }.Order(), recordings.Select(r => r.MusicBrainzId!.Value).Order());
        Assert.All(recordings, r => Assert.Equal(AcoustIdA, r.AcoustId));

        // MusicBrainz: the release with the same number of tracks, its year and the album artist.
        var album = await db.Albums.Include(a => a.AlbumArtist).Include(a => a.Artwork).SingleAsync(Cancel);
        Assert.Equal(Release, album.MusicBrainzReleaseId);
        Assert.Equal(2001, album.Year);
        Assert.Equal(ArtistMbid, album.AlbumArtist.MusicBrainzId);

        // Cover Art Archive: the album had no cover.
        Assert.Equal(ArtworkSource.Online, album.Artwork!.Source);

        // LRCLib: lyrics for one track, nothing for the other.
        var lyrics = await db.Lyrics.Include(l => l.Track).SingleAsync(Cancel);
        Assert.Equal(("Slow", LyricsSource.Online, "La la"), (lyrics.Track.Title, lyrics.Source, lyrics.Plain));

        // A result is saved a moment before its lookup is recorded.
        JsonElement lyricsStatus = default;
        for (var attempt = 0; attempt < 50; attempt++)
        {
            status = await _admin.GetFromJsonAsync<JsonElement>("/rest/v1/online-services", ArcanistFactory.Json, Cancel);
            lyricsStatus = status.GetProperty("services").EnumerateArray().Single(s => s.GetProperty("service").GetString() == "LYRICS");
            if (lyricsStatus.GetProperty("found").GetInt32() + lyricsStatus.GetProperty("notFound").GetInt32() == 2)
                break;
            await Task.Delay(100, Cancel);
        }

        Assert.Equal((1, 1), (lyricsStatus.GetProperty("found").GetInt32(), lyricsStatus.GetProperty("notFound").GetInt32()));

        // They identify Arcanist, as MusicBrainz and LRCLib ask.
        Assert.All(_online.Requests, r => Assert.StartsWith("Arcanist/", r.Headers.UserAgent.ToString()));
        // Found or not, nothing is asked twice.
        var asked = _online.Requests.Count;
        _factory.Services.GetRequiredService<OnlineMonitor>().RequestRun();
        await Task.Delay(300, Cancel);
        Assert.Equal(asked, _online.Requests.Count);
    }

    [Fact]
    public async Task Nothing_is_sent_while_services_are_off()
    {
        _online.Respond = Canned;
        await StartAsync();

        var worker = _factory.Services.GetRequiredService<EnrichmentWorker>();
        var gate = new RateGate(TimeSpan.Zero, TimeProvider.System);
        foreach (var service in Enum.GetValues<OnlineService>())
            Assert.True(await worker.StepAsync(service, gate, Cancel) > TimeSpan.Zero);

        Assert.Empty(_online.Requests);
        var status = await _admin.GetFromJsonAsync<JsonElement>("/rest/v1/online-services", ArcanistFactory.Json, Cancel);
        Assert.All(status.GetProperty("services").EnumerateArray(), s => Assert.False(s.GetProperty("enabled").GetBoolean()));
    }

    [Fact]
    public async Task AcoustId_uses_the_project_s_key_until_the_administrator_sets_their_own()
    {
        _online.Respond = Canned;
        await StartAsync(acoustIdKey: null);
        await _admin.PutAsJsonAsync("/rest/v1/online-services", new SetOnlineServicesInput(false, true, false, false), Cancel);

        var status = await _admin.GetFromJsonAsync<JsonElement>("/rest/v1/online-services", ArcanistFactory.Json, Cancel);
        var acoustId = status.GetProperty("services").EnumerateArray().Single(s => s.GetProperty("service").GetString() == "ACOUST_ID");
        Assert.True(acoustId.GetProperty("enabled").GetBoolean());
        Assert.True(acoustId.GetProperty("ready").GetBoolean());
        Assert.True(status.GetProperty("hasAcoustIdKey").GetBoolean());
        Assert.False(status.GetProperty("hasOwnAcoustIdKey").GetBoolean());
        await WaitForOnlineServicesAsync();
        var first = _online.Requests.First(r => r.RequestUri!.Host == "api.acoustid.org");
        Assert.Contains($"client={OnlineOptions.ProjectAcoustIdKey}", _online.Bodies[first]);

        var withKey = await _admin.PutAsJsonAsync("/rest/v1/online-services/acoustid-key", new SetAcoustIdKeyInput("my-own-key"), Cancel);
        var updated = await withKey.Content.ReadFromJsonAsync<JsonElement>(Cancel);
        Assert.True(updated.GetProperty("hasOwnAcoustIdKey").GetBoolean());
        Assert.Equal("my-own-key", await _factory.Services.GetRequiredService<Arcanist.Core.Online.OnlineSettings>().AcoustIdKeyAsync(Cancel));
    }

    [Fact]
    public async Task Connection_problems_pause_the_service_without_marking_anything()
    {
        _online.Respond = _ => throw new HttpRequestException("No route to host");
        await StartAsync();
        await _admin.PutAsJsonAsync("/rest/v1/online-services", new SetOnlineServicesInput(true, false, false, false), Cancel);

        var worker = _factory.Services.GetRequiredService<EnrichmentWorker>();
        var delay = await worker.StepAsync(OnlineService.MusicBrainz, new RateGate(TimeSpan.Zero, TimeProvider.System), Cancel);

        Assert.True(delay >= TimeSpan.FromMinutes(1));
        await using var db = Db();
        Assert.Empty(await db.OnlineLookups.ToListAsync(Cancel));
        var status = await _admin.GetFromJsonAsync<JsonElement>("/rest/v1/online-services", ArcanistFactory.Json, Cancel);
        var musicBrainz = status.GetProperty("services").EnumerateArray().Single(s => s.GetProperty("service").GetString() == "MUSIC_BRAINZ");
        Assert.Equal("connection_failed", musicBrainz.GetProperty("lastError").GetString());
        Assert.Equal(1, musicBrainz.GetProperty("pending").GetInt32());
    }

    [Fact]
    public async Task The_setup_wizard_stores_the_choice_and_others_cannot_change_it()
    {
        _factory = new ArcanistFactory(services: _online.Register);
        var response = await _factory.CreateClient().PostAsJsonAsync("/rest/v1/setup/complete", new SetupRequest(
            new SetupDatabase(DatabaseProvider.Sqlite, null), new SetupAdmin("admin", ArcanistFactory.Password, null), [], false, null,
            OnlineServices: new OnlineServicesSettings(MusicBrainz: true, AcoustId: false, CoverArtArchive: true, Lyrics: false),
            AcceptedTerms: Arcanist.Core.Legal.LegalService.CurrentVersion), ArcanistFactory.Json, Cancel);
        response.EnsureSuccessStatusCode();
        _admin = await _factory.CreateAuthenticatedClientAsync();

        var status = await _admin.GraphQLAsync("{ onlineServices { settings { musicBrainz acoustId coverArtArchive lyrics } } }");
        var settings = status.GetProperty("data").GetProperty("onlineServices").GetProperty("settings");
        Assert.Equal((true, false, true, false), (settings.GetProperty("musicBrainz").GetBoolean(), settings.GetProperty("acoustId").GetBoolean(),
            settings.GetProperty("coverArtArchive").GetBoolean(), settings.GetProperty("lyrics").GetBoolean()));

        await _factory.CreateUserAsync("listener");
        var listener = await _factory.CreateAuthenticatedClientAsync("listener");
        Assert.Equal(HttpStatusCode.Forbidden, (await listener.GetAsync("/rest/v1/online-services", Cancel)).StatusCode);
    }

    public async ValueTask DisposeAsync()
    {
        if (_factory is not null)
            await _factory.DisposeAsync();
        Directory.Delete(_music, recursive: true);
    }
}

public sealed class OnlineClientTests
{
    [Fact]
    public void Release_search_escapes_quotes_and_backslashes()
    {
        Assert.Equal("AC\\\\DC \\\"Live\\\"", MusicBrainzClient.Escape("AC\\DC \"Live\""));
    }

    [Fact]
    public async Task The_rate_gate_spaces_requests()
    {
        var time = new FakeTimeProvider();
        var gate = new RateGate(TimeSpan.FromSeconds(1), time);

        await gate.WaitAsync(TestContext.Current.CancellationToken);
        var second = gate.WaitAsync(TestContext.Current.CancellationToken);
        Assert.False(second.IsCompleted);

        time.Advance(TimeSpan.FromMilliseconds(999));
        Assert.False(second.IsCompleted);
        time.Advance(TimeSpan.FromMilliseconds(1));
        await second.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
    }
}
