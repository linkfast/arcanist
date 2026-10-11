using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Arcanist.Core.Api.Rest;
using Arcanist.Core.Auth;
using Arcanist.Core.Data;
using Arcanist.Core.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Arcanist.Core.Tests.Auth;

public sealed class AuthTests : IAsyncLifetime
{
    private readonly FakeTimeProvider _time = new(DateTimeOffset.UtcNow);
    private readonly ArcanistFactory _factory;

    public AuthTests() => _factory = new ArcanistFactory(time: _time);

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => await _factory.CompleteSetupAsync();

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    [Fact]
    public async Task Login_returns_tokens_that_identify_the_user()
    {
        var tokens = await _factory.LoginAsync();

        Assert.NotNull(tokens.RefreshToken);
        Assert.Equal(UserRole.Admin, tokens.User.Role);

        var me = await _factory.CreateClient(tokens.AccessToken).GetFromJsonAsync<AuthenticatedUser>("/rest/v1/me", ArcanistFactory.Json, Cancel);
        Assert.Equal(ArcanistFactory.AdminUserName, me!.UserName);
    }

    [Theory]
    [InlineData(ArcanistFactory.AdminUserName, "wrong password")]
    [InlineData("nobody", ArcanistFactory.Password)]
    public async Task Wrong_credentials_are_rejected_without_revealing_which_part_is_wrong(string userName, string password)
    {
        var response = await _factory.CreateClient().PostAsJsonAsync("/rest/v1/auth/login", new LoginRequest(userName, password, null), Cancel);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("invalid_credentials", await Code(response));
    }

    [Fact]
    public async Task Protected_endpoints_require_a_token()
    {
        var response = await _factory.CreateClient().GetAsync("/rest/v1/me", Cancel);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("unauthorized", await Code(response));
    }

    [Fact]
    public async Task Refresh_rotates_the_refresh_token()
    {
        var first = await _factory.LoginAsync();
        var client = _factory.CreateClient();

        var refreshed = await client.PostAsJsonAsync("/rest/v1/auth/refresh", new RefreshRequest(first.RefreshToken), Cancel);
        Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode);
        var second = (await refreshed.Content.ReadFromJsonAsync<TokenResponse>(ArcanistFactory.Json, Cancel))!;
        Assert.NotEqual(first.RefreshToken, second.RefreshToken);

        var reused = await client.PostAsJsonAsync("/rest/v1/auth/refresh", new RefreshRequest(first.RefreshToken), Cancel);
        Assert.Equal(HttpStatusCode.Unauthorized, reused.StatusCode);
        Assert.Equal("invalid_refresh_token", await Code(reused));

