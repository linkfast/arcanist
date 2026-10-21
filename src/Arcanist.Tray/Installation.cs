using System.Diagnostics;
using System.Reflection;

namespace Arcanist.Tray;

/// <summary>Where the pieces of an installation live.</summary>
/// <param name="ServerDirectory">
/// The server shipped with this app: <c>server</c> next to its executable, inside the Velopack install
/// (<c>%LocalAppData%\Arcanist\current</c> on Windows, <c>Arcanist.app/Contents/MacOS</c> on macOS).
/// <c>ARCANIST_SERVER_DIR</c> overrides it during development.
/// </param>
/// <param name="DataDirectory">
/// The server's own default, or <c>ARCANIST_DATA_DIR</c>: <c>%ProgramData%\Arcanist</c> on Windows, shared by the
/// background app and the service; <c>~/Library/Application Support/Arcanist</c> on macOS.
/// </param>
internal sealed record Installation(string ServerDirectory, string DataDirectory)
{
    public const string ServiceName = "Arcanist";

    public static string ServerExecutableName { get; } = OperatingSystem.IsWindows() ? "arcanist.exe" : "arcanist";

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
            : BundledServer(),
        Environment.GetEnvironmentVariable("ARCANIST_DATA_DIR") is { Length: > 0 } data
            ? Path.GetFullPath(data)
            : DefaultDataDirectory());

    /// <summary>
    /// <c>server</c> next to the executable; in Arcanist.app, <c>Contents/Resources/server</c> (Contents/MacOS may only
    /// hold code), or next to it where an update put it back.
    /// </summary>
    private static string BundledServer()
    {
        var resources = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "Resources", "server"));
        return OperatingSystem.IsMacOS() && Directory.Exists(resources) ? resources : Path.Combine(AppContext.BaseDirectory, "server");
    }

    private static string DefaultDataDirectory() => OperatingSystem.IsMacOS()
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support", "Arcanist")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Arcanist");

    /// <summary>Product version of a server executable, which identifies its build; null if it is missing.</summary>
    public static string? VersionOf(string executable) =>
        File.Exists(executable) ? FileVersionInfo.GetVersionInfo(executable).ProductVersion : null;
}
