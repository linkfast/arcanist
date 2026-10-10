using Arcanist.Core.Capabilities;

namespace Arcanist.Core.Tests.Capabilities;

public sealed class CapabilityServiceTests
{
    [Theory]
    [InlineData(CloudTier.None, Capability.UltimateCloudSource, false)]
    [InlineData(CloudTier.None, Capability.CloudSync, false)]
    [InlineData(CloudTier.Free, Capability.UltimateCloudSource, true)]
    [InlineData(CloudTier.Free, Capability.CloudSync, false)]
    [InlineData(CloudTier.Free, Capability.ThirdPartyStorage, false)]
    [InlineData(CloudTier.Free, Capability.CloudConnect, false)]
    [InlineData(CloudTier.Paid, Capability.UltimateCloudSource, true)]
    [InlineData(CloudTier.Paid, Capability.CloudSync, true)]
    [InlineData(CloudTier.Paid, Capability.ThirdPartyStorage, true)]
    [InlineData(CloudTier.Paid, Capability.CloudConnect, true)]
    public void Capabilities_follow_the_linked_cloud_tier(CloudTier tier, Capability capability, bool expected)
    {
        var service = new CapabilityService(new FixedTier(tier));

        Assert.Equal(expected, service.IsEnabled(capability));
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
