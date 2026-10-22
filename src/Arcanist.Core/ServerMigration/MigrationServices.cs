namespace Arcanist.Core.ServerMigration;

internal static class MigrationServices
{
    /// <summary>Migrations between Arcanist servers: the export API's codes, and bringing another server over.</summary>
    public static IServiceCollection AddArcanistMigration(this IServiceCollection services)
    {
        services.AddScoped<Application.ServerMigration.MigrationCodeService>();
        services.AddScoped<Application.ServerMigration.ArcanistImportService>();
        services.AddSingleton<ArcanistSourceClient>();
        services.AddSingleton<ArcanistImportWorker>();
        services.AddHostedService(provider => provider.GetRequiredService<ArcanistImportWorker>());
        services.AddHttpClient(ArcanistSourceClient.HttpClientName, client =>
        {
            client.Timeout = TimeSpan.FromHours(2);
            client.DefaultRequestHeaders.UserAgent.ParseAdd(ArcanistSourceClient.UserAgent);
        });
        return services;
    }
}
