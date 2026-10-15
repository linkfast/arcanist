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

    [Name("refreshUltimateCloud")]
    [Description("Reads the account's plan again now. Administrators only.")]
    public static Task<UltimateCloudStatus> RefreshUltimateCloud([FromServices] UltimateCloudService cloud, CancellationToken cancellationToken) =>
        cloud.RefreshAsync(cancellationToken);
}
