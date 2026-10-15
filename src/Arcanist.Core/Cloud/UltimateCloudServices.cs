using Arcanist.Core.Capabilities;

namespace Arcanist.Core.Cloud;

public static class UltimateCloudServiceCollectionExtensions
{
    public static IServiceCollection AddUltimateCloud(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<UltimateCloudOptions>(configuration.GetSection(UltimateCloudOptions.Section));
        services.AddSingleton<UltimateCloudAuth>();
        services.AddSingleton<UltimateCloudClient>();
        services.AddSingleton<UltimateCloudAccount>();
        services.AddSingleton<ICloudLinkState>(provider => provider.GetRequiredService<UltimateCloudAccount>());
        services.AddHostedService(provider => provider.GetRequiredService<UltimateCloudAccount>());
        services.AddHttpClient(UltimateCloudClient.HttpClientName, client => client.Timeout = TimeSpan.FromSeconds(30));
        services.AddScoped<Application.Cloud.UltimateCloudService>();
        return services;
    }
}
