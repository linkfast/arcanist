using Arcanist.Core.Application.ServerMigration;

namespace Arcanist.Core.Api.Rest;

public static class MigrationEndpoints
{
    public static IEndpointRouteBuilder MapMigrationEndpoints(this IEndpointRouteBuilder rest)
    {
        var codes = rest.MapGroup("/migration-codes").WithTags("Import");

        rest.MapGet("/me/export/files", (Guid? libraryId, string? after, int? first, PersonalExportService service, CancellationToken ct) =>
                service.FilesAsync(libraryId, after, first, ct))
            .WithTags("Migration")
            .WithName("MyExportFiles")
            .WithSummary("A user's own copy of the music they can play (a local backup, without being an administrator): the original files, page by page, with library, path, size, audio hash and a resumable download URL. Requires DOWNLOAD.");

        rest.MapGet("/me/export/data", async (PersonalExportService service, CancellationToken ct) =>
                Results.Text(await service.DataAsync(ct), "application/json"))
            .WithTags("Migration")
            .WithName("MyExportData")
            .WithSummary("The user's own data (ratings, StarTracks, play counts, plays, playlists) in the backups' format (docs/backups.md): `{ catalog, userData }`.")
            .Produces<string>(StatusCodes.Status200OK, "application/json");

        codes.MapGet("", (MigrationCodeService service, CancellationToken ct) => service.ListAsync(ct))
            .WithName("MigrationCodes")
            .WithSummary("The latest migration codes of this server, active or not. Administrators only.");

        codes.MapPost("", (MigrationCodeService service, CancellationToken ct) => service.CreateAsync(ct))
            .WithName("CreateMigrationCode")
            .WithSummary("A code another Arcanist uses, for a day, to bring this server over (users, libraries and music, users' data, settings, " +
                         "extensions). Shown once. Administrators only, with a recent authentication.");

        codes.MapPost("/{id:guid}/revoke", (Guid id, MigrationCodeService service, CancellationToken ct) => service.RevokeAsync(id, ct))
            .WithName("RevokeMigrationCode")
            .WithSummary("Stops a migration code from working. Administrators only.");

        var import = rest.MapGroup("/arcanist-import").WithTags("Import");

        import.MapGet("/servers", (ArcanistImportService service, CancellationToken ct) => service.ServersAsync(ct))
            .WithName("ArcanistServers")
            .WithSummary("Other Arcanist servers: those on the local networks (a few seconds' search) and those paired through the P2P network. " +
                         "Administrators only.");

        import.MapPost("/analyze", (ArcanistSourceInput input, ArcanistImportService service, CancellationToken ct) => service.AnalyzeAsync(input, ct))
            .WithName("AnalyzeArcanistServer")
            .WithSummary("What another Arcanist holds (libraries, users, extensions, players), read with its migration code. Administrators only.");

        import.MapGet("/imports", (ArcanistImportService service, CancellationToken ct) => service.ListAsync(ct))
            .WithName("ArcanistImports")
            .WithSummary("The latest migrations from other Arcanist servers and how far they got. Administrators only.");

        import.MapPost("/imports", (StartArcanistImportInput input, ArcanistImportService service, CancellationToken ct) => service.StartAsync(input, ct))
            .WithName("StartArcanistImport")
            .WithSummary("Brings another Arcanist into this server, in the background: the chosen users, libraries and music, and optionally the " +
                         "users' data, settings, extensions and players. Administrators only, with a recent authentication.");

        import.MapPost("/imports/{id:guid}/cancel", (Guid id, ArcanistImportService service, CancellationToken ct) => service.CancelAsync(id, ct))
            .WithName("CancelArcanistImport")
            .WithSummary("Stops a migration from another Arcanist; what it brought stays. Administrators only.");

        import.MapPost("/imports/{id:guid}/retry", (Guid id, ArcanistImportService service, CancellationToken ct) => service.RetryAsync(id, ct))
            .WithName("RetryArcanistImport")
            .WithSummary("Goes on with a migration that stopped, copying again the files that failed. Administrators only, with a recent authentication.");

        import.MapDelete("/imports/{id:guid}", (Guid id, ArcanistImportService service, CancellationToken ct) => service.DeleteAsync(id, ct))
            .WithName("DeleteArcanistImport")
            .WithSummary("Removes a finished migration from the history; what it brought stays. Administrators only.");

        return rest;
    }
}
