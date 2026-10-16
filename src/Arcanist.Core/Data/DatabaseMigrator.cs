using System.Reflection;
using Arcanist.Core.Application;
using Arcanist.Core.Auth;
using Arcanist.Core.Hosting;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Options;

namespace Arcanist.Core.Data;

// Types in this file are part of the public API contracts (GraphQL schema and OpenAPI document).

public enum MigrationPhase
{
    /// <summary>Waiting for running work to finish before pausing writes.</summary>
    Pausing,

    /// <summary>Creating the tables in the new database.</summary>
    CreatingSchema,

    Copying,

    /// <summary>Counting the rows of every table in both databases.</summary>
    Verifying,

    /// <summary>Making the new database the server's.</summary>
    Switching,

    Completed,

    /// <summary>Nothing changed: the server keeps its database (and the new one is emptied again).</summary>
    Failed,
}

/// <param name="Name">The table.</param>
public sealed record MigrationTable(string Name, long Rows, long Copied);

/// <summary>A migration to PostgreSQL, while it runs and after (until the server restarts).</summary>
/// <param name="Rows">Rows to copy in all; known once copying starts.</param>
public sealed record DatabaseMigration(
    MigrationPhase Phase,
    IReadOnlyList<MigrationTable> Tables,
    long Rows,
    long Copied,
    string? ErrorCode,
    DateTime StartedAt,
    DateTime? FinishedAt);

/// <summary>The SQLite database left behind by a migration, until the administrator decides what to do with it.</summary>
/// <param name="Path">Its file (with its <c>-wal</c> and <c>-shm</c> companions when present).</param>
/// <param name="SizeBytes">The files together.</param>
/// <param name="Exists">False when someone already moved or deleted it.</param>
public sealed record PreviousSqliteFile(string Path, long SizeBytes, bool Exists, DateTime MigratedAt);

