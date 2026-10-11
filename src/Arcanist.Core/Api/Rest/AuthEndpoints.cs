using Arcanist.Core.Application;
using Arcanist.Core.Application.Users;
using Arcanist.Core.Auth;

namespace Arcanist.Core.Api.Rest;

public sealed record LoginRequest(string? UserName, string? Password, string? DeviceName, bool UseCookie = false);

public sealed record RefreshRequest(string? RefreshToken);

public sealed record ReauthenticateRequest(string? Password);

/// <param name="RefreshToken">Omitted when the client asked for the cookie, and on re-authentication.</param>
public sealed record TokenResponse(
    string AccessToken,
    DateTime AccessTokenExpiresAt,
    string? RefreshToken,
    DateTime SessionExpiresAt,
    UserAccount User);

public static class AuthEndpoints
{
    /// <summary>
    /// Refresh token for browser clients (the admin panel): HttpOnly, only sent to the auth endpoints, never
    /// readable by scripts. Native clients receive the refresh token in the response body instead.
    /// </summary>
    public const string RefreshCookie = "arcanist_refresh";

    private const string CookiePath = "/rest/v1/auth";

    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder rest)
    {
        var auth = rest.MapGroup("/auth").WithTags("Authentication");

        auth.MapPost("/login", async (LoginRequest request, HttpContext context, AuthService service, CancellationToken cancellationToken) =>
            {
                var client = new ClientInfo(
                    context.Connection.RemoteIpAddress?.ToString(),
                    context.Request.Headers.UserAgent.ToString(),
                    request.DeviceName);

                var result = await service.LoginAsync(request.UserName, request.Password, client, cancellationToken);
                return ToResponse(result, context, request.UseCookie);
            })
            .RequireRateLimiting(AuthServiceCollectionExtensions.AuthRateLimit)
            .WithName("Login")
            .Produces<TokenResponse>();

        auth.MapPost("/refresh", async (RefreshRequest? request, HttpContext context, AuthService service, CancellationToken cancellationToken) =>
            {
                var cookie = context.Request.Cookies[RefreshCookie];
                var result = await service.RefreshAsync(request?.RefreshToken ?? cookie, cancellationToken);
                return ToResponse(result, context, useCookie: request?.RefreshToken is null && cookie is not null);
            })
            .RequireRateLimiting(AuthServiceCollectionExtensions.AuthRateLimit)
            .WithName("RefreshToken")
            .Produces<TokenResponse>();

        auth.MapPost("/logout", async (RefreshRequest? request, HttpContext context, ICurrentUser user, AuthService service, CancellationToken cancellationToken) =>
            {
                var refreshToken = request?.RefreshToken ?? context.Request.Cookies[RefreshCookie];
                await service.LogoutAsync(refreshToken, user.IsAuthenticated ? user.SessionId : null, cancellationToken);
                context.Response.Cookies.Delete(RefreshCookie, new CookieOptions { Path = CookiePath });
                return Results.NoContent();
            })
            .WithName("Logout");

        auth.MapPost("/reauthenticate", async (ReauthenticateRequest request, HttpContext context, ICurrentUser user, AuthService service, CancellationToken cancellationToken) =>
                ToResponse(await service.ReauthenticateAsync(user.SessionId, request.Password, cancellationToken), context, useCookie: false))
            .RequireAuthorization()
            .RequireRateLimiting(AuthServiceCollectionExtensions.AuthRateLimit)
            .WithName("Reauthenticate")
            .Produces<TokenResponse>();

        return rest;
    }

    private static IResult ToResponse(AuthResult result, HttpContext context, bool useCookie)
    {
        switch (result)
        {
            case AuthResult.Success { Tokens: var tokens }:
                if (useCookie && tokens.RefreshToken is not null)
                {
                    context.Response.Cookies.Append(RefreshCookie, tokens.RefreshToken, new CookieOptions
                    {
                        HttpOnly = true,
                        Secure = context.Request.IsHttps,
                        SameSite = SameSiteMode.Strict,
                        Path = CookiePath,
                        Expires = tokens.SessionExpiresAt,
                    });
                }

                return Results.Ok(new TokenResponse(
                    tokens.AccessToken.Token,
                    tokens.AccessToken.ExpiresAt,
                    useCookie ? null : tokens.RefreshToken,
                    tokens.SessionExpiresAt,
                    tokens.User));

            case AuthResult.Failure { Code: ErrorCodes.TooManyRequests, RetryAfter: var retryAfter }:
                if (retryAfter is { } wait)
                    context.Response.Headers.RetryAfter = ((int)Math.Ceiling(wait.TotalSeconds)).ToString();
                return Problems.Create(StatusCodes.Status429TooManyRequests, ErrorCodes.TooManyRequests);

            case AuthResult.Failure { Code: ErrorCodes.AccountDisabled }:
                return Problems.Create(StatusCodes.Status403Forbidden, ErrorCodes.AccountDisabled);

            case AuthResult.Failure failure:
                return Problems.Create(StatusCodes.Status401Unauthorized, failure.Code);

            default:
                throw new InvalidOperationException($"Unexpected result {result}.");
        }
    }
}
