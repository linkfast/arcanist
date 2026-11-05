using System.Buffers.Text;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Arcanist.Core.Auth;
using Arcanist.Core.Data;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Arcanist.Core.Tests.Auth;

/// <summary>Apps that sign in through the browser: back to the app with PKCE, or with a code the device shows.</summary>
public sealed class BrowserLoginTests : IAsyncLifetime
{
    private const string Redirect = "http://127.0.0.1:53124/callback";
    private const string Verifier = "dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk";

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private readonly FakeTimeProvider _time = new(DateTimeOffset.UtcNow);
    private ArcanistFactory _factory = null!;

    public async ValueTask InitializeAsync()
    {
        _factory = new ArcanistFactory(time: _time);
        await _factory.CompleteSetupAsync();
    }

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();

    private static string Challenge(string verifier = Verifier) =>
        Base64Url.EncodeToString(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

    private static object Request(string redirectUri = Redirect, string? state = "xyz") =>
        new { clientName = "Ultimate Desktop", deviceName = "Sala", redirectUri, codeChallenge = Challenge(), state };

    private static async Task<(HttpStatusCode Status, JsonElement Body)> PostAsync(HttpClient client, string path, object? body = null)
    {
        var response = await client.PostAsJsonAsync(path, body ?? new { }, ArcanistFactory.Json, Cancel);
        var text = await response.Content.ReadAsStringAsync(Cancel);
        return (response.StatusCode, text.Length == 0 ? default : JsonDocument.Parse(text).RootElement.Clone());
    }

    private static string Code(JsonElement body) => body.GetProperty("code").GetString()!;

    /// <summary>The user lets the app in, and the redirect's query.</summary>
    private static async Task<Dictionary<string, string>> ApproveAsync(HttpClient client, string redirectUri = Redirect)
    {
        var (status, body) = await PostAsync(client, "/rest/v1/auth/browser/approve", Request(redirectUri));
        Assert.Equal(HttpStatusCode.OK, status);
        var url = body.GetProperty("redirectUrl").GetString()!;
        Assert.StartsWith(redirectUri + "?", url);
        return QueryHelpers.ParseQuery(url[url.IndexOf('?')..]).ToDictionary(q => q.Key, q => q.Value.ToString());
    }

    private Task<(HttpStatusCode Status, JsonElement Body)> RedeemAsync(string code, string verifier = Verifier, string redirectUri = Redirect) =>
        PostAsync(_factory.CreateClient(), "/rest/v1/auth/browser/token", new { code, codeVerifier = verifier, redirectUri });

    [Fact]
    public async Task The_browser_gives_the_app_a_code_only_its_verifier_can_trade_once()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var query = await ApproveAsync(client);
        Assert.Equal("xyz", query["state"]);

        var (status, tokens) = await RedeemAsync(query["code"]);
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.False(string.IsNullOrEmpty(tokens.GetProperty("refreshToken").GetString()));
        Assert.Equal(ArcanistFactory.AdminUserName, tokens.GetProperty("user").GetProperty("userName").GetString());

        var (again, body) = await RedeemAsync(query["code"]);
        Assert.Equal(HttpStatusCode.Conflict, again);
        Assert.Equal("browser_login_expired", Code(body));

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ArcanistDbContext>();
        Assert.True(await db.UserSessions.AnyAsync(s => s.DeviceName == "Sala", Cancel));
    }

    [Fact]
    public async Task A_wrong_verifier_or_address_spends_the_code()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var first = await ApproveAsync(client);
        var other = new string('a', 43);
        Assert.Equal(HttpStatusCode.Conflict, (await RedeemAsync(first["code"], other)).Status);
        Assert.Equal(HttpStatusCode.Conflict, (await RedeemAsync(first["code"])).Status);

