using System.Diagnostics;
using System.Security.Principal;
using System.ServiceProcess;

namespace Arcanist.Tray;

/// <summary>
/// The privileged half of the Windows service mode: <c>Arcanist.Tray.exe --service-helper &lt;command&gt;</c>, started
/// elevated by <see cref="WindowsService.RunHelperAsync"/>. Commands:
/// <list type="bullet">
/// <item><c>install --data-dir D --owner-sid S [--grant FOLDER]…</c>: copies the bundled server to Program Files,
/// registers it to run as the virtual account <c>NT SERVICE\Arcanist</c>, lets it write the data directory and read
/// the given folders, lets user S start and stop it, opens the port for private networks, and starts it.</item>
/// <item><c>update --data-dir D</c>: replaces the service's server with the bundled one.</item>
/// <item><c>uninstall --data-dir D</c>: removes the service, its firewall rule and its copy of the server. The data
/// directory stays.</item>
/// <item><c>start</c> / <c>stop</c>.</item>
/// </list>
/// Progress and errors go to <c>logs\service-helper.log</c> in the data directory.
/// </summary>
internal sealed class ServiceHelper
{
    public const string Switch = "--service-helper";

    private const string Account = @"NT SERVICE\" + Installation.ServiceName;

    private static readonly TimeSpan ServiceTimeout = TimeSpan.FromSeconds(60);

    private readonly Installation _installation;
    private readonly StreamWriter _log;

    private ServiceHelper(Installation installation)
    {
        _installation = installation;
        Directory.CreateDirectory(installation.LogsDirectory);
        _log = new StreamWriter(Path.Combine(installation.LogsDirectory, "service-helper.log"), append: true) { AutoFlush = true };
    }

    /// <summary>Entry point for <see cref="Switch"/>; returns the process exit code.</summary>
    public static int Run(string[] args)
    {
        var command = args.ElementAtOrDefault(1) ?? "";
        string? dataDirectory = null, ownerSid = null;
        var grants = new List<string>();
        for (var i = 2; i + 1 < args.Length; i += 2)
        {
            switch (args[i])
            {
                case "--data-dir": dataDirectory = args[i + 1]; break;
                case "--owner-sid": ownerSid = args[i + 1]; break;
                case "--grant": grants.Add(args[i + 1]); break;
                default: return 2;
            }
        }

        var detected = Installation.Detect();
        var installation = detected with { DataDirectory = Path.GetFullPath(dataDirectory ?? detected.DataDirectory) };
        var helper = new ServiceHelper(installation);
        try
        {
            helper.Log($"{command} (version {Installation.VersionOf(installation.ServerExecutable)})");
            switch (command)
            {
                case "install": helper.Install(ownerSid ?? throw new HelperException("--owner-sid is required"), grants); break;
                case "update": helper.Update(); break;
                case "uninstall": helper.Uninstall(); break;
                case "start": helper.Start(); break;
                case "stop": helper.Stop(); break;
                default: throw new HelperException($"Unknown command '{command}'");
            }

            helper.Log("Done");
            return 0;
        }
        catch (Exception e)
        {
            helper.Log($"Failed: {e}");
            return 1;
        }
        finally
        {
            helper._log.Dispose();
        }
    }

    private void Install(string ownerSid, IReadOnlyList<string> grants)
    {
        // Validates the SID before it ends up in a security descriptor.
        ownerSid = new SecurityIdentifier(ownerSid).Value;

        Stop();
        CopyServer();

        var binPath = $"\"{Installation.ServiceExecutable}\" --data-dir \"{_installation.DataDirectory.TrimEnd('\\')}\"";
        string[] settings = ["binPath=", binPath, "start=", "delayed-auto", "obj=", Account, "DisplayName=", "Arcanist"];
        Require("sc.exe", [WindowsService.IsInstalled ? "config" : "create", Installation.ServiceName, .. settings]);
        Require("sc.exe", "description", Installation.ServiceName, "Arcanist music server");
        Require("sc.exe", "failure", Installation.ServiceName, "reset=", "86400", "actions=", "restart/10000/restart/30000/restart/60000");
        // Also when the server stops by itself with an error code: a factory reset ends that way to be started again.
        Require("sc.exe", "failureflag", Installation.ServiceName, "1");

        var (_, sddl) = Execute("sc.exe", "sdshow", Installation.ServiceName);
        Require("sc.exe", "sdset", Installation.ServiceName, GrantControl(sddl.Trim(), ownerSid));

        Directory.CreateDirectory(_installation.DataDirectory);
        Require("icacls.exe", _installation.DataDirectory, "/grant", $"{Account}:(OI)(CI)M");
        foreach (var folder in grants)
        {
            // A folder that cannot be granted (gone, on a share) must not undo the whole installation.
            if (Execute("icacls.exe", folder, "/grant", $"{Account}:(OI)(CI)RX").ExitCode != 0)
                Log($"Could not grant read access to {folder}");
        }

        // Private and domain networks only: a music server on a public Wi-Fi network should stay invisible.
        Execute("netsh.exe", "advfirewall", "firewall", "delete", "rule", $"name={Installation.ServiceName}");
        Require("netsh.exe", "advfirewall", "firewall", "add", "rule", $"name={Installation.ServiceName}", "dir=in", "action=allow",
            $"program={Installation.ServiceExecutable}", "enable=yes", "profile=private,domain");

        Start();
    }

