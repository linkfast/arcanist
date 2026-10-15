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

        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }

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

    public void Register(IServiceCollection services) =>
        services.AddHttpClient(UltimateCloudClient.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => this);
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
