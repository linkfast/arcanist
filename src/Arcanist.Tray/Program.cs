using Avalonia;
using Avalonia.Controls;
using Velopack;

namespace Arcanist.Tray;

internal static class Program
{
    /// <summary>Passed by an update applied while the service is installed, so the service is updated too.</summary>
    public const string UpdateServiceSwitch = "--update-service";

    [STAThread]
    public static int Main(string[] args)
    {
        // The elevated half of the service mode: no UI, no Velopack hooks, no single-instance check.
        if (args.FirstOrDefault() == ServiceHelper.Switch)
            return ServiceHelper.Run(args);

        VelopackApp.Build()
            .OnFirstRun(_ => AutoStart.Set(true))
            .OnBeforeUninstallFastCallback(_ => BeforeUninstall())
            .Run();

        using var instance = new Mutex(initiallyOwned: true, @"Local\Arcanist.Tray", out var first);
        if (!first)
        {
            // Launched again (from the Start menu, say): show the running server instead.
            OpenRunningServer();
            return 0;
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args, ShutdownMode.OnExplicitShutdown);
        return 0;
    }

    /// <summary>Used by the Avalonia designer as well.</summary>
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UsePlatformDetect();

    private static void BeforeUninstall()
    {
        AutoStart.Set(false);
        // The service runs from Program Files, outside what Velopack removes. Velopack gives hooks a limited time.
        if (WindowsService.IsInstalled)
            WindowsService.RunHelperAsync(Installation.Detect(), "uninstall").Wait(TimeSpan.FromSeconds(25));
    }

    private static void OpenRunningServer()
    {
        using var probe = new ServerProbe(Installation.Detect());
        if (probe.ReadRuntimeInfo() is { } info && ServerProbe.LocalUrl(info.Addresses) is { } url)
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(new Uri(url, "admin/").ToString()) { UseShellExecute = true })?.Dispose();
    }
}
