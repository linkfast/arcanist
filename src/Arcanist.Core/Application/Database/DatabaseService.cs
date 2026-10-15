using System.Text.Json;
using Arcanist.Core.Data;
using Arcanist.Core.Data.Entities;
using Arcanist.Core.Hosting;
using Arcanist.Core.Setup;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Arcanist.Core.Application.Database;

// Types in this file are part of the public API contracts (GraphQL schema and OpenAPI document).

/// <summary>The server's database, and its migration to PostgreSQL.</summary>
/// <param name="LockedByEnvironment">The database is set outside the panel (environment variables, appsettings): it can only be changed there.</param>
/// <param name="Location">The SQLite file, or PostgreSQL's user, host and database (never the password).</param>
/// <param name="SizeBytes">The SQLite file's size; null for PostgreSQL.</param>
/// <param name="Migration">The last migration since the server started.</param>
/// <param name="PreviousSqlite">The SQLite file a migration left, while the administrator has not decided what to do with it.</param>
public sealed record DatabaseStatus(
    DatabaseProvider Provider,
    bool LockedByEnvironment,
    string Location,
    long? SizeBytes,
    DatabaseMigration? Migration,
    PreviousSqliteFile? PreviousSqlite);

public enum PreviousSqliteAction
{
    /// <summary>Leave it where it is (it is no longer used).</summary>
    Keep,

    /// <summary>Move it to <see cref="ResolvePreviousSqliteInput.Folder"/>.</summary>
    Move,

    Delete,
}

public sealed record ResolvePreviousSqliteInput(PreviousSqliteAction Action, string? Folder);

