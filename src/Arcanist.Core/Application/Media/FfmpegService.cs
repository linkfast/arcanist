using Arcanist.Core.Analysis;
using Arcanist.Core.Data;
using Arcanist.Core.Data.Entities;
using Arcanist.Core.Media;

namespace Arcanist.Core.Application.Media;

// FfmpegStatus is part of the public API contracts (GraphQL schema and OpenAPI document).

/// <summary>Where Arcanist found ffmpeg, which it needs to transcode and to analyze some formats.</summary>
/// <param name="Path">The ffmpeg in use, if any.</param>
/// <param name="ConfiguredPath">The path set in the setup wizard, the admin panel or <c>Arcanist:Ffmpeg:Path</c>.</param>
/// <param name="ConfiguredPathWorks">False when a configured path is not a working ffmpeg (another one may be in use).</param>
/// <param name="CanDownload">Whether a build can be downloaded for this operating system and processor.</param>
public sealed record FfmpegStatus(
    bool Available,
    string? Path,
    string? Version,
    FfmpegSource? Source,
    string? ConfiguredPath,
    bool ConfiguredPathWorks,
    bool CanDownload,
    FfmpegDownloadProgress Download);

public sealed class FfmpegService(
    AccessGuard guard,
    FfmpegLocator locator,
    FfmpegDownloader downloader,
    SettingsStore settings,
    AnalysisMonitor analysis)
{
    /// <summary>Requires MANAGE_SETTINGS.</summary>
    public async Task<FfmpegStatus> StatusAsync(CancellationToken cancellationToken)
    {
        guard.Permission(Permissions.ManageSettings);
        return Status(await locator.ResolveAsync(cancellationToken));
    }

    /// <summary>
    /// Uses the ffmpeg at <paramref name="path"/>, or finds one automatically when null. Administrators only, after
    /// entering the password again: the server will run that file.
    /// </summary>
    public async Task<FfmpegStatus> SetPathAsync(string? path, CancellationToken cancellationToken)
    {
        guard.Admin();
        guard.RecentAuthentication();

        path = string.IsNullOrWhiteSpace(path) ? null : path.Trim();
        if (path is not null && await FfmpegLocator.ProbeAsync(path, cancellationToken) is null)
            throw ArcanistException.Field("path", ErrorCodes.NotFfmpeg);

        await settings.SetAsync(SettingKeys.FfmpegPath, path, cancellationToken);
        var resolution = await locator.RefreshAsync(cancellationToken);
        analysis.RequestRun();
        return Status(resolution);
    }

    /// <summary>Downloads ffmpeg in the background; follow it through the status. Administrators only, with step-up.</summary>
    public async Task<FfmpegStatus> InstallAsync(CancellationToken cancellationToken)
    {
        guard.Admin();
        guard.RecentAuthentication();

        if (FfmpegDownloader.Platform is null)
            throw new ArcanistException.Conflict(ErrorCodes.FfmpegPlatformUnsupported);

        downloader.Start();
        return Status(await locator.ResolveAsync(cancellationToken));
    }

    private FfmpegStatus Status(FfmpegLocator.Resolution resolution) => new(
        resolution.Installation is not null,
        resolution.Installation?.Path,
        resolution.Installation?.Version,
        resolution.Installation?.Source,
        resolution.ConfiguredPath,
        resolution.ConfiguredPathWorks,
        FfmpegDownloader.Platform is not null,
        downloader.Progress);
}
