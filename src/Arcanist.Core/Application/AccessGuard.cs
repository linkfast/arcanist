using Arcanist.Core.Auth;
using Arcanist.Core.Data.Entities;
using Microsoft.Extensions.Options;

namespace Arcanist.Core.Application;

/// <summary>
/// Authorization checks for application services. Every operation exposed by an API starts with one of these,
/// so GraphQL, REST and future extensions enforce exactly the same rules.
/// </summary>
public sealed class AccessGuard(ICurrentUser currentUser, IOptions<AuthOptions> options, TimeProvider time)
{
    public UserAccess Authenticated() =>
        currentUser.IsAuthenticated ? currentUser.Access : throw new ArcanistException.Unauthorized();

    public UserAccess Permission(Permissions permission)
    {
        var access = Authenticated();
        return access.Has(permission) ? access : throw new ArcanistException.Forbidden();
    }

    public UserAccess Admin()
    {
        var access = Authenticated();
        return access.IsAdmin ? access : throw new ArcanistException.Forbidden();
    }

    /// <summary>For sensitive changes: the password must have been entered recently on this session.</summary>
    public void RecentAuthentication()
    {
        Authenticated();
        if (currentUser.AuthenticatedAt is not { } authenticatedAt
            || time.GetUtcNow() - authenticatedAt > options.Value.StepUpWindow)
        {
            throw new ArcanistException.Forbidden(ErrorCodes.ReauthenticationRequired);
        }
    }

    public Guid SessionId => currentUser.SessionId;
}