/// <summary>
/// The database: what it is, and moving from SQLite to PostgreSQL with the server in maintenance. Administrators
/// only; migrating and deciding about the old file need a recent authentication.
/// </summary>
public sealed class DatabaseService(
    ArcanistDbContext db,
    AccessGuard guard,
    IOptionsMonitor<DatabaseOptions> options,
    ISettingsFileWriter settingsWriter,
    DatabaseInitializer initializer,
    DatabaseMigrator migrator,
    ArcanistPaths paths,
    IServiceProvider services)
{
    public async Task<DatabaseStatus> StatusAsync(CancellationToken cancellationToken)
    {
        guard.Admin();
        var current = options.CurrentValue;
        var provider = current.Provider ?? DatabaseProvider.Sqlite;
        string location;
        long? size = null;
        if (provider == DatabaseProvider.Sqlite)
        {
            location = DatabaseMigrator.SqlitePath(current, paths);
            size = SizeOf(location);
        }
        else
        {
            var builder = new NpgsqlConnectionStringBuilder(current.ConnectionString);
            location = $"{builder.Username}@{builder.Host}:{builder.Port}/{builder.Database}";
        }

        return new DatabaseStatus(provider, IsLocked, location, size, migrator.Current, await PreviousAsync(cancellationToken));
    }

    /// <summary>Checks that PostgreSQL answers with that connection string.</summary>
    public async Task<DatabaseTestResult> TestPostgresAsync(string connectionString, CancellationToken cancellationToken)
    {
        guard.Admin();
        if (string.IsNullOrWhiteSpace(connectionString))
            throw ArcanistException.Field("connectionString", ErrorCodes.Required);
        var error = await initializer.TestConnectionAsync(Postgres(connectionString), cancellationToken);
        return new DatabaseTestResult(error is null, error);
    }

    /// <summary>
    /// Starts copying everything to an empty PostgreSQL database; the server is in maintenance until it ends, and
    /// then uses PostgreSQL. Follow it with <see cref="StatusAsync"/>, or anonymously with the maintenance state.
    /// </summary>
    public async Task<DatabaseStatus> MigrateToPostgresAsync(string connectionString, CancellationToken cancellationToken)
    {
        guard.Admin();
        guard.RecentAuthentication();
        if (options.CurrentValue.Provider != DatabaseProvider.Sqlite)
            throw new ArcanistException.Conflict(ErrorCodes.AlreadyPostgres);
        if (IsLocked)
            throw new ArcanistException.Conflict(ErrorCodes.SetByEnvironment);
        if (migrator.IsRunning || await WorkInProgressAsync(cancellationToken))
            throw new ArcanistException.Conflict(ErrorCodes.Busy);
        if (string.IsNullOrWhiteSpace(connectionString))
            throw ArcanistException.Field("connectionString", ErrorCodes.Required);

        var target = Postgres(connectionString);
        if (await initializer.TestConnectionAsync(target, cancellationToken) is not null)
            throw ArcanistException.Field("connectionString", ErrorCodes.ConnectionFailed);
        if (!await IsEmptyAsync(target, cancellationToken))
            throw ArcanistException.Field("connectionString", ErrorCodes.DatabaseNotEmpty);

        migrator.Start(target);
        return await StatusAsync(cancellationToken);
    }

    /// <summary>Keeps, moves or deletes the SQLite file a migration left; afterwards the panel stops asking.</summary>
    public async Task<DatabaseStatus> ResolvePreviousSqliteAsync(ResolvePreviousSqliteInput input, CancellationToken cancellationToken)
    {
        guard.Admin();
        guard.RecentAuthentication();
        var setting = await db.Settings.SingleOrDefaultAsync(s => s.Key == SettingKeys.PreviousSqlite, cancellationToken)
                      ?? throw new ArcanistException.NotFound();
        var previous = JsonSerializer.Deserialize<DatabaseMigrator.PreviousSqlite>(setting.Value, JsonSerializerOptions.Web)!;
        var files = Companions(previous.Path).Where(File.Exists).ToList();
        SqliteConnection.ClearAllPools();

        switch (input.Action)
        {
            case PreviousSqliteAction.Keep:
                break;
            case PreviousSqliteAction.Move:
                var folder = string.IsNullOrWhiteSpace(input.Folder) ? null : input.Folder.Trim();
                if (folder is null || !Path.IsPathFullyQualified(folder) || !Directory.Exists(folder))
                    throw ArcanistException.Field("folder", ErrorCodes.NotFound);
                var name = Unique(folder, Path.GetFileName(previous.Path));
                try
                {
                    foreach (var file in files)
                        File.Move(file, Path.Combine(folder, name + file[previous.Path.Length..]));
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    throw ArcanistException.Field("folder", ErrorCodes.AccessDenied);
                }

                break;
            case PreviousSqliteAction.Delete:
                try
                {
                    foreach (var file in files)
                        File.Delete(file);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    throw new ArcanistException.Conflict(ErrorCodes.AccessDenied);
                }

                break;
            default:
                throw ArcanistException.Field("action", ErrorCodes.InvalidFormat);
        }

        db.Settings.Remove(setting);
        await db.SaveChangesAsync(cancellationToken);
        return await StatusAsync(cancellationToken);
    }

    /// <summary>What anyone may know while the server is in maintenance.</summary>
    public static MaintenanceState Maintenance(DatabaseMaintenance maintenance) => maintenance.State;

    private bool IsLocked => settingsWriter.IsSetOutsideSettingsFile(DatabaseOptions.Section + ":" + nameof(DatabaseOptions.Provider));

    private static DatabaseOptions Postgres(string connectionString) =>
        new() { Provider = DatabaseProvider.Postgres, ConnectionString = connectionString.Trim() };

    /// <summary>Scans, organizing, imports from drop folders and copies of shares write a lot for long: they finish first.</summary>
    private async Task<bool> WorkInProgressAsync(CancellationToken cancellationToken) =>
        await db.ScanJobs.AnyAsync(j => j.Status == ScanStatus.Running, cancellationToken)
        || await db.OrganizeRuns.AnyAsync(r => r.Status == OrganizeRunStatus.Running || r.Status == OrganizeRunStatus.Undoing, cancellationToken)
        || services.GetRequiredService<Core.P2P.ShareCopier>().IsBusy;

    /// <summary>No Arcanist data: a new database, or one an earlier attempt emptied again.</summary>
    private async Task<bool> IsEmptyAsync(DatabaseOptions target, CancellationToken cancellationToken)
    {
        await using var postgres = ArcanistDbContextFactory.Create(target, paths);
        // A database that does not exist yet is created by the migration.
        if (!await postgres.GetService<IRelationalDatabaseCreator>().ExistsAsync(cancellationToken)
            || !(await postgres.Database.GetAppliedMigrationsAsync(cancellationToken)).Any())
            return true;
        return !await postgres.Users.AnyAsync(cancellationToken) && !await postgres.Libraries.AnyAsync(cancellationToken)
                                                                 && !await postgres.Settings.AnyAsync(cancellationToken);
    }

    private async Task<PreviousSqliteFile?> PreviousAsync(CancellationToken cancellationToken)
    {
        var value = await db.Settings.AsNoTracking().Where(s => s.Key == SettingKeys.PreviousSqlite).Select(s => s.Value).FirstOrDefaultAsync(cancellationToken);
        if (value is null)
            return null;
        var previous = JsonSerializer.Deserialize<DatabaseMigrator.PreviousSqlite>(value, JsonSerializerOptions.Web)!;
        var size = SizeOf(previous.Path);
        return new PreviousSqliteFile(previous.Path, size ?? 0, size is not null, previous.MigratedAt);
    }

    /// <summary>The database file and its write-ahead log and shared memory, when present.</summary>
    private static IEnumerable<string> Companions(string path) => [path, path + "-wal", path + "-shm"];

    private static long? SizeOf(string path) =>
        File.Exists(path) ? Companions(path).Where(File.Exists).Sum(f => new FileInfo(f).Length) : null;

    /// <summary>The file name, or "name (2).db"… when the folder already has it.</summary>
    private static string Unique(string folder, string name)
    {
        var candidate = name;
        for (var i = 2; File.Exists(Path.Combine(folder, candidate)); i++)
            candidate = $"{Path.GetFileNameWithoutExtension(name)} ({i}){Path.GetExtension(name)}";
        return candidate;
    }
}
