using Arcanist.Extensibility;
using Microsoft.AspNetCore.Routing;

namespace Arcanist.Core.Extensions.UltimateCloud;

/// <summary>
/// Everything that talks to Ultimate Cloud, LinkFast's music service: linking an account, its libraries and Cloud
/// Drop, syncing, S3 libraries and the transparent P2P connection (which depend on its plan), and its online services
/// (play counts for the rankings, StarTracks, the account's statistics). On by default; switched off, Arcanist never
/// contacts Ultimate Cloud and those libraries are hidden. Its API lives with the rest (<c>/rest/v1/ultimate-cloud</c>,
/// <c>ultimateCloud</c>) and answers <c>cloud_disabled</c> while it is off.
/// </summary>
internal sealed class UltimateCloudExtension : IBuiltInExtension
{
    public const string Id = "ultimate-cloud";

    public ExtensionManifest Manifest { get; } = new(
        Id,
        "Ultimate Cloud",
        typeof(UltimateCloudExtension).Assembly.GetName().Version?.ToString(3) ?? "1.0.0",
        "Ultimate Cloud by LinkFast: its libraries and Cloud Drop, syncing, S3 libraries, the transparent P2P connection and its online services (rankings, StarTracks, statistics).",
        "LinkFast",
        "AGPL-3.0-or-later");

    public bool EnabledByDefault => true;

    public void MapEndpoints(IEndpointRouteBuilder endpoints, ExtensionContext context)
    {
        // Nothing of its own: its operations are part of the API (/rest/v1 and GraphQL), which extensions cannot map.
    }
}
