using System.Security.Claims;
using Arcanist.Core.Auth;
using Arcanist.Core.Data.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Arcanist.Core.Tests.Auth;

public sealed class AuthorizationHandlerTests
{
    private readonly FakeTimeProvider _time = new(DateTimeOffset.UtcNow);

    [Theory]
    [InlineData(UserRole.Admin, Permissions.None, Permissions.ManageUsers, true)]
    [InlineData(UserRole.User, Permissions.ManageUsers | Permissions.RunScans, Permissions.ManageUsers, true)]
    [InlineData(UserRole.User, Permissions.RunScans, Permissions.ManageUsers, false)]
    [InlineData(UserRole.User, Permissions.None, Permissions.Download, false)]
    public async Task Permissions_come_from_flags_or_the_admin_role(UserRole role, Permissions granted, Permissions required, bool expected)
    {
        var context = await Authorize(Access(role, granted), new PermissionRequirement(required));

        Assert.Equal(expected, context.HasSucceeded);
    }

    [Fact]
    public async Task Admin_requirement_needs_the_admin_role()
    {
        Assert.False((await Authorize(Access(UserRole.User, (Permissions)~0L), new AdminRequirement())).HasSucceeded);
        Assert.True((await Authorize(Access(UserRole.Admin, Permissions.None), new AdminRequirement())).HasSucceeded);
    }

    [Theory]
    [InlineData(5, true)]
    [InlineData(11, false)]
    public async Task Recent_authentication_expires_after_the_step_up_window(int minutesAgo, bool expected)
    {
        var authenticatedAt = _time.GetUtcNow().AddMinutes(-minutesAgo).ToUnixTimeSeconds();

        var context = await Authorize(Access(UserRole.Admin, Permissions.None), new RecentAuthenticationRequirement(), authenticatedAt);

        Assert.Equal(expected, context.HasSucceeded);
    }

    [Fact]
    public void Library_access_is_explicit_for_users_and_implicit_for_admins()
    {
        var library = Guid.NewGuid();

        Assert.True(Access(UserRole.Admin, Permissions.None).CanAccessLibrary(library));
        Assert.False(Access(UserRole.User, Permissions.None).CanAccessLibrary(library));
        Assert.True(Access(UserRole.User, Permissions.None, library).CanAccessLibrary(library));
    }

    private async Task<AuthorizationHandlerContext> Authorize(UserAccess access, IAuthorizationRequirement requirement, long? authenticatedAt = null)
    {
        var claims = authenticatedAt is null ? [] : new[] { new Claim(ArcanistClaims.AuthenticatedAt, authenticatedAt.Value.ToString()) };
        var context = new AuthorizationHandlerContext([requirement], new ClaimsPrincipal(new ClaimsIdentity(claims, "test")), null);
        var handler = new ArcanistAuthorizationHandler(new FixedUser(access), Options.Create(new AuthOptions()), _time);

        await handler.HandleAsync(context);
        return context;
    }

    private static UserAccess Access(UserRole role, Permissions permissions, params Guid[] libraries) =>
        new(Guid.NewGuid(), "user", null, role, permissions, "stamp", false, libraries.ToHashSet());

    private sealed class FixedUser(UserAccess access) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public UserAccess Access => access;
        public Guid SessionId => Guid.Empty;
        public DateTimeOffset? AuthenticatedAt => null;
    }
}
