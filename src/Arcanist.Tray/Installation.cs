using System.Diagnostics;
using System.Reflection;

namespace Arcanist.Tray;

/// <summary>Where the pieces of a Windows installation live.</summary>
/// <param name="ServerDirectory">
/// The server shipped with this app: <c>server\</c> next to the tray executable, inside the Velopack install
/// (<c>%LocalAppData%\Arcanist\current</c>). <c>ARCANIST_SERVER_DIR</c> overrides it during development.
/// </param>
/// <param name="DataDirectory">
/// Shared by both modes, so switching between the background app and the service keeps the same library:
/// <c>%ProgramData%\Arcanist</c>, the server's own default on Windows, or <c>ARCANIST_DATA_DIR</c>.
/// </param>
internal sealed record Installation(string ServerDirectory, string DataDirectory)
{
    public const string ServiceName = "Arcanist";

    public const string ServerExecutableName = "arcanist.exe";

    /// <summary>
    /// Where the service runs from. Not the Velopack folder: a service running as another account must not execute
    /// files the signed-in user can modify, and Velopack replaces that folder on updates.
    /// </summary>
    public static string ServiceDirectory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Arcanist", "server");

    public static string ServiceExecutable => Path.Combine(ServiceDirectory, ServerExecutableName);

    public string ServerExecutable => Path.Combine(ServerDirectory, ServerExecutableName);

    /// <summary>Written by the server while it runs; see <c>RuntimeInfo</c> in Arcanist.Core.</summary>
    public string RuntimeFile => Path.Combine(DataDirectory, "runtime.json");

    public string LogsDirectory => Path.Combine(DataDirectory, "logs");

    public static string RepositoryUrl { get; } = typeof(Installation).Assembly
        .GetCustomAttributes<AssemblyMetadataAttribute>()
        .First(a => a.Key == "RepositoryUrl").Value!;

    public static Installation Detect() => new(
        Environment.GetEnvironmentVariable("ARCANIST_SERVER_DIR") is { Length: > 0 } server
            ? Path.GetFullPath(server)
            : Path.Combine(AppContext.BaseDirectory, "server"),
        Environment.GetEnvironmentVariable("ARCANIST_DATA_DIR") is { Length: > 0 } data
            ? Path.GetFullPath(data)
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Arcanist"));

    /// <summary>Product version of a server executable, which identifies its build; null if it is missing.</summary>
    public static string? VersionOf(string executable) =>
        File.Exists(executable) ? FileVersionInfo.GetVersionInfo(executable).ProductVersion : null;
}
