using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Arcanist.Core.Auth;

/// <summary>Checks that a token's user, security stamp and session are still current.</summary>
public static class SessionValidator
{
    public static async Task<UserAccess?> ValidateAsync(ClaimsPrincipal principal, IServiceProvider services, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(principal.FindFirst(ArcanistClaims.UserId)?.Value, out var userId)
            || !Guid.TryParse(principal.FindFirst(ArcanistClaims.SessionId)?.Value, out var sessionId))
        {
            return null;
        }

        var cache = services.GetRequiredService<UserAccessCache>();
        var access = await cache.GetUserAsync(userId, cancellationToken);

        return access is null
               || access.IsDisabled
               || access.SecurityStamp != principal.FindFirst(ArcanistClaims.SecurityStamp)?.Value
               || !await cache.IsSessionActiveAsync(sessionId, cancellationToken)
            ? null
            : access;
    }

    /// <summary>
    /// Validates a raw access token (signature, lifetime and session) the same way the Bearer handler does, for
    /// transports that cannot send headers, such as the GraphQL WebSocket's <c>connection_init</c>.
    /// </summary>
    public static async Task<(ClaimsPrincipal Principal, UserAccess Access)?> ValidateTokenAsync(
        string token, IServiceProvider services, CancellationToken cancellationToken)
    {
        var parameters = services.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme).TokenValidationParameters;

        var result = await new JsonWebTokenHandler().ValidateTokenAsync(token, parameters);
        if (!result.IsValid)
            return null;

        var principal = new ClaimsPrincipal(result.ClaimsIdentity);
        var access = await ValidateAsync(principal, services, cancellationToken);
        return access is null ? null : (principal, access);
    }
}
