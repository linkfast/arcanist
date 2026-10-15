using Arcanist.Core.Application.Cloud;

namespace Arcanist.Core.Api.Rest;

public static class UltimateCloudEndpoints
{
    public static IEndpointRouteBuilder MapUltimateCloudEndpoints(this IEndpointRouteBuilder rest)
    {
        var cloud = rest.MapGroup("/ultimate-cloud").WithTags("Ultimate Cloud");

        cloud.MapGet("", (UltimateCloudService service, CancellationToken ct) => service.StatusAsync(ct))
            .WithName("UltimateCloud")
            .WithSummary("The linked Ultimate Cloud account, its plan and what it enables. Administrators only.");

        cloud.MapPost("/link", (UltimateCloudService service, CancellationToken ct) => service.StartLinkAsync(ct))
            .WithName("StartUltimateCloudLink")
            .WithSummary("Asks Ultimate Cloud for a code to approve on its site; the server waits for the approval. " +
                         "Administrators only, with a recent authentication.");

        cloud.MapPost("/link/cancel", (UltimateCloudService service, CancellationToken ct) => service.CancelLinkAsync(ct))
            .WithName("CancelUltimateCloudLink")
            .WithSummary("Stops waiting for a code's approval. Administrators only.");

        cloud.MapPost("/unlink", (UltimateCloudService service, CancellationToken ct) => service.UnlinkAsync(ct))
            .WithName("UnlinkUltimateCloud")
            .WithSummary("Forgets the account; only the Ultimate Cloud features turn off. Administrators only, with a recent authentication.");

        cloud.MapPost("/refresh", (UltimateCloudService service, CancellationToken ct) => service.RefreshAsync(ct))
            .WithName("RefreshUltimateCloud")
            .WithSummary("Reads the account's plan again now. Administrators only.");

        return rest;
    }
}
