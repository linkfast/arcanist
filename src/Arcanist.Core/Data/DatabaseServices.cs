using Arcanist.Core.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;

namespace Arcanist.Core.Data;

public enum DatabaseProvider
{
    Sqlite,
    Postgres,
}

/// <summary>Bound from <c>Arcanist:Database</c>; written to <c>arcanist.json</c> by the setup wizard.</summary>
public sealed class DatabaseOptions
{
    public const string Section = "Arcanist:Database";

    /// <summary>Null until the database has been chosen during setup.</summary>
    public DatabaseProvider? Provider { get; set; }

    /// <summary>Required for PostgreSQL. For SQLite, defaults to <c>arcanist.db</c> in the data directory.</summary>
    public string? ConnectionString { get; set; }
}

public sealed class DatabaseNotConfiguredException()
    : InvalidOperationException("No database has been configured yet; complete the setup wizard first.");

/// <summary>
/// Creates contexts for whichever provider is currently configured. Reads the options on every call, so
/// choosing a database in the setup wizard takes effect without restarting.
/// </summary>
public interface IArcanistDbContextFactory
{
    bool IsConfigured { get; }

    ArcanistDbContext CreateDbContext();
}

internal sealed class ArcanistDbContextFactory(
    IOptionsMonitor<DatabaseOptions> options,
    ArcanistPaths paths,
    ILoggerFactory loggerFactory,
    DatabaseMaintenance maintenance,
    IHostEnvironment environment) : IArcanistDbContextFactory
{
    private readonly MaintenanceInterceptor _maintenance = new(maintenance);
    // In the tests, a query that limits rows without an order fails instead of warning: its results would change
    // from one run to the next (and its warning filled the logs of a server scanning a library).
    private readonly bool _strict = environment.IsEnvironment("Testing");

    public bool IsConfigured => options.CurrentValue.Provider is not null;

    /// <summary>A context of the server's database, whose writes are refused while it is frozen for maintenance.</summary>
    public ArcanistDbContext CreateDbContext() => Create(options.CurrentValue, paths, loggerFactory, _maintenance, _strict);

    /// <summary>A context of any database; the setup wizard's and the migrator's have no maintenance interceptor.</summary>
    public static ArcanistDbContext Create(
        DatabaseOptions options, ArcanistPaths paths, ILoggerFactory? loggerFactory = null, IInterceptor? interceptor = null, bool strict = false)
    {
        var connectionString = string.IsNullOrWhiteSpace(options.ConnectionString) ? null : options.ConnectionString;

        return options.Provider switch
        {
            DatabaseProvider.Sqlite => new SqliteArcanistDbContext(
                Configure(new DbContextOptionsBuilder<SqliteArcanistDbContext>(), loggerFactory, interceptor, strict)
                    .UseSqlite(connectionString ?? DefaultSqliteConnectionString(paths))
                    .Options),
            DatabaseProvider.Postgres => new PostgresArcanistDbContext(
                Configure(new DbContextOptionsBuilder<PostgresArcanistDbContext>(), loggerFactory, interceptor, strict)
                    .UseNpgsql(connectionString
                               ?? throw new InvalidOperationException("PostgreSQL requires a connection string."))
                    .Options),
            null => throw new DatabaseNotConfiguredException(),
            _ => throw new ArgumentOutOfRangeException(nameof(options), options.Provider, null),
        };
    }

    public static string DefaultSqliteConnectionString(ArcanistPaths paths) =>
        $"Data Source={Path.Combine(paths.Root, "arcanist.db")}";

    private static DbContextOptionsBuilder<T> Configure<T>(DbContextOptionsBuilder<T> builder, ILoggerFactory? loggerFactory, IInterceptor? interceptor, bool strict)
        where T : DbContext
    {
        if (loggerFactory is not null)
            builder.UseLoggerFactory(loggerFactory);
        if (interceptor is not null)
            builder.AddInterceptors(interceptor);
        if (strict)
            builder.ConfigureWarnings(w => w.Throw(CoreEventId.RowLimitingOperationWithoutOrderByWarning));

        return builder;
    }
}

public static class DatabaseServiceCollectionExtensions
{
    public static IServiceCollection AddArcanistDatabase(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<DatabaseOptions>(configuration.GetSection(DatabaseOptions.Section));
        services.AddSingleton<DatabaseMaintenance>();
        services.AddSingleton<IArcanistDbContextFactory, ArcanistDbContextFactory>();
        services.AddSingleton<DatabaseMigrator>();
        services.AddScoped<Application.Database.DatabaseService>();
        services.AddScoped<ArcanistDbContext>(sp => sp.GetRequiredService<IArcanistDbContextFactory>().CreateDbContext());
        services.AddSingleton<DatabaseInitializer>();
        services.AddHostedService<DatabaseMigrationService>();
        return services;
    }
}