        var second = await ApproveAsync(client);
        Assert.Equal(HttpStatusCode.Conflict, (await RedeemAsync(second["code"], Verifier, "http://127.0.0.1:1/other")).Status);
        Assert.Equal(HttpStatusCode.Conflict, (await RedeemAsync(second["code"])).Status);
    }

    [Fact]
    public async Task A_code_lasts_a_minute()
    {
        var query = await ApproveAsync(await _factory.CreateAuthenticatedClientAsync());
        _time.Advance(BrowserLoginStore.GrantLifetime + TimeSpan.FromSeconds(1));
        Assert.Equal(HttpStatusCode.Conflict, (await RedeemAsync(query["code"])).Status);
    }

    [Theory]
    [InlineData("https://evil.example/callback")]
    [InlineData("http://evil.example/callback")]
    [InlineData("http://localhost:5000/callback")]
    [InlineData("http://127.0.0.1.evil.example/callback")]
    [InlineData("http://user@127.0.0.1/callback")]
    [InlineData("http://127.0.0.1/callback#fragment")]
    [InlineData(@"http://127.0.0.1\@evil.example/")]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html,x")]
    [InlineData("file:///etc/passwd")]
    [InlineData("otherapp://callback")]
    [InlineData("/admin/")]
    public async Task Only_this_devices_loopback_and_the_listed_schemes_can_get_a_code(string redirectUri)
    {
        var (check, body) = await PostAsync(_factory.CreateClient(), "/rest/v1/auth/browser/check", Request(redirectUri));
        Assert.Equal(HttpStatusCode.BadRequest, check);
        Assert.True(body.GetProperty("errors").TryGetProperty("redirectUri", out _));

        var client = await _factory.CreateAuthenticatedClientAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await PostAsync(client, "/rest/v1/auth/browser/approve", Request(redirectUri))).Status);
    }

    [Theory]
    [InlineData(Redirect, "LOOPBACK")]
    [InlineData("http://[::1]:7000/", "LOOPBACK")]
    [InlineData("ultimate://auth/callback", "APP")]
    public async Task A_link_says_where_it_goes_back_and_how_to_say_no(string redirectUri, string target)
    {
        var (status, body) = await PostAsync(_factory.CreateClient(), "/rest/v1/auth/browser/check", Request(redirectUri));
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(target, body.GetProperty("target").GetString());
        Assert.Equal(redirectUri + "?error=access_denied&state=xyz", body.GetProperty("denyUrl").GetString());
    }

    [Fact]
    public void A_setting_can_add_an_apps_scheme_but_never_one_that_opens_something_else()
    {
        Assert.True(BrowserRedirects.IsAllowedScheme("ultimate", []));
        Assert.True(BrowserRedirects.IsAllowedScheme("myplayer", ["myplayer"]));
        Assert.False(BrowserRedirects.IsAllowedScheme("myplayer", []));
        Assert.False(BrowserRedirects.IsAllowedScheme("javascript", ["javascript"]));
        Assert.False(BrowserRedirects.IsAllowedScheme("https", ["https"]));
    }

    [Fact]
    public async Task Approving_takes_a_session_and_a_proper_challenge()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await PostAsync(_factory.CreateClient(), "/rest/v1/auth/browser/approve", Request())).Status);

        var client = await _factory.CreateAuthenticatedClientAsync();
        var (status, _) = await PostAsync(client, "/rest/v1/auth/browser/approve",
            new { clientName = "App", redirectUri = Redirect, codeChallenge = "plain" });
        Assert.Equal(HttpStatusCode.BadRequest, status);
    }

    [Fact]
    public async Task The_apps_session_never_has_a_fresher_password_than_the_browsers()
    {
        var browser = await _factory.CreateAuthenticatedClientAsync();
        _time.Advance(TimeSpan.FromMinutes(12));
        var query = await ApproveAsync(browser);

        var (status, tokens) = await RedeemAsync(query["code"]);
        Assert.Equal(HttpStatusCode.OK, status);

        var app = _factory.CreateClient();
        app.DefaultRequestHeaders.Authorization = new("Bearer", tokens.GetProperty("accessToken").GetString());
        var started = await PostAsync(_factory.CreateClient(), "/rest/v1/auth/device", new { clientName = "TV" });
        var (approved, body) = await PostAsync(app, "/rest/v1/auth/device/approve", new { userCode = started.Body.GetProperty("userCode").GetString() });
        Assert.Equal(HttpStatusCode.Forbidden, approved);
        Assert.Equal("reauthentication_required", Code(body));
    }

    [Fact]
    public async Task A_new_password_ends_what_was_approved_before_it()
    {
        var query = await ApproveAsync(await _factory.CreateAuthenticatedClientAsync());

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ArcanistDbContext>();
            var user = await db.Users.SingleAsync(u => u.UserName == ArcanistFactory.AdminUserName, Cancel);
            user.SecurityStamp = Guid.NewGuid().ToString();
            await db.SaveChangesAsync(Cancel);
        }

        var (status, body) = await RedeemAsync(query["code"]);
        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.Equal("browser_login_expired", Code(body));
    }

    [Fact]
    public async Task A_device_waits_for_someone_to_approve_its_code_and_gets_the_tokens_once()
    {
        var anonymous = _factory.CreateClient();
        var (started, start) = await PostAsync(anonymous, "/rest/v1/auth/device", new { clientName = "TV", deviceName = "Salón" });
        Assert.Equal(HttpStatusCode.OK, started);
        var deviceCode = start.GetProperty("deviceCode").GetString()!;
        var userCode = start.GetProperty("userCode").GetString()!;
        Assert.Matches("^[A-Z]{4}-[A-Z]{4}$", userCode);
        Assert.Equal("/admin/device", start.GetProperty("verificationUrl").GetString());
        Assert.EndsWith("?code=" + userCode, start.GetProperty("verificationUrlComplete").GetString());

        var (waiting, body) = await PostAsync(anonymous, "/rest/v1/auth/device/token", new { deviceCode });
        Assert.Equal(HttpStatusCode.Conflict, waiting);
        Assert.Equal("browser_login_pending", Code(body));

        var (tooSoon, soon) = await PostAsync(anonymous, "/rest/v1/auth/device/token", new { deviceCode });
        Assert.Equal(HttpStatusCode.TooManyRequests, tooSoon);
        Assert.Equal("browser_login_slow_down", Code(soon));

        var browser = await _factory.CreateAuthenticatedClientAsync();
        // However they type it.
        var (found, details) = await PostAsync(browser, "/rest/v1/auth/device/lookup", new { userCode = userCode.ToLowerInvariant().Replace("-", " ") });
        Assert.Equal(HttpStatusCode.OK, found);
        Assert.Equal("TV", details.GetProperty("clientName").GetString());
        Assert.Equal("Salón", details.GetProperty("deviceName").GetString());

        Assert.Equal(HttpStatusCode.NoContent, (await PostAsync(browser, "/rest/v1/auth/device/approve", new { userCode })).Status);
        // A code answers once.
        Assert.Equal(HttpStatusCode.Conflict, (await PostAsync(browser, "/rest/v1/auth/device/approve", new { userCode })).Status);

        var (done, tokens) = await PostAsync(anonymous, "/rest/v1/auth/device/token", new { deviceCode });
        Assert.Equal(HttpStatusCode.OK, done);
        Assert.False(string.IsNullOrEmpty(tokens.GetProperty("accessToken").GetString()));

        var (over, last) = await PostAsync(anonymous, "/rest/v1/auth/device/token", new { deviceCode });
        Assert.Equal(HttpStatusCode.Conflict, over);
        Assert.Equal("browser_login_expired", Code(last));
    }

    [Fact]
    public async Task Approving_a_device_asks_for_the_password_again()
    {
        var (_, start) = await PostAsync(_factory.CreateClient(), "/rest/v1/auth/device", new { clientName = "TV" });
        var browser = await _factory.CreateAuthenticatedClientAsync();
        _time.Advance(TimeSpan.FromMinutes(11));

        var (status, body) = await PostAsync(browser, "/rest/v1/auth/device/approve", new { userCode = start.GetProperty("userCode").GetString() });
        Assert.Equal(HttpStatusCode.Forbidden, status);
        Assert.Equal("reauthentication_required", Code(body));
    }

    [Fact]
    public async Task A_device_hears_no_and_a_code_that_took_too_long()
    {
        var anonymous = _factory.CreateClient();
        var browser = await _factory.CreateAuthenticatedClientAsync();

        var (_, refused) = await PostAsync(anonymous, "/rest/v1/auth/device", new { clientName = "TV" });
        Assert.Equal(HttpStatusCode.NoContent, (await PostAsync(browser, "/rest/v1/auth/device/deny", new { userCode = refused.GetProperty("userCode").GetString() })).Status);
        var (_, no) = await PostAsync(anonymous, "/rest/v1/auth/device/token", new { deviceCode = refused.GetProperty("deviceCode").GetString() });
        Assert.Equal("browser_login_denied", Code(no));

        var (_, late) = await PostAsync(anonymous, "/rest/v1/auth/device", new { clientName = "TV" });
        _time.Advance(BrowserLoginStore.DeviceLifetime + TimeSpan.FromSeconds(1));
        var (_, gone) = await PostAsync(anonymous, "/rest/v1/auth/device/token", new { deviceCode = late.GetProperty("deviceCode").GetString() });
        Assert.Equal("browser_login_expired", Code(gone));
    }

    [Fact]
    public async Task Guessing_codes_locks_the_user_out_of_it()
    {
        var browser = await _factory.CreateAuthenticatedClientAsync();
        HttpStatusCode last = default;
        for (var i = 0; i < 6; i++)
            (last, _) = await PostAsync(browser, "/rest/v1/auth/device/lookup", new { userCode = "BCDF-GHJK" });
        Assert.Equal(HttpStatusCode.TooManyRequests, last);
    }

    [Fact]
    public async Task The_server_says_it_can_and_where()
    {
        var client = _factory.CreateClient();
        var info = await client.GetFromJsonAsync<JsonElement>("/rest/v1/server-info", ArcanistFactory.Json, Cancel);
        Assert.Contains("BROWSER_LOGIN", info.GetProperty("features").EnumerateArray().Select(f => f.GetString()));

        var urls = await client.GetFromJsonAsync<JsonElement>("/rest/v1/auth/browser", ArcanistFactory.Json, Cancel);
        Assert.Equal("/admin/authorize", urls.GetProperty("authorizeUrl").GetString());
        Assert.Equal("/admin/device", urls.GetProperty("deviceUrl").GetString());
    }
}
