using Arcanist.Core.Application;
using Arcanist.Core.Application.Users;
using Arcanist.Core.Auth;

namespace Arcanist.Core.Api.Rest;

public sealed record LoginRequest(string? UserName, string? Password, string? DeviceName, bool UseCookie = false);

public sealed record RefreshRequest(string? RefreshToken);

/// <param name="Code">A code from the authenticator app or a recovery code, for users with a second factor.</param>
/// <param name="Passkey">Their passkey's answer instead, as JSON.</param>
public sealed record ReauthenticateRequest(string? Password, string? Code = null, string? Passkey = null);

/// <param name="Challenge">What the login's answer gave (<c>second_factor_required</c> or <c>second_factor_setup_required</c>).</param>
public sealed record SecondFactorChallengeRequest(string? Challenge);

public sealed record SecondFactorCodeRequest(string? Challenge, string? Code);

/// <param name="Assertion">The passkey's answer, as JSON.</param>
public sealed record PasskeyLoginRequest(string? Challenge, string? Assertion);

/// <param name="Attestation">The new passkey, as JSON.</param>
public sealed record PasskeySetupRequest(string? Challenge, string? Name, string? Attestation);

/// <param name="Options">What <c>navigator.credentials</c> needs, as JSON.</param>
public sealed record PasskeyOptionsResponse(string Options);

/// <param name="Secret">For typing it into the app.</param>
/// <param name="Uri">For its QR code.</param>
public sealed record TotpSetupResponse(string Secret, string Uri);

