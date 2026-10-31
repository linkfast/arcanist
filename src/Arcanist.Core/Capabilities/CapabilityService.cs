using Arcanist.Core.Extensions;
using Arcanist.Extensibility;

namespace Arcanist.Core.Capabilities;

public interface ICapabilityService
{
    bool IsEnabled(Capability capability);
}

/// <summary>Tier of the Ultimate Cloud account linked to this server.</summary>
public interface ICloudLinkState
{
    CloudTier Tier { get; }
}

/// <summary>
/// What Ultimate Cloud lets this server do. Using Ultimate Cloud as a library source needs any linked account; the
/// Cloud plan's features (sync, S3, the transparent connection) are not part of Arcanist: an extension provides them
/// (<see cref="ICloudPlusFeatures"/>, Ultimate Cloud Plus), and they are off without one.
/// </summary>
public sealed class CapabilityService(ICloudLinkState cloudLink, ExtensionHost extensions, IServiceProvider services) : ICapabilityService
{
    public bool IsEnabled(Capability capability)
    {
        if (capability == Capability.UltimateCloudSource)
            return cloudLink.Tier >= CloudTier.Free;
        if (extensions.Active<ICloudPlusFeatures>(services) is not { } plus)
            return false;
        return capability switch
        {
            Capability.CloudSync => plus.CloudSync,
            Capability.ThirdPartyStorage => plus.ThirdPartyStorage,
            Capability.CloudConnect => plus.CloudConnect,
            _ => throw new ArgumentOutOfRangeException(nameof(capability), capability, null),
        };
    }

    /// <summary>The tier a capability needs when it is available at all.</summary>
    public static CloudTier RequiredTier(Capability capability) => capability switch
    {
        Capability.UltimateCloudSource or Capability.CloudConnect => CloudTier.Free,
        Capability.CloudSync or Capability.ThirdPartyStorage => CloudTier.Paid,
        _ => throw new ArgumentOutOfRangeException(nameof(capability), capability, null),
    };
}

public static class CapabilityServiceCollectionExtensions
{
    public static IServiceCollection AddCapabilities(this IServiceCollection services)
    {
        services.AddSingleton<ICapabilityService, CapabilityService>();
        return services;
    }
}
