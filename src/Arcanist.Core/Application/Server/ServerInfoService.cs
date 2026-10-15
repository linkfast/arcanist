using System.Reflection;
using Arcanist.Core.Capabilities;
using Microsoft.Extensions.Options;

namespace Arcanist.Core.Application.Server;

/// <summary>Bound from <c>Arcanist</c>.</summary>
public sealed class ServerOptions
{
    /// <summary>
    /// Where users of this server can obtain its source code, as required by section 13 of the AGPL. Anyone
    /// running a modified version must point this to their modified source. Defaults to the repository the build
    /// came from (<c>RepositoryUrl</c> in <c>Directory.Build.props</c>).
    /// </summary>
    public string? SourceUrl { get; set; } = DefaultSourceUrl;

    internal static readonly string? DefaultSourceUrl = typeof(ServerOptions).Assembly
        .GetCustomAttributes<AssemblyMetadataAttribute>()
        .FirstOrDefault(a => a.Key == "RepositoryUrl")?.Value;
}

/// <summary>Public description of the server, for clients deciding how to talk to it. Part of the API contract.</summary>
/// <param name="ApiVersion">Major version of the GraphQL and REST contracts. Incremented only on breaking changes.</param>
/// <param name="Capabilities">Ultimate Cloud features currently enabled; local features are always available.</param>
/// <param name="TranscodingFormats">Formats the stream endpoint can convert to (none without ffmpeg).</param>
/// <param name="RatingMode">How users rate tracks here: show five stars, or a single star.</param>
/// <param name="Features">Parts of the API this version has, for clients that also talk to older servers.</param>
public sealed record ServerInfo(
    string Name,
    string Version,
    int ApiVersion,
    string? SourceUrl,
    bool SetupCompleted,
    IReadOnlyList<Capability> Capabilities,
    CloudTier CloudTier,
    IReadOnlyList<Core.Media.TranscodeFormat> TranscodingFormats,
    Data.Entities.RatingMode RatingMode,
    IReadOnlyList<ApiFeature> Features);

/// <summary>A part of the API that clients may need to know a server has (older versions lack it).</summary>
public enum ApiFeature
{
    /// <summary><c>libraryId</c> on tracks, albums and artists, and <c>Track.libraryIds</c>.</summary>
    LibraryFilter,

    /// <summary>Uploading files to a library (<c>POST /rest/v1/libraries/{id}/uploads</c>, permission UPLOAD).</summary>
    Uploads,

    /// <summary><c>matchTracks</c>: finding the server's tracks for an app's own.</summary>
    TrackMatching,

    /// <summary><c>importListening</c>: bringing an app's ratings and play counts.</summary>
    ListeningImport,
}

public sealed class ServerInfoService(
    ICapabilityService capabilities,
    ICloudLinkState cloudLink,
    IOptionsMonitor<ServerOptions> options,
    IOptionsMonitor<Setup.SetupOptions> setup,
    Core.Media.FfmpegLocator ffmpeg,
    Core.Media.Transcoder transcoder,
    Listening.RatingSettings ratings)
{
    public const int ApiVersion = 1;

    private static readonly string Version =
        typeof(ServerInfoService).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            .Split('+')[0]
        ?? "0.0.0";

    /// <summary>Available without authentication.</summary>
    public ServerInfo Get() => new(
        "Arcanist",
        Version,
        ApiVersion,
        options.CurrentValue.SourceUrl,
        setup.CurrentValue.Completed,
        Enum.GetValues<Capability>().Where(capabilities.IsEnabled).ToList(),
        cloudLink.Tier,
        transcoder.Formats(ffmpeg.Current),
        ratings.Current,
        Enum.GetValues<ApiFeature>());
}
