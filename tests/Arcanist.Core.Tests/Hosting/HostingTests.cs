using System.Net;
using Arcanist.Core.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Arcanist.Core.Tests.Hosting;

public sealed class HostingTests : IAsyncLifetime
{
    private readonly ArcanistFactory _factory = new();

    [Fact]
    public async Task Health_endpoint_reports_healthy()
    {
        var response = await _factory.CreateClient().GetAsync("/health", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Data_directory_layout_is_created_at_the_configured_location()
    {
        var paths = _factory.Services.GetRequiredService<ArcanistPaths>();

        Assert.Equal(Path.GetFullPath(_factory.DataPath), paths.Root);
        Assert.All(new[] { paths.Keys, paths.Cache, paths.Frontends, paths.Extensions, paths.Logs },
            directory => Assert.True(Directory.Exists(directory), directory));
    }

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public ValueTask DisposeAsync() => _factory.DisposeAsync();
}
