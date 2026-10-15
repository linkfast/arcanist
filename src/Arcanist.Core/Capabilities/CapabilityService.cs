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

public sealed class CapabilityService(ICloudLinkState cloudLink) : ICapabilityService
{
    public bool IsEnabled(Capability capability) => cloudLink.Tier >= RequiredTier(capability);

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
