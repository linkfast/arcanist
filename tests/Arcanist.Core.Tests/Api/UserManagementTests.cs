using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Arcanist.Core.Api.Rest;
using Arcanist.Core.Application.Users;
using Arcanist.Core.Data;
using Arcanist.Core.Data.Entities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Arcanist.Core.Tests.Api;

public sealed class UserManagementTests : IAsyncLifetime
{
    private readonly FakeTimeProvider _time = new(DateTimeOffset.UtcNow);
    private readonly ArcanistFactory _factory;
    private HttpClient _admin = null!;

    public UserManagementTests() => _factory = new ArcanistFactory(time: _time);

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        await _factory.CompleteSetupAsync();
        _admin = await _factory.CreateAuthenticatedClientAsync();
    }

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    [Fact]
    public async Task Server_info_is_public_on_both_apis()
    {
        var anonymous = _factory.CreateClient();

        var rest = await anonymous.GetFromJsonAsync<JsonElement>("/rest/v1/server-info", Cancel);
        var graphql = await anonymous.GraphQLAsync("{ serverInfo { name apiVersion setupCompleted capabilities cloudTier } }");

        Assert.Equal("Arcanist", rest.GetProperty("name").GetString());
        Assert.Equal("NONE", rest.GetProperty("cloudTier").GetString());
        Assert.Equal("https://github.com/linkfast/arcanist", rest.GetProperty("sourceUrl").GetString());
        Assert.Equal(1, graphql.GetProperty("data").GetProperty("serverInfo").GetProperty("apiVersion").GetInt32());
        Assert.Equal("NONE", graphql.GetProperty("data").GetProperty("serverInfo").GetProperty("cloudTier").GetString());
    }

    [Fact]
    public async Task Me_requires_authentication_on_both_apis()
    {
        var anonymous = _factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/rest/v1/me", Cancel)).StatusCode);
        Assert.Equal("unauthorized", (await anonymous.GraphQLAsync("{ me { id } }")).ErrorCode());

        var me = await _admin.GraphQLAsync("{ me { userName role permissions } }");
        Assert.Equal("admin", me.GetProperty("data").GetProperty("me").GetProperty("userName").GetString());
        Assert.Equal("ADMIN", me.GetProperty("data").GetProperty("me").GetProperty("role").GetString());
    }

    [Fact]
    public async Task Admin_creates_lists_updates_and_deletes_users()
    {
        var created = await CreateUserAsync(new CreateUserInput("Alice", ArcanistFactory.Password, "Alice A.", UserRole.User,
            [Permission.Download], null));

        Assert.Equal([Permission.Download], created.Permissions);

        var listed = await _admin.GraphQLAsync("{ users { userName permissions } }");
        Assert.Contains(listed.GetProperty("data").GetProperty("users").EnumerateArray(),
            u => u.GetProperty("userName").GetString() == "Alice");

        var updated = await _admin.PutAsJsonAsync($"/rest/v1/users/{created.Id}",
            new UpdateUserInput("Alice", UserRole.User, [Permission.Download, Permission.RunScans], IsDisabled: false), ArcanistFactory.Json, Cancel);
        var account = (await updated.Content.ReadFromJsonAsync<UserAccount>(ArcanistFactory.Json, Cancel))!;
        Assert.Equal([Permission.RunScans, Permission.Download], account.Permissions);

        var deleted = await _admin.GraphQLAsync("mutation($id: ID!) { deleteUser(id: $id) }", new { id = created.Id });
        Assert.Null(deleted.ErrorCode());
        Assert.Equal(HttpStatusCode.NotFound, (await _admin.GetAsync($"/rest/v1/users/{created.Id}", Cancel)).StatusCode);
    }

    [Fact]
    public async Task Validation_errors_are_reported_per_field_on_both_apis()
    {
        await CreateUserAsync(new CreateUserInput("alice", ArcanistFactory.Password, null, UserRole.User, null, null));

        var rest = await _admin.PostAsJsonAsync("/rest/v1/users",
            new CreateUserInput("ALICE", ArcanistFactory.Password, null, UserRole.User, null, null), ArcanistFactory.Json, Cancel);
        Assert.Equal(HttpStatusCode.BadRequest, rest.StatusCode);
        var problem = await rest.Content.ReadFromJsonAsync<JsonElement>(Cancel);
        Assert.Equal("validation_failed", problem.GetProperty("code").GetString());
        Assert.Equal("duplicate", problem.GetProperty("errors").GetProperty("userName")[0].GetString());

        var graphql = await _admin.GraphQLAsync("""
            mutation { createUser(input: { userName: "x", password: "short", role: USER }) { id } }
            """);
        Assert.Equal("validation_failed", graphql.ErrorCode());
        var errors = graphql.GetProperty("errors")[0].GetProperty("extensions").GetProperty("errors");
        Assert.Equal("too_short", errors.GetProperty("userName")[0].GetString());
        Assert.Equal("too_short", errors.GetProperty("password")[0].GetString());
    }

    [Fact]
    public async Task Sensitive_changes_require_a_recent_authentication()
    {
        _time.Advance(TimeSpan.FromMinutes(11));
        var tokens = await _factory.LoginAsync(); // a fresh login restores it
        _time.Advance(TimeSpan.FromMinutes(11));
        var stale = _factory.CreateClient((await RefreshAsync(tokens.RefreshToken!)).AccessToken);

        var input = new CreateUserInput("bob", ArcanistFactory.Password, null, UserRole.User, null, null);
        var rest = await stale.PostAsJsonAsync("/rest/v1/users", input, ArcanistFactory.Json, Cancel);
        Assert.Equal(HttpStatusCode.Forbidden, rest.StatusCode);
        Assert.Equal("reauthentication_required", (await rest.Content.ReadFromJsonAsync<JsonElement>(Cancel)).GetProperty("code").GetString());

        var graphql = await stale.GraphQLAsync("""mutation { createUser(input: { userName: "bob", password: "correct horse battery", role: USER }) { id } }""");
        Assert.Equal("reauthentication_required", graphql.ErrorCode());

        var reauthenticated = await stale.PostAsJsonAsync("/rest/v1/auth/reauthenticate", new ReauthenticateRequest(ArcanistFactory.Password), Cancel);
        var fresh = _factory.CreateClient((await reauthenticated.Content.ReadFromJsonAsync<TokenResponse>(ArcanistFactory.Json, Cancel))!.AccessToken);
        Assert.Equal(HttpStatusCode.Created, (await fresh.PostAsJsonAsync("/rest/v1/users", input, ArcanistFactory.Json, Cancel)).StatusCode);
    }

    [Fact]
    public async Task Users_without_the_permission_cannot_manage_users()
    {
        await _factory.CreateUserAsync("listener");
        var listener = await _factory.CreateAuthenticatedClientAsync("listener");

        Assert.Equal(HttpStatusCode.Forbidden, (await listener.GetAsync("/rest/v1/users", Cancel)).StatusCode);
        Assert.Equal("forbidden", (await listener.GraphQLAsync("{ users { id } }")).ErrorCode());
    }

    [Fact]
    public async Task User_managers_cannot_escalate_privileges()
    {
        await _factory.CreateUserAsync("manager", permissions: Permissions.ManageUsers | Permissions.Download);
        var manager = await _factory.CreateAuthenticatedClientAsync("manager");

        async Task<HttpStatusCode> Create(string name, UserRole role, params Permission[] permissions) =>
            (await manager.PostAsJsonAsync("/rest/v1/users",
                new CreateUserInput(name, ArcanistFactory.Password, null, role, permissions, null), ArcanistFactory.Json, Cancel)).StatusCode;

        Assert.Equal(HttpStatusCode.Forbidden, await Create("boss", UserRole.Admin));
        Assert.Equal(HttpStatusCode.Forbidden, await Create("scanner", UserRole.User, Permission.RunScans));
        Assert.Equal(HttpStatusCode.Created, await Create("helper", UserRole.User, Permission.Download, Permission.ManageUsers));

        var admin = (await _admin.GetFromJsonAsync<UserAccount>("/rest/v1/me", ArcanistFactory.Json, Cancel))!;
        var resetAdmin = await manager.PostAsJsonAsync($"/rest/v1/users/{admin.Id}/password",
            new ResetUserPasswordInput("taken over now"), ArcanistFactory.Json, Cancel);
        Assert.Equal(HttpStatusCode.Forbidden, resetAdmin.StatusCode);
    }

    [Fact]
    public async Task The_last_administrator_cannot_be_demoted_disabled_or_deleted()
    {
        var admin = (await _admin.GetFromJsonAsync<UserAccount>("/rest/v1/me", ArcanistFactory.Json, Cancel))!;

        var demote = await _admin.PutAsJsonAsync($"/rest/v1/users/{admin.Id}",
            new UpdateUserInput(null, UserRole.User, null, IsDisabled: false), ArcanistFactory.Json, Cancel);
        Assert.Equal(HttpStatusCode.Conflict, demote.StatusCode);
        Assert.Equal("last_administrator", (await demote.Content.ReadFromJsonAsync<JsonElement>(Cancel)).GetProperty("code").GetString());

        var deleteSelf = await _admin.DeleteAsync($"/rest/v1/users/{admin.Id}", Cancel);
        Assert.Equal(HttpStatusCode.Conflict, deleteSelf.StatusCode);

        // With a second administrator the first one can step down.
        await CreateUserAsync(new CreateUserInput("admin2", ArcanistFactory.Password, null, UserRole.Admin, null, null));
        var stepDown = await _admin.PutAsJsonAsync($"/rest/v1/users/{admin.Id}",
            new UpdateUserInput(null, UserRole.User, null, IsDisabled: false), ArcanistFactory.Json, Cancel);
        Assert.Equal(HttpStatusCode.OK, stepDown.StatusCode);
    }

    [Fact]
    public async Task Changing_privileges_invalidates_the_users_access_tokens()
    {
        var user = await _factory.CreateUserAsync("carol");
        var carol = await _factory.CreateAuthenticatedClientAsync("carol");
        Assert.Equal(HttpStatusCode.OK, (await carol.GetAsync("/rest/v1/me", Cancel)).StatusCode);

        await _admin.PutAsJsonAsync($"/rest/v1/users/{user.Id}",
            new UpdateUserInput(null, UserRole.User, [Permission.Download], IsDisabled: false), ArcanistFactory.Json, Cancel);

        Assert.Equal(HttpStatusCode.Unauthorized, (await carol.GetAsync("/rest/v1/me", Cancel)).StatusCode);
    }

    [Fact]
    public async Task Library_access_is_granted_per_user()
    {
        var music = await AddLibraryAsync("Music");
        var audiobooks = await AddLibraryAsync("Audiobooks");
        var user = await _factory.CreateUserAsync("dave");

        var response = await _admin.PutAsJsonAsync($"/rest/v1/users/{user.Id}/libraries",
            new SetUserLibrariesInput([music, audiobooks]), ArcanistFactory.Json, Cancel);
        var account = (await response.Content.ReadFromJsonAsync<UserAccount>(ArcanistFactory.Json, Cancel))!;
        Assert.Equal(new[] { music, audiobooks }.Order(), account.LibraryIds);

        var unknown = await _admin.PutAsJsonAsync($"/rest/v1/users/{user.Id}/libraries",
            new SetUserLibrariesInput([Guid.NewGuid()]), ArcanistFactory.Json, Cancel);
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);

        var narrowed = await _admin.GraphQLAsync(
            "mutation($id: ID!, $libs: [ID!]!) { setUserLibraries(id: $id, input: { libraryIds: $libs }) { libraryIds } }",
            new { id = user.Id, libs = new[] { audiobooks } });
        Assert.Equal(audiobooks.ToString(),
            narrowed.GetProperty("data").GetProperty("setUserLibraries").GetProperty("libraryIds")[0].GetString());
    }

    [Fact]
    public async Task Resetting_a_password_signs_the_user_out_everywhere()
    {
        var user = await _factory.CreateUserAsync("erin");
        var tokens = await _factory.LoginAsync("erin");

        var reset = await _admin.PostAsJsonAsync($"/rest/v1/users/{user.Id}/password",
            new ResetUserPasswordInput("a brand new password"), ArcanistFactory.Json, Cancel);
        Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await _factory.CreateClient(tokens.AccessToken).GetAsync("/rest/v1/me", Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _factory.CreateClient().PostAsJsonAsync("/rest/v1/auth/refresh",
            new RefreshRequest(tokens.RefreshToken), Cancel)).StatusCode);
        await _factory.LoginAsync("erin", "a brand new password");
    }

    [Fact]
    public async Task Changing_my_password_keeps_this_device_and_signs_out_the_others()
    {
        await _factory.CreateUserAsync("frank");
        var other = await _factory.LoginAsync("frank");
        var current = await _factory.LoginAsync("frank");
        var client = _factory.CreateClient(current.AccessToken);

        var wrong = await client.PostAsJsonAsync("/rest/v1/me/password", new ChangeMyPasswordInput("nope", "another password"), Cancel);
        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);

        var changed = await client.PostAsJsonAsync("/rest/v1/me/password",
            new ChangeMyPasswordInput(ArcanistFactory.Password, "another password"), Cancel);
        Assert.Equal(HttpStatusCode.NoContent, changed.StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/rest/v1/me", Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _factory.CreateClient(other.AccessToken).GetAsync("/rest/v1/me", Cancel)).StatusCode);
    }

    [Fact]
    public async Task Users_see_and_revoke_their_own_sessions()
    {
        await _factory.CreateUserAsync("gina");
        var phone = await _factory.LoginAsync("gina");
        var laptop = _factory.CreateClient((await _factory.LoginAsync("gina")).AccessToken);

        var sessions = (await laptop.GetFromJsonAsync<List<Session>>("/rest/v1/me/sessions", ArcanistFactory.Json, Cancel))!;
        Assert.Equal(2, sessions.Count);
        var phoneSession = Assert.Single(sessions, s => !s.IsCurrent);

        var revoked = await laptop.GraphQLAsync("mutation($id: ID!) { revokeMySession(id: $id) }", new { id = phoneSession.Id });
        Assert.Null(revoked.ErrorCode());
        Assert.Equal(HttpStatusCode.Unauthorized, (await _factory.CreateClient(phone.AccessToken).GetAsync("/rest/v1/me", Cancel)).StatusCode);

        // Someone else's session is indistinguishable from a missing one.
        var adminSession = (await _admin.GetFromJsonAsync<List<Session>>("/rest/v1/me/sessions", ArcanistFactory.Json, Cancel))![0];
        Assert.Equal(HttpStatusCode.NotFound, (await laptop.DeleteAsync($"/rest/v1/me/sessions/{adminSession.Id}", Cancel)).StatusCode);
    }

    private async Task<UserAccount> CreateUserAsync(CreateUserInput input)
    {
        var response = await _admin.PostAsJsonAsync("/rest/v1/users", input, ArcanistFactory.Json, Cancel);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<UserAccount>(ArcanistFactory.Json, Cancel))!;
    }

    private async Task<TokenResponse> RefreshAsync(string refreshToken)
    {
        var response = await _factory.CreateClient().PostAsJsonAsync("/rest/v1/auth/refresh", new RefreshRequest(refreshToken), Cancel);
        return (await response.Content.ReadFromJsonAsync<TokenResponse>(ArcanistFactory.Json, Cancel))!;
    }

    private async Task<Guid> AddLibraryAsync(string name)
    {
        await using var db = _factory.Services.GetRequiredService<IArcanistDbContextFactory>().CreateDbContext();
        var library = new Library { Name = name };
        db.Libraries.Add(library);
        await db.SaveChangesAsync(Cancel);
        return library.Id;
    }
}
