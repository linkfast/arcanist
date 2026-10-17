namespace Arcanist.Core.ServerAudio;

public static class ServerAudioServiceCollectionExtensions
{
    public static IServiceCollection AddServerAudio(this IServiceCollection services)
    {
        services.AddSingleton<IServerAudioOutput, NativeServerAudioOutput>();
        services.AddSingleton<ServerAudioPlayer>();
        services.AddHostedService(sp => sp.GetRequiredService<ServerAudioPlayer>());
        services.AddScoped<Application.ServerAudio.ServerAudioService>();
        services.AddSingleton<Cloud.ServerAudioConnect>();
        services.AddHostedService(sp => sp.GetRequiredService<Cloud.ServerAudioConnect>());
        return services;
    }
}
