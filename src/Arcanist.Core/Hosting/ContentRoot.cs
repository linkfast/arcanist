namespace Arcanist.Core.Hosting;

public static class ContentRoot
{
    /// <summary>
    /// The content root (where <c>appsettings.json</c> and <c>wwwroot</c> are) is the working directory by default.
    /// Installed builds may be started from anywhere (a launcher, a shell in another folder), so when the working
    /// directory has no <c>appsettings.json</c> but the executable's directory does, use the latter. Returns null to
    /// keep the default, as during development and in tests.
    /// </summary>
    public static string? Resolve() =>
        !File.Exists(Path.Combine(Environment.CurrentDirectory, "appsettings.json"))
        && File.Exists(Path.Combine(AppContext.BaseDirectory, "appsettings.json"))
            ? AppContext.BaseDirectory
            : null;
}
