using System.Net;
using System.Threading.RateLimiting;
using Arcanist.Core.Api;
using Arcanist.Core.Application;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Arcanist.Core.Auth;

/// <summary>Bound from <c>Arcanist:Network</c>.</summary>
public sealed class NetworkOptions
{
    public const string Section = "Arcanist:Network";

    /// <summary>
    /// Reverse proxies (IP addresses or CIDR ranges) whose <c>X-Forwarded-*</c> headers are trusted, in addition
    /// to loopback. Without this, a proxy in another container or host makes every client look the same.
    /// </summary>
    public List<string> TrustedProxies { get; set; } = [];
}

public static class AuthServiceCollectionExtensions
{
    public const string AuthRateLimit = "auth";

    public static IServiceCollection AddArcanistAuth(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<AuthOptions>(configuration.GetSection(AuthOptions.Section));
        services.Configure<NetworkOptions>(configuration.GetSection(NetworkOptions.Section));
        services.TryAddSingleton(TimeProvider.System);
        services.AddMemoryCache();
        services.AddHttpContextAccessor();

        services.AddSingleton<SigningKey>();
        services.AddSingleton<TokenService>();
        services.AddSingleton<StreamTokens>();
        services.AddSingleton<LoginThrottle>();
        services.AddSingleton<UserAccessCache>();
        services.AddScoped<ICurrentUser, HttpCurrentUser>();
        services.AddScoped<AuthService>();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<SigningKey, TimeProvider>(ConfigureJwtBearer);

        services.AddAuthorization();
        services.AddSingleton<IAuthorizationPolicyProvider, ArcanistPolicyProvider>();
        services.AddScoped<IAuthorizationHandler, ArcanistAuthorizationHandler>();
        services.AddSingleton<Microsoft.AspNetCore.Authorization.IAuthorizationMiddlewareResultHandler, ProblemAuthorizationResultHandler>();

        services.AddOptions<ForwardedHeadersOptions>().Configure<IOptions<NetworkOptions>>(ConfigureForwardedHeaders);
        services.AddRateLimiter(ConfigureRateLimiter);

        return services;
    }

    private static void ConfigureJwtBearer(JwtBearerOptions options, SigningKey signingKey, TimeProvider time)
    {
        var clockSkew = TimeSpan.FromSeconds(30);

        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = TokenService.Issuer,
            ValidAudience = TokenService.Audience,
            IssuerSigningKey = signingKey.Key,
            NameClaimType = ArcanistClaims.Name,
            ClockSkew = clockSkew,
            // Validated against the injected clock so lifetimes are testable.
            LifetimeValidator = (notBefore, expires, _, _) =>
            {
                var now = time.GetUtcNow().UtcDateTime;
                return (notBefore is null || notBefore <= now + clockSkew) && (expires is null || now < expires + clockSkew);
            },
        };

        options.Events = new JwtBearerEvents
        {
            // A valid signature is not enough: the user, its security stamp and the session must still be current.
            OnTokenValidated = async context =>
            {
                var access = await SessionValidator.ValidateAsync(context.Principal!, context.HttpContext.RequestServices,
                    context.HttpContext.RequestAborted);

                if (access is null)
                    context.Fail("Session is no longer valid.");
                else
                    context.HttpContext.Items[typeof(UserAccess)] = access;
            },
        };
    }

    private static void ConfigureForwardedHeaders(ForwardedHeadersOptions options, IOptions<NetworkOptions> network)
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;

        foreach (var proxy in network.Value.TrustedProxies)
        {
            if (System.Net.IPNetwork.TryParse(proxy, out var range))
                options.KnownIPNetworks.Add(range);
            else if (IPAddress.TryParse(proxy, out var address))
                options.KnownProxies.Add(address);
        }
    }

    private static void ConfigureRateLimiter(RateLimiterOptions options)
    {
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

        options.AddPolicy(AuthRateLimit, context =>
        {
            var limit = context.RequestServices.GetRequiredService<IOptions<AuthOptions>>().Value.RequestsPerMinute;
            return RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "local",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = limit, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 });
        });

        options.OnRejected = async (context, cancellationToken) =>
        {
            if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                context.HttpContext.Response.Headers.RetryAfter = ((int)retryAfter.TotalSeconds).ToString();

            await context.HttpContext.RequestServices.GetRequiredService<IProblemDetailsService>().WriteAsync(new ProblemDetailsContext
            {
                HttpContext = context.HttpContext,
                ProblemDetails = new ProblemDetails
                {
                    Status = StatusCodes.Status429TooManyRequests,
                    Extensions = { ["code"] = ErrorCodes.TooManyRequests },
                },
            });
        };
    }
}
