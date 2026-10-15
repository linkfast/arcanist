using System.Text.RegularExpressions;
using Arcanist.Core.Api;
using Arcanist.Core.Application;
using Arcanist.Core.Application.Server;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;

namespace Arcanist.Core.Hosting;

/// <summary>Bound from <c>Arcanist:Frontends</c>.</summary>
public sealed class FrontendOptions
{
    public const string Section = "Arcanist:Frontends";

    /// <summary>Built admin panel (Angular <c>dist/arcanist-web/browser</c>). Default: <c>wwwroot/admin</c>.</summary>
    public string? AdminRoot { get; set; }

    /// <summary>
    /// Web player served at <c>/</c>. Default: <c>wwwroot/player</c>, where releases bundle Ultimate Web Player.
    /// When absent, <c>/</c> shows a page pointing to the admin panel.
    /// </summary>
    public string? PlayerRoot { get; set; }
}

/// <summary>
/// Serves the single-page frontends on the shared port: the admin panel under <c>/admin</c> and the active web
/// player at <c>/</c>. Unknown paths fall back to the frontend's <c>index.html</c> so client-side routes work,
/// except under API and reserved prefixes, which answer 404 instead of HTML.
/// </summary>
public static partial class FrontendHost
{
    public const string AdminPath = "/admin";

    /// <summary>Prefixes owned by APIs or kept for extensions (mStream compatibility, Subsonic, players, extensions).</summary>
    private static readonly string[] ReservedPrefixes =
        ["/rest", "/graphql", "/health", "/api", "/media", "/album-art", "/transcode", "/shared", "/ext"];

    public const string PlayersPath = "/players";

    public static IServiceCollection AddFrontends(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<FrontendOptions>(configuration.GetSection(FrontendOptions.Section));
        services.AddSingleton<FrontendCatalog>();
        return services;
    }

    public static WebApplication UseFrontends(this WebApplication app)
    {
        var options = app.Services.GetRequiredService<IOptions<FrontendOptions>>().Value;
        var adminRoot = Resolve(options.AdminRoot, app.Environment, Path.Combine("wwwroot", "admin"));
        var catalog = app.Services.GetRequiredService<FrontendCatalog>();

        // Only GET/HEAD: these routes exist so that browsers never get HTML for API paths, and other methods
        // must keep reaching (or failing on) the API endpoints.
        string[] read = [HttpMethods.Get, HttpMethods.Head];

        // Specific endpoints win over these catch-alls, so only unmatched paths reach them.
        foreach (var prefix in ReservedPrefixes)
        {
            app.MapMethods(prefix + "/{**path}", read, () => Problems.Create(StatusCodes.Status404NotFound, ErrorCodes.NotFound))
                .ExcludeFromDescription();
        }

        // Also matches "/admin" itself; the panel's <base href="/admin/"> makes that equivalent to "/admin/".
        app.MapFallback(AdminPath + "/{**path}", (HttpContext context, string? path) => Serve(context, adminRoot, path, MissingAdminPage))
            .WithMetadata(new HttpMethodMetadata(read))
            .ExcludeFromDescription();

        // Every frontend, default or not, also lives under /players/<id>/ (the slash matters for relative URLs).
        app.MapMethods(PlayersPath + "/{id}/{**path}", read, (HttpContext context, string id, string? path) =>
                catalog.Find(id) is not { } frontend ? Problems.Create(StatusCodes.Status404NotFound, ErrorCodes.NotFound)
                : string.IsNullOrEmpty(path) && !context.Request.Path.Value!.EndsWith('/') ? Results.Redirect($"{PlayersPath}/{id}/")
                : Serve(context, frontend, $"{PlayersPath}/{id}/", path))
            .ExcludeFromDescription();

        app.MapFallback(async (HttpContext context) => await catalog.DefaultAsync(context.RequestAborted) is { } frontend
                ? Serve(context, frontend, "/", context.Request.Path.Value)
                : Html(context, PlaceholderPage(context.RequestServices.GetRequiredService<ServerInfoService>().Get())))
            .WithMetadata(new HttpMethodMetadata(read))
            .ExcludeFromDescription();

        return app;
    }

    private static string? Resolve(string? configured, IWebHostEnvironment environment, string fallback)
    {
        var path = Path.GetFullPath(configured ?? fallback, environment.ContentRootPath);
        return File.Exists(Path.Combine(path, "index.html")) ? path : null;
    }

