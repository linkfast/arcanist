using System.Text.Json;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;

namespace Arcanist.Core.Hosting;

/// <summary>Bound from <c>Arcanist:Hosting</c>. Used by launchers; not something users normally set.</summary>
public sealed class HostingOptions
{
    public const string Section = "Arcanist:Hosting";

    /// <summary>
    /// Stop gracefully when standard input reaches end of file. A supervisor (the Windows tray app) stops the server
    /// by closing the pipe, and the server never outlives a supervisor that crashed, because the OS closes it then.
    /// </summary>
    public bool ExitWhenInputCloses { get; set; }
}

/// <summary>What <c>runtime.json</c> in the data directory says about the running server.</summary>
public sealed record RuntimeInfo(int ProcessId, string Version, IReadOnlyList<string> Addresses, DateTime StartedAt);

/// <summary>
/// Lets launchers find a running server: writes <see cref="ArcanistPaths.RuntimeFile"/> with the process id, version
/// and listening addresses once Kestrel is listening, and removes it on shutdown.
/// </summary>
internal sealed class RuntimeInfoWriter(
    IServer server,
    IHostApplicationLifetime lifetime,
    ArcanistPaths paths,
    Application.Server.ServerInfoService serverInfo,
    TimeProvider time,
    ILogger<RuntimeInfoWriter> logger) : IHostedService
{
    private bool _written;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        lifetime.ApplicationStarted.Register(Write);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        if (_written)
            File.Delete(paths.RuntimeFile);
        return Task.CompletedTask;
    }

    private void Write()
    {
        // The test server has no addresses; there is nothing to announce then.
        var addresses = server.Features.Get<IServerAddressesFeature>()?.Addresses.ToList() ?? [];
        if (addresses.Count == 0)
            return;

        var info = new RuntimeInfo(Environment.ProcessId, serverInfo.Get().Version, addresses, time.GetUtcNow().UtcDateTime);
        try
        {
            File.WriteAllText(paths.RuntimeFile, JsonSerializer.Serialize(info, JsonSerializerOptions.Web));
            _written = true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(e, "Could not write {RuntimeFile}", paths.RuntimeFile);
        }
    }
}

/// <summary>Implements <see cref="HostingOptions.ExitWhenInputCloses"/>.</summary>
internal sealed class InputCloseWatcher(IHostApplicationLifetime lifetime, ILogger<InputCloseWatcher> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        // A background thread rather than an awaited read: console reads cannot be cancelled, and a pending one
        // must not hold up a shutdown that started for another reason.
        new Thread(Watch) { IsBackground = true, Name = "Input close watcher" }.Start();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private void Watch()
    {
        try
        {
            using var input = Console.OpenStandardInput();
            var buffer = new byte[256];
            while (input.Read(buffer) > 0)
            {
            }
        }
        catch (IOException)
        {
        }

        logger.LogInformation("Standard input closed; stopping");
        lifetime.StopApplication();
    }
}

public static class SupervisionServiceCollectionExtensions
{
    public static IServiceCollection AddSupervision(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddHostedService<RuntimeInfoWriter>();
        if (configuration.GetSection(HostingOptions.Section).Get<HostingOptions>()?.ExitWhenInputCloses == true)
            services.AddHostedService<InputCloseWatcher>();
        return services;
    }
}
