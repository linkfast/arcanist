namespace Arcanist.Core.Cloud;

/// <summary>
/// Tells whoever cares (extensions, through <c>IUltimateCloud.Changed</c>) that Ultimate Cloud's accounts changed: one
/// was linked or unlinked, a plan was checked, or Ultimate Cloud was turned on or off.
/// </summary>
public sealed class UltimateCloudSignals(ILogger<UltimateCloudSignals> logger)
{
    public event Action? Changed;

    public void Raise()
    {
        foreach (var handler in Changed?.GetInvocationList() ?? [])
        {
            try
            {
                ((Action)handler)();
            }
            catch (Exception e)
            {
                // One listener failing does not keep the others from hearing it.
                logger.LogWarning(e, "A listener of Ultimate Cloud's changes failed");
            }
        }
    }
}
