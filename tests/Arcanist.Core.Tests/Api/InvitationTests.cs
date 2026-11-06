using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Arcanist.Core.Data.Entities;
using Arcanist.Core.Setup;

namespace Arcanist.Core.Tests.Api;

/// <summary>Invitations: a link with the server's public URL for Ultimate apps, and accounts that wait for approval.</summary>
public sealed class InvitationTests : IAsyncLifetime
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private readonly string _root = Directory.CreateTempSubdirectory("arcanist-invitations-").FullName;
    private readonly ArcanistFactory _factory = new();
    private HttpClient _admin = null!;
    private Guid _libraryId;

    public async ValueTask InitializeAsync()
    {
        Directory.CreateDirectory(Path.Combine(_root, "Private"));
        await _factory.CompleteSetupAsync(new SetupLibrary("Private", [Path.Combine(_root, "Private")], VisibleToAll: false));
        _admin = await _factory.CreateAuthenticatedClientAsync();
        _libraryId = (await _admin.GetFromJsonAsync<JsonElement>("/rest/v1/libraries", ArcanistFactory.Json, Cancel))
            .EnumerateArray().Single().GetProperty("id").GetGuid();
        await _factory.CreateUserAsync("host", permissions: Permissions.Invite);
        await _factory.CreateUserAsync("plain");
    }

    public async ValueTask DisposeAsync()
    {
        await _factory.DisposeAsync();
        Directory.Delete(_root, recursive: true);
    }

    private static async Task<JsonElement> OkAsync(Task<HttpResponseMessage> request)
    {
        var response = await request;
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode}: {await response.Content.ReadAsStringAsync(Cancel)}");
        return await response.Content.ReadFromJsonAsync<JsonElement>(ArcanistFactory.Json, Cancel);
    }

    private static async Task<(HttpStatusCode, string?)> FailAsync(Task<HttpResponseMessage> request)
    {
        var response = await request;
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ArcanistFactory.Json, Cancel);
        return (response.StatusCode, body.TryGetProperty("code", out var code) ? code.GetString() : null);
    }

    private Task<JsonElement> SetSharingAsync(string? publicUrl, bool? signup) =>
        OkAsync(_admin.PutAsJsonAsync("/rest/v1/sharing",
            new { publicLinks = false, jamGuests = false, guestAddLimit = 5, maxGuests = 100, publicUrl, invitationSignup = signup }, ArcanistFactory.Json, Cancel));

    private Task<HttpResponseMessage> CheckAsync(HttpClient client, string code) =>
        client.PostAsJsonAsync("/rest/v1/public/invitations/check", new { code }, ArcanistFactory.Json, Cancel);

    private Task<HttpResponseMessage> SignUpAsync(HttpClient client, string code, string userName) =>
        client.PostAsJsonAsync("/rest/v1/public/invitations/signup",
            new { code, userName, password = ArcanistFactory.Password, displayName = "Ana" }, ArcanistFactory.Json, Cancel);

    [Fact]
    public async Task An_invitation_adds_the_server_and_its_account_waits_for_an_administrator()
    {
        var host = await _factory.CreateAuthenticatedClientAsync("host");
        var plain = await _factory.CreateAuthenticatedClientAsync("plain");

        // Without the server's public URL there is nowhere to send people; without INVITE, nothing to make.
        Assert.Equal((HttpStatusCode.Conflict, "public_url_required"), await FailAsync(host.PostAsJsonAsync("/rest/v1/invitations", new { }, ArcanistFactory.Json, Cancel)));
        Assert.Equal(HttpStatusCode.Forbidden, (await plain.PostAsJsonAsync("/rest/v1/invitations", new { }, ArcanistFactory.Json, Cancel)).StatusCode);

        await SetSharingAsync("music.example.com", null);
        var status = await OkAsync(host.GetAsync("/rest/v1/sharing", Cancel));
        Assert.True(status.GetProperty("canInvite").GetBoolean());
        Assert.False(status.GetProperty("invitationSignup").GetBoolean());

        var made = await OkAsync(host.PostAsJsonAsync("/rest/v1/invitations", new { note = "Ana" }, ArcanistFactory.Json, Cancel));
        var code = made.GetProperty("code").GetString()!;
        Assert.Matches("^[A-Z2-9]{4}-[A-Z2-9]{4}-[A-Z2-9]{4}$", code);
        Assert.Equal("https://music.example.com", made.GetProperty("serverUrl").GetString());
        Assert.Equal($"https://music.example.com/admin/join#{code}", made.GetProperty("url").GetString());
        Assert.False(made.GetProperty("signupAllowed").GetBoolean());

        // Anyone with it, without an account, learns where the server is; typed in lower case and without dashes too.
        var anonymous = _factory.CreateClient();
        var preview = await OkAsync(CheckAsync(anonymous, code.Replace("-", "").ToLowerInvariant()));
        Assert.Equal(("https://music.example.com", "host", false),
            (preview.GetProperty("serverUrl").GetString(), preview.GetProperty("invitedBy").GetString(), preview.GetProperty("signupAllowed").GetBoolean()));

        // Accounts only when the server allows them.
        Assert.Equal((HttpStatusCode.Conflict, "invitation_signup_disabled"), await FailAsync(SignUpAsync(anonymous, code, "ana")));
        await SetSharingAsync(null, true);
        var signedUp = await OkAsync(SignUpAsync(anonymous, code, "ana"));
        Assert.True(signedUp.GetProperty("pendingApproval").GetBoolean());

        // Used once: it does not work again. The account waits, and says so only to who knows its password.
        Assert.Equal((HttpStatusCode.Forbidden, "invitation_invalid"), await FailAsync(CheckAsync(anonymous, code)));
        Assert.Equal("account_pending_approval", (await FailAsync(anonymous.PostAsJsonAsync("/rest/v1/auth/login",
            new { userName = "ana", password = ArcanistFactory.Password }, ArcanistFactory.Json, Cancel))).Item2);

        var mine = await OkAsync(host.GetAsync("/rest/v1/invitations", Cancel));
        Assert.Equal(("USED", "ana", "Ana"), (mine[0].GetProperty("state").GetString(), mine[0].GetProperty("usedBy").GetString(), mine[0].GetProperty("note").GetString()));

        // An administrator lets it in with what it gets.
        var ana = (await OkAsync(_admin.GetAsync("/rest/v1/users", Cancel))).EnumerateArray().Single(u => u.GetProperty("userName").GetString() == "ana");
        Assert.True(ana.GetProperty("pendingApproval").GetBoolean());
        Assert.True(ana.GetProperty("isDisabled").GetBoolean());
        var approved = await OkAsync(_admin.PostAsJsonAsync($"/rest/v1/users/{ana.GetProperty("id")}/approve",
            new { permissions = new[] { "DOWNLOAD" }, libraryIds = new[] { _libraryId } }, ArcanistFactory.Json, Cancel));
        Assert.False(approved.GetProperty("pendingApproval").GetBoolean());
        Assert.Equal(["DOWNLOAD"], approved.GetProperty("permissions").EnumerateArray().Select(p => p.GetString()));
        Assert.Equal((HttpStatusCode.Conflict, "not_pending_approval"), await FailAsync(_admin.PostAsJsonAsync($"/rest/v1/users/{ana.GetProperty("id")}/approve",
            new { }, ArcanistFactory.Json, Cancel)));
        var signedIn = await _factory.CreateAuthenticatedClientAsync("ana");
        Assert.Contains((await OkAsync(signedIn.GetAsync("/rest/v1/libraries", Cancel))).EnumerateArray(), l => l.GetProperty("id").GetGuid() == _libraryId);
    }

    [Fact]
    public async Task Revoked_invitations_and_those_of_who_can_no_longer_invite_do_not_work()
    {
        await SetSharingAsync("https://music.example.com/arcanist", true);
        var host = await _factory.CreateAuthenticatedClientAsync("host");
        var anonymous = _factory.CreateClient();

        var revoked = await OkAsync(host.PostAsJsonAsync("/rest/v1/invitations", new { }, ArcanistFactory.Json, Cancel));
        Assert.StartsWith("https://music.example.com/arcanist/admin/join#", revoked.GetProperty("url").GetString());
        await OkAsync(host.PostAsync($"/rest/v1/invitations/{revoked.GetProperty("id")}/revoke", null, Cancel));
        Assert.Equal((HttpStatusCode.Forbidden, "invitation_invalid"), await FailAsync(CheckAsync(anonymous, revoked.GetProperty("code").GetString()!)));

        // Someone else's invitation is not theirs to revoke, but whoever manages users sees and revokes all.
        var other = await OkAsync(host.PostAsJsonAsync("/rest/v1/invitations", new { }, ArcanistFactory.Json, Cancel));
        var plain = await _factory.CreateAuthenticatedClientAsync("plain");
        Assert.Equal(HttpStatusCode.NotFound, (await plain.PostAsync($"/rest/v1/invitations/{other.GetProperty("id")}/revoke", null, Cancel)).StatusCode);
        Assert.Equal(2, (await OkAsync(_admin.GetAsync("/rest/v1/invitations?all=true", Cancel))).GetArrayLength());

        // The inviter loses INVITE: what they made stops working.
        var hostUser = (await OkAsync(_admin.GetAsync("/rest/v1/users", Cancel))).EnumerateArray().Single(u => u.GetProperty("userName").GetString() == "host");
        await OkAsync(_admin.PutAsJsonAsync($"/rest/v1/users/{hostUser.GetProperty("id")}",
            new { displayName = (string?)null, role = "USER", permissions = Array.Empty<string>(), isDisabled = false }, ArcanistFactory.Json, Cancel));
        Assert.Equal((HttpStatusCode.Forbidden, "invitation_invalid"), await FailAsync(SignUpAsync(anonymous, other.GetProperty("code").GetString()!, "luis")));
    }
}
