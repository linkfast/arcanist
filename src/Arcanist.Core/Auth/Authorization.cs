using Arcanist.Core.Api;
using Arcanist.Core.Application;
using Arcanist.Core.Data.Entities;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Arcanist.Core.Auth;

/// <summary>Policy names for endpoint-level checks (<c>RequireAuthorization</c>). Application services use <c>AccessGuard</c> instead.</summary>
public static class Policies
{
    private const string PermissionPrefix = "perm:";

    public const string Admin = "admin";

    /// <summary>The password was entered recently (<see cref="AuthOptions.StepUpWindow"/>). For sensitive admin actions.</summary>
    public const string RecentAuthentication = "recent-auth";

    public static string Permission(Permissions permission) => PermissionPrefix + permission;

    internal static bool TryParsePermission(string policyName, out Permissions permission)
    {
        permission = Permissions.None;
        return policyName.StartsWith(PermissionPrefix, StringComparison.Ordinal)
               && Enum.TryParse(policyName[PermissionPrefix.Length..], out permission);
    }
}

public sealed record PermissionRequirement(Permissions Permission) : IAuthorizationRequirement;

public sealed record AdminRequirement : IAuthorizationRequirement;

public sealed record RecentAuthenticationRequirement : IAuthorizationRequirement;

/// <summary>Builds <c>perm:*</c> policies on demand so every permission has one without registering each.</summary>
internal sealed class ArcanistPolicyProvider(IOptions<AuthorizationOptions> options) : DefaultAuthorizationPolicyProvider(options)
{
    public override async Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (Policies.TryParsePermission(policyName, out var permission))
            return Authenticated().AddRequirements(new PermissionRequirement(permission)).Build();

        return policyName switch
        {
            Policies.Admin => Authenticated().AddRequirements(new AdminRequirement()).Build(),
            Policies.RecentAuthentication => Authenticated().AddRequirements(new RecentAuthenticationRequirement()).Build(),
            _ => await base.GetPolicyAsync(policyName),
        };
    }

    private static AuthorizationPolicyBuilder Authenticated() => new AuthorizationPolicyBuilder().RequireAuthenticatedUser();
}

internal sealed class ArcanistAuthorizationHandler(ICurrentUser currentUser, IOptions<AuthOptions> options, TimeProvider time)
    : IAuthorizationHandler
{
    public Task HandleAsync(AuthorizationHandlerContext context)
    {
        if (!currentUser.IsAuthenticated)
            return Task.CompletedTask;

        var access = currentUser.Access;

        foreach (var requirement in context.PendingRequirements.ToList())
        {
            var satisfied = requirement switch
            {
                PermissionRequirement p => access.Has(p.Permission),
                AdminRequirement => access.IsAdmin,
                RecentAuthenticationRequirement => IsRecent(context.User.FindFirst(ArcanistClaims.AuthenticatedAt)?.Value),
                _ => (bool?)null,
            };

            if (satisfied == true)
                context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }

    private bool IsRecent(string? authenticatedAt) =>
        long.TryParse(authenticatedAt, out var seconds)
        && time.GetUtcNow() - DateTimeOffset.FromUnixTimeSeconds(seconds) <= options.Value.StepUpWindow;
}

/// <summary>Writes 401/403 as problem details with a code, so clients can tell "log in again" from
/// "confirm your password" (<see cref="ErrorCodes.ReauthenticationRequired"/>) from a plain denial.</summary>
internal sealed class ProblemAuthorizationResultHandler(IProblemDetailsService problems) : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler _default = new();

    public async Task HandleAsync(RequestDelegate next, HttpContext context, AuthorizationPolicy policy, PolicyAuthorizationResult result)
    {
        if (result.Succeeded)
        {
            await _default.HandleAsync(next, context, policy, result);
            return;
        }

        var (status, code) = result.Challenged
            ? (StatusCodes.Status401Unauthorized, ErrorCodes.Unauthorized)
            : result.AuthorizationFailure?.FailedRequirements.OfType<RecentAuthenticationRequirement>().Any() == true
                ? (StatusCodes.Status403Forbidden, ErrorCodes.ReauthenticationRequired)
                : (StatusCodes.Status403Forbidden, ErrorCodes.Forbidden);

        if (result.Challenged)
            await context.ChallengeAsync();

        context.Response.StatusCode = status;
        await problems.WriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = new ProblemDetails { Status = status, Extensions = { ["code"] = code } },
        });
    }
}
