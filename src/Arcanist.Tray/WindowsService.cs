using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.Versioning;
using System.ServiceProcess;

namespace Arcanist.Tray;

/// <summary>
/// The optional Windows service, seen from the unelevated app. Querying, starting and stopping work without
/// elevation because installing grants those rights to the user who installed it; everything else goes through
/// <see cref="ServiceHelper"/> in an elevated copy of this executable.
/// </summary>
internal static class WindowsService
{
    /// <summary>Null when there is no service (or not on Windows).</summary>
    public static ServiceControllerStatus? Status()
    {
        if (!OperatingSystem.IsWindows())
            return null;

        try
        {
            using var service = new ServiceController(Installation.ServiceName);
            return service.Status;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    [SupportedOSPlatformGuard("windows")]
    public static bool IsInstalled => OperatingSystem.IsWindows() && Status() is not null;

    /// <summary>The server's state as the service reports it; null when there is no service (or not on Windows).</summary>
    /// <param name="healthy">Whether the server answers its health check.</param>
    public static ServerState? State(bool healthy) => OperatingSystem.IsWindows() && Status() is { } status
        ? status switch
        {
            ServiceControllerStatus.Running => healthy ? ServerState.Running : ServerState.Starting,
            ServiceControllerStatus.StartPending or ServiceControllerStatus.ContinuePending => ServerState.Starting,
            ServiceControllerStatus.StopPending or ServiceControllerStatus.PausePending => ServerState.Stopping,
            _ => ServerState.Stopped,
        }
        : null;

    public static string? InstalledVersion => Installation.VersionOf(Installation.ServiceExecutable);

    /// <summary>Starts or stops the service, asking for elevation if this user was not granted the right.</summary>
    [SupportedOSPlatform("windows")]
    public static async Task<bool> ControlAsync(bool start, Installation installation)
    {
        try
        {
            using var service = new ServiceController(Installation.ServiceName);
            if (start)
                service.Start();
            else
                service.Stop();
            return true;
        }
        catch (InvalidOperationException e) when (e.InnerException is Win32Exception { NativeErrorCode: 5 })
        {
            return await RunHelperAsync(installation, start ? "start" : "stop");
        }
    }

    /// <summary>Runs <see cref="ServiceHelper"/> elevated. False if it failed or the user declined the UAC prompt.</summary>
    [SupportedOSPlatform("windows")]
    public static async Task<bool> RunHelperAsync(Installation installation, string command, params IEnumerable<string> arguments)
    {
        var start = new ProcessStartInfo(Environment.ProcessPath!)
        {
            UseShellExecute = true,
            Verb = "runas",
        };
        start.ArgumentList.Add(ServiceHelper.Switch);
        start.ArgumentList.Add(command);
        start.ArgumentList.Add("--data-dir");
        start.ArgumentList.Add(installation.DataDirectory);
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);

        try
        {
            using var process = Process.Start(start)!;
            await process.WaitForExitAsync();
            return process.ExitCode == 0;
        }
        catch (Win32Exception e) when (e.NativeErrorCode == 1223)
        {
            // ERROR_CANCELLED: the user said no to the UAC prompt.
            return false;
        }
    }
}
