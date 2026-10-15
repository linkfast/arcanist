using System.ComponentModel;
using Arcanist.Core.Application.Cloud;
using GraphQL;

namespace Arcanist.Core.Api.GraphQL;

public sealed partial class Query
{
    [Name("ultimateCloud")]
    [Description("The linked Ultimate Cloud account, its plan and what it enables. Administrators only.")]
    public static Task<UltimateCloudStatus> UltimateCloud([FromServices] UltimateCloudService cloud, CancellationToken cancellationToken) =>
        cloud.StatusAsync(cancellationToken);

    [Name("ultimateCloudMetrics")]
    [Description("The linked account's listening statistics as Ultimate Cloud counts them. Administrators only.")]
    public static Task<UltimateCloudMetrics> UltimateCloudMetrics([FromServices] UltimateCloudService cloud, CancellationToken cancellationToken) =>
        cloud.MetricsAsync(cancellationToken);

    [Name("cloudSyncs")]
    [Description("How each library with folders on this server syncs with the Ultimate Cloud account. Administrators only.")]
    public static Task<IReadOnlyList<LibraryCloudSyncState>> CloudSyncs([FromServices] CloudSyncService sync, CancellationToken cancellationToken) =>
        sync.ListAsync(cancellationToken);
}

public sealed partial class Mutation
{
    [Name("startUltimateCloudLink")]
    [Description("Asks Ultimate Cloud for a code to approve on its site; the server waits for the approval. Administrators only, with a recent authentication.")]
    public static Task<UltimateCloudStatus> StartUltimateCloudLink([FromServices] UltimateCloudService cloud, CancellationToken cancellationToken) =>
        cloud.StartLinkAsync(cancellationToken);

    [Name("cancelUltimateCloudLink")]
    [Description("Stops waiting for a code's approval. Administrators only.")]
    public static Task<UltimateCloudStatus> CancelUltimateCloudLink([FromServices] UltimateCloudService cloud, CancellationToken cancellationToken) =>
        cloud.CancelLinkAsync(cancellationToken);

    [Name("unlinkUltimateCloud")]
    [Description("Forgets the account; only the Ultimate Cloud features turn off. Administrators only, with a recent authentication.")]
    public static Task<UltimateCloudStatus> UnlinkUltimateCloud([FromServices] UltimateCloudService cloud, CancellationToken cancellationToken) =>
        cloud.UnlinkAsync(cancellationToken);

    [Name("addUltimateCloudLibrary")]
    [Description("Shows a collection of the account (its library or Cloud Drop) as a library of this server; nothing is downloaded. Administrators only, with a recent authentication.")]
    public static Task<UltimateCloudStatus> AddUltimateCloudLibrary(
        AddUltimateCloudLibraryInput input, [FromServices] UltimateCloudService cloud, CancellationToken cancellationToken) =>
        cloud.AddLibraryAsync(input, cancellationToken);

    [Name("setLibraryCloudSync")]
    [Description("Uploads, downloads or both between a library and the Ultimate Cloud account (Cloud plan); nothing is deleted on either side. Administrators only; turning it on needs a recent authentication.")]
    public static Task<LibraryCloudSyncState> SetLibraryCloudSync(
        [Id] Guid libraryId, SetLibraryCloudSyncInput input, [FromServices] CloudSyncService sync, CancellationToken cancellationToken) =>
        sync.SetAsync(libraryId, input, cancellationToken);

    [Name("syncLibraryWithCloud")]
    [Description("Syncs a library with Ultimate Cloud now (otherwise every 6 hours). Administrators only.")]
    public static Task<LibraryCloudSyncState> SyncLibraryWithCloud([Id] Guid libraryId, [FromServices] CloudSyncService sync, CancellationToken cancellationToken) =>
        sync.SyncNowAsync(libraryId, cancellationToken);

    [Name("setUltimateCloudServices")]
    [Description("Chooses which of Ultimate Cloud's online services this server takes part in: plays for the statistics and rankings, albums heard whole, StarTracks, the account's statistics. Administrators only, with a recent authentication.")]
    public static Task<UltimateCloudStatus> SetUltimateCloudServices(UltimateCloudServicesInput input, [FromServices] UltimateCloudService cloud, CancellationToken cancellationToken) =>
        cloud.SetServicesAsync(input, cancellationToken);

    [Name("refreshUltimateCloud")]
    [Description("Reads the account's plan again now. Administrators only.")]
    public static Task<UltimateCloudStatus> RefreshUltimateCloud([FromServices] UltimateCloudService cloud, CancellationToken cancellationToken) =>
        cloud.RefreshAsync(cancellationToken);
}
