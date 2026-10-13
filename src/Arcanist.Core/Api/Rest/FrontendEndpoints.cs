using Arcanist.Core.Application;
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
