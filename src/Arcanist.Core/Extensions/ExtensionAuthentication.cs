using System.Security.Claims;
using Arcanist.Core.Auth;

namespace Arcanist.Core.Extensions;

/// <summary>
/// For extensions that check credentials of their own (a Subsonic app password, an mStream token): makes the rest of
/// the request act as that user, so the application services apply their permissions and library access. There is
/// no Arcanist session behind such a request: nothing that needs one (step-up, revoking it) applies.
/// </summary>
public static class ExtensionAuthentication
{
    /// <returns>False when the user does not exist or is disabled.</returns>
    public static async Task<bool> SignInAsync(HttpContext context, Guid userId, string scheme, CancellationToken cancellationToken)
    {
        var access = await context.RequestServices.GetRequiredService<UserAccessCache>().GetUserAsync(userId, cancellationToken);
        if (access is null || access.IsDisabled)
            return false;

        context.User = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ArcanistClaims.UserId, userId.ToString()),
            new Claim(ArcanistClaims.SessionId, Guid.Empty.ToString()),
        ], scheme));
        context.Items[typeof(UserAccess)] = access;
        return true;
    }
}
