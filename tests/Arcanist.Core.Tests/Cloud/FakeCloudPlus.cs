using Arcanist.Extensibility;
using Microsoft.Extensions.DependencyInjection;

namespace Arcanist.Core.Tests.Cloud;

/// <summary>
/// A stand-in for Ultimate Cloud Plus (the extension with the Cloud plan's features), through the SDK only: S3 libraries
/// and backups while an account has the Cloud plan. Its real rules (an S3 library is never the only one…) are tested
/// with the extension, in its own repository.
/// </summary>
public sealed class FakeCloudPlus(IUltimateCloud cloud) : ICloudPlusFeatures, IS3LibraryPolicy
{
    public static void Register(IServiceCollection services)
    {
        services.AddSingleton<FakeCloudPlus>();
        services.AddSingleton<ICloudPlusFeatures>(provider => provider.GetRequiredService<FakeCloudPlus>());
        services.AddSingleton<IS3LibraryPolicy>(provider => provider.GetRequiredService<FakeCloudPlus>());
    }

    private bool Paid => cloud.ServerPlan == CloudPlan.Paid;

    public bool CloudSync => Paid;

    public bool ThirdPartyStorage => Paid;

    public bool CloudConnect => cloud.ServerPlan != CloudPlan.None;

    public bool WishListSync => false;

    public Task<string?> CanCreateAsync(CancellationToken cancellationToken) => Task.FromResult(Paid ? null : "s3_unavailable");

    public bool CanUse => Paid;

    public Task<string?> CanDeleteAsync(Guid libraryId, CancellationToken cancellationToken) => Task.FromResult<string?>(null);
}
