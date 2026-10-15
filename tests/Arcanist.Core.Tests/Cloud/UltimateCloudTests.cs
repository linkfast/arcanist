using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Arcanist.Core.Cloud;
using Arcanist.Core.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Arcanist.Core.Tests.Cloud;

/// <summary>
/// Plays Keycloak (discovery, device authorization, tokens, revocation) and Ultimate Cloud's GraphQL API from
/// memory. Nothing reaches the network.
/// </summary>
public sealed class FakeUltimateCloud : HttpMessageHandler
{
    public const string Authority = "https://auth.test/realms/clients";
    public const string ApiUrl = "https://cloud.test/graphql/";

    public bool Approve { get; set; }
    public bool Deny { get; set; }
    public bool SessionEnded { get; set; }
    public string Plan { get; set; } = "Cloud";
    public ConcurrentQueue<(string Operation, HttpRequestMessage Request, string Body)> Api { get; } = new();
    public ConcurrentQueue<string> Revoked { get; } = new();
    public string? RefreshToken { get; private set; }
    private int _issued;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var url = request.RequestUri!.ToString();
        var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
        var form = request.Content is FormUrlEncodedContent
            ? body.Split('&').Select(p => p.Split('=', 2)).ToDictionary(p => p[0], p => Uri.UnescapeDataString(p[1].Replace('+', ' ')))
            : [];

        if (url == $"{Authority}/.well-known/openid-configuration")
        {
            return Json(new
            {
                issuer = Authority,
                device_authorization_endpoint = $"{Authority}/device",
                token_endpoint = $"{Authority}/token",
                revocation_endpoint = $"{Authority}/revoke",
            });
        }

        if (url == $"{Authority}/device")
        {
            Assert.Equal("arcanist", form["client_id"]);
            Assert.Contains("offline_access", form["scope"]);
            return Json(new
            {
                device_code = "device-code",
                user_code = "WDJB-MJHT",
                verification_uri = "https://auth.test/device",
                verification_uri_complete = "https://auth.test/device?user_code=WDJB-MJHT",
                expires_in = 600,
                interval = 0,
            });
        }

        if (url == $"{Authority}/token")
        {
            if (form["grant_type"] == "urn:ietf:params:oauth:grant-type:device_code")
            {
                if (Deny)
                    return Error("access_denied");
                return Approve ? Tokens() : Error("authorization_pending");
            }

            Assert.Equal("refresh_token", form["grant_type"]);
            return SessionEnded || form["refresh_token"] != RefreshToken ? Error("invalid_grant") : Tokens();
        }

        if (url == $"{Authority}/revoke")
        {
            Revoked.Enqueue(form["token"]);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }

        if (url == ApiUrl)
        {
            var query = JsonNode.Parse(body)!["query"]!.GetValue<string>();
            Api.Enqueue((query.Contains("touchSyncedDevice") ? "touch" : "plan", request, body));
            if (request.Headers.Authorization is not { Scheme: "Bearer", Parameter: not null })
                return new HttpResponseMessage(HttpStatusCode.Unauthorized);
            if (query.Contains("touchSyncedDevice"))
                return Json(new { data = new { touchSyncedDevice = true } });
            if (query.Contains("myLibrary"))
                return Json(new { data = new { myLibrary = new { id = "lib-1" } } });
            if (query.Contains("cloudDropTracks"))
                return Json(new { data = new { cloudDropTracks = Array.Empty<object>() } });
            if (query.Contains("library {"))
                return Json(new { data = new { library = Tracks.Select(t => t.Payload).ToArray() } });
            var variables = JsonNode.Parse(body)!["variables"];
            if (query.Contains("streamUrl"))
            {
                var id = variables!["id"]!.GetValue<string>();
                Interlocked.Increment(ref _streamUrls);
                return Tracks.Any(t => t.Id == id)
                    ? Json(new { data = new { streamUrl = new { url = $"{FilesBase}/file/bucket/{id}?Authorization=token", validForSeconds = 3600 } } })
                    : Json(new { errors = new[] { new { message = $"Track '{id}' not found in library." } } });
            }

            if (query.Contains("coverImageUrl"))
                return Json(new { data = new { coverImageUrl = new { url = $"{FilesBase}/file/bucket/cover-{variables!["id"]!.GetValue<string>()}", validForSeconds = 3600 } } });
            return Json(new
            {
                data = new
                {
                    myPlan = new
                    {
                        plan = Plan,
                        featuresUnlocked = Plan != "Standard",
                        cloudStatus = Plan == "Cloud" ? "Active" : "None",
                        renewsAt = (DateTime?)null,
                        storage = new { usedBytes = 1024L, limitBytes = Plan == "Cloud" ? 1L << 40 : 0L, isReadOnly = Plan != "Cloud" },
                    },
                },
            });
        }

