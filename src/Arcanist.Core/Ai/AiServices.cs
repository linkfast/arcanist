using Arcanist.Core.Media;

namespace Arcanist.Core.Ai;

public static class AiServiceCollectionExtensions
{
    /// <summary>The AI extension's analysis: settings, progress, events and the background worker.</summary>
    public static IServiceCollection AddAi(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<AiOptions>(configuration.GetSection(AiOptions.Section));
        services.AddSingleton<AiSettings>();
        services.AddSingleton<AiMonitor>();
        services.AddSingleton<TrackAnalysisEvents>();
        services.AddSingleton<StreamActivity>();
        services.AddSingleton<TrackAnalysisWorker>();
        services.AddHostedService(provider => provider.GetRequiredService<TrackAnalysisWorker>());
        services.AddScoped<Application.Ai.TrackAnalysisService>();
        services.AddScoped<Application.Ai.AiAdminService>();
        return services;
    }
}
