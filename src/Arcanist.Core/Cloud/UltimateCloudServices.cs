using Arcanist.Core.Capabilities;
using Arcanist.Core.Storage;

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
        services.AddSingleton<UltimateAudioTool>();
        services.AddSingleton<IUltimateAudio>(provider => provider.GetRequiredService<UltimateAudioTool>());
        services.AddSingleton<CloudSyncWorker>();
        services.AddHostedService(provider => provider.GetRequiredService<CloudSyncWorker>());
        services.AddSingleton<UltimateCloudStorage>();
        services.AddSingleton<ILibraryStorage>(provider => provider.GetRequiredService<UltimateCloudStorage>());
        // Audio lasts as long as the music: requests end with their cancellation, not with a timeout.
        services.AddHttpClient(UltimateCloudStorage.FilesClientName, client => client.Timeout = Timeout.InfiniteTimeSpan);
        services.AddScoped<Application.Cloud.UltimateCloudService>();
        services.AddScoped<Application.Cloud.CloudSyncService>();
        return services;
    }
}