/// <summary>
/// Copies the SQLite database to PostgreSQL and switches the server to it, without restarting:
/// <list type="number">
/// <item>pauses the server's writes (<see cref="DatabaseMaintenance"/>), waiting at most a minute for running work;</item>
/// <item>creates the schema in PostgreSQL with the migrations, and its full-text index;</item>
/// <item>copies every table, parents before children, in batches, with the same keys;</item>
/// <item>compares the row counts of every table;</item>
/// <item>writes the new database to <c>arcanist.json</c> (the server reads it at once) and resumes.</item>
/// </list>
/// If anything fails, the server keeps SQLite and what was copied is deleted from PostgreSQL. SQLite is never
/// changed; afterwards the administrator chooses what to do with its file.
/// </summary>
public sealed class DatabaseMigrator(
    IOptionsMonitor<DatabaseOptions> options,
    DatabaseInitializer initializer,
    DatabaseMaintenance maintenance,
    ISettingsFileWriter settingsWriter,
    UserAccessCache accessCache,
    ArcanistPaths paths,
    TimeProvider time,
    ILogger<DatabaseMigrator> logger)
{
    private const int BatchSize = 1000;
    internal static readonly TimeSpan PauseTimeout = TimeSpan.FromMinutes(1);

    private readonly Lock _lock = new();
    private Progress? _progress;
    private Task _running = Task.CompletedTask;

    public DatabaseMigration? Current => _progress?.Snapshot();

    public bool IsRunning => _progress is { } progress && progress.Phase is not (MigrationPhase.Completed or MigrationPhase.Failed);

    /// <summary>The migration in the background, for tests.</summary>
    internal Task Running => _running;

    /// <summary>Starts migrating to <paramref name="target"/>; the checks are the caller's (see <c>DatabaseService</c>).</summary>
    public void Start(DatabaseOptions target)
    {
        lock (_lock)
        {
            if (IsRunning)
                throw new ArcanistException.Conflict(ErrorCodes.Busy);
            _progress = new Progress(Now());
            var progress = _progress;
            _running = Task.Run(() => RunAsync(options.CurrentValue, target, progress));
        }
    }

    private async Task RunAsync(DatabaseOptions source, DatabaseOptions target, Progress progress)
    {
        var cancellationToken = CancellationToken.None;
        progress.Phase = MigrationPhase.Pausing;
        maintenance.Phase = "pausing";
        if (!await maintenance.EnterAsync(PauseTimeout, cancellationToken))
        {
            progress.Fail(ErrorCodes.Busy, Now());
            logger.LogWarning("Migration to PostgreSQL not started: work in progress did not finish in time");
            return;
        }

        var order = ModelOrder();
        var copiedSomething = false;
        try
        {
            logger.LogInformation("Migrating the database to PostgreSQL");
            progress.Phase = MigrationPhase.CreatingSchema;
            maintenance.Phase = "schema";
            await initializer.MigrateAsync(target, cancellationToken);

            await using var from = ArcanistDbContextFactory.Create(source, paths);
            await using var to = ArcanistDbContextFactory.Create(target, paths);
            if (await to.Users.AnyAsync(cancellationToken))
                throw new ArcanistException.Conflict(ErrorCodes.DatabaseNotEmpty);

            foreach (var type in order)
                progress.Add(type.GetTableName()!, await CountAsync(type, from, cancellationToken));

            progress.Phase = MigrationPhase.Copying;
            maintenance.Phase = "copying";
            copiedSomething = true;
            foreach (var type in order)
            {
                await CopyTableAsync(type, from, to, progress.Copy, cancellationToken);
                maintenance.Percent = progress.Percent;
            }

            progress.Phase = MigrationPhase.Verifying;
            maintenance.Phase = "verifying";
            foreach (var type in order)
            {
                var (expected, actual) = (await CountAsync(type, from, cancellationToken), await CountAsync(type, to, cancellationToken));
                if (expected != actual)
                {
                    logger.LogError("Table {Table}: {Expected} rows in SQLite, {Actual} in PostgreSQL", type.GetTableName(), expected, actual);
                    throw new ArcanistException.Conflict(ErrorCodes.VerificationFailed);
                }
            }

            progress.Phase = MigrationPhase.Switching;
            maintenance.Phase = "switching";
            var previous = SqlitePath(source, paths);
            to.Settings.Add(new Entities.Setting
            {
                Key = SettingKeys.PreviousSqlite,
                Value = System.Text.Json.JsonSerializer.Serialize(new PreviousSqlite(previous, Now()), System.Text.Json.JsonSerializerOptions.Web),
                UpdatedAt = Now(),
            });
            await to.SaveChangesAsync(cancellationToken);
            await settingsWriter.SetAsync(new Dictionary<string, object?>
            {
                [DatabaseOptions.Section + ":Provider"] = DatabaseProvider.Postgres,
                [DatabaseOptions.Section + ":ConnectionString"] = target.ConnectionString,
            }, cancellationToken);
            // SQLite is not used any more: its file can be moved or deleted.
            SqliteConnection.ClearAllPools();
            accessCache.InvalidateAll();
            progress.Complete(Now());
            logger.LogInformation("The database is now PostgreSQL: {Rows} rows copied", progress.Copied);
        }
        catch (Exception e)
        {
            var code = e is ArcanistException { Code: var known } && known is ErrorCodes.DatabaseNotEmpty or ErrorCodes.VerificationFailed
                ? known
                : ErrorCodes.MigrationFailed;
            logger.LogError(e, "Migration to PostgreSQL failed; the server keeps SQLite");
            if (copiedSomething)
                await EmptyAsync(target, order);
            progress.Fail(code, Now());
        }
        finally
        {
            maintenance.Leave();
        }
    }

    /// <summary>Deletes what was copied, children first, so the database can be used for another attempt.</summary>
    private async Task EmptyAsync(DatabaseOptions target, IReadOnlyList<IEntityType> order)
    {
        try
        {
            await using var to = ArcanistDbContextFactory.Create(target, paths);
            foreach (var type in order.Reverse())
                await Invoke(nameof(DeleteAllAsync), type, to);
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "What was copied to PostgreSQL could not be deleted");
        }
    }

    private static Task<long> CountAsync(IEntityType type, ArcanistDbContext db, CancellationToken cancellationToken) =>
        (Task<long>)typeof(DatabaseMigrator).GetMethod(nameof(CountOfAsync), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(type.ClrType).Invoke(null, [db, cancellationToken])!;

    private static Task<long> CountOfAsync<T>(ArcanistDbContext db, CancellationToken cancellationToken) where T : class =>
        db.Set<T>().LongCountAsync(cancellationToken);

    private static Task Invoke(string method, IEntityType type, params object[] arguments) =>
        (Task)typeof(DatabaseMigrator).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(type.ClrType).Invoke(null, arguments)!;

    /// <summary>Every table of the model, parents before children (see <see cref="Order"/>).</summary>
    internal static IReadOnlyList<IEntityType> ModelOrder()
    {
        using var model = new SqliteArcanistDbContext(new DbContextOptionsBuilder<SqliteArcanistDbContext>().UseSqlite("Data Source=:memory:").Options);
        return Order(model.Model);
    }

    /// <summary>Copies one table between databases of either provider (also backups' export of PostgreSQL to SQLite).</summary>
    internal static Task CopyTableAsync(IEntityType type, ArcanistDbContext from, ArcanistDbContext to, Action<string, int> copied, CancellationToken cancellationToken) =>
        Invoke(nameof(CopyAsync), type, from, to, copied, cancellationToken);

    /// <summary>Streams a table from one database and inserts it in batches into the other, with the same keys.</summary>
    private static async Task CopyAsync<T>(ArcanistDbContext from, ArcanistDbContext to, Action<string, int> copied, CancellationToken cancellationToken) where T : class
    {
        var table = to.Model.FindEntityType(typeof(T))!.GetTableName()!;
        var batch = new List<T>(BatchSize);
        await foreach (var row in from.Set<T>().AsNoTracking().AsAsyncEnumerable().WithCancellation(cancellationToken))
        {
            batch.Add(row);
            if (batch.Count == BatchSize)
                await FlushAsync();
        }

        await FlushAsync();

        async Task FlushAsync()
        {
            if (batch.Count == 0)
                return;
            to.Set<T>().AddRange(batch);
            await to.SaveChangesAsync(cancellationToken);
            to.ChangeTracker.Clear();
            copied(table, batch.Count);
            batch.Clear();
        }
    }

    private static Task DeleteAllAsync<T>(ArcanistDbContext db) where T : class => db.Set<T>().ExecuteDeleteAsync();

    /// <summary>Every entity type, each after the ones its foreign keys point to.</summary>
    internal static IReadOnlyList<IEntityType> Order(IModel model)
    {
        var types = model.GetEntityTypes().Where(t => t.GetTableName() is not null).OrderBy(t => t.Name, StringComparer.Ordinal).ToList();
        var ordered = new List<IEntityType>();
        var placed = new HashSet<IEntityType>();
        while (ordered.Count < types.Count)
        {
            var ready = types.Where(t => !placed.Contains(t)
                                         && t.GetForeignKeys().All(fk => fk.PrincipalEntityType == t || placed.Contains(fk.PrincipalEntityType)))
                .ToList();
            if (ready.Count == 0)
                throw new InvalidOperationException("The model has a cycle of foreign keys.");
            ordered.AddRange(ready);
            placed.UnionWith(ready);
        }

        return ordered;
    }

    internal static string SqlitePath(DatabaseOptions source, ArcanistPaths paths) =>
        Path.GetFullPath(new SqliteConnectionStringBuilder(
            string.IsNullOrWhiteSpace(source.ConnectionString) ? ArcanistDbContextFactory.DefaultSqliteConnectionString(paths) : source.ConnectionString).DataSource);

    private DateTime Now() => time.GetUtcNow().UtcDateTime;

    /// <summary>Kept in the new database until the administrator decides about the file.</summary>
    internal sealed record PreviousSqlite(string Path, DateTime MigratedAt);

    private sealed class Progress(DateTime startedAt)
    {
        private readonly Lock _lock = new();
        private readonly List<(string Name, long Rows, long Copied)> _tables = [];
        private string? _error;
        private DateTime? _finishedAt;

        public volatile MigrationPhase Phase;

        public long Copied
        {
            get
            {
                lock (_lock)
                    return _tables.Sum(t => t.Copied);
            }
        }

        public int? Percent
        {
            get
            {
                lock (_lock)
                {
                    var rows = _tables.Sum(t => t.Rows);
                    return rows == 0 ? null : (int)(_tables.Sum(t => t.Copied) * 100 / rows);
                }
            }
        }

        public void Add(string table, long rows)
        {
            lock (_lock)
                _tables.Add((table, rows, 0));
        }

        public void Copy(string table, int rows)
        {
            lock (_lock)
            {
                var index = _tables.FindIndex(t => t.Name == table);
                _tables[index] = _tables[index] with { Copied = _tables[index].Copied + rows };
            }
        }

        public void Complete(DateTime at)
        {
            _finishedAt = at;
            Phase = MigrationPhase.Completed;
        }

        public void Fail(string code, DateTime at)
        {
            _error = code;
            _finishedAt = at;
            Phase = MigrationPhase.Failed;
        }

        public DatabaseMigration Snapshot()
        {
            lock (_lock)
            {
                return new DatabaseMigration(Phase, _tables.Select(t => new MigrationTable(t.Name, t.Rows, t.Copied)).ToList(),
                    _tables.Sum(t => t.Rows), _tables.Sum(t => t.Copied), _error, startedAt, _finishedAt);
            }
        }
    }
}
