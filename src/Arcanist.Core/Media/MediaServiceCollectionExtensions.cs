namespace Arcanist.Core.Media;

public static class MediaServiceCollectionExtensions
{
    public static IServiceCollection AddMediaTools(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<FfmpegOptions>(configuration.GetSection(FfmpegOptions.Section));
        services.AddSingleton<FfmpegLocator>();
        services.AddSingleton<FfmpegDownloader>();
        services.Configure<TranscodingOptions>(configuration.GetSection(TranscodingOptions.Section));
        services.AddSingleton<Transcoder>();
        services.AddHostedService<FfmpegWarmup>();
        services.AddHttpClient(FfmpegDownloader.HttpClientName, client =>
        {
            // The GitHub API rejects requests without a user agent.
            client.DefaultRequestHeaders.UserAgent.ParseAdd($"Arcanist/{typeof(FfmpegDownloader).Assembly.GetName().Version}");
            client.Timeout = TimeSpan.FromMinutes(30);
        });
        return services;
    }
}

/// <summary>Looks for ffmpeg once at startup, so serverInfo can tell clients what they can transcode to.</summary>
internal sealed class FfmpegWarmup(FfmpegLocator locator, ILogger<FfmpegWarmup> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();
        try
        {
            await locator.ResolveAsync(stoppingToken);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // An exception escaping a background service stops the host; looking again later is enough.
            logger.LogWarning(e, "Could not look for ffmpeg at startup");
        }
    }
}
