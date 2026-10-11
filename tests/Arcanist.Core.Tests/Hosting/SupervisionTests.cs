using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Arcanist.Core.Hosting;

namespace Arcanist.Core.Tests.Hosting;

/// <summary>
/// The contract launchers rely on (the Windows tray app, service scripts), checked against the real executable:
/// <c>runtime.json</c> announces where the server listens, and closing standard input stops it gracefully.
/// </summary>
public sealed class SupervisionTests : IDisposable
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private readonly string _root = Directory.CreateTempSubdirectory("arcanist-supervision-").FullName;

    [Fact]
    public async Task Server_announces_its_address_and_stops_when_input_closes()
    {
        var data = Path.Combine(_root, "data");
        // Another working directory, as when a launcher starts the server: settings must still be found.
        var start = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "arcanist.exe" : "arcanist"))
        {
            ArgumentList = { "--data-dir", data, "--urls", "http://127.0.0.1:0", "--Arcanist:Hosting:ExitWhenInputCloses=true" },
            WorkingDirectory = _root,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            Environment = { ["ASPNETCORE_ENVIRONMENT"] = "Production", ["DOTNET_ENVIRONMENT"] = null },
        };

        using var process = Process.Start(start)!;
        try
        {
            var runtimeFile = new ArcanistPaths(data).RuntimeFile;
            var info = await WaitForAsync(() => File.Exists(runtimeFile)
                ? JsonSerializer.Deserialize<RuntimeInfo>(File.ReadAllText(runtimeFile), JsonSerializerOptions.Web)
                : null);

            Assert.Equal(process.Id, info.ProcessId);
            using var http = new HttpClient { BaseAddress = new Uri(Assert.Single(info.Addresses)) };
            Assert.Equal(HttpStatusCode.OK, (await http.GetAsync("/health", Cancel)).StatusCode);

            process.StandardInput.Close();

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Cancel);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            await process.WaitForExitAsync(timeout.Token);

            Assert.Equal(0, process.ExitCode);
            Assert.False(File.Exists(runtimeFile));
        }
        finally
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
    }

    private static async Task<T> WaitForAsync<T>(Func<T?> read) where T : class
    {
        for (var attempt = 0; attempt < 300; attempt++)
        {
            try
            {
                if (read() is { } value)
                    return value;
            }
            catch (Exception e) when (e is IOException or JsonException)
            {
                // Read while being written.
            }

            await Task.Delay(100, Cancel);
        }

        throw new TimeoutException();
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
