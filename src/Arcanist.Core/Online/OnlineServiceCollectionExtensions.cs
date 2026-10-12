using Arcanist.Core.Application.Server;
using Microsoft.Extensions.Options;

namespace Arcanist.Core.Online;

public static class OnlineServiceCollectionExtensions
{
    public static IServiceCollection AddOnlineServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<OnlineOptions>(configuration.GetSection(OnlineOptions.Section));
        services.AddSingleton<OnlineSettings>();
        services.AddSingleton<OnlineMonitor>();
        services.AddSingleton<AcoustIdClient>();
        services.AddSingleton<MusicBrainzClient>();
        services.AddSingleton<CoverArtClient>();
        services.AddSingleton<LrcLibClient>();

        foreach (var name in new[] { OnlineHttp.MusicBrainz, OnlineHttp.AcoustId, OnlineHttp.CoverArtArchive, OnlineHttp.LrcLib })
        {
            services.AddHttpClient(name, (provider, client) =>
            {
                client.DefaultRequestHeaders.UserAgent.ParseAdd(OnlineHttp.UserAgent(provider.GetRequiredService<IOptions<ServerOptions>>().Value.SourceUrl));
                client.Timeout = TimeSpan.FromSeconds(30);
            });
        }

        services.AddSingleton<EnrichmentWorker>();
        services.AddHostedService(provider => provider.GetRequiredService<EnrichmentWorker>());
        return services;
    }
}
