using System.Diagnostics;
using System.Security.Principal;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform;
using Avalonia.Threading;

namespace Arcanist.Tray;

internal enum ServerState { Stopped, Starting, Running, Stopping, Failed, Missing }

/// <summary>
/// The tray icon and its menu. Runs the server as a child process, or controls the Windows service when one is
/// installed, and keeps the menu in sync with what it finds every couple of seconds.
/// </summary>
internal sealed class TrayController : IDisposable
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan UpdateCheckInterval = TimeSpan.FromHours(12);

    private readonly IClassicDesktopStyleApplicationLifetime _lifetime;
    private readonly Installation _installation = Installation.Detect();
    private readonly ServerProcess _server;
    private readonly ServerProbe _probe;
    private readonly Updater _updater = new();
    private readonly Strings _text = Strings.Current;
    private readonly TrayIcon _icon;
    private readonly DispatcherTimer _refreshTimer;
    private readonly DispatcherTimer _updateTimer;

    private readonly NativeMenuItem _status = new() { IsEnabled = false };
    private readonly NativeMenuItem _openPlayer;
    private readonly NativeMenuItem _openAdmin;
    private readonly NativeMenuItem _start;
    private readonly NativeMenuItem _stop;
    private readonly NativeMenuItem _restart;
    private readonly NativeMenuItem _autoStart;
    private readonly NativeMenuItem _installService;
    private readonly NativeMenuItem _updateService;
    private readonly NativeMenuItem _uninstallService;
    private readonly NativeMenuItem _updates;

    private Uri? _url;
    private bool _refreshing;
    private bool _busy;
    private bool _failed;
    private int _crashes;
    private bool _checkingUpdates;
    private bool _upToDate;
    private bool _openWhenReady = Program.FirstRun;

    public TrayController(IClassicDesktopStyleApplicationLifetime lifetime)
    {
        _lifetime = lifetime;
        _server = new ServerProcess(_installation);
        _probe = new ServerProbe(_installation);
        _server.ExitedUnexpectedly += _ => Dispatcher.UIThread.Post(OnServerCrashed);

        _openPlayer = Item(_text.OpenPlayer, () => Open(_url));
        _openAdmin = Item(_text.OpenAdmin, () => Open(_url is null ? null : new Uri(_url, "admin/")));
        _start = Item(_text.Start, () => RunAsync(StartAsync));
        _stop = Item(_text.Stop, () => RunAsync(StopAsync));
        _restart = Item(_text.Restart, () => RunAsync(async () =>
        {
            await StopAsync();
            await StartAsync();
        }));
        _autoStart = Item(_text.StartAutomatically, () => AutoStart.Set(!AutoStart.IsEnabled));
        _autoStart.ToggleType = MenuItemToggleType.CheckBox;
        _installService = Item(_text.InstallService, () => RunAsync(InstallServiceAsync));
        _updateService = Item("", () => RunAsync(UpdateServiceAsync));
        _uninstallService = Item(_text.UninstallService, () => RunAsync(UninstallServiceAsync));
        _updates = Item(_text.CheckForUpdates, () => _updater.Ready is null ? CheckForUpdatesAsync(manual: true) : ApplyUpdateAsync());

        var menu = new NativeMenu();
        foreach (var item in new NativeMenuItemBase[]
                 {
                     _status, _openPlayer, _openAdmin, new NativeMenuItemSeparator(),
                     _start, _stop, _restart, Item(_text.OpenLogs, () => Open(_installation.LogsDirectory)), new NativeMenuItemSeparator(),
                     _autoStart, _installService, _updateService, _uninstallService, new NativeMenuItemSeparator(),
                     _updates, Item(_text.SourceCode, () => Open(Installation.RepositoryUrl)), Item(_text.Exit, () => RunAsync(ExitAsync)),
                 })
        {
            menu.Items.Add(item);
        }

        _icon = new TrayIcon
        {
            Icon = new WindowIcon(AssetLoader.Open(new Uri(OperatingSystem.IsWindows()
                ? "avares://Arcanist.Tray/Assets/arcanist.ico"
                : "avares://Arcanist.Tray/Assets/arcanist.png"))),
            ToolTipText = "Arcanist",
            Menu = menu,
        };
        _icon.Clicked += (_, _) => Open(_url is null ? null : new Uri(_url, "admin/"));

        _refreshTimer = new DispatcherTimer { Interval = RefreshInterval };
        _refreshTimer.Tick += (_, _) => _ = RefreshAsync();
        _updateTimer = new DispatcherTimer { Interval = UpdateCheckInterval };
        _updateTimer.Tick += (_, _) => _ = CheckForUpdatesAsync(manual: false);
    }

    public TrayIcon Icon => _icon;

    /// <param name="updateService">Set after an update applied from service mode: bring the service along.</param>
    public async Task StartAsync(bool updateService)
    {
        _refreshTimer.Start();
        _updateTimer.Start();

        if (WindowsService.IsInstalled)
        {
            if (updateService && IsServiceOutdated())
                await RunAsync(UpdateServiceAsync);
        }
        else if (_server.CanStart && await FindExternalServerAsync() is null)
        {
            _server.Start();
        }

        await RefreshAsync();
        DispatcherTimer.RunOnce(() => _ = CheckForUpdatesAsync(manual: false), TimeSpan.FromSeconds(30));
    }

    private bool IsServiceOutdated() =>
        Installation.VersionOf(_installation.ServerExecutable) is { } bundled && bundled != WindowsService.InstalledVersion;

    /// <summary>A healthy server not started by this app (a development run, another copy), which it leaves alone.</summary>
    private async Task<Uri?> FindExternalServerAsync() =>
        _probe.ReadRuntimeInfo() is { } info && ServerProbe.LocalUrl(info.Addresses) is { } url && await _probe.IsHealthyAsync(url)
            ? url
            : null;

    private async Task RefreshAsync()
    {
        if (_refreshing)
            return;

        _refreshing = true;
        try
        {
            var service = WindowsService.IsInstalled;
            var info = _probe.ReadRuntimeInfo();
            _url = info is null ? null : ServerProbe.LocalUrl(info.Addresses);
            var healthy = _url is not null && await _probe.IsHealthyAsync(_url);
            if (!healthy)
                _url = null;

            var state = WindowsService.State(healthy) ?? (
                healthy ? ServerState.Running
                : _failed ? ServerState.Failed
                : _server.IsRunning ? ServerState.Starting
                : !_server.CanStart ? ServerState.Missing
                : ServerState.Stopped);

            Render(state, service);
            if (_openWhenReady && state == ServerState.Running)
            {
                _openWhenReady = false;
                Open(new Uri(_url!, "admin/"));
            }
        }
        finally
        {
            _refreshing = false;
        }
    }

    private void Render(ServerState state, bool service)
    {
        var description = state switch
        {
            ServerState.Running => $"{_text.StatusRunning} · {_url?.Authority}",
            ServerState.Starting => _text.StatusStarting,
            ServerState.Stopping => _text.StatusStopping,
            ServerState.Failed => _text.StatusFailed,
            ServerState.Missing => _text.StatusMissing,
            _ => _text.StatusStopped,
        };
        if (service)
            description += $" ({_text.ServiceSuffix})";

        _status.Header = "Arcanist — " + description;
        _icon.ToolTipText = "Arcanist — " + description;

        var idle = !_busy;
        _openPlayer.IsEnabled = _openAdmin.IsEnabled = state == ServerState.Running;
        // In app mode only the server this app started can be stopped; an external one is left alone.
        var controllable = service || _server.IsRunning;
        _start.IsEnabled = idle && state is ServerState.Stopped or ServerState.Failed;
        _stop.IsEnabled = _restart.IsEnabled = idle && controllable && state is ServerState.Running or ServerState.Starting;

        _autoStart.IsVisible = AutoStart.IsSupported;
        _autoStart.IsChecked = AutoStart.IsEnabled;
        // The service mode is Windows's; macOS starts the app at login instead.
        _installService.IsVisible = !service && OperatingSystem.IsWindows();
        _installService.IsEnabled = idle && _server.CanStart;
        _uninstallService.IsVisible = service;
        _uninstallService.IsEnabled = idle;
        _updateService.IsVisible = service && IsServiceOutdated();
        _updateService.IsEnabled = idle;
        _updateService.Header = string.Format(_text.UpdateService, Installation.VersionOf(_installation.ServerExecutable)?.Split('+')[0]);

        _updates.IsVisible = _updater.IsSupported;
        _updates.IsEnabled = !_checkingUpdates && idle;
        _updates.Header = _updater.Ready is { } ready ? string.Format(_text.ApplyUpdate, ready.TargetFullRelease.Version)
            : _checkingUpdates ? _text.Checking
            : _upToDate ? _text.UpToDate
            : _text.CheckForUpdates;
    }

    private async Task StartAsync()
    {
        if (WindowsService.IsInstalled)
        {
            await WindowsService.ControlAsync(start: true, _installation);
            return;
        }

        _failed = false;
        _crashes = 0;
        _server.Start();
    }

    private async Task StopAsync()
    {
        if (WindowsService.IsInstalled)
            await WindowsService.ControlAsync(start: false, _installation);
        else
            await _server.StopAsync();
    }

    private void OnServerCrashed()
    {
        // Restarts with a growing delay, and gives up after repeated crashes shortly after starting.
        if (_server.StartedAt is { } started && DateTime.UtcNow - started > TimeSpan.FromMinutes(5))
            _crashes = 0;

        if (++_crashes > 3)
        {
            _failed = true;
            _ = RefreshAsync();
            return;
        }

        DispatcherTimer.RunOnce(() =>
        {
            if (!_failed && !_server.IsRunning && !WindowsService.IsInstalled)
                _server.Start();
        }, TimeSpan.FromSeconds(5 * _crashes));
    }

    private async Task InstallServiceAsync()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var folders = await new ServiceWindow().AskAsync();
        if (folders is null)
            return;

        // The service takes over the port and the data directory.
        await _server.StopAsync();
        string[] arguments =
        [
            "--owner-sid", WindowsIdentity.GetCurrent().User!.Value,
            .. folders.SelectMany(folder => new[] { "--grant", folder }),
        ];

        if (!await WindowsService.RunHelperAsync(_installation, "install", arguments))
        {
            if (!WindowsService.IsInstalled)
                _server.Start();
            await MessageWindow.ShowAsync(_text.ServiceTitle, _text.ServiceFailed, _text.Close);
        }
    }

    private async Task UninstallServiceAsync()
    {
        if (!OperatingSystem.IsWindows())
            return;

        if (!await MessageWindow.ShowAsync(_text.ServiceTitle, _text.UninstallServiceConfirm, _text.Uninstall, _text.Cancel))
            return;

        if (await WindowsService.RunHelperAsync(_installation, "uninstall"))
            await StartAsync();
        else
            await MessageWindow.ShowAsync(_text.ServiceTitle, _text.ServiceFailed, _text.Close);
    }

    private async Task UpdateServiceAsync()
    {
        if (!OperatingSystem.IsWindows())
            return;

        if (!await WindowsService.RunHelperAsync(_installation, "update"))
            await MessageWindow.ShowAsync(_text.ServiceTitle, _text.ServiceFailed, _text.Close);
    }

    private async Task CheckForUpdatesAsync(bool manual)
    {
        if (_checkingUpdates || _updater.Ready is not null)
            return;

        _checkingUpdates = true;
        await RefreshAsync();
        try
        {
            _upToDate = !await _updater.CheckAsync() && manual;
        }
        catch (Exception e) when (e is HttpRequestException or IOException)
        {
            // Offline or GitHub unavailable: try again at the next interval.
        }
        finally
        {
            _checkingUpdates = false;
            await RefreshAsync();
        }
    }

    private async Task ApplyUpdateAsync()
    {
        var service = WindowsService.IsInstalled;
        // The bundled server is replaced with the app; it must not be running from these files.
        await _server.StopAsync();
        _updater.ApplyAndRestart(service ? [Program.UpdateServiceSwitch] : []);
    }

    private async Task ExitAsync()
    {
        _refreshTimer.Stop();
        await _server.StopAsync();
        _lifetime.Shutdown();
    }

    /// <summary>Runs a menu action that changes state, with the controls disabled meanwhile.</summary>
    private async Task RunAsync(Func<Task> action)
    {
        if (_busy)
            return;

        _busy = true;
        await RefreshAsync();
        try
        {
            await action();
        }
        catch (Exception e)
        {
            await MessageWindow.ShowAsync("Arcanist", e.Message, _text.Close);
        }
        finally
        {
            _busy = false;
            await RefreshAsync();
        }
    }

    private static void Open(object? target)
    {
        if (target?.ToString() is { } path)
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true })?.Dispose();
    }

    private static NativeMenuItem Item(string header, Action onClick)
    {
        var item = new NativeMenuItem(header);
        item.Click += (_, _) => onClick();
        return item;
    }

    private static NativeMenuItem Item(string header, Func<Task> onClick) => Item(header, () => { _ = onClick(); });

    public void Dispose()
    {
        _server.Dispose();
        _probe.Dispose();
        _icon.Dispose();
    }
}
