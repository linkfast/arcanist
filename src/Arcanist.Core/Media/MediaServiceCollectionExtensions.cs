namespace Arcanist.Core.Media;

public static class MediaServiceCollectionExtensions
{
    public static IServiceCollection AddMediaTools(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<FfmpegOptions>(configuration.GetSection(FfmpegOptions.Section));
        services.AddSingleton<FfmpegLocator>();
        services.AddSingleton<FfmpegDownloader>();
        services.AddHttpClient(FfmpegDownloader.HttpClientName, client =>
        {
            // The GitHub API rejects requests without a user agent.
            client.DefaultRequestHeaders.UserAgent.ParseAdd($"Arcanist/{typeof(FfmpegDownloader).Assembly.GetName().Version}");
            client.Timeout = TimeSpan.FromMinutes(30);
        });
        return services;
    }
}
