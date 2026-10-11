namespace Arcanist.Tray.Tests;

public sealed class ServerProbeTests
{
    [Theory]
    [InlineData("http://[::]:8484", "http://localhost:8484/")]
    [InlineData("http://0.0.0.0:8484", "http://localhost:8484/")]
    [InlineData("http://*:9000", "http://localhost:9000/")]
    [InlineData("http://+:9000", "http://localhost:9000/")]
    [InlineData("http://127.0.0.1:5263", "http://127.0.0.1:5263/")]
    public void Wildcard_addresses_become_localhost(string address, string expected)
    {
        Assert.Equal(new Uri(expected), ServerProbe.LocalUrl([address]));
    }

    [Fact]
    public void Plain_http_is_preferred_over_https()
    {
        Assert.Equal(new Uri("http://localhost:5263/"), ServerProbe.LocalUrl(["https://[::]:7137", "http://[::]:5263"]));
    }

    [Fact]
    public void No_usable_address_gives_null()
    {
        Assert.Null(ServerProbe.LocalUrl([]));
        Assert.Null(ServerProbe.LocalUrl(["not a url"]));
    }

    [Fact]
    public void A_missing_or_stale_runtime_file_means_no_server()
    {
        var directory = Directory.CreateTempSubdirectory("arcanist-probe-").FullName;
        try
        {
            using var probe = new ServerProbe(new Installation("unused", directory));
            Assert.Null(probe.ReadRuntimeInfo());

            // A process id that cannot exist: the server that wrote it is gone.
            File.WriteAllText(Path.Combine(directory, "runtime.json"),
                """{"processId":2147483647,"version":"1.0.0","addresses":["http://[::]:8484"],"startedAt":"2026-01-01T00:00:00Z"}""");
            Assert.Null(probe.ReadRuntimeInfo());

            File.WriteAllText(Path.Combine(directory, "runtime.json"),
                $$"""{"processId":{{Environment.ProcessId}},"version":"1.0.0","addresses":["http://[::]:8484"],"startedAt":"2026-01-01T00:00:00Z"}""");
            Assert.Equal(Environment.ProcessId, probe.ReadRuntimeInfo()?.ProcessId);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
