using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Arcanist.Core.Data;

// One context type per provider, so each keeps its own migrations (Data/Migrations/Sqlite, /Postgres):
//
//   dotnet ef migrations add <Name> --project src/Arcanist.Core --context SqliteArcanistDbContext --output-dir Data/Migrations/Sqlite
//   dotnet ef migrations add <Name> --project src/Arcanist.Core --context PostgresArcanistDbContext --output-dir Data/Migrations/Postgres

public sealed class SqliteArcanistDbContext(DbContextOptions<SqliteArcanistDbContext> options)
    : ArcanistDbContext(options);

public sealed class PostgresArcanistDbContext(DbContextOptions<PostgresArcanistDbContext> options)
    : ArcanistDbContext(options);

// Design-time factories used only by `dotnet ef`; generating migrations does not connect to a database.

internal sealed class SqliteDesignTimeFactory : IDesignTimeDbContextFactory<SqliteArcanistDbContext>
{
    public SqliteArcanistDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<SqliteArcanistDbContext>().UseSqlite("Data Source=design-time.db").Options);
}

internal sealed class PostgresDesignTimeFactory : IDesignTimeDbContextFactory<PostgresArcanistDbContext>
{
    public PostgresArcanistDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<PostgresArcanistDbContext>().UseNpgsql("Host=localhost;Database=arcanist").Options);
}
