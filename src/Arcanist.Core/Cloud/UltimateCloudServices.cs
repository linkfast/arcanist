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
        services.AddHostedService(provider => provider.GetRequiredService<UltimateCloudAccount>());
        services.AddSingleton<UserCloudAccounts>();
        services.AddHostedService(provider => provider.GetRequiredService<UserCloudAccounts>());
        services.AddSingleton<ICloudLinkState, CloudTiers>();
        services.AddSingleton<UltimateCloudSignals>();
        services.AddHttpClient(UltimateCloudClient.HttpClientName, client => client.Timeout = TimeSpan.FromSeconds(30));
        services.AddSingleton<CloudTrackDownloader>();
        services.AddSingleton<CloudDropWorker>();
        services.AddSingleton<UltimateCloudEvents>();
        services.AddHostedService(provider => provider.GetRequiredService<UltimateCloudEvents>());
        services.AddHostedService(provider => provider.GetRequiredService<CloudDropWorker>());
        services.AddSingleton<UltimateCloudServicesSettings>();

        // What extensions see of Ultimate Cloud (Ultimate Cloud Plus takes on the Cloud plan's features with it).
        services.AddSingleton<Extensibility.IUltimateCloud, Extensions.Sdk.CoreUltimateCloud>();
        services.AddSingleton<Extensibility.IMusicLibraries, Extensions.Sdk.CoreMusicLibraries>();
        services.AddSingleton<Extensibility.ICloudSyncStore, Extensions.Sdk.CoreCloudSyncStore>();
        services.AddSingleton<Extensibility.IMyCloudUploadQueue, Extensions.Sdk.CoreMyCloudUploadQueue>();
        services.AddSingleton<Extensibility.ICloudDropUploads, Extensions.Sdk.CoreCloudDropUploads>();
        services.AddSingleton<Extensibility.IP2PDevices, Extensions.Sdk.CoreP2PDevices>();
        services.AddSingleton<UltimateCloudReporter>();
        services.AddHostedService(provider => provider.GetRequiredService<UltimateCloudReporter>());
        services.AddSingleton<UltimateCloudStorage>();
        services.AddSingleton<ILibraryStorage>(provider => provider.GetRequiredService<UltimateCloudStorage>());
        // Audio lasts as long as the music: requests end with their cancellation, not with a timeout.
        services.AddHttpClient(UltimateCloudStorage.FilesClientName, client => client.Timeout = Timeout.InfiniteTimeSpan);
        services.AddScoped<Application.Cloud.UltimateCloudService>();
        services.AddScoped<Application.Cloud.CloudSyncService>();
        services.AddSingleton<Application.Cloud.CloudPlus>();
        services.AddScoped<Application.Cloud.MyUltimateCloudService>();
        return services;
    }
}
