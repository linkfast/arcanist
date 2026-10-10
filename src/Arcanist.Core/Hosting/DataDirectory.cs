namespace Arcanist.Core.Hosting;

/// <summary>
/// Resolves the directory where Arcanist keeps its state (settings, database, keys, caches, logs).
/// </summary>
public static class DataDirectory
{
    /// <summary>Configuration key; also set by <c>--data-dir</c> and the <c>Arcanist__DataDir</c> environment variable.</summary>
    public const string ConfigurationKey = "Arcanist:DataDir";

    /// <summary>Shorthand environment variable, convenient for Docker and service definitions.</summary>
    public const string EnvironmentVariable = "ARCANIST_DATA_DIR";

    /// <summary>Command-line switch mapped to <see cref="ConfigurationKey"/>.</summary>
    public const string CommandLineSwitch = "--data-dir";

    public static string Resolve(IConfiguration configuration, IHostEnvironment environment) =>
        Resolve(configuration[ConfigurationKey], environment, Environment.GetEnvironmentVariable);

    internal static string Resolve(string? configured, IHostEnvironment environment, Func<string, string?> getEnvironmentVariable)
    {
        var path = FirstNonEmpty(configured, getEnvironmentVariable(EnvironmentVariable))
                   ?? GetDefault(environment, getEnvironmentVariable);

        return Path.GetFullPath(path, environment.ContentRootPath);
    }

    private static string GetDefault(IHostEnvironment environment, Func<string, string?> getEnvironmentVariable)
    {
        // Keeps a developer's data next to the project instead of mixing it with an installed instance.
        if (environment.IsDevelopment())
            return Path.Combine(environment.ContentRootPath, ".data");

        if (getEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") == "true")
            return "/data";

        // systemd units with StateDirectory= receive the directory they own.
        if (FirstNonEmpty(getEnvironmentVariable("STATE_DIRECTORY")) is { } stateDirectory)
            return stateDirectory;

        if (OperatingSystem.IsWindows())
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Arcanist");

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        if (OperatingSystem.IsMacOS())
            return Path.Combine(home, "Library", "Application Support", "Arcanist");

        if (Environment.UserName == "root")
            return "/var/lib/arcanist";

        var dataHome = FirstNonEmpty(getEnvironmentVariable("XDG_DATA_HOME")) ?? Path.Combine(home, ".local", "share");
        return Path.Combine(dataHome, "arcanist");
    }

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
}
