using Arcanist.Core.Hosting;
using Microsoft.EntityFrameworkCore;
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
    ILoggerFactory loggerFactory) : IArcanistDbContextFactory
{
    public bool IsConfigured => options.CurrentValue.Provider is not null;

    public ArcanistDbContext CreateDbContext() => Create(options.CurrentValue, paths, loggerFactory);

    public static ArcanistDbContext Create(DatabaseOptions options, ArcanistPaths paths, ILoggerFactory? loggerFactory = null) =>
        options.Provider switch
        {
            DatabaseProvider.Sqlite => new SqliteArcanistDbContext(
                Configure(new DbContextOptionsBuilder<SqliteArcanistDbContext>(), loggerFactory)
                    .UseSqlite(options.ConnectionString ?? DefaultSqliteConnectionString(paths))
                    .Options),
            DatabaseProvider.Postgres => new PostgresArcanistDbContext(
                Configure(new DbContextOptionsBuilder<PostgresArcanistDbContext>(), loggerFactory)
                    .UseNpgsql(options.ConnectionString
                               ?? throw new InvalidOperationException("PostgreSQL requires a connection string."))
                    .Options),
            null => throw new DatabaseNotConfiguredException(),
            _ => throw new ArgumentOutOfRangeException(nameof(options), options.Provider, null),
        };

    public static string DefaultSqliteConnectionString(ArcanistPaths paths) =>
        $"Data Source={Path.Combine(paths.Root, "arcanist.db")}";

    private static DbContextOptionsBuilder<T> Configure<T>(DbContextOptionsBuilder<T> builder, ILoggerFactory? loggerFactory)
        where T : DbContext
    {
        if (loggerFactory is not null)
            builder.UseLoggerFactory(loggerFactory);

        return builder;
    }
}

public static class DatabaseServiceCollectionExtensions
{
    public static IServiceCollection AddArcanistDatabase(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<DatabaseOptions>(configuration.GetSection(DatabaseOptions.Section));
        services.AddSingleton<IArcanistDbContextFactory, ArcanistDbContextFactory>();
        services.AddScoped<ArcanistDbContext>(sp => sp.GetRequiredService<IArcanistDbContextFactory>().CreateDbContext());
        return services;
    }
}