        // Backblaze B2: the tracks' audio (with Range) and covers.
        if (url.StartsWith(FilesBase + "/file/bucket/", StringComparison.Ordinal))
        {
            var name = request.RequestUri!.AbsolutePath["/file/bucket/".Length..];
            if (name.StartsWith("cover-", StringComparison.Ordinal))
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Cover) };
            if (Tracks.FirstOrDefault(t => t.Id == name) is not { Audio: not null } track)
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            Ranges.Enqueue(request.Headers.Range?.ToString());
            if (request.Headers.Range?.Ranges.SingleOrDefault() is { } range)
            {
                var from = (int)range.From!.Value;
                var to = (int)Math.Min(range.To ?? track.Audio.Length - 1, track.Audio.Length - 1);
                var part = new ByteArrayContent(track.Audio[from..(to + 1)]);
                part.Headers.ContentRange = new System.Net.Http.Headers.ContentRangeHeaderValue(from, to, track.Audio.Length);
                return new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = part };
            }

            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(track.Audio) };
        }

        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    public const string FilesBase = "https://f000.backblazeb2.test";

    /// <summary>The account's library: what <c>library</c> describes, and the audio B2 serves.</summary>
    public List<(string Id, object Payload, byte[] Audio)> Tracks { get; } = [];

    public byte[] Cover { get; set; } = [];

    public ConcurrentQueue<string?> Ranges { get; } = new();

    public int StreamUrls => _streamUrls;

    private int _streamUrls;

    public void AddTrack(string id, string title, string artist, string album, int track, byte[] audio, string? cover = null) =>
        Tracks.Add((id, new
        {
            cloudTrackId = id, title, artist, album, genre = "Ambient", trackNumber = track, discNumber = 1, year = 2020,
            duration = 8.5, audioBitrate = 900, audioSampleRate = 44100, audioFormat = "FLAC", size = (long)audio.Length,
            coverImageId = cover, musicBrainzRecordingId = (string?)null, musicBrainzArtistId = (string?)null,
            dateUpdated = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        }, audio));

    /// <summary>A new access token (expiring at once, so every call refreshes) and a rotated refresh token.</summary>
    private HttpResponseMessage Tokens()
    {
        var n = Interlocked.Increment(ref _issued);
        RefreshToken = $"refresh-{n}";
        var payload = Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(new { sub = "user-1", preferred_username = "ana", email = "ana@example.com" }))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return Json(new { access_token = $"eyJhbGciOiJub25lIn0.{payload}.", expires_in = 10, refresh_token = RefreshToken, token_type = "Bearer" });
    }

    private static HttpResponseMessage Json(object value) =>
        new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json") };

    private static HttpResponseMessage Error(string error) =>
        new(HttpStatusCode.BadRequest) { Content = new StringContent(JsonSerializer.Serialize(new { error }), Encoding.UTF8, "application/json") };

    public void Register(IServiceCollection services)
    {
        services.AddHttpClient(UltimateCloudClient.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => this);
        services.AddHttpClient(UltimateCloudStorage.FilesClientName).ConfigurePrimaryHttpMessageHandler(() => this);
    }
}

public sealed class UltimateCloudTests : IAsyncDisposable
{
    private const string Status = """
        query { ultimateCloud { state account pending { userCode verificationUri verificationUriComplete } plan { name usedBytes readOnly } tier capabilities errorCode } }
        """;

    private readonly FakeUltimateCloud _cloud = new();
    private ArcanistFactory _factory = null!;

    private async Task<HttpClient> StartAsync(bool configured = true)
    {
        _factory = new ArcanistFactory(new Dictionary<string, string?>
        {
            ["Arcanist:UltimateCloud:ApiUrl"] = configured ? FakeUltimateCloud.ApiUrl : null,
            ["Arcanist:UltimateCloud:Authority"] = FakeUltimateCloud.Authority,
        }, services: _cloud.Register);
        await _factory.CompleteSetupAsync();
        return await _factory.CreateAuthenticatedClientAsync();
    }

    private static async Task<JsonElement> StatusAsync(HttpClient admin) =>
        (await admin.GraphQLAsync(Status)).GetProperty("data").GetProperty("ultimateCloud");

