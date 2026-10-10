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

/// <summary>Used until account linking exists: no cloud capabilities are enabled.</summary>
internal sealed class UnlinkedCloudLinkState : ICloudLinkState
{
    public CloudTier Tier => CloudTier.None;
}

public sealed class CapabilityService(ICloudLinkState cloudLink) : ICapabilityService
{
    public bool IsEnabled(Capability capability) => cloudLink.Tier >= RequiredTier(capability);

    public static CloudTier RequiredTier(Capability capability) => capability switch
    {
        Capability.UltimateCloudSource => CloudTier.Free,
        Capability.CloudSync or Capability.ThirdPartyStorage or Capability.CloudConnect => CloudTier.Paid,
        _ => throw new ArgumentOutOfRangeException(nameof(capability), capability, null),
    };
}

public static class CapabilityServiceCollectionExtensions
{
    public static IServiceCollection AddCapabilities(this IServiceCollection services)
    {
        services.AddSingleton<ICloudLinkState, UnlinkedCloudLinkState>();
        services.AddSingleton<ICapabilityService, CapabilityService>();
        return services;
    }
}
