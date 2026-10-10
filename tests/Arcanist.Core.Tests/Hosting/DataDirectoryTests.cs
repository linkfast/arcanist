using Arcanist.Core.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.EnvironmentVariables;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.Configuration.Memory;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace Arcanist.Core.Tests.Hosting;

public sealed class DataDirectoryTests
{
    private static readonly string ContentRoot = Path.Combine(Path.GetTempPath(), "arcanist-content-root");

    [Fact]
    public void Configured_value_wins_over_environment_variable()
    {
        var resolved = Resolve(configured: "/srv/configured", Env((DataDirectory.EnvironmentVariable, "/srv/env")));

        Assert.Equal(Path.GetFullPath("/srv/configured"), resolved);
    }

    [Fact]
    public void Environment_variable_is_used_when_nothing_is_configured()
    {
        var resolved = Resolve(configured: null, Env((DataDirectory.EnvironmentVariable, "/srv/env")));

        Assert.Equal(Path.GetFullPath("/srv/env"), resolved);
    }

    [Fact]
    public void Relative_paths_are_resolved_against_the_content_root()
    {
        var resolved = Resolve(configured: "state", Env());

        Assert.Equal(Path.Combine(ContentRoot, "state"), resolved);
    }

    [Fact]
    public void Development_defaults_to_a_folder_next_to_the_project()
    {
        var resolved = Resolve(configured: null, Env(), environmentName: Environments.Development);

        Assert.Equal(Path.Combine(ContentRoot, ".data"), resolved);
    }

    [Fact]
    public void Containers_default_to_slash_data()
    {
        var resolved = Resolve(configured: null, Env(("DOTNET_RUNNING_IN_CONTAINER", "true")));

        Assert.Equal(Path.GetFullPath("/data"), resolved);
    }

    [Fact]
    public void Systemd_state_directory_is_used_when_provided()
    {
        var resolved = Resolve(configured: null, Env(("STATE_DIRECTORY", "/var/lib/arcanist-unit")));

        Assert.Equal(Path.GetFullPath("/var/lib/arcanist-unit"), resolved);
    }

    [Fact]
    public void Settings_file_sits_between_appsettings_and_user_environment_variables()
    {
        var builder = new ConfigurationBuilder();
        builder.Sources.Add(new EnvironmentVariablesConfigurationSource { Prefix = "DOTNET_" });
        builder.Sources.Add(new MemoryConfigurationSource()); // stands in for appsettings.json
        builder.Sources.Add(new EnvironmentVariablesConfigurationSource());
        builder.Sources.Add(new MemoryConfigurationSource()); // stands in for the command line

        var root = Directory.CreateTempSubdirectory("arcanist-settings").FullName;
        try
        {
            builder.AddArcanistSettingsFile(new ArcanistPaths(root));
        }
        finally
        {
            Directory.Delete(root);
        }

        Assert.IsType<JsonConfigurationSource>(builder.Sources[2]);
        Assert.IsType<EnvironmentVariablesConfigurationSource>(builder.Sources[3]);
    }

    [Fact]
    public void Settings_file_values_are_overridden_by_later_sources()
    {
        var root = Directory.CreateTempSubdirectory("arcanist-settings").FullName;
        try
        {
            File.WriteAllText(Path.Combine(root, ArcanistPaths.SettingsFileName),
                """{ "Arcanist": { "Sample": "from-file", "Other": "from-file" } }""");

            var builder = new ConfigurationBuilder();
            builder.Sources.Add(new MemoryConfigurationSource
            {
                InitialData = new Dictionary<string, string?> { ["Arcanist:Sample"] = "from-appsettings" },
            });
            builder.Sources.Add(new EnvironmentVariablesConfigurationSource());
            builder.Sources.Add(new MemoryConfigurationSource
            {
                InitialData = new Dictionary<string, string?> { ["Arcanist:Other"] = "from-command-line" },
            });
            builder.AddArcanistSettingsFile(new ArcanistPaths(root));

            var configuration = builder.Build();

            Assert.Equal("from-file", configuration["Arcanist:Sample"]);
            Assert.Equal("from-command-line", configuration["Arcanist:Other"]);
            (configuration as IDisposable)?.Dispose();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string Resolve(string? configured, Func<string, string?> env, string environmentName = "Production") =>
        DataDirectory.Resolve(configured, new TestHostEnvironment(environmentName), env);

    private static Func<string, string?> Env(params (string Name, string Value)[] variables) =>
        name => variables.FirstOrDefault(v => v.Name == name).Value;

    private sealed class TestHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "Arcanist.Core";
        public string ContentRootPath { get; set; } = ContentRoot;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
