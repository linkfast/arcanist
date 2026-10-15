namespace Arcanist.Core.Extensions.Mstream;

public static class MstreamServiceCollectionExtensions
{
    /// <summary>What the mStream extension shares with the rest of the server: its settings, and the panel's API.</summary>
    public static IServiceCollection AddMstream(this IServiceCollection services)
    {
        services.AddSingleton<MstreamSettings>();
        services.AddSingleton<MstreamNowPlaying>();
        services.AddSingleton<MstreamQuickConnect>();
        services.AddSingleton<P2P.IAppTunnels>(provider => provider.GetRequiredService<MstreamQuickConnect>());
        services.AddScoped<Application.Mstream.MstreamService>();
        return services;
    }
}
