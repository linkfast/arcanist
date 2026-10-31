using Arcanist.Core.Application;
using Arcanist.Core.Application.Extensions;
using Arcanist.Core.Application.Frontends;
using Arcanist.Core.Packages;
using Microsoft.AspNetCore.Mvc;

namespace Arcanist.Core.Api.Rest;

public sealed record InstallFromUrlInput(string Url);

public static class FrontendEndpoints
{
    public static IEndpointRouteBuilder MapFrontendEndpoints(this IEndpointRouteBuilder rest)
    {
        var frontends = rest.MapGroup("/frontends").WithTags("Frontends");

        frontends.MapGet("", (FrontendService service, CancellationToken ct) => service.ListAsync(ct))
            .WithName("Frontends")
            .WithSummary("Installed web players; the default one is served at /. Administrators only.");

        frontends.MapGet("/available", (FrontendService service, CancellationToken ct) => service.AvailableAsync(ct))
            .WithName("AvailablePlayers")
            .WithSummary("The web players any user can open (to switch from one to another), the default one first.");

        // Uploads exist only in REST: GraphQL has no file type.
        frontends.MapPost("", async (IFormFile file, FrontendService service, CancellationToken ct) =>
            {
                await using var archive = file.OpenReadStream();
                return Results.Ok(await service.InstallAsync(archive, ct));
            })
            .WithName("UploadFrontend")
            .WithSummary("Installs (or updates) the web player in an uploaded .zip with arcanist-frontend.json at its root (form field `file`). Administrators only, with a recent authentication.")
            .DisableAntiforgery()
            .WithMetadata(new RequestSizeLimitAttribute(PackageArchive.MaxArchiveBytes), new RequestFormLimitsAttribute { MultipartBodyLengthLimit = PackageArchive.MaxArchiveBytes })
            .Produces<Frontend>();

        frontends.MapPost("/from-url", (InstallFromUrlInput input, FrontendService service, CancellationToken ct) =>
                service.InstallFromUrlAsync(input.Url, ct))
            .WithName("InstallFrontendFromUrl")
            .WithSummary("Downloads a web player's .zip and installs it (or updates it). It runs with the user's session: install only what you trust. Administrators only, with a recent authentication.");

        frontends.MapGet("/marketplace", (string? language, bool? refresh, PlayerMarketplaceService marketplace, CancellationToken ct) =>
                marketplace.GetAsync(language, refresh ?? false, ct))
            .WithName("PlayerMarketplace")
            .WithSummary("The web players of the catalog (github.com/linkfast/arcanist-extensions by default: Ultimate Web Player and those anyone publishes), named in `language`, with what is installed here. Read again after an hour, or now with `refresh`. Administrators only.");

        // Without authentication, for <img>: only icons the catalog lists, which are public anyway.
        frontends.MapGet("/marketplace/icons/{id}", async (string id, HttpContext context, MarketplaceCatalog catalog, CancellationToken ct) =>
            {
                if (await catalog.IconAsync(id, ct, player: true) is not { } icon)
                    return Results.NotFound();
                // An SVG shown on its own could run scripts: nothing may load or run in it.
                context.Response.Headers.ContentSecurityPolicy = "default-src 'none'; style-src 'unsafe-inline'; sandbox";
                context.Response.Headers.CacheControl = "public, max-age=3600";
                return Results.Bytes(icon.Bytes, icon.ContentType);
            })
            .AllowAnonymous()
            .ExcludeFromDescription();

        frontends.MapPost("/marketplace/install", (InstallMarketplacePlayerInput input, PlayerMarketplaceService marketplace, CancellationToken ct) =>
                marketplace.InstallAsync(input, ct))
            .WithName("InstallMarketplacePlayer")
            .WithSummary("Installs (or updates) a web player of the catalog, checked against the SHA-256 the catalog publishes, and serves it at / when `makeDefault`. It runs with its users' sessions. Administrators only, with a recent authentication.");

        frontends.MapPut("/default/{id}", (string id, FrontendService service, CancellationToken ct) => service.SetDefaultAsync(id, ct))
            .WithName("SetDefaultFrontend")
            .WithSummary("Serves this web player at /. Administrators only, with a recent authentication.");

        frontends.MapDelete("/{id}", async (string id, FrontendService service, CancellationToken ct) =>
            {
                await service.UninstallAsync(id, ct);
                return Results.NoContent();
            })
            .WithName("UninstallFrontend")
            .WithSummary("Removes an installed web player. Administrators only, with a recent authentication.");

        return rest;
    }
}
