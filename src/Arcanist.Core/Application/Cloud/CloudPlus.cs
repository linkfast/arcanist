using Arcanist.Core.Extensions;
using Arcanist.Extensibility;

namespace Arcanist.Core.Application.Cloud;

/// <summary>
/// The Cloud plan's features, which are not part of Arcanist: an extension provides them (Ultimate Cloud Plus,
/// <see cref="ExtensionId"/>). The server keeps their API, settings and records, and asks the extension's providers
/// to do the work; without it (not installed, switched off, or blocked by the 100% open source mode) they answer
/// <c>cloud_plus_required</c> and S3 libraries are unavailable.
/// </summary>
public sealed class CloudPlus(ExtensionHost extensions, IServiceProvider services)
{
    /// <summary>The extension's id in the marketplace, offered to install when its features are asked for.</summary>
    public const string ExtensionId = "ultimate-cloud-plus";

    public ICloudPlusFeatures? Features => extensions.Active<ICloudPlusFeatures>(services);

    public ICloudSyncProvider? Sync => extensions.Active<ICloudSyncProvider>(services);

    public IMyCloudUploadProvider? Uploads => extensions.Active<IMyCloudUploadProvider>(services);

    public IS3LibraryPolicy? S3 => extensions.Active<IS3LibraryPolicy>(services);

    /// <summary>Whether its extension provides anything now.</summary>
    public bool Available => Features is not null;

    /// <summary>The extension is installed (whether it runs or not).</summary>
    public bool Installed => extensions.Find(ExtensionId) is not null;
}
