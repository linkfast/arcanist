using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Arcanist.Core.Auth;
using Arcanist.Core.Auth.SecondFactor;
using Arcanist.Core.Data;
using Arcanist.Core.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Arcanist.Core.Tests.Auth;

/// <summary>Signing in with a code from an app, a recovery code or a passkey, and pairing them.</summary>
public sealed class SecondFactorTests : IAsyncLifetime
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private readonly FakeTimeProvider _time = new(DateTimeOffset.UtcNow);
    private ArcanistFactory _factory = null!;

    public async ValueTask InitializeAsync()
    {
        _factory = new ArcanistFactory(time: _time);
        await _factory.CompleteSetupAsync();
    }

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();

    private HttpClient Anonymous() => _factory.CreateClient();

    private static async Task<(HttpStatusCode Status, JsonElement Body)> PostAsync(HttpClient client, string path, object? body = null)
    {
        var response = await client.PostAsJsonAsync(path, body ?? new { }, ArcanistFactory.Json, Cancel);
        var text = await response.Content.ReadAsStringAsync(Cancel);
        return (response.StatusCode, text.Length == 0 ? default : JsonDocument.Parse(text).RootElement.Clone());
    }

    private Task<(HttpStatusCode Status, JsonElement Body)> LoginAsync(string userName = ArcanistFactory.AdminUserName) =>
        PostAsync(Anonymous(), "/rest/v1/auth/login", new { userName, password = ArcanistFactory.Password });

    private static string Code(JsonElement body) => body.GetProperty("code").GetString()!;

    private static byte[] Decode(string base32)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var bytes = new List<byte>();
        int buffer = 0, bits = 0;
        foreach (var c in base32)
        {
            buffer = (buffer << 5) | alphabet.IndexOf(c);
            bits += 5;
            if (bits >= 8)
            {
                bytes.Add((byte)(buffer >> (bits - 8)));
                bits -= 8;
            }
        }

        return bytes.ToArray();
    }

    private string CodeNow(byte[] secret) => Totp.Code(secret, Totp.StepAt(_time.GetUtcNow()));

    /// <summary>A code the app would show later: the last one used never works again.</summary>
    private string NextCode(byte[] secret)
    {
        _time.Advance(TimeSpan.FromSeconds(Totp.Period * 2));
        return CodeNow(secret);
    }

    private async Task<(HttpClient Client, byte[] Secret, List<string> RecoveryCodes)> PairAppAsync(string userName = ArcanistFactory.AdminUserName)
    {
        var client = await _factory.CreateAuthenticatedClientAsync(userName);
        var (status, setup) = await PostAsync(client, "/rest/v1/me/second-factor/totp");
        Assert.Equal(HttpStatusCode.OK, status);
        var secret = Decode(setup.GetProperty("secret").GetString()!);
        Assert.StartsWith("otpauth://totp/Arcanist:", setup.GetProperty("uri").GetString());

        var (confirmed, codes) = await PostAsync(client, "/rest/v1/me/second-factor/totp/confirm", new { code = CodeNow(secret) });
        Assert.Equal(HttpStatusCode.OK, confirmed);
        return (client, secret, codes.GetProperty("codes").EnumerateArray().Select(c => c.GetString()!).ToList());
    }

    [Theory]
    [InlineData(59, "94287082")]
    [InlineData(1111111109, "07081804")]
    [InlineData(1234567890, "89005924")]
    [InlineData(20000000000, "65353130")]
    public void Codes_are_the_ones_of_RFC_6238(long time, string expected) =>
        Assert.Equal(expected, Totp.Code(Encoding.ASCII.GetBytes("12345678901234567890"), time / Totp.Period, 8));

    [Fact]
    public void A_secret_is_written_in_base32_without_padding() =>
        Assert.Equal("MZXW6YTBOI", Totp.Base32("foobar"u8));

    [Fact]
    public async Task A_user_without_a_factor_signs_in_as_always()
    {
        var (status, body) = await LoginAsync();
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.False(body.GetProperty("user").GetProperty("hasSecondFactor").GetBoolean());
    }

    [Fact]
    public async Task With_the_app_the_password_alone_is_not_enough_and_a_code_never_works_twice()
    {
        var (_, secret, codes) = await PairAppAsync();
        Assert.Equal(10, codes.Count);
        Assert.All(codes, code => Assert.Matches("^[a-z2-9]{4}-[a-z2-9]{4}-[a-z2-9]{4}$", code));

        var (status, body) = await LoginAsync();
        Assert.Equal(HttpStatusCode.Unauthorized, status);
        Assert.Equal("second_factor_required", Code(body));
        Assert.Equal(["TOTP", "RECOVERY_CODE"], body.GetProperty("methods").EnumerateArray().Select(m => m.GetString()));
        var challenge = body.GetProperty("challenge").GetString();

        var wrong = await PostAsync(Anonymous(), "/rest/v1/auth/login/second-factor", new { challenge, code = "000000" });
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.Status);
        Assert.Equal("invalid_second_factor", Code(wrong.Body));

        var code = NextCode(secret);
        var (signedIn, tokens) = await PostAsync(Anonymous(), "/rest/v1/auth/login/second-factor", new { challenge = (await LoginAsync()).Body.GetProperty("challenge").GetString(), code });
        Assert.Equal(HttpStatusCode.OK, signedIn);
        Assert.True(tokens.GetProperty("user").GetProperty("hasSecondFactor").GetBoolean());

        var again = await PostAsync(Anonymous(), "/rest/v1/auth/login/second-factor", new { challenge = (await LoginAsync()).Body.GetProperty("challenge").GetString(), code });
        Assert.Equal("invalid_second_factor", Code(again.Body));
    }

    [Fact]
    public async Task A_recovery_code_works_once_however_it_is_typed()
    {
        var (_, _, codes) = await PairAppAsync();

        var challenge = (await LoginAsync()).Body.GetProperty("challenge").GetString();
        var first = await PostAsync(Anonymous(), "/rest/v1/auth/login/second-factor", new { challenge, code = " " + codes[0].ToUpperInvariant().Replace("-", " ") });
        Assert.Equal(HttpStatusCode.OK, first.Status);

        var second = await PostAsync(Anonymous(), "/rest/v1/auth/login/second-factor", new { challenge, code = codes[0] });
        Assert.Equal("invalid_second_factor", Code(second.Body));
    }

    [Fact]
    public async Task Regenerating_recovery_codes_ends_the_old_ones()
    {
        var (client, _, codes) = await PairAppAsync();
        var (_, renewed) = await PostAsync(client, "/rest/v1/me/second-factor/recovery-codes");
        var fresh = renewed.GetProperty("codes").EnumerateArray().Select(c => c.GetString()!).ToList();
        Assert.Equal(10, fresh.Count);

        var challenge = (await LoginAsync()).Body.GetProperty("challenge").GetString();
        Assert.Equal("invalid_second_factor", Code((await PostAsync(Anonymous(), "/rest/v1/auth/login/second-factor", new { challenge, code = codes[0] })).Body));
        Assert.Equal(HttpStatusCode.OK, (await PostAsync(Anonymous(), "/rest/v1/auth/login/second-factor", new { challenge, code = fresh[0] })).Status);
    }

    [Fact]
    public async Task A_challenge_expires_and_too_many_wrong_codes_lock_it_out()
    {
        await PairAppAsync();

        var challenge = (await LoginAsync()).Body.GetProperty("challenge").GetString();
        _time.Advance(SecondFactorChallenges.Lifetime + TimeSpan.FromSeconds(1));
        Assert.Equal("second_factor_expired", Code((await PostAsync(Anonymous(), "/rest/v1/auth/login/second-factor", new { challenge, code = "000000" })).Body));

        challenge = (await LoginAsync()).Body.GetProperty("challenge").GetString();
        var last = default((HttpStatusCode Status, JsonElement Body));
        for (var i = 0; i < 6; i++)
            last = await PostAsync(Anonymous(), "/rest/v1/auth/login/second-factor", new { challenge, code = "000000" });
        Assert.Equal(HttpStatusCode.TooManyRequests, last.Status);
    }

    [Fact]
    public async Task Pairing_a_factor_signs_the_other_devices_out()
    {
        var other = await _factory.CreateAuthenticatedClientAsync();
        Assert.Equal(HttpStatusCode.OK, (await other.GetAsync("/rest/v1/me", Cancel)).StatusCode);

        var (client, _, _) = await PairAppAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, (await other.GetAsync("/rest/v1/me", Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/rest/v1/me", Cancel)).StatusCode);
    }

    [Fact]
    public async Task A_step_up_asks_for_the_factor_too()
    {
        var (client, secret, _) = await PairAppAsync();

        var (status, body) = await PostAsync(client, "/rest/v1/auth/reauthenticate", new { password = ArcanistFactory.Password });
        Assert.Equal(HttpStatusCode.Unauthorized, status);
        Assert.Equal("second_factor_required", Code(body));
        Assert.Contains("TOTP", body.GetProperty("methods").EnumerateArray().Select(m => m.GetString()));

        var wrongPassword = await PostAsync(client, "/rest/v1/auth/reauthenticate", new { password = "no", code = "000000" });
        Assert.Equal("invalid_credentials", Code(wrongPassword.Body));

        var done = await PostAsync(client, "/rest/v1/auth/reauthenticate", new { password = ArcanistFactory.Password, code = NextCode(secret) });
        Assert.Equal(HttpStatusCode.OK, done.Status);
    }

    [Fact]
    public async Task When_the_server_requires_it_the_user_pairs_a_factor_to_sign_in_and_cannot_remove_the_last()
    {
        await _factory.CreateUserAsync("ana");
        var admin = await _factory.CreateAuthenticatedClientAsync();
        var set = await admin.PutAsJsonAsync("/rest/v1/server/second-factor", new { required = true }, ArcanistFactory.Json, Cancel);
        Assert.Equal(HttpStatusCode.OK, set.StatusCode);
        var policy = await set.Content.ReadFromJsonAsync<JsonElement>(ArcanistFactory.Json, Cancel);
        Assert.Equal(2, policy.GetProperty("usersWithout").GetInt32());

        // Sessions already open go on.
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/rest/v1/me", Cancel)).StatusCode);

        var (status, body) = await LoginAsync("ana");
        Assert.Equal(HttpStatusCode.Unauthorized, status);
        Assert.Equal("second_factor_setup_required", Code(body));
        var challenge = body.GetProperty("challenge").GetString();

        // A challenge to pair one never finishes a sign-in by itself.
        Assert.Equal("second_factor_expired", Code((await PostAsync(Anonymous(), "/rest/v1/auth/login/second-factor", new { challenge, code = "000000" })).Body));

        var (_, setup) = await PostAsync(Anonymous(), "/rest/v1/auth/login/setup/totp", new { challenge });
        var secret = Decode(setup.GetProperty("secret").GetString()!);
        Assert.Equal("invalid_second_factor", Code((await PostAsync(Anonymous(), "/rest/v1/auth/login/setup/totp/confirm", new { challenge, code = "000000" })).Body));

        var (confirmed, tokens) = await PostAsync(Anonymous(), "/rest/v1/auth/login/setup/totp/confirm", new { challenge, code = CodeNow(secret) });
        Assert.Equal(HttpStatusCode.OK, confirmed);
        Assert.Equal(10, tokens.GetProperty("recoveryCodes").GetArrayLength());

        // Once paired, that challenge is spent.
        Assert.Equal("second_factor_expired", Code((await PostAsync(Anonymous(), "/rest/v1/auth/login/setup/totp", new { challenge })).Body));

        var ana = _factory.CreateClient(tokens.GetProperty("accessToken").GetString()!);
        var removed = await ana.DeleteAsync("/rest/v1/me/second-factor/totp", Cancel);
        Assert.Equal(HttpStatusCode.Conflict, removed.StatusCode);
        Assert.Equal("second_factor_last", Code(await removed.Content.ReadFromJsonAsync<JsonElement>(ArcanistFactory.Json, Cancel)));
    }

    [Fact]
    public async Task Removing_the_last_factor_takes_the_recovery_codes_with_it()
    {
        var (client, _, codes) = await PairAppAsync();
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync("/rest/v1/me/second-factor/totp", Cancel)).StatusCode);

        var mine = await client.GetFromJsonAsync<JsonElement>("/rest/v1/me/second-factor", ArcanistFactory.Json, Cancel);
        Assert.False(mine.GetProperty("enabled").GetBoolean());
        Assert.Equal(0, mine.GetProperty("recoveryCodesLeft").GetInt32());
        Assert.Equal(HttpStatusCode.OK, (await LoginAsync()).Status);
    }

    [Fact]
    public async Task An_administrator_resets_who_lost_their_factor_and_they_pair_one_again()
    {
        await _factory.CreateUserAsync("ana");
        var (ana, _, _) = await PairAppAsync("ana");
        var admin = await _factory.CreateAuthenticatedClientAsync();
        var users = await admin.GetFromJsonAsync<List<JsonElement>>("/rest/v1/users", ArcanistFactory.Json, Cancel);
        var record = users!.Single(u => u.GetProperty("userName").GetString() == "ana");
        Assert.True(record.GetProperty("hasSecondFactor").GetBoolean());

        var reset = await admin.DeleteAsync($"/rest/v1/users/{record.GetProperty("id").GetString()}/second-factor", Cancel);
        Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await ana.GetAsync("/rest/v1/me", Cancel)).StatusCode);
        Assert.Equal("second_factor_setup_required", Code((await LoginAsync("ana")).Body));
    }

    [Fact]
    public async Task Who_manages_users_does_not_reset_an_administrator_nor_a_user_their_own()
    {
        await _factory.CreateUserAsync("manager", permissions: Permissions.ManageUsers);
        var manager = await _factory.CreateAuthenticatedClientAsync("manager");
        var admin = await _factory.CreateAuthenticatedClientAsync();
        var me = await admin.GetFromJsonAsync<JsonElement>("/rest/v1/me", ArcanistFactory.Json, Cancel);
        var id = me.GetProperty("id").GetString();

        Assert.Equal(HttpStatusCode.Forbidden, (await manager.DeleteAsync($"/rest/v1/users/{id}/second-factor", Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.DeleteAsync($"/rest/v1/users/{id}/second-factor", Cancel)).StatusCode);
    }

    [Fact]
    public async Task The_mstream_password_sign_in_refuses_who_has_or_needs_a_factor()
    {
        await _factory.CreateUserAsync("ana");
        await using (var scope = _factory.Services.CreateAsyncScope())
            Assert.NotNull((await scope.ServiceProvider.GetRequiredService<AuthService>().VerifyPasswordAsync("ana", ArcanistFactory.Password, Cancel)).UserId);

        await PairAppAsync("ana");

        await using (var scope = _factory.Services.CreateAsyncScope())
            Assert.Equal("second_factor_required", (await scope.ServiceProvider.GetRequiredService<AuthService>().VerifyPasswordAsync("ana", ArcanistFactory.Password, Cancel)).ErrorCode);
    }

    [Fact]
    public async Task Set_by_the_environment_the_panel_cannot_change_it()
    {
        await using var factory = new ArcanistFactory(new Dictionary<string, string?> { [SecondFactorSettings.EnvironmentKey] = "true" }, time: _time);
        await factory.CompleteSetupAsync();

        var (status, body) = await PostAsync(factory.CreateClient(), "/rest/v1/auth/login", new { userName = ArcanistFactory.AdminUserName, password = ArcanistFactory.Password });
        Assert.Equal(HttpStatusCode.Unauthorized, status);
        Assert.Equal("second_factor_setup_required", Code(body));
    }

    [Fact]
    public async Task A_passkey_pairs_and_signs_in_and_a_counter_going_backwards_is_refused()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var authenticator = new SoftAuthenticator();

        var (_, registration) = await PostAsync(client, "/rest/v1/me/second-factor/passkeys/options");
        var attestation = authenticator.Create(registration.GetProperty("options").GetString()!);
        var (registered, passkey) = await PostAsync(client, "/rest/v1/me/second-factor/passkeys", new { name = "Mi llave", attestation });
        Assert.Equal(HttpStatusCode.OK, registered);
        Assert.Equal("Mi llave", passkey.GetProperty("passkey").GetProperty("name").GetString());
        Assert.Equal("localhost", passkey.GetProperty("passkey").GetProperty("rpId").GetString());
        Assert.Equal(10, passkey.GetProperty("recoveryCodes").GetArrayLength());

        var (status, body) = await LoginAsync();
        Assert.Equal("second_factor_required", Code(body));
        Assert.Contains("PASSKEY", body.GetProperty("methods").EnumerateArray().Select(m => m.GetString()));
        Assert.Equal(["localhost"], body.GetProperty("passkeyDomains").EnumerateArray().Select(d => d.GetString()));
        var challenge = body.GetProperty("challenge").GetString();

        var (_, options) = await PostAsync(Anonymous(), "/rest/v1/auth/login/passkey/options", new { challenge });
        authenticator.Counter = 5;
        var assertion = authenticator.Get(options.GetProperty("options").GetString()!);
        var (signedIn, _) = await PostAsync(Anonymous(), "/rest/v1/auth/login/passkey", new { challenge, assertion });
        Assert.Equal(HttpStatusCode.OK, signedIn);

        // Every question is answered once.
        Assert.Equal("second_factor_expired", Code((await PostAsync(Anonymous(), "/rest/v1/auth/login/passkey", new { challenge, assertion })).Body));

        var (_, again) = await PostAsync(Anonymous(), "/rest/v1/auth/login/passkey/options", new { challenge });
        authenticator.Counter = 3;
        var refused = await PostAsync(Anonymous(), "/rest/v1/auth/login/passkey", new { challenge, assertion = authenticator.Get(again.GetProperty("options").GetString()!) });
        Assert.Equal("passkey_failed", Code(refused.Body));

        await using var scope = _factory.Services.CreateAsyncScope();
        var stored = await scope.ServiceProvider.GetRequiredService<ArcanistDbContext>().UserPasskeys.SingleAsync(Cancel);
        Assert.Equal(5, stored.SignCount);
        Assert.NotNull(stored.LastUsedAt);
    }

    [Fact]
    public async Task A_passkey_can_be_the_factor_paired_at_sign_in()
    {
        await _factory.CreateUserAsync("ana");
        var admin = await _factory.CreateAuthenticatedClientAsync();
        await admin.PutAsJsonAsync("/rest/v1/server/second-factor", new { required = true }, ArcanistFactory.Json, Cancel);
        var authenticator = new SoftAuthenticator();

        var challenge = (await LoginAsync("ana")).Body.GetProperty("challenge").GetString();
        var (_, options) = await PostAsync(Anonymous(), "/rest/v1/auth/login/setup/passkey/options", new { challenge });
        var (status, tokens) = await PostAsync(Anonymous(), "/rest/v1/auth/login/setup/passkey",
            new { challenge, name = "", attestation = authenticator.Create(options.GetProperty("options").GetString()!) });

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(10, tokens.GetProperty("recoveryCodes").GetArrayLength());
        Assert.True(tokens.GetProperty("user").GetProperty("hasSecondFactor").GetBoolean());
    }

    [Fact]
    public async Task A_passkey_works_only_on_its_domain()
    {
        await using var factory = new ArcanistFactory(new Dictionary<string, string?> { ["Arcanist:Network:PublicUrl"] = "http://music.test" }, time: _time);
        await factory.CompleteSetupAsync();
        var client = await factory.CreateAuthenticatedClientAsync();
        client.DefaultRequestHeaders.Add("Origin", "http://music.test");
        var authenticator = new SoftAuthenticator("http://music.test");

        var (_, registration) = await PostAsync(client, "/rest/v1/me/second-factor/passkeys/options");
        var (registered, passkey) = await PostAsync(client, "/rest/v1/me/second-factor/passkeys",
            new { name = "Llave", attestation = authenticator.Create(registration.GetProperty("options").GetString()!) });
        Assert.Equal(HttpStatusCode.OK, registered);
        Assert.Equal("music.test", passkey.GetProperty("passkey").GetProperty("rpId").GetString());

        // Opened by another address, it cannot be used.
        var login = await PostAsync(factory.CreateClient(), "/rest/v1/auth/login", new { userName = ArcanistFactory.AdminUserName, password = ArcanistFactory.Password });
        var challenge = login.Body.GetProperty("challenge").GetString();
        var other = await PostAsync(factory.CreateClient(), "/rest/v1/auth/login/passkey/options", new { challenge });
        Assert.Equal("passkey_other_domain", Code(other.Body));

        // Opened by its own, it can.
        var mine = factory.CreateClient();
        mine.DefaultRequestHeaders.Add("Origin", "http://music.test");
        var (_, options) = await PostAsync(mine, "/rest/v1/auth/login/passkey/options", new { challenge });
        authenticator.Counter = 2;
        var (signedIn, _) = await PostAsync(mine, "/rest/v1/auth/login/passkey", new { challenge, assertion = authenticator.Get(options.GetProperty("options").GetString()!) });
        Assert.Equal(HttpStatusCode.OK, signedIn);
    }
}