    private void Update()
    {
        if (!WindowsService.IsInstalled)
            throw new HelperException("The service is not installed");

        Stop();
        CopyServer();
        Start();
    }

    private void Uninstall()
    {
        if (WindowsService.IsInstalled)
        {
            Stop();
            Require("sc.exe", "delete", Installation.ServiceName);
        }

        Execute("netsh.exe", "advfirewall", "firewall", "delete", "rule", $"name={Installation.ServiceName}");
        DeleteDirectory(Installation.ServiceDirectory);

        var parent = Path.GetDirectoryName(Installation.ServiceDirectory)!;
        if (Directory.Exists(parent) && !Directory.EnumerateFileSystemEntries(parent).Any())
            Directory.Delete(parent);
    }

    private void Start()
    {
        using var service = new ServiceController(Installation.ServiceName);
        if (service.Status != ServiceControllerStatus.Running)
        {
            service.Start();
            service.WaitForStatus(ServiceControllerStatus.Running, ServiceTimeout);
        }
    }

    private void Stop()
    {
        if (!WindowsService.IsInstalled)
            return;

        using var service = new ServiceController(Installation.ServiceName);
        if (service.Status != ServiceControllerStatus.Stopped)
        {
            if (service.Status != ServiceControllerStatus.StopPending)
                service.Stop();
            service.WaitForStatus(ServiceControllerStatus.Stopped, ServiceTimeout);
        }
    }

    private void CopyServer()
    {
        var source = _installation.ServerDirectory;
        if (!File.Exists(Path.Combine(source, Installation.ServerExecutableName)))
            throw new HelperException($"No server to install in {source}");

        DeleteDirectory(Installation.ServiceDirectory);
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(Installation.ServiceDirectory, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }

        Log($"Copied {source} to {Installation.ServiceDirectory}");
    }

    /// <summary>Retries for a while: a stopped service's process can take a moment to release its files.</summary>
    private static void DeleteDirectory(string path)
    {
        for (var attempt = 1; Directory.Exists(path); attempt++)
        {
            try
            {
                Directory.Delete(path, recursive: true);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException && attempt < 20)
            {
                Thread.Sleep(500);
            }
        }
    }

    /// <summary>
    /// Adds an ACE letting <paramref name="sid"/> query, start and stop the service to its security descriptor, in
    /// the DACL (before the SACL, if any).
    /// </summary>
    internal static string GrantControl(string sddl, string sid)
    {
        // RP start, WP stop, LC query status, LO interrogate, RC read permissions.
        var ace = $"(A;;RPWPLCLORC;;;{sid})";
        if (sddl.Contains(ace, StringComparison.OrdinalIgnoreCase))
            return sddl;

        var sacl = sddl.IndexOf("S:", StringComparison.Ordinal);
        return sacl < 0 ? sddl + ace : sddl.Insert(sacl, ace);
    }

    private void Require(string tool, params string[] arguments)
    {
        var (exitCode, output) = Execute(tool, arguments);
        if (exitCode != 0)
            throw new HelperException($"{tool} {arguments.FirstOrDefault()} failed with {exitCode}: {output.Trim()}");
    }

    private (int ExitCode, string Output) Execute(string tool, params string[] arguments)
    {
        // Full path: an elevated process must not pick up a tool from the working directory or PATH.
        var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, tool))
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);

        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        var text = output.Result + error.Result;

        Log($"{tool} {arguments.FirstOrDefault()} -> {process.ExitCode}");
        return (process.ExitCode, text);
    }

    private void Log(string message) => _log.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}");

    private sealed class HelperException(string message) : Exception(message);
}
