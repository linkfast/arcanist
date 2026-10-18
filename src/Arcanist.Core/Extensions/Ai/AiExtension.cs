using Arcanist.Extensibility;
using Microsoft.AspNetCore.Routing;

namespace Arcanist.Core.Extensions.Ai;

/// <summary>
/// AI: what clients with AI assistance need to understand the music better. Today, the analysis for DJ software
/// (beat grid, sections, energy, voice, mix and scratch points) that the AI DJ of ULTIMATE plays with, done here so
/// clients never download the audio for it. Off by default. Its API lives with the rest (<c>/rest/v1/tracks/…/analysis</c>,
/// <c>trackAnalysis</c>, <c>/rest/v1/ai</c>) and answers <c>ai_disabled</c> while it is off.
/// </summary>
internal sealed class AiExtension : IBuiltInExtension
{
    public const string Id = "ai";

    public ExtensionManifest Manifest { get; } = new(
        Id,
        "AI",
        typeof(AiExtension).Assembly.GetName().Version?.ToString(3) ?? "1.0.0",
        "Analyzes the library for clients with AI assistance, such as the AI DJ of ULTIMATE: beat grids, sections, energy, voice, and points to mix and scratch.",
        "LinkFast",
        "AGPL-3.0-or-later");

    public void MapEndpoints(IEndpointRouteBuilder endpoints, ExtensionContext context)
    {
        // Nothing of its own: its operations are part of the API (/rest/v1 and GraphQL), which extensions cannot map.
    }
}