        var again = await client.PostAsJsonAsync("/rest/v1/auth/refresh", new RefreshRequest(second.RefreshToken), Cancel);
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
    }

    [Fact]
    public async Task Browser_clients_keep_the_refresh_token_in_an_http_only_cookie()
    {
        var client = _factory.CreateClient();

        var login = await client.PostAsJsonAsync("/rest/v1/auth/login",
            new LoginRequest(ArcanistFactory.AdminUserName, ArcanistFactory.Password, null, UseCookie: true), Cancel);
        var body = (await login.Content.ReadFromJsonAsync<TokenResponse>(ArcanistFactory.Json, Cancel))!;
        var cookie = Assert.Single(login.Headers.GetValues("Set-Cookie"));

        Assert.Null(body.RefreshToken);
        Assert.StartsWith(AuthEndpoints.RefreshCookie + "=", cookie);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/rest/v1/auth", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);

        // The test client stores the cookie and sends it back, as a browser would.
        var refreshed = await client.PostAsync("/rest/v1/auth/refresh", null, Cancel);
        Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode);
        Assert.Null((await refreshed.Content.ReadFromJsonAsync<TokenResponse>(ArcanistFactory.Json, Cancel))!.RefreshToken);
    }

    [Fact]
    public async Task Logout_revokes_the_session_immediately()
    {
        var tokens = await _factory.LoginAsync();
        var client = _factory.CreateClient(tokens.AccessToken);

        var logout = await client.PostAsJsonAsync("/rest/v1/auth/logout", new RefreshRequest(tokens.RefreshToken), Cancel);
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/rest/v1/me", Cancel)).StatusCode);
        var refresh = await _factory.CreateClient().PostAsJsonAsync("/rest/v1/auth/refresh", new RefreshRequest(tokens.RefreshToken), Cancel);
        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
    }

    [Fact]
    public async Task Access_tokens_expire()
    {
        var tokens = await _factory.LoginAsync();

        _time.Advance(TimeSpan.FromMinutes(16));

        Assert.Equal(HttpStatusCode.Unauthorized, (await _factory.CreateClient(tokens.AccessToken).GetAsync("/rest/v1/me", Cancel)).StatusCode);
    }

    [Fact]
    public async Task Repeated_failures_lock_the_account_for_a_while()
    {
        var client = _factory.CreateClient();
        for (var i = 0; i < 5; i++)
            await client.PostAsJsonAsync("/rest/v1/auth/login", new LoginRequest(ArcanistFactory.AdminUserName, "wrong", null), Cancel);

        var locked = await client.PostAsJsonAsync("/rest/v1/auth/login",
            new LoginRequest(ArcanistFactory.AdminUserName, ArcanistFactory.Password, null), Cancel);
        Assert.Equal(HttpStatusCode.TooManyRequests, locked.StatusCode);
        Assert.Equal("too_many_requests", await Code(locked));
        Assert.True(locked.Headers.RetryAfter is not null);

        _time.Advance(TimeSpan.FromSeconds(31));

        var unlocked = await client.PostAsJsonAsync("/rest/v1/auth/login",
            new LoginRequest(ArcanistFactory.AdminUserName, ArcanistFactory.Password, null), Cancel);
        Assert.Equal(HttpStatusCode.OK, unlocked.StatusCode);
    }

    [Fact]
    public async Task Each_client_address_is_rate_limited()
    {
        var client = _factory.CreateRemoteClient("203.0.113.9");
        HttpResponseMessage? last = null;

        // Different unknown accounts each time, so the per-account lockout is not what stops it.
        for (var i = 0; i < 21; i++)
            last = await client.PostAsJsonAsync("/rest/v1/auth/login", new LoginRequest($"user{i}", "x", null), Cancel);

        Assert.Equal(HttpStatusCode.TooManyRequests, last!.StatusCode);
        var otherAddress = await _factory.CreateRemoteClient("203.0.113.10")
            .PostAsJsonAsync("/rest/v1/auth/login", new LoginRequest("someone", "x", null), Cancel);
        Assert.Equal(HttpStatusCode.Unauthorized, otherAddress.StatusCode);
    }

    [Fact]
    public async Task Disabled_users_lose_access_and_cannot_log_in()
    {
        await _factory.CreateUserAsync("bob");
        var tokens = await _factory.LoginAsync("bob");

        await using (var db = _factory.Services.GetRequiredService<IArcanistDbContextFactory>().CreateDbContext())
        {
            await db.Users.Where(u => u.UserName == "bob").ExecuteUpdateAsync(u => u.SetProperty(x => x.IsDisabled, true), Cancel);
        }
        _factory.Services.GetRequiredService<UserAccessCache>().InvalidateUser(tokens.User.Id);

        Assert.Equal(HttpStatusCode.Unauthorized, (await _factory.CreateClient(tokens.AccessToken).GetAsync("/rest/v1/me", Cancel)).StatusCode);
        var login = await _factory.CreateClient().PostAsJsonAsync("/rest/v1/auth/login", new LoginRequest("bob", ArcanistFactory.Password, null), Cancel);
        Assert.Equal(HttpStatusCode.Forbidden, login.StatusCode);
        Assert.Equal("account_disabled", await Code(login));
    }

    [Fact]
    public async Task Reauthenticating_refreshes_the_authentication_time()
    {
        var tokens = await _factory.LoginAsync();
        _time.Advance(TimeSpan.FromMinutes(12));

        var client = _factory.CreateClient(tokens.AccessToken);
        var wrong = await client.PostAsJsonAsync("/rest/v1/auth/reauthenticate", new ReauthenticateRequest("nope"), Cancel);
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);

        var response = await client.PostAsJsonAsync("/rest/v1/auth/reauthenticate", new ReauthenticateRequest(ArcanistFactory.Password), Cancel);
        var renewed = (await response.Content.ReadFromJsonAsync<TokenResponse>(ArcanistFactory.Json, Cancel))!;

        Assert.True(AuthenticatedAt(renewed.AccessToken) - AuthenticatedAt(tokens.AccessToken) >= TimeSpan.FromMinutes(12));
    }

    private static DateTimeOffset AuthenticatedAt(string accessToken) =>
        DateTimeOffset.FromUnixTimeSeconds(long.Parse(new JsonWebToken(accessToken).GetClaim(ArcanistClaims.AuthenticatedAt).Value));

    private static async Task<string?> Code(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>(Cancel)).GetProperty("code").GetString();
}
