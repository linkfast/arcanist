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

        cloud.MapPost("/libraries", (AddUltimateCloudLibraryInput input, UltimateCloudService service, CancellationToken ct) =>
                service.AddLibraryAsync(input, ct))
            .WithName("AddUltimateCloudLibrary")
            .WithSummary("Shows a collection of the account (its library or Cloud Drop) as a library of this server; nothing is downloaded. " +
                         "Administrators only, with a recent authentication.");

        cloud.MapPost("/refresh", (UltimateCloudService service, CancellationToken ct) => service.RefreshAsync(ct))
            .WithName("RefreshUltimateCloud")
            .WithSummary("Reads the account's plan again now. Administrators only.");

        cloud.MapGet("/syncs", (CloudSyncService service, CancellationToken ct) => service.ListAsync(ct))
            .WithName("CloudSyncs")
            .WithSummary("How each library with folders on this server syncs with the Ultimate Cloud account. Administrators only.");

        rest.MapPut("/libraries/{libraryId:guid}/cloud-sync", (Guid libraryId, SetLibraryCloudSyncInput input, CloudSyncService service, CancellationToken ct) =>
                service.SetAsync(libraryId, input, ct))
            .WithTags("Ultimate Cloud")
            .WithName("SetLibraryCloudSync")
            .WithSummary("Uploads, downloads or both between a library and the Ultimate Cloud account (Cloud plan); nothing is deleted on either side. " +
                         "Administrators only; turning it on needs a recent authentication.");

        rest.MapPost("/libraries/{libraryId:guid}/cloud-sync/run", (Guid libraryId, CloudSyncService service, CancellationToken ct) =>
                service.SyncNowAsync(libraryId, ct))
            .WithTags("Ultimate Cloud")
            .WithName("SyncLibraryWithCloud")
            .WithSummary("Syncs a library with Ultimate Cloud now (otherwise every 6 hours). Administrators only.");

        return rest;
    }
}
