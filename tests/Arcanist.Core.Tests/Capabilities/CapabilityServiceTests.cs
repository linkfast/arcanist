using Arcanist.Core.Capabilities;

namespace Arcanist.Core.Tests.Capabilities;

public sealed class CapabilityServiceTests
{
    [Theory]
    [InlineData(CloudTier.None, false)]
    [InlineData(CloudTier.Free, true)]
    [InlineData(CloudTier.Paid, true)]
    public void Ultimate_Cloud_as_a_library_source_follows_the_linked_cloud_tier(CloudTier tier, bool expected)
    {
        // The Cloud plan's features come from Ultimate Cloud Plus (an extension): UltimateCloudTests and the S3 tests cover them.
        var service = new CapabilityService(new FixedTier(tier), null!, null!);

        Assert.Equal(expected, service.IsEnabled(Capability.UltimateCloudSource));
    }
    [Fact]
    public void Every_capability_declares_a_required_tier()
    {
        foreach (var capability in Enum.GetValues<Capability>())
            Assert.NotEqual(CloudTier.None, CapabilityService.RequiredTier(capability));
    }

    private sealed class FixedTier(CloudTier tier) : ICloudLinkState
    {
        public CloudTier Tier => tier;
    }
}
