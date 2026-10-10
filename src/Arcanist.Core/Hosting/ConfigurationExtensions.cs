using Microsoft.Extensions.Configuration.EnvironmentVariables;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.FileProviders;

namespace Arcanist.Core.Hosting;

public static class ConfigurationExtensions
{
    /// <summary>
    /// Adds <c>arcanist.json</c> from the data directory. It overrides <c>appsettings*.json</c>, while
    /// environment variables and the command line still override it, so Docker and advanced users can
    /// pin any value the setup wizard would otherwise write.
    /// </summary>
    public static IConfigurationBuilder AddArcanistSettingsFile(this IConfigurationBuilder builder, ArcanistPaths paths)
    {
        var source = new JsonConfigurationSource
        {
            FileProvider = new PhysicalFileProvider(paths.Root),
            Path = ArcanistPaths.SettingsFileName,
            Optional = true,
            ReloadOnChange = true,
        };

        // Prefixed environment sources (DOTNET_, ASPNETCORE_) are host settings that come before appsettings;
        // the unprefixed one marks where user-level overrides start.
        var userEnvironment = builder.Sources.FirstOrDefault(
            s => s is EnvironmentVariablesConfigurationSource { Prefix: null or "" });
        var index = userEnvironment is null ? builder.Sources.Count : builder.Sources.IndexOf(userEnvironment);

        builder.Sources.Insert(index, source);
        return builder;
    }
}
