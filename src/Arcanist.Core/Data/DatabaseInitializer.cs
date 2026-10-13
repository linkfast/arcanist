using Arcanist.Core.Hosting;
using Arcanist.Core.Setup;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Arcanist.Core.Data;

public sealed class DatabaseInitializer(ArcanistPaths paths, ILoggerFactory loggerFactory, ILogger<DatabaseInitializer> logger)
{
    /// <summary>Checks that the database can be reached, returning the failure message otherwise.</summary>
    public async Task<string?> TestConnectionAsync(DatabaseOptions options, CancellationToken cancellationToken)
    {
        try
        {
            await using var db = ArcanistDbContextFactory.Create(options, paths);

            // PostgreSQL databases are created by the migration, so only the server has to be reachable here.
            if (options.Provider == DatabaseProvider.Postgres)
            {
                if (!await db.GetService<IRelationalDatabaseCreator>().ExistsAsync(cancellationToken))
                {
                    await using var connection = new NpgsqlConnection(
                        new NpgsqlConnectionStringBuilder(options.ConnectionString) { Database = "postgres" }.ConnectionString);
                    await connection.OpenAsync(cancellationToken);
                }

                return null;
            }

            await db.Database.OpenConnectionAsync(cancellationToken);
            return null;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            logger.LogWarning(e, "Database connection test failed for {Provider}", options.Provider);
            return e.Message;
        }
    }

    /// <summary>Creates the database if needed and applies pending migrations.</summary>
    public async Task MigrateAsync(DatabaseOptions options, CancellationToken cancellationToken)
    {
        await using var db = ArcanistDbContextFactory.Create(options, paths, loggerFactory);

        var pending = (await db.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
        if (pending.Count > 0)
            logger.LogInformation("Applying {Count} database migration(s): {Migrations}", pending.Count, pending);

        await db.Database.MigrateAsync(cancellationToken);

        if (options.Provider == DatabaseProvider.Sqlite)
        {
            // Persistent per database file: readers no longer block the scanner's writes.
            await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;", cancellationToken);
        }

        await Search.FullTextSearch.EnsureAsync(db, cancellationToken);
    }
}

/// <summary>Brings an already configured database up to date before the server starts accepting requests.</summary>
internal sealed class DatabaseMigrationService(
    IOptions<DatabaseOptions> database,
    IOptions<SetupOptions> setup,
    DatabaseInitializer initializer) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken) =>
        setup.Value.Completed && database.Value.Provider is not null
            ? initializer.MigrateAsync(database.Value, cancellationToken)
            : Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
