using Arcanist.Core.Application.Extensions;
using Arcanist.Core.Packages;
using Microsoft.AspNetCore.Mvc;

namespace Arcanist.Core.Api.Rest;

public sealed record SetExtensionEnabledInput(bool Enabled);

public static class ExtensionEndpoints
{
    public static IEndpointRouteBuilder MapExtensionEndpoints(this IEndpointRouteBuilder rest)
    {
        var extensions = rest.MapGroup("/extensions").WithTags("Extensions");

        rest.MapGet("/extension-apps", (string? language, ExtensionService service, CancellationToken ct) => service.AppsAsync(language, ct))
            .WithTags("Extensions")
            .WithName("ExtensionApps")
            .WithSummary("Pages of the running extensions the user may open from a player, in a window (an iframe) or a tab of its own, named in `language` (or Accept-Language). They get the session and the player's theme with the host protocol of docs/developers/extensions.md.");

        extensions.MapGet("", (ExtensionService service) => service.List())
            .WithName("Extensions")
            .WithSummary("Built-in and installed extensions, with what happened to them at startup. Administrators only.");

        extensions.MapPost("", async (IFormFile file, ExtensionService service, CancellationToken ct) =>
            {
                await using var archive = file.OpenReadStream();
                return Results.Ok(await service.InstallAsync(archive, ct));
            })
            .WithName("UploadExtension")
            .WithSummary("Installs (or updates) the extension in an uploaded .zip with arcanist-extension.json at its root (form field `file`); it runs once enabled and the server restarts. Administrators only, with a recent authentication.")
            .DisableAntiforgery()
            .WithMetadata(new RequestSizeLimitAttribute(PackageArchive.MaxArchiveBytes), new RequestFormLimitsAttribute { MultipartBodyLengthLimit = PackageArchive.MaxArchiveBytes })
            .Produces<Extension>();

        extensions.MapPost("/from-url", (InstallFromUrlInput input, ExtensionService service, CancellationToken ct) =>
                service.InstallFromUrlAsync(input.Url, ct))
            .WithName("InstallExtensionFromUrl")
            .WithSummary("Downloads an extension's .zip and installs it (or updates it); it runs once enabled and the server restarts. Extensions run inside the server with full trust. Administrators only, with a recent authentication.");

        extensions.MapPut("/{id}/enabled", (string id, SetExtensionEnabledInput input, ExtensionService service, CancellationToken ct) =>
                service.SetEnabledAsync(id, input.Enabled, ct))
            .WithName("SetExtensionEnabled")
            .WithSummary("Switches an extension on or off. Built-in extensions and switching off apply at once; an installed one runs after a restart. Administrators only, with a recent authentication.");

        extensions.MapDelete("/{id}", async (string id, ExtensionService service, CancellationToken ct) =>
            {
                await service.UninstallAsync(id, ct);
                return Results.NoContent();
            })
            .WithName("UninstallExtension")
            .WithSummary("Removes an installed extension (it stops at once and is gone after a restart). Administrators only, with a recent authentication.");

        return rest;
    }
}