    /// <summary>Sends a file of an installed frontend, or its entry page (based at <paramref name="mount"/>) for client-side routes.</summary>
    private static IResult Serve(HttpContext context, InstalledFrontend frontend, string mount, string? path)
    {
        var relative = (path ?? "").TrimStart('/');
        if (relative.Length > 0 && relative != frontend.Manifest.Entry
            && Files(frontend.Root).GetFileInfo(relative) is { Exists: true, IsDirectory: false, PhysicalPath: { } file })
        {
            context.Response.Headers.CacheControl = HashedAsset().IsMatch(file) ? "public, max-age=31536000, immutable" : "no-cache";
            return Results.File(file, ContentTypes.TryGetContentType(file, out var type) ? type : "application/octet-stream",
                lastModified: File.GetLastWriteTimeUtc(file), enableRangeProcessing: true);
        }

        if (Path.HasExtension(relative) && relative != frontend.Manifest.Entry)
            return Problems.Create(StatusCodes.Status404NotFound, ErrorCodes.NotFound);

        return Html(context, FrontendCatalog.EntryPage(frontend, mount));
    }

    /// <summary>Sends a file of the frontend, or its index.html for client-side routes.</summary>
    private static IResult Serve(HttpContext context, string? root, string? path, string missingPage)
    {
        if (root is null)
            return Html(context, missingPage, StatusCodes.Status404NotFound);

        var relative = (path ?? "").TrimStart('/');
        if (relative.Length > 0 && Files(root).GetFileInfo(relative) is { Exists: true, IsDirectory: false, PhysicalPath: { } file })
        {
            // Bundlers put a content hash in asset names; those never change. Everything else is revalidated.
            context.Response.Headers.CacheControl = HashedAsset().IsMatch(file) ? "public, max-age=31536000, immutable" : "no-cache";
            return Results.File(file, ContentTypes.TryGetContentType(file, out var type) ? type : "application/octet-stream",
                lastModified: File.GetLastWriteTimeUtc(file), enableRangeProcessing: true);
        }

        // Paths with an extension are missing files, not client-side routes.
        if (Path.HasExtension(relative))
            return Problems.Create(StatusCodes.Status404NotFound, ErrorCodes.NotFound);

        context.Response.Headers.CacheControl = "no-cache";
        return Results.File(Path.Combine(root, "index.html"), "text/html; charset=utf-8");
    }

    private static readonly FileExtensionContentTypeProvider ContentTypes = new();
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, PhysicalFileProvider> Providers = new();

    private static PhysicalFileProvider Files(string root) => Providers.GetOrAdd(root, r => new PhysicalFileProvider(r));

    private static IResult Html(HttpContext context, string html, int status = StatusCodes.Status200OK)
    {
        context.Response.Headers.CacheControl = "no-cache";
        return Results.Content(html, "text/html; charset=utf-8", statusCode: status);
    }

    [GeneratedRegex(@"[.-][0-9A-Za-z]{8,}\.(js|css|woff2?|png|jpe?g|svg|webp|ico)$")]
    private static partial Regex HashedAsset();

    private const string MissingAdminPage = """
        <!doctype html>
        <html lang="en"><head><meta charset="utf-8"><title>Arcanist</title></head>
        <body style="font-family: system-ui, sans-serif; max-width: 40rem; margin: 4rem auto; padding: 0 1rem">
          <h1>Admin panel not built</h1>
          <p>This server has no admin panel files. In development, run <code>npm start</code> in
          <code>src/Arcanist.Web</code> and open the URL it prints; release builds include the panel.</p>
        </body></html>
        """;

    private static string PlaceholderPage(ServerInfo info)
    {
        var source = info.SourceUrl is { } url
            ? $"""<p>An open source product of LinkFast. <a href="{System.Net.WebUtility.HtmlEncode(url)}">Source code</a> (AGPL-3.0-or-later)</p>"""
            : "<p>Arcanist is an open source product of LinkFast, free software under the AGPL-3.0-or-later.</p>";

        return $"""
            <!doctype html>
            <html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
            <title>Arcanist</title></head>
            <body style="font-family: system-ui, sans-serif; max-width: 40rem; margin: 4rem auto; padding: 0 1rem">
              <h1>Arcanist {System.Net.WebUtility.HtmlEncode(info.Version)}</h1>
              <p>Your music server is running. No web player is installed yet.</p>
              <p><a href="{AdminPath}/">Open the admin panel</a></p>
              {source}
            </body></html>
            """;
    }
}
