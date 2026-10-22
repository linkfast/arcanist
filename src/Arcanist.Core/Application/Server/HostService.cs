using System.Runtime.InteropServices;
using Arcanist.Core.Hosting;

namespace Arcanist.Core.Application.Server;

/// <summary>The operating system the server runs on. Part of the API contract.</summary>
public enum HostOs
{
    Windows,
    MacOS,
    Linux,
    Other,
}

/// <summary>
/// Where and how this server runs, so that clients give the steps that fit it when something needs doing outside
/// Arcanist (a music folder it cannot read, a sound card, a restart, a network setting). Part of the API contract.
/// </summary>
/// <param name="Kind">How it was installed (Windows, Docker, Synology, QNAP…), as the update notice says it too.</param>
/// <param name="Container">It runs in a container (Docker, TrueNAS, Unraid, Rockstor): folders and devices come from its settings.</param>
/// <param name="Supervised">Something starts it again when it stops to restart (exit code 75): the tray, a service, systemd, a container's restart policy, a NAS package.</param>
/// <param name="User">The system user it runs as: the one that needs access to the music folders.</param>
/// <param name="DataDirectory">Where its data lives.</param>
public sealed record ServerHost(
    InstallKind Kind,
    HostOs Os,
    string Architecture,
    bool Container,
    bool Supervised,
    string User,
    string DataDirectory);

/// <summary>Reads <see cref="ServerHost"/> once: none of it changes while the server runs.</summary>
public sealed class HostPlatform(ArcanistPaths paths, IConfiguration configuration)
{
    private ServerHost? _host;

    public ServerHost Current => _host ??= Detect();

    private ServerHost Detect()
    {
        var kind = UpdateChecker.DetectInstallKind();
        var os = OperatingSystem.IsWindows() ? HostOs.Windows
            : OperatingSystem.IsMacOS() ? HostOs.MacOS
            : OperatingSystem.IsLinux() ? HostOs.Linux
            : HostOs.Other;
        var container = kind is InstallKind.Docker or InstallKind.TrueNas or InstallKind.Unraid or InstallKind.Rockstor
                        || Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") == "true";
        // The tray (Windows and macOS) runs it with ExitWhenInputCloses.
        var tray = configuration.GetSection(HostingOptions.Section).Get<HostingOptions>()?.ExitWhenInputCloses == true;
        var supervised = tray || container || kind is InstallKind.WindowsService or InstallKind.Synology or InstallKind.Qnap
            or InstallKind.DebianPackage or InstallKind.OpenMediaVault or InstallKind.Systemd;
        return new ServerHost(kind, os, RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant(), container, supervised,
            Environment.UserName, paths.Root);
    }
}

/// <summary>Administrators read where the server runs.</summary>
public sealed class HostService(AccessGuard guard, HostPlatform platform)
{
    public ServerHost Get()
    {
        guard.Admin();
        return platform.Current;
    }
}