/// <param name="RefreshToken">Omitted when the client asked for the cookie, and on re-authentication.</param>
/// <param name="RecoveryCodes">The user's new recovery codes, when pairing their first factor signed them in. Shown only now.</param>
public sealed record TokenResponse(
    string AccessToken,
    DateTime AccessTokenExpiresAt,
    string? RefreshToken,
    DateTime SessionExpiresAt,
    UserAccount User,
    IReadOnlyList<string>? RecoveryCodes = null);

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
                var result = await service.LoginAsync(request.UserName, request.Password, Client(context, request.DeviceName), request.UseCookie, cancellationToken);
                return ToResponse(result, context, request.UseCookie);
            })
            .RequireRateLimiting(AuthServiceCollectionExtensions.AuthRateLimit)
            .WithName("Login")
            .WithDescription("With a second factor, the answer is 401 with `code` `second_factor_required` (or `second_factor_setup_required`), `challenge`, `methods` and `passkeyDomains`: the login goes on below.")
            .Produces<TokenResponse>();

        auth.MapPost("/login/second-factor", async (SecondFactorCodeRequest request, HttpContext context, AuthService service, CancellationToken ct) =>
                ToResponse(await service.LoginWithCodeAsync(request.Challenge, request.Code, Client(context), ct), context, useCookie: false))
            .RequireRateLimiting(AuthServiceCollectionExtensions.AuthRateLimit)
            .WithName("LoginWithSecondFactor")
            .WithSummary("Finishes a sign-in with a code from the authenticator app or a recovery code, which is then spent.")
            .Produces<TokenResponse>();

        auth.MapPost("/login/passkey/options", async (SecondFactorChallengeRequest request, HttpContext context, AuthService service, CancellationToken ct) =>
                ToResponse(await service.LoginPasskeyOptionsAsync(request.Challenge, ct), context, useCookie: false))
            .RequireRateLimiting(AuthServiceCollectionExtensions.AuthRateLimit)
            .WithName("LoginPasskeyOptions")
            .WithSummary("What the passkey has to answer to finish a sign-in.")
            .Produces<PasskeyOptionsResponse>();

        auth.MapPost("/login/passkey", async (PasskeyLoginRequest request, HttpContext context, AuthService service, CancellationToken ct) =>
                ToResponse(await service.LoginWithPasskeyAsync(request.Challenge, request.Assertion, Client(context), ct), context, useCookie: false))
            .RequireRateLimiting(AuthServiceCollectionExtensions.AuthRateLimit)
            .WithName("LoginWithPasskey")
            .WithSummary("Finishes a sign-in with a passkey.")
            .Produces<TokenResponse>();

        auth.MapPost("/login/setup/totp", async (SecondFactorChallengeRequest request, HttpContext context, AuthService service, CancellationToken ct) =>
                ToResponse(await service.SetupTotpAsync(request.Challenge, ct), context, useCookie: false))
            .RequireRateLimiting(AuthServiceCollectionExtensions.AuthRateLimit)
            .WithName("LoginSetupTotp")
            .WithSummary("A secret for an authenticator app, for a user who must pair a second factor to sign in.")
            .Produces<TotpSetupResponse>();

        auth.MapPost("/login/setup/totp/confirm", async (SecondFactorCodeRequest request, HttpContext context, AuthService service, CancellationToken ct) =>
                ToResponse(await service.SetupTotpConfirmAsync(request.Challenge, request.Code, Client(context), ct), context, useCookie: false))
            .RequireRateLimiting(AuthServiceCollectionExtensions.AuthRateLimit)
            .WithName("LoginSetupTotpConfirm")
            .WithSummary("Pairs the authenticator app with its first code and signs the user in; the answer has their recovery codes.")
            .Produces<TokenResponse>();

        auth.MapPost("/login/setup/passkey/options", async (SecondFactorChallengeRequest request, HttpContext context, AuthService service, CancellationToken ct) =>
                ToResponse(await service.SetupPasskeyOptionsAsync(request.Challenge, ct), context, useCookie: false))
            .RequireRateLimiting(AuthServiceCollectionExtensions.AuthRateLimit)
            .WithName("LoginSetupPasskeyOptions")
            .WithSummary("What a browser needs to create a passkey, for a user who must pair a second factor to sign in.")
            .Produces<PasskeyOptionsResponse>();

        auth.MapPost("/login/setup/passkey", async (PasskeySetupRequest request, HttpContext context, AuthService service, CancellationToken ct) =>
                ToResponse(await service.SetupPasskeyAsync(request.Challenge, request.Name, request.Attestation, Client(context), ct), context, useCookie: false))
            .RequireRateLimiting(AuthServiceCollectionExtensions.AuthRateLimit)
            .WithName("LoginSetupPasskey")
            .WithSummary("Pairs a passkey and signs the user in; the answer has their recovery codes.")
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
                ToResponse(await service.ReauthenticateAsync(user.SessionId, request.Password, request.Code, request.Passkey, cancellationToken), context, useCookie: false))
            .RequireAuthorization()
            .RequireRateLimiting(AuthServiceCollectionExtensions.AuthRateLimit)
            .WithName("Reauthenticate")
            .WithDescription("Who has a second factor sends it too: without it the answer is 401 with `code` `second_factor_required`, `methods` and `passkeyDomains`.")
            .Produces<TokenResponse>();

        auth.MapPost("/reauthenticate/passkey/options", async (HttpContext context, ICurrentUser user, AuthService service, CancellationToken ct) =>
                ToResponse(await service.ReauthenticatePasskeyOptionsAsync(user.SessionId, ct), context, useCookie: false))
            .RequireAuthorization()
            .RequireRateLimiting(AuthServiceCollectionExtensions.AuthRateLimit)
            .WithName("ReauthenticatePasskeyOptions")
            .WithSummary("What the passkey has to answer to confirm a re-authentication.")
            .Produces<PasskeyOptionsResponse>();

        MapBrowserLogin(auth);

        return rest;
    }

    /// <summary>Signing an app in through the browser (the developers' guide tells the steps).</summary>
    private static void MapBrowserLogin(RouteGroupBuilder auth)
    {
        auth.MapGet("/browser", async (BrowserLoginService service, CancellationToken ct) => await service.InfoAsync(ct))
            .RequireRateLimiting(AuthServiceCollectionExtensions.PublicRateLimit)
            .WithName("BrowserLoginInfo")
            .WithSummary("Where an app opens the browser to sign in. Relative without a public URL: to the address the app uses.");

        auth.MapPost("/browser/check", (BrowserLoginRequest request, BrowserLoginService service) => service.Check(request))
            .RequireRateLimiting(AuthServiceCollectionExtensions.PublicRateLimit)
            .WithName("CheckBrowserLogin")
            .WithSummary("Whether a link to the authorize page can be honoured.");

        auth.MapPost("/browser/approve", async (BrowserLoginRequest request, BrowserLoginService service, CancellationToken ct) =>
                await service.ApproveAsync(request, ct))
            .RequireAuthorization()
            .RequireRateLimiting(AuthServiceCollectionExtensions.AuthRateLimit)
            .WithName("ApproveBrowserLogin")
            .WithSummary("The signed-in user lets the app in: where the browser goes next, with a code for the app.");

        auth.MapPost("/browser/token", async (BrowserLoginTokenRequest request, HttpContext context, BrowserLoginService service, CancellationToken ct) =>
                ToResponse(await service.RedeemAsync(request, Client(context), ct), context, useCookie: false))
            .RequireRateLimiting(AuthServiceCollectionExtensions.AuthRateLimit)
            .WithName("RedeemBrowserLogin")
            .WithSummary("Trades the code and the PKCE verifier for tokens. The code works once, for 60 seconds.")
            .Produces<TokenResponse>();

        auth.MapPost("/device", async (DeviceLoginStartRequest request, HttpContext context, BrowserLoginService service, CancellationToken ct) =>
                await service.StartDeviceAsync(request, context.Connection.RemoteIpAddress?.ToString(), ct))
            .RequireRateLimiting(AuthServiceCollectionExtensions.AuthRateLimit)
            .WithName("StartDeviceLogin")
            .WithSummary("A code for the person to approve from a browser, for 10 minutes.");

        // Its own limit: a device asks every few seconds, and that must not use up the logins of its address.
        auth.MapPost("/device/token", async (DeviceLoginTokenRequest request, HttpContext context, BrowserLoginService service, CancellationToken ct) =>
                ToResponse(await service.PollDeviceAsync(request, Client(context), ct), context, useCookie: false))
            .RequireRateLimiting(AuthServiceCollectionExtensions.PublicRateLimit)
            .WithName("PollDeviceLogin")
            .WithDescription("Until someone answers, `409 browser_login_pending`. `429 browser_login_slow_down` means asking too often; `409 browser_login_denied` and `409 browser_login_expired` end it.")
            .Produces<TokenResponse>();

        auth.MapPost("/device/lookup", (DeviceLoginCodeRequest request, BrowserLoginService service) => service.LookUpDevice(request))
            .RequireAuthorization()
            .RequireRateLimiting(AuthServiceCollectionExtensions.AuthRateLimit)
            .WithName("LookUpDeviceLogin")
            .WithSummary("Which device a code is for, before approving it.");

        auth.MapPost("/device/approve", async (DeviceLoginCodeRequest request, BrowserLoginService service, CancellationToken ct) =>
            {
                await service.ApproveDeviceAsync(request, ct);
                return Results.NoContent();
            })
            .RequireAuthorization()
            .RequireRateLimiting(AuthServiceCollectionExtensions.AuthRateLimit)
            .WithName("ApproveDeviceLogin")
            .WithSummary("Lets the device in. Needs a recent password.");

        auth.MapPost("/device/deny", (DeviceLoginCodeRequest request, BrowserLoginService service) =>
            {
                service.DenyDevice(request);
                return Results.NoContent();
            })
            .RequireAuthorization()
            .RequireRateLimiting(AuthServiceCollectionExtensions.AuthRateLimit)
            .WithName("DenyDeviceLogin");
    }

    private static ClientInfo Client(HttpContext context, string? deviceName = null) => new(
        context.Connection.RemoteIpAddress?.ToString(),
        context.Request.Headers.UserAgent.ToString(),
        deviceName);

    private static IResult ToResponse(AuthResult result, HttpContext context, bool useCookie)
    {
        switch (result)
        {
            case AuthResult.PasskeyOptions { Options: var options }:
                return Results.Ok(new PasskeyOptionsResponse(options));

            case AuthResult.TotpSecret { Secret: var secret, Uri: var uri }:
                return Results.Ok(new TotpSetupResponse(secret, uri));

            case AuthResult.SecondFactorNeeded { Code: var code, Challenge: var challenge, Factors: var factors }:
                return Results.Problem(statusCode: StatusCodes.Status401Unauthorized, extensions: new Dictionary<string, object?>
                {
                    ["code"] = code,
                    ["challenge"] = challenge,
                    ["methods"] = factors.Methods,
                    ["passkeyDomains"] = factors.PasskeyDomains,
                });

            case AuthResult.Success { Tokens: var tokens, UseCookie: var challengeCookie, RecoveryCodes: var recoveryCodes }:
                useCookie = challengeCookie ?? useCookie;
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
                    tokens.User,
                    recoveryCodes));

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
