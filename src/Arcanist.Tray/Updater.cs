using Velopack;
using Velopack.Sources;

namespace Arcanist.Tray;

/// <summary>
/// Updates through Velopack from the project's GitHub Releases. Updates download in the background; applying one
/// restarts the app, so it waits for the user to choose it from the menu.
/// </summary>
internal sealed class Updater
{
    private readonly UpdateManager _manager = new(new GithubSource(Installation.RepositoryUrl, accessToken: null, prerelease: false));

    /// <summary>False when running from a build folder instead of an installation, where there is nothing to update.</summary>
    public bool IsSupported => _manager.IsInstalled;

    public string? CurrentVersion => _manager.CurrentVersion?.ToString();

    /// <summary>Downloaded and ready to apply.</summary>
    public UpdateInfo? Ready { get; private set; }

    /// <summary>Checks for a newer release and downloads it. Returns whether one is ready.</summary>
    public async Task<bool> CheckAsync()
    {
        if (!IsSupported)
            return false;

        if (await _manager.CheckForUpdatesAsync() is { } update)
        {
            await _manager.DownloadUpdatesAsync(update);
            Ready = update;
        }

        return Ready is not null;
    }

    /// <summary>Exits this process; Velopack swaps the files and starts the new version with <paramref name="restartArgs"/>.</summary>
    public void ApplyAndRestart(string[] restartArgs)
    {
        if (Ready is not null)
            _manager.ApplyUpdatesAndRestart(Ready.TargetFullRelease, restartArgs);
    }
}
