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

        cloud.MapPut("/sync", (SetUltimateCloudSyncInput input, UltimateCloudService service, CancellationToken ct) => service.SetSyncAsync(input.Enabled, ct))
            .WithName("SetUltimateCloudSync")
            .WithSummary("Turns Ultimate Cloud's sync component (ultimate-audio, closed source) on or off: on, it is installed and syncing libraries, Cloud Drop and uploads to Ultimate Cloud work, and the server's edition is ULTIMATE; off, it is removed. Administrators only, with a recent authentication.");

        cloud.MapPost("/refresh", (UltimateCloudService service, CancellationToken ct) => service.RefreshAsync(ct))
            .WithName("RefreshUltimateCloud")
            .WithSummary("Reads the account's plan again now. Administrators only.");

        cloud.MapPut("/services", (UltimateCloudServicesInput input, UltimateCloudService service, CancellationToken ct) => service.SetServicesAsync(input, ct))
            .WithName("SetUltimateCloudServices")
            .WithSummary("Chooses which of Ultimate Cloud's online services this server takes part in: plays for the statistics and rankings, " +
                         "albums heard whole, StarTracks, the account's statistics. Administrators only, with a recent authentication.");

        cloud.MapGet("/accounts", (UltimateCloudService service, CancellationToken ct) => service.AccountsAsync(ct))
            .WithName("UltimateCloudAccounts")
            .WithSummary("The Ultimate Cloud accounts linked to this server, for choosing where uploads go. Administrators only.");

        cloud.MapGet("/metrics", (UltimateCloudService service, CancellationToken ct) => service.MetricsAsync(ct))
            .WithName("UltimateCloudMetrics")
            .WithSummary("The linked account's listening statistics as Ultimate Cloud counts them. Administrators only.");

        cloud.MapGet("/syncs", (CloudSyncService service, CancellationToken ct) => service.ListAsync(ct))
            .WithName("CloudSyncs")
            .WithSummary("How each library with folders on this server syncs with the Ultimate Cloud account. Administrators only.");

        var mine = rest.MapGroup("/me/ultimate-cloud").WithTags("Ultimate Cloud");

        mine.MapGet("", (MyUltimateCloudService service, CancellationToken ct) => service.StatusAsync(ct))
            .WithName("MyUltimateCloud")
            .WithSummary("The caller's own Ultimate Cloud account and what it brings them: their music as a personal library, their plays, statistics, StarTracks and devices.");

        mine.MapPost("/link", (StartMyUltimateCloudLinkInput input, MyUltimateCloudService service, CancellationToken ct) => service.StartLinkAsync(input, ct))
            .WithName("StartMyUltimateCloudLink")
            .WithSummary("Asks Ultimate Cloud for a code to approve on its site, to link the caller's own account; the server waits for the approval. " +
                         "Needs a recent authentication.");

        mine.MapPost("/link/cancel", (MyUltimateCloudService service, CancellationToken ct) => service.CancelLinkAsync(ct))
            .WithName("CancelMyUltimateCloudLink")
            .WithSummary("Stops waiting for the approval of the caller's code.");

        mine.MapPost("/unlink", (MyUltimateCloudService service, CancellationToken ct) => service.UnlinkAsync(ct))
            .WithName("UnlinkMyUltimateCloud")
            .WithSummary("Forgets the caller's own account and removes their personal library; nothing is touched in Ultimate Cloud. Needs a recent authentication.");

        mine.MapPost("/refresh", (MyUltimateCloudService service, CancellationToken ct) => service.RefreshAsync(ct))
            .WithName("RefreshMyUltimateCloud")
            .WithSummary("Reads the plan of the caller's own account again now.");

        mine.MapPut("/services", (UltimateCloudServicesInput input, MyUltimateCloudService service, CancellationToken ct) => service.SetServicesAsync(input, ct))
            .WithName("SetMyUltimateCloudServices")
            .WithSummary("Chooses what the caller's own account gets from this server: their plays, albums heard whole, StarTracks, statistics.");

        mine.MapGet("/metrics", (MyUltimateCloudService service, CancellationToken ct) => service.MetricsAsync(ct))
            .WithName("MyUltimateCloudMetrics")
            .WithSummary("The caller's own account's listening statistics as Ultimate Cloud counts them.");

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
