using System.Text;
using Arcanist.Core.Application;
using Arcanist.Core.Application.Server;
using Arcanist.Core.Application.Users;
using Arcanist.Core.Auth;
using Arcanist.Extensibility;
using Microsoft.AspNetCore.Routing;

namespace Arcanist.Core.Extensions.Subsonic;

/// <summary>
/// The Subsonic API (1.16.1, with OpenSubsonic extensions) at <c>/rest/{method}[.view]</c>, so the many Subsonic apps
/// can play from Arcanist. It is a thin layer over the application services: users see what they see in Arcanist.
/// Apps sign in with an app password (as the password, as a token or as an OpenSubsonic API key), never with the
/// user's own password.
/// </summary>
internal sealed class SubsonicExtension : IBuiltInExtension
{
    public const string Id = "subsonic";

    public ExtensionManifest Manifest { get; } = new(
        Id,
        "Subsonic",
        typeof(SubsonicExtension).Assembly.GetName().Version?.ToString(3) ?? "1.0.0",
        "Subsonic and OpenSubsonic API for Subsonic apps (Symfonium, DSub, Feishin, Amperfy…). Apps sign in with an app password.",
        "Arcanist",
        "AGPL-3.0-or-later",
        Routes: ["/rest"]);

    public void ConfigureServices(IServiceCollection services, ExtensionContext context) =>
        services.AddSingleton<SubsonicNowPlaying>();

    public void MapEndpoints(IEndpointRouteBuilder endpoints, ExtensionContext context)
    {
        endpoints.MapMethods("/rest/{method}", [HttpMethods.Get, HttpMethods.Post, HttpMethods.Head], HandleAsync)
            .ExcludeFromDescription()
            .DisableAntiforgery();

        // Web players (Feishin, Airsonic Refix…) call from their own origin; credentials travel in parameters.
        endpoints.MapMethods("/rest/{method}", [HttpMethods.Options], (HttpContext context) =>
            {
                AllowAnyOrigin(context);
                context.Response.Headers.AccessControlAllowMethods = "GET, POST, HEAD, OPTIONS";
                context.Response.Headers.AccessControlAllowHeaders = "Content-Type, Range";
                context.Response.Headers.AccessControlMaxAge = "86400";
                return Results.NoContent();
            })
            .ExcludeFromDescription();
    }

    private static async Task<IResult> HandleAsync(string method, HttpContext context, CancellationToken cancellationToken)
    {
        AllowAnyOrigin(context);
        var name = method.EndsWith(".view", StringComparison.OrdinalIgnoreCase) ? method[..^5] : method;
        var parameters = await SubsonicParameters.ReadAsync(context.Request, cancellationToken);
        var responder = new SubsonicResponder(parameters, context.RequestServices.GetRequiredService<ServerInfoService>().Get().Version);

        try
        {
            if (!SubsonicMethods.All.TryGetValue(name, out var handler))
                return responder.Error(SubsonicErrors.Generic, $"Unknown method: {name}");

            var call = new SubsonicCall(context, parameters, responder, cancellationToken);
            if (!SubsonicMethods.Public.Contains(name))
                await AuthenticateAsync(call);
            return await handler(call);
        }
        catch (SubsonicException e)
        {
            return responder.Error(e.Code, e.Message);
        }
        catch (ArcanistException e)
        {
            return e switch
            {
                ArcanistException.NotFound => responder.Error(SubsonicErrors.NotFound, "The requested data was not found"),
                ArcanistException.Forbidden or ArcanistException.Unauthorized => responder.Error(SubsonicErrors.NotAuthorized, "User is not authorized for the given operation"),
                ArcanistException.Invalid invalid => responder.Error(SubsonicErrors.MissingParameter, $"Invalid parameters: {string.Join(", ", invalid.Errors.ToDictionary().Keys)}"),
                _ => responder.Error(SubsonicErrors.Generic, e.Code),
            };
        }
    }

    /// <summary>
    /// Checks the credentials of the call in the order OpenSubsonic sets: an API key alone, or a user name with a
    /// password or a token. All of them are app passwords.
    /// </summary>
    private static async Task AuthenticateAsync(SubsonicCall call)
    {
        var parameters = call.Parameters;
        var passwords = call.Service<AppPasswordService>();
        Guid? userId;

        if (parameters["apiKey"] is { Length: > 0 } apiKey)
        {
            if (parameters.Has("u") || parameters.Has("p") || parameters.Has("t") || parameters.Has("s"))
                throw new SubsonicException(SubsonicErrors.ConflictingAuthentication, "Use the API key alone, without u, p, t or s");
            userId = await passwords.VerifyApiKeyAsync(apiKey, call.Cancellation)
                     ?? throw new SubsonicException(SubsonicErrors.InvalidApiKey, "Invalid API key");
        }
        else
        {
            var user = parameters.Required("u");
            var password = parameters["p"];
            var token = parameters["t"];
            var salt = parameters["s"];
            if (password is not null && (token is not null || salt is not null))
                throw new SubsonicException(SubsonicErrors.ConflictingAuthentication, "Send either p, or t and s");

            if (token is { Length: > 0 } && salt is { Length: > 0 })
                userId = await passwords.VerifyTokenAsync(user, token, salt, call.Cancellation);
            else if (password is { Length: > 0 })
                userId = await passwords.VerifyPasswordAsync(user, Decode(password), call.Cancellation);
            else
                throw SubsonicException.Missing(token is null ? "p" : "s");
        }

        if (userId is not { } id || !await ExtensionAuthentication.SignInAsync(call.Http, id, "subsonic", call.Cancellation))
        {
            throw new SubsonicException(SubsonicErrors.WrongCredentials,
                "Wrong username or password. Use an app password, created in your Arcanist account.");
        }
    }

    /// <summary>A password sent as <c>enc:</c> and hex.</summary>
    private static string Decode(string password)
    {
        if (!password.StartsWith("enc:", StringComparison.Ordinal))
            return password;
        try
        {
            return Encoding.UTF8.GetString(Convert.FromHexString(password[4..]));
        }
        catch (FormatException)
        {
            return password;
        }
    }

    private static void AllowAnyOrigin(HttpContext context) => context.Response.Headers.AccessControlAllowOrigin = "*";
}

/// <summary>One Subsonic call: its parameters, how to answer, and the signed-in user once authenticated.</summary>
internal sealed class SubsonicCall(HttpContext http, SubsonicParameters parameters, SubsonicResponder responder, CancellationToken cancellation)
{
    public HttpContext Http { get; } = http;

    public SubsonicParameters Parameters { get; } = parameters;

    public SubsonicResponder Responder { get; } = responder;

    public CancellationToken Cancellation { get; } = cancellation;

    public UserAccess Access => Http.Items[typeof(UserAccess)] as UserAccess ?? throw new ArcanistException.Unauthorized();

    public T Service<T>() where T : notnull => Http.RequestServices.GetRequiredService<T>();

    public IResult Ok(SubsonicNode? payload = null) => Responder.Ok(payload);
}
