using System.ComponentModel;
using Arcanist.Core.Application.Server;
using GraphQL;

namespace Arcanist.Core.Api.GraphQL;

public sealed partial class Query
{
    [Name("storageUsage")]
    [Description("How much space Arcanist takes, part by part (the database too), its database's tables, how much is in the catalog and the music of each library. Measured at most every five minutes unless refresh is true. Administrators only.")]
    public static Task<StorageUsage> StorageUsage([FromServices] StorageUsageService storage, CancellationToken cancellationToken, bool refresh = false) =>
        storage.UsageAsync(refresh, cancellationToken);
}
