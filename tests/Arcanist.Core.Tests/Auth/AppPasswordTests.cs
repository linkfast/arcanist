using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Arcanist.Core.Application.Users;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Arcanist.Core.Tests.Auth;

/// <summary>Passwords users create for Subsonic and mStream apps.</summary>
public sealed class AppPasswordTests : IAsyncLifetime
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private readonly FakeTimeProvider _time = new(DateTimeOffset.UtcNow);
    private ArcanistFactory _factory = null!;
    private HttpClient _admin = null!;

    public async ValueTask InitializeAsync()
    {
        _factory = new ArcanistFactory(time: _time);
        await _factory.CompleteSetupAsync();
        _admin = await _factory.CreateAuthenticatedClientAsync();
    }

    private async Task<(string Id, string Secret)> CreateAsync(string name = "Phone")
    {
        var response = await _admin.PostAsJsonAsync("/rest/v1/me/app-passwords", new { name }, ArcanistFactory.Json, Cancel);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<JsonElement>(ArcanistFactory.Json, Cancel);
        return (created.GetProperty("appPassword").GetProperty("id").GetString()!, created.GetProperty("secret").GetString()!);
    }

    private async Task<T> WithServiceAsync<T>(Func<AppPasswordService, Task<T>> action)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<AppPasswordService>());
    }

    [Fact]
    public async Task An_app_password_works_as_password_token_and_api_key_and_is_shown_once()
    {
        var (_, secret) = await CreateAsync();
        Assert.Matches("^[a-z2-9]{4}(-[a-z2-9]{4}){3}$", secret);

        var listed = Assert.Single((await _admin.GetFromJsonAsync<List<JsonElement>>("/rest/v1/me/app-passwords", ArcanistFactory.Json, Cancel))!);
        Assert.Equal("Phone", listed.GetProperty("name").GetString());
        Assert.False(listed.TryGetProperty("secret", out _));

        Assert.NotNull(await WithServiceAsync(s => s.VerifyPasswordAsync("ADMIN", secret, Cancel)));
        Assert.Null(await WithServiceAsync(s => s.VerifyPasswordAsync("admin", ArcanistFactory.Password, Cancel)));

        var token = Convert.ToHexStringLower(MD5.HashData(Encoding.UTF8.GetBytes(secret + "c19b2d")));
        Assert.NotNull(await WithServiceAsync(s => s.VerifyTokenAsync("admin", token, "c19b2d", Cancel)));
        Assert.Null(await WithServiceAsync(s => s.VerifyTokenAsync("admin", token, "other", Cancel)));
        Assert.NotNull(await WithServiceAsync(s => s.VerifyApiKeyAsync(secret, Cancel)));

        // Used: the list says when.
        listed = Assert.Single((await _admin.GetFromJsonAsync<List<JsonElement>>("/rest/v1/me/app-passwords", ArcanistFactory.Json, Cancel))!);
        Assert.NotEqual(JsonValueKind.Null, listed.GetProperty("lastUsedAt").ValueKind);
    }

    [Fact]
    public async Task Revoked_passwords_stop_working_and_guessing_locks_them_out()
    {
        var (id, secret) = await CreateAsync();
        for (var i = 0; i < 10; i++)
            await WithServiceAsync(s => s.VerifyPasswordAsync("admin", "wrong", Cancel));
        Assert.Null(await WithServiceAsync(s => s.VerifyPasswordAsync("admin", secret, Cancel)));

        // The main sign-in is not affected by guesses at app passwords.
        await _factory.LoginAsync();

        _time.Advance(TimeSpan.FromMinutes(20));
        Assert.NotNull(await WithServiceAsync(s => s.VerifyPasswordAsync("admin", secret, Cancel)));

        var admin = await _factory.CreateAuthenticatedClientAsync();
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/rest/v1/me/app-passwords/{id}", Cancel)).StatusCode);
        Assert.Null(await WithServiceAsync(s => s.VerifyApiKeyAsync(secret, Cancel)));
    }

    [Fact]
    public async Task Creating_one_needs_a_recent_password()
    {
        _time.Advance(TimeSpan.FromMinutes(11));
        var response = await _admin.PostAsJsonAsync("/rest/v1/me/app-passwords", new { name = "Late" }, ArcanistFactory.Json, Cancel);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();
}
