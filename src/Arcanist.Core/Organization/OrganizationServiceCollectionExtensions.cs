namespace Arcanist.Core.Organization;

public static class OrganizationServiceCollectionExtensions
{
    public static IServiceCollection AddOrganization(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<OrganizationOptions>(configuration.GetSection(OrganizationOptions.Section));
        services.AddSingleton<LibraryLocks>();
        services.AddSingleton<LibraryOrganizer>();
        services.AddSingleton<OrganizeQueue>();
        services.AddHostedService<OrganizeWorker>();
        services.AddSingleton<LibraryWatchSignal>();
        services.AddHostedService<LibraryWatcher>();
        services.AddSingleton<DropFolderImporter>();
        services.AddSingleton<ImportMonitor>();
        services.AddHostedService<DropFolderWorker>();
        services.AddSingleton<ImportSuggester>();
        services.AddHostedService(sp => sp.GetRequiredService<ImportSuggester>());
        return services;
    }
}
