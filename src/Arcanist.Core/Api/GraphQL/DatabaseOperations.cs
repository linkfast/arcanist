using System.ComponentModel;
using Arcanist.Core.Application.Database;
using Arcanist.Core.Setup;
using GraphQL;

namespace Arcanist.Core.Api.GraphQL;

public sealed partial class Query
{
    [Name("database")]
    [Description("The server's database, the last migration and the SQLite file it left. Administrators only.")]
    public static Task<DatabaseStatus> Database([FromServices] DatabaseService database, CancellationToken cancellationToken) =>
        database.StatusAsync(cancellationToken);
}

public sealed partial class Mutation
{
    [Name("testPostgresConnection")]
    [Description("Checks that PostgreSQL answers with a connection string. Administrators only.")]
    public static Task<DatabaseTestResult> TestPostgresConnection(string connectionString, [FromServices] DatabaseService database, CancellationToken cancellationToken) =>
        database.TestPostgresAsync(connectionString, cancellationToken);

    [Name("migrateToPostgres")]
    [Description("Copies everything to an empty PostgreSQL database with the server in maintenance, then switches to it. Administrators only, with a recent authentication.")]
    public static Task<DatabaseStatus> MigrateToPostgres(string connectionString, [FromServices] DatabaseService database, CancellationToken cancellationToken) =>
        database.MigrateToPostgresAsync(connectionString, cancellationToken);

    [Name("resolvePreviousSqlite")]
    [Description("Keeps, moves or deletes the SQLite file a migration left. Administrators only, with a recent authentication.")]
    public static Task<DatabaseStatus> ResolvePreviousSqlite(ResolvePreviousSqliteInput input, [FromServices] DatabaseService database, CancellationToken cancellationToken) =>
        database.ResolvePreviousSqliteAsync(input, cancellationToken);
}
