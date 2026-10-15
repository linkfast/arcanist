using Arcanist.Core.Application.Database;
using Arcanist.Core.Data;

namespace Arcanist.Core.Api.Rest;

public sealed record PostgresConnectionInput(string ConnectionString);

public static class DatabaseEndpoints
{
    public static IEndpointRouteBuilder MapDatabaseEndpoints(this IEndpointRouteBuilder rest)
    {
        var database = rest.MapGroup("/database").WithTags("Database");

        database.MapGet("", (DatabaseService service, CancellationToken ct) => service.StatusAsync(ct))
            .WithName("Database")
            .WithSummary("The server's database, the last migration and the SQLite file it left. Administrators only.");

        database.MapPost("/test", (PostgresConnectionInput input, DatabaseService service, CancellationToken ct) =>
                service.TestPostgresAsync(input.ConnectionString, ct))
            .WithName("TestPostgresConnection")
            .WithSummary("Checks that PostgreSQL answers with a connection string. Administrators only.");

        database.MapPost("/migrate", (PostgresConnectionInput input, DatabaseService service, CancellationToken ct) =>
                service.MigrateToPostgresAsync(input.ConnectionString, ct))
            .WithName("MigrateToPostgres")
            .WithSummary("Copies everything to an empty PostgreSQL database with the server in maintenance, then switches to it. " +
                         "Administrators only, with a recent authentication.");

        database.MapPost("/previous-sqlite", (ResolvePreviousSqliteInput input, DatabaseService service, CancellationToken ct) =>
                service.ResolvePreviousSqliteAsync(input, ct))
            .WithName("ResolvePreviousSqlite")
            .WithSummary("Keeps, moves or deletes the SQLite file a migration left. Administrators only, with a recent authentication.");

        rest.MapGet("/maintenance", (DatabaseMaintenance maintenance) => DatabaseService.Maintenance(maintenance))
            .WithTags("Database")
            .WithName("MaintenanceState")
            .WithSummary("Whether the server is in maintenance (migrating its database) and how far it got. Anyone can ask, also during maintenance.");

        return rest;
    }
}
