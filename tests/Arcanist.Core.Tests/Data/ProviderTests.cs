using Arcanist.Core.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Arcanist.Core.Tests.Data;

public sealed class SqliteProviderTests : DatabaseProviderTests, IDisposable
{
    // An in-memory database lives as long as its connection, so every context of a test shares this one.
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private bool _migrated;

    protected override async Task<ArcanistDbContext> OpenAsync()
    {
        if (_connection.State != System.Data.ConnectionState.Open)
            await _connection.OpenAsync(TestContext.Current.CancellationToken);

        var db = new SqliteArcanistDbContext(
            new DbContextOptionsBuilder<SqliteArcanistDbContext>().UseSqlite(_connection).Options);

        if (!_migrated)
        {
            await db.Database.MigrateAsync(TestContext.Current.CancellationToken);
            _migrated = true;
        }

        return db;
    }

    public void Dispose() => _connection.Dispose();
}

public sealed class PostgresProviderTests(PostgresContainer postgres)
    : DatabaseProviderTests, IClassFixture<PostgresContainer>
{
    private readonly string _connectionString = postgres.ConnectionStringFor($"arcanist_{Guid.NewGuid():N}");
    private bool _migrated;

    protected override async Task<ArcanistDbContext> OpenAsync()
    {
        Assert.SkipUnless(postgres.IsAvailable, $"PostgreSQL tests need Docker: {postgres.UnavailableReason}");

        var db = new PostgresArcanistDbContext(
            new DbContextOptionsBuilder<PostgresArcanistDbContext>().UseNpgsql(_connectionString).Options);

        if (!_migrated)
        {
            // Creates the per-test database before applying migrations.
            await db.Database.MigrateAsync(TestContext.Current.CancellationToken);
            _migrated = true;
        }

        return db;
    }
}

/// <summary>A disposable PostgreSQL server shared by the tests of a class. Each test uses its own database.</summary>
public sealed class PostgresContainer : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17-alpine").Build();

    public bool IsAvailable { get; private set; }

    public string? UnavailableReason { get; private set; }

    public string ConnectionStringFor(string database) =>
        new NpgsqlConnectionStringBuilder(_container.GetConnectionString()) { Database = database }.ConnectionString;

    public async ValueTask InitializeAsync()
    {
        try
        {
            await _container.StartAsync();
            IsAvailable = true;
        }
        catch (Exception e)
        {
            UnavailableReason = e.Message;
        }
    }

    public async ValueTask DisposeAsync() => await _container.DisposeAsync();
}
