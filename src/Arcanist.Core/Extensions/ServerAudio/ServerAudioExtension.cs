using Arcanist.Extensibility;
using Microsoft.AspNetCore.Routing;

namespace Arcanist.Core.Extensions.ServerAudio;

/// <summary>
/// Server audio: plays music on an audio device connected to the server (speakers, a USB DAC, HDMI). Off by default.
/// On, the server is a device to play to: in Arcanist's "play to" (the renderer <c>server</c>) and, with an Ultimate
/// Cloud account linked, in Ultimate Connect. Its API lives with the rest (<c>/rest/v1/server-audio</c>,
/// <c>serverAudio</c>) and answers <c>server_audio_disabled</c> while it is off.
/// </summary>
internal sealed class ServerAudioExtension : IBuiltInExtension
{
    public const string Id = "server-audio";

    public ExtensionManifest Manifest { get; } = new(
        Id,
        "Server audio",
        typeof(ServerAudioExtension).Assembly.GetName().Version?.ToString(3) ?? "1.0.0",
        "Plays music on an audio device of the server, controlled from the players through Arcanist's \"play to\" and Ultimate Connect.",
        "LinkFast",
        "AGPL-3.0-or-later");

    public void MapEndpoints(IEndpointRouteBuilder endpoints, ExtensionContext context)
    {
        // Nothing of its own: its operations are part of the API (/rest/v1 and GraphQL), which extensions cannot map.
    }
}
