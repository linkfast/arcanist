using Arcanist.Core.Hosting;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Arcanist.Core.Tests;

/// <summary>Runs the server in-memory against a throwaway data directory.</summary>
public sealed class ArcanistFactory : WebApplicationFactory<Program>
{
    public string DataPath { get; } =
        Path.Combine(Path.GetTempPath(), "arcanist-tests", Guid.NewGuid().ToString("N"));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting(DataDirectory.ConfigurationKey, DataPath);
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();

        try
        {
            Directory.Delete(DataPath, recursive: true);
        }
        catch (IOException)
        {
            // A log file can still be held briefly on Windows; the OS temp cleanup will take it.
        }
    }
}
