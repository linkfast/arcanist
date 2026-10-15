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
            if (query.Contains("myP2PNodes"))
            {
                var cloud = Plan == "Cloud";
                return Json(new
                {
                    data = new
                    {
                        p2pRelay = cloud && RelayUrl is not null ? new { url = RelayUrl } : null,
                        myP2PNodes = cloud
                            ? DeviceNodes.Select(d => new { nodeId = d.Key, kind = "DEVICE", name = d.Value })
                                .Concat(RegisteredNodes.Select(n => new { nodeId = n.Key, kind = "SERVER", name = n.Value })).ToArray()
                            : [],
                    },
                });
            }

            if (query.Contains("registerP2PNode"))
            {
                if (Plan != "Cloud")
                    return Json(new { errors = new[] { new { message = "Needs the Cloud plan.", extensions = new { code = "PLAN_REQUIRED" } } } });
                RegisteredNodes[variables!["n"]!.GetValue<string>()] = variables["name"]!.GetValue<string>();
                RegisteredTickets.Enqueue(variables["t"]?.GetValue<string>());
                return Json(new { data = new { registerP2PNode = new { nodeId = variables["n"]!.GetValue<string>() } } });
            }

            if (query.Contains("checkCloudFile"))
            {
                var hash = variables!["h"]!.GetValue<string>();
                var track = TrackHashes.FirstOrDefault(t => t.Value == hash).Key;
                return Json(new
                {
                    data = new
                    {
                        checkCloudFile = new
                        {
                            blobExists = Blobs.ContainsKey(hash) || track is not null,
                            cloudFileId = Blobs.GetValueOrDefault(hash),
                            alreadyInLibrary = track is not null,
                            existingTrackId = track,
                        },
                    },
                });
            }

            if (query.Contains("uploadUrl("))
            {
                if (Full)
                    return Json(new { errors = new[] { new { message = "Over quota.", extensions = new { code = "QUOTA_EXCEEDED" } } } });
                return Json(new { data = new { uploadUrl = new { uploadUrl = $"{FilesBase}/b2api/upload", authorizationToken = "upload-token", filePrefix = "CloudFiles/" } } });
            }

            if (query.Contains("registerCloudUpload"))
            {
                var input = variables!["i"]!;
                var hash = input["audioHash"]!.GetValue<string>();
                Assert.Equal("CloudFiles/" + hash, input["fileName"]!.GetValue<string>());
                Blobs[hash] = "file-" + hash[..8];
                return Json(new { data = new { registerCloudUpload = new { cloudFileId = Blobs[hash] } } });
            }

            if (query.Contains("addCloudTrack"))
            {
                var fileId = variables!["f"]!.GetValue<string>();
                var hash = Blobs.Single(b => b.Value == fileId).Key;
                var input = variables["i"]!;
                var id = $"up-{Tracks.Count + 1}";
                AddTrack(id, input["title"]!.GetValue<string>(), input["artist"]?.GetValue<string>() ?? "", input["album"]?.GetValue<string>() ?? "",
                    input["trackNumber"]?.GetValue<int>() ?? 0, Uploaded[$"CloudFiles/{hash}"], hash: hash, format: "WAV");
                return Json(new { data = new { addCloudTrack = new { cloudTrackId = id } } });
            }

            if (query.Contains("downloadUrl"))
            {
                var id = variables!["id"]!.GetValue<string>();
                return Json(new { data = new { downloadUrl = new { url = $"{FilesBase}/file/bucket/{id}?Authorization=token" } } });
            }

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

        // The tools Ultimate Cloud signs for Arcanist servers.
        if (url == "https://cloud.test/arcanist/tools/manifest")
        {
            if (!ServeTool)
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            var manifest = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
            {
                tool = "ultimate-audio",
                version = "1.0.0",
                protocol = 1,
                files = new[]
                {
                    new
                    {
                        rid = UltimateAudioTool.Platform ?? "none",
                        name = ToolName,
                        size = (long)ToolBytes.Length,
                        sha256 = TamperTool ? new string('0', 64) : Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(ToolBytes)),
                    },
                },
            }));
            var signer = SignWithOtherKey ? System.Security.Cryptography.ECDsa.Create(System.Security.Cryptography.ECCurve.NamedCurves.nistP256) : ToolKey;
            return Json(new { manifest = Convert.ToBase64String(manifest), signature = Convert.ToBase64String(signer.SignData(manifest, System.Security.Cryptography.HashAlgorithmName.SHA256)) });
        }

        if (url == $"https://cloud.test/arcanist/tools/files/{ToolName}")
        {
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(ToolBytes) };
        }

        // Backblaze B2 uploads, checked as B2 checks them.
        if (url == $"{FilesBase}/b2api/upload")
        {
            Assert.Equal("upload-token", request.Headers.GetValues("Authorization").Single());
            var bytes = await request.Content!.ReadAsByteArrayAsync(cancellationToken);
            if (!request.Headers.GetValues("X-Bz-Content-Sha1").Single().Equals(Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(bytes)), StringComparison.OrdinalIgnoreCase))
                return new HttpResponseMessage(HttpStatusCode.BadRequest);
            Uploaded[Uri.UnescapeDataString(request.Headers.GetValues("X-Bz-File-Name").Single())] = bytes;
            return Json(new { fileId = $"b2-{Uploaded.Count}" });
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

    /// <summary>The key the fake signs its tools with; Arcanist is told to trust it.</summary>
    public System.Security.Cryptography.ECDsa ToolKey { get; } = System.Security.Cryptography.ECDsa.Create(System.Security.Cryptography.ECCurve.NamedCurves.nistP256);

    public bool ServeTool { get; set; }

    public bool SignWithOtherKey { get; set; }

    public bool TamperTool { get; set; }

    /// <summary>A stand-in for ultimate-audio: a script that answers version and hash, and fails the rest.</summary>
    public static string ToolName => OperatingSystem.IsWindows() ? "ultimate-audio-test.cmd" : "ultimate-audio-test";

    public static byte[] ToolBytes { get; } = Encoding.ASCII.GetBytes(OperatingSystem.IsWindows()
        ? "@echo off\r\n" +
          "if \"%1\"==\"version\" (echo {\"tool\":\"ultimate-audio\",\"version\":\"1.0.0\",\"protocol\":1}& exit /b 0)\r\n" +
          "if \"%1\"==\"hash\" (echo {\"audioHash\":\"abc123\"}& exit /b 0)\r\n" +
          "echo {\"error\":\"corrupt_file\",\"message\":\"bad\"}\r\nexit /b 1\r\n"
        : "#!/bin/sh\n" +
          "case \"$1\" in\n" +
          "  version) echo '{\"tool\":\"ultimate-audio\",\"version\":\"1.0.0\",\"protocol\":1}' ;;\n" +
          "  hash) echo '{\"audioHash\":\"abc123\"}' ;;\n" +
          "  *) echo '{\"error\":\"corrupt_file\",\"message\":\"bad\"}'; exit 1 ;;\n" +
          "esac\n");

    /// <summary>The account's library: what <c>library</c> describes, and the audio B2 serves.</summary>
    public List<(string Id, object Payload, byte[] Audio)> Tracks { get; } = [];

    public byte[] Cover { get; set; } = [];

    public ConcurrentQueue<string?> Ranges { get; } = new();

    public int StreamUrls => _streamUrls;

    private int _streamUrls;

    /// <summary>Ultimate's audio hash of each track (its blob).</summary>
    public ConcurrentDictionary<string, string> TrackHashes { get; } = new();

    /// <summary>Blobs by audio hash: their CloudFile ids.</summary>
    public ConcurrentDictionary<string, string> Blobs { get; } = new();

    /// <summary>What reached B2, by file name.</summary>
    public ConcurrentDictionary<string, byte[]> Uploaded { get; } = new();

    /// <summary>The account's devices with an iroh node (node id → name).</summary>
    public ConcurrentDictionary<string, string> DeviceNodes { get; } = new();

    /// <summary>Servers that registered their node (node id → name), and the tickets they gave.</summary>
    public ConcurrentDictionary<string, string> RegisteredNodes { get; } = new();

    public ConcurrentQueue<string?> RegisteredTickets { get; } = new();

    /// <summary>Ultimate's relay, for Cloud accounts.</summary>
    public string? RelayUrl { get; set; } = "https://relay.ultimate.test/";

    /// <summary>The account's storage is full.</summary>
    public bool Full { get; set; }

    public void AddTrack(string id, string title, string artist, string album, int track, byte[] audio, string? cover = null, string? hash = null,
        string format = "FLAC")
    {
        TrackHashes[id] = hash ?? Convert.ToHexStringLower(System.Security.Cryptography.SHA1.HashData(audio));
        AddDescribed(id, title, artist, album, track, audio, cover, format);
    }

    private void AddDescribed(string id, string title, string artist, string album, int track, byte[] audio, string? cover, string format) =>
        Tracks.Add((id, new
        {
            cloudTrackId = id, title, artist, album, genre = "Ambient", trackNumber = track, discNumber = 1, year = 2020,
            duration = 8.5, audioBitrate = 900, audioSampleRate = 44100, audioFormat = format, size = (long)audio.Length,
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
            ["Arcanist:UltimateCloud:ToolsPublicKey"] = _cloud.ToolKey.ExportSubjectPublicKeyInfoPem(),
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

    /// <summary>The audio tool is installed right after the plan is read.</summary>
    private static async Task<JsonElement> WaitForToolAsync(HttpClient admin)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (true)
        {
            var tool = (await admin.GraphQLAsync("query { ultimateCloud { audioTool { version errorCode } } }"))
                .GetProperty("data").GetProperty("ultimateCloud").GetProperty("audioTool");
            if (tool.GetProperty("version").ValueKind != JsonValueKind.Null || tool.GetProperty("errorCode").ValueKind != JsonValueKind.Null)
                return tool;
            if (DateTime.UtcNow > deadline)
                Assert.Fail($"Still {tool}");
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
    public async Task The_audio_tool_comes_with_the_account_and_runs_as_a_separate_program()
    {
        _cloud.ServeTool = true;
        var admin = await StartAsync();
        await admin.GraphQLAsync("mutation { startUltimateCloudLink { state } }");
        _cloud.Approve = true;
        await WaitForStateAsync(admin, "LINKED");

        var tool = await WaitForToolAsync(admin);
        Assert.Equal("1.0.0", tool.GetProperty("version").GetString());
        Assert.Equal(JsonValueKind.Null, tool.GetProperty("errorCode").ValueKind);
        Assert.True(File.Exists(Path.Combine(_factory.DataPath, "tools", "ultimate-audio", FakeUltimateCloud.ToolName)));

        var audio = _factory.Services.GetRequiredService<UltimateAudioTool>();
        Assert.Equal("abc123", await audio.HashAsync(Path.Combine(_factory.DataPath, "anything.flac"), TestContext.Current.CancellationToken));
        var failure = await Assert.ThrowsAsync<UltimateCloudException>(() =>
            audio.StripAsync("in.flac", "out.flac", TestContext.Current.CancellationToken));
        Assert.Equal("tool:corrupt_file", failure.Code);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task A_tool_not_signed_by_Ultimate_or_altered_is_not_installed(bool otherKey, bool tampered)
    {
        (_cloud.ServeTool, _cloud.SignWithOtherKey, _cloud.TamperTool) = (true, otherKey, tampered);
        var admin = await StartAsync();
        await admin.GraphQLAsync("mutation { startUltimateCloudLink { state } }");
        _cloud.Approve = true;
        await WaitForStateAsync(admin, "LINKED");

        var tool = await WaitForToolAsync(admin);
        Assert.Equal(JsonValueKind.Null, tool.GetProperty("version").ValueKind);
        Assert.Equal("cloud_tool_invalid", tool.GetProperty("errorCode").GetString());
        Assert.False(File.Exists(Path.Combine(_factory.DataPath, "tools", "ultimate-audio", FakeUltimateCloud.ToolName)));
        await Assert.ThrowsAsync<UltimateCloudException>(() => _factory.Services.GetRequiredService<UltimateAudioTool>()
            .HashAsync("x.flac", TestContext.Current.CancellationToken));
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