    private static async Task<JsonElement> WaitForStateAsync(HttpClient admin, string state)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (true)
        {
            var status = await StatusAsync(admin);
            if (status.GetProperty("state").GetString() == state && status.GetProperty("pending").ValueKind == JsonValueKind.Null
                && (state != "LINKED" || status.GetProperty("plan").ValueKind != JsonValueKind.Null))
                return status;
            if (DateTime.UtcNow > deadline)
                Assert.Fail($"Still {status}");
            await Task.Delay(50, TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task Without_an_address_linking_is_unavailable()
    {
        var admin = await StartAsync(configured: false);

        var status = await StatusAsync(admin);
        Assert.Equal("NOT_CONFIGURED", status.GetProperty("state").GetString());
        Assert.Equal("NONE", status.GetProperty("tier").GetString());
        Assert.Equal("cloud_not_configured", (await admin.GraphQLAsync("mutation { startUltimateCloudLink { state } }")).ErrorCode());
    }

    [Fact]
    public async Task Linking_with_a_code_reads_the_plan_and_enables_the_capabilities()
    {
        var admin = await StartAsync();

        var started = await admin.GraphQLAsync("mutation { startUltimateCloudLink { state pending { userCode verificationUriComplete } } }");
        var link = started.GetProperty("data").GetProperty("startUltimateCloudLink");
        Assert.Equal("PENDING", link.GetProperty("state").GetString());
        Assert.Equal("WDJB-MJHT", link.GetProperty("pending").GetProperty("userCode").GetString());
        Assert.Equal("https://auth.test/device?user_code=WDJB-MJHT", link.GetProperty("pending").GetProperty("verificationUriComplete").GetString());

        _cloud.Approve = true;
        var status = await WaitForStateAsync(admin, "LINKED");
        Assert.Equal("ana", status.GetProperty("account").GetString());
        Assert.Equal("Cloud", status.GetProperty("plan").GetProperty("name").GetString());
        Assert.Equal("PAID", status.GetProperty("tier").GetString());
        Assert.Contains("CLOUD_SYNC", status.GetProperty("capabilities").EnumerateArray().Select(c => c.GetString()));

        var info = await admin.GetFromJsonAsync<JsonElement>("/rest/v1/server-info", TestContext.Current.CancellationToken);
        Assert.Equal("PAID", info.GetProperty("cloudTier").GetString());

        // The server registered itself as a device, identifying itself as every Ultimate client must.
        var touch = Assert.Single(_cloud.Api, a => a.Operation == "touch");
        Assert.Contains("SERVER", touch.Body);
        Assert.Equal("arcanist-server", touch.Request.Headers.GetValues("X-Client-App").Single());
        Assert.StartsWith("arcanist-", touch.Request.Headers.GetValues("X-Device-Id").Single());

        // The refresh token is kept protected, never as text.
        await using (var db = _factory.Services.GetRequiredService<IArcanistDbContextFactory>().CreateDbContext())
        {
            var stored = await db.Settings.SingleAsync(s => s.Key == SettingKeys.UltimateCloudLink, TestContext.Current.CancellationToken);
            Assert.DoesNotContain("refresh-", stored.Value);
        }

        // Changing plans shows after a refresh.
        _cloud.Plan = "Standard";
        var refreshed = (await admin.GraphQLAsync("mutation { refreshUltimateCloud { tier capabilities plan { name } } }"))
            .GetProperty("data").GetProperty("refreshUltimateCloud");
        Assert.Equal("FREE", refreshed.GetProperty("tier").GetString());
        Assert.Equal(["ULTIMATE_CLOUD_SOURCE"], refreshed.GetProperty("capabilities").EnumerateArray().Select(c => c.GetString()));

        // Unlinking ends the session on Ultimate Cloud and turns everything off.
        var unlinked = (await admin.GraphQLAsync("mutation { unlinkUltimateCloud { state tier plan { name } } }"))
            .GetProperty("data").GetProperty("unlinkUltimateCloud");
        Assert.Equal("UNLINKED", unlinked.GetProperty("state").GetString());
        Assert.Equal("NONE", unlinked.GetProperty("tier").GetString());
        Assert.Equal(JsonValueKind.Null, unlinked.GetProperty("plan").ValueKind);
        Assert.Equal(_cloud.RefreshToken, Assert.Single(_cloud.Revoked));
    }

    [Fact]
    public async Task A_refused_code_leaves_the_server_unlinked()
    {
        var admin = await StartAsync();

        await admin.GraphQLAsync("mutation { startUltimateCloudLink { state } }");
        _cloud.Deny = true;

        var status = await WaitForStateAsync(admin, "UNLINKED");
        Assert.Equal("cloud_link_denied", status.GetProperty("errorCode").GetString());
        Assert.Equal("NONE", status.GetProperty("tier").GetString());
    }

    [Fact]
    public async Task A_session_ended_on_Ultimate_Cloud_asks_to_link_again()
    {
        var admin = await StartAsync();
        await admin.GraphQLAsync("mutation { startUltimateCloudLink { state } }");
        _cloud.Approve = true;
        await WaitForStateAsync(admin, "LINKED");

        _cloud.SessionEnded = true;
        var status = (await admin.GraphQLAsync("mutation { refreshUltimateCloud { state tier errorCode } }"))
            .GetProperty("data").GetProperty("refreshUltimateCloud");
        Assert.Equal("cloud_link_expired", status.GetProperty("errorCode").GetString());
        Assert.Equal("NONE", status.GetProperty("tier").GetString());
    }

    [Fact]
    public async Task The_music_of_the_account_is_a_library_streamed_on_demand()
    {
        var audio = Enumerable.Range(0, 5000).Select(i => (byte)i).ToArray();
        _cloud.Plan = "Standard";
        _cloud.Cover = Scanning.AudioFixtures.Png(3);
        _cloud.AddTrack("t1", "Drift", "Haze", "Low Tide", 1, audio, cover: "c1");
        _cloud.AddTrack("t2", "Undertow", "Haze", "Low Tide", 2, audio[..1000], cover: "c1");
        var admin = await StartAsync();
        const string add = "mutation($c: UltimateCloudCollection!) { addUltimateCloudLibrary(input: {collection: $c}) { libraries { libraryId name collection } } }";

        // Only with a linked account.
        Assert.Equal("cloud_not_linked", (await admin.GraphQLAsync(add, new { c = "LIBRARY" })).ErrorCode());
        await admin.GraphQLAsync("mutation { startUltimateCloudLink { state } }");
        _cloud.Approve = true;
        await WaitForStateAsync(admin, "LINKED");

        var added = (await admin.GraphQLAsync(add, new { c = "LIBRARY" })).GetProperty("data").GetProperty("addUltimateCloudLibrary");
        var library = added.GetProperty("libraries").EnumerateArray().Single();
        Assert.Equal(("Ultimate Cloud", "LIBRARY"), (library.GetProperty("name").GetString(), library.GetProperty("collection").GetString()));
        Assert.Equal("validation_failed", (await admin.GraphQLAsync(add, new { c = "LIBRARY" })).ErrorCode());
        await _factory.WaitForScansAsync(admin);

        // Described by Ultimate Cloud: nothing but the cover was downloaded to catalog it.
        var tracks = (await admin.GraphQLAsync("query { tracks(first: 10) { items { id title artworkId } } }"))
            .GetProperty("data").GetProperty("tracks").GetProperty("items").EnumerateArray().OrderBy(t => t.GetProperty("title").GetString()).ToList();
        Assert.Equal(["Drift", "Undertow"], tracks.Select(t => t.GetProperty("title").GetString()));
        Assert.NotEqual(JsonValueKind.Null, tracks[0].GetProperty("artworkId").ValueKind);
        Assert.Equal(0, _cloud.StreamUrls);

        // Played through the server: Range goes to the signed URL, which is reused.
        var trackId = tracks[0].GetProperty("id").GetString();
        using var partial = new HttpRequestMessage(HttpMethod.Get, $"/rest/v1/stream/{trackId}");
        partial.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(100, 199);
        var response = await admin.SendAsync(partial, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.PartialContent, response.StatusCode);
        Assert.Equal("audio/flac", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(audio[100..200], await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken));
        Assert.Equal(audio, await admin.GetByteArrayAsync($"/rest/v1/stream/{trackId}", TestContext.Current.CancellationToken));
        Assert.Equal(1, _cloud.StreamUrls);

        // Folders are not added to it.
        var libraryId = library.GetProperty("libraryId").GetString();
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync($"/rest/v1/libraries/{libraryId}/sources",
            new { location = Path.GetTempPath() }, TestContext.Current.CancellationToken)).StatusCode);

        // Unlinked, the library stays as it is: a scan finds the source unavailable and marks nothing missing.
        await admin.GraphQLAsync("mutation { unlinkUltimateCloud { state } }");
        await admin.GraphQLAsync("mutation($id: ID!) { startScan(libraryId: $id, full: false) { id } }", new { id = libraryId });
        await _factory.WaitForScansAsync(admin);
        Assert.Equal(2, (await admin.GraphQLAsync("query { tracks(first: 10) { totalCount } }"))
            .GetProperty("data").GetProperty("tracks").GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task Only_administrators_see_the_account()
    {
        var admin = await StartAsync();
        await _factory.CreateUserAsync("bob");
        var bob = await _factory.CreateAuthenticatedClientAsync("bob");

        Assert.Equal("forbidden", (await bob.GraphQLAsync(Status)).ErrorCode());
        Assert.Equal(HttpStatusCode.Forbidden, (await bob.PostAsync("/rest/v1/ultimate-cloud/link", null, TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/rest/v1/ultimate-cloud", TestContext.Current.CancellationToken)).StatusCode);
    }

    public async ValueTask DisposeAsync()
    {
        if (_factory is not null)
            await _factory.DisposeAsync();
    }
}
