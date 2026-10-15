using Arcanist.Core.Storage;
using Microsoft.AspNetCore.Server.Kestrel.Core;

namespace Arcanist.Core.P2P;

public static class P2PServiceCollectionExtensions
{
    public static IServiceCollection AddP2P(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<P2POptions>(configuration.GetSection(P2POptions.Section));
        services.AddSingleton<P2PSettings>();
        services.AddSingleton<P2PKeys>();
        services.AddSingleton<P2PPeerDirectory>();
        services.AddSingleton<P2PNode>();
        services.AddHostedService(provider => provider.GetRequiredService<P2PNode>());
        services.AddSingleton<P2PActivityLog>();
        services.AddHostedService(provider => provider.GetRequiredService<P2PActivityLog>());
        services.AddSingleton<P2PTraffic>();
        services.AddHostedService(provider => provider.GetRequiredService<P2PTraffic>());
        services.AddSingleton<PeerClient>();
        services.AddSingleton<PeerApi>();
        services.AddSingleton<ILibraryStorage, PeerStorage>();
        // Streams last as long as the music: requests end with their cancellation, not with a timeout.
        services.AddHttpClient(PeerClient.HttpClientName, client => client.Timeout = Timeout.InfiniteTimeSpan);
        services.AddScoped<Application.P2P.P2PService>();

        // Tunnelled connections arrive on the server's own endpoints (see P2PConnections).
        services.Configure<KestrelServerOptions>(options => options.ConfigureEndpointDefaults(P2PConnections.Use));
        return services;
    }

    /// <summary>Before the forwarded headers: tunnelled requests come from loopback, but must not be trusted as local.</summary>
    public static IApplicationBuilder UseP2PGate(this IApplicationBuilder app) => app.UseMiddleware<P2PGateMiddleware>();
}
