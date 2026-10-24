using System.ComponentModel;
using Arcanist.Core.Application.ServerMigration;
using GraphQL;

namespace Arcanist.Core.Api.GraphQL;

public sealed partial class Query
{
    [Name("migrationCodes")]
    [Description("The latest migration codes of this server, active or not. Administrators only.")]
    public static Task<IReadOnlyList<MigrationCodeInfo>> MigrationCodes([FromServices] MigrationCodeService codes, CancellationToken cancellationToken) =>
        codes.ListAsync(cancellationToken);

    [Name("arcanistServers")]
    [Description("Other Arcanist servers: those on the local networks (a few seconds' search) and those paired through the P2P network. Administrators only.")]
    public static Task<IReadOnlyList<ArcanistServer>> ArcanistServers([FromServices] ArcanistImportService import, CancellationToken cancellationToken) =>
        import.ServersAsync(cancellationToken);

    [Name("analyzeArcanistServer")]
    [Description("What another Arcanist holds (libraries, users, extensions, players), read with its migration code. Administrators only.")]
    public static Task<ArcanistSourceAnalysis> AnalyzeArcanistServer(
        ArcanistSourceInput input, [FromServices] ArcanistImportService import, CancellationToken cancellationToken) =>
        import.AnalyzeAsync(input, cancellationToken);

    [Name("myExportFiles")]
    [Description("A user's own copy of the music they can play (a local backup, without being an administrator): the original files, page by page (`first`, default 500, at most 2000, after the `after` cursor), with library, path, size, audio hash and a resumable download URL. Optionally one library. Requires DOWNLOAD (forbidden otherwise). Nothing counts as a play.")]
    public static Task<ExportableFilePage> MyExportFiles([Id] Guid? libraryId, string? after, int? first, [FromServices] PersonalExportService export,
        CancellationToken cancellationToken) =>
        export.FilesAsync(libraryId, after, first, cancellationToken);

    [Name("myExportData")]
    [Description("The user's own data (ratings, StarTracks, play counts, plays and the playlists they own) with what recognises its tracks elsewhere, as a JSON document in the backups' format (docs/backups.md): `{ catalog, userData }`. Any signed-in user.")]
    public static Task<string> MyExportData([FromServices] PersonalExportService export, CancellationToken cancellationToken) =>
        export.DataAsync(cancellationToken);

    [Name("arcanistImports")]
    [Description("The latest migrations from other Arcanist servers and how far they got. Administrators only.")]
    public static Task<IReadOnlyList<ArcanistImportJob>> ArcanistImports([FromServices] ArcanistImportService import, CancellationToken cancellationToken) =>
        import.ListAsync(cancellationToken);
}

public sealed partial class Mutation
{
    [Name("createMigrationCode")]
    [Description("A code another Arcanist uses, for a day, to bring this server over (users, libraries and music, users' data, settings, extensions). Shown once. Administrators only, with a recent authentication.")]
    public static Task<MigrationCode> CreateMigrationCode([FromServices] MigrationCodeService codes, CancellationToken cancellationToken) =>
        codes.CreateAsync(cancellationToken);

    [Name("revokeMigrationCode")]
    [Description("Stops a migration code from working. Administrators only.")]
    public static Task<bool> RevokeMigrationCode([Id] Guid id, [FromServices] MigrationCodeService codes, CancellationToken cancellationToken) =>
        codes.RevokeAsync(id, cancellationToken);

    [Name("startArcanistImport")]
    [Description("Brings another Arcanist into this server, in the background: the chosen users, libraries and music, and optionally the users' data, settings, extensions and players. Administrators only, with a recent authentication.")]
    public static Task<ArcanistImportJob> StartArcanistImport(
        StartArcanistImportInput input, [FromServices] ArcanistImportService import, CancellationToken cancellationToken) =>
        import.StartAsync(input, cancellationToken);

    [Name("cancelArcanistImport")]
    [Description("Stops a migration from another Arcanist; what it brought stays. Administrators only.")]
    public static Task<ArcanistImportJob> CancelArcanistImport([Id] Guid id, [FromServices] ArcanistImportService import, CancellationToken cancellationToken) =>
        import.CancelAsync(id, cancellationToken);

    [Name("retryArcanistImport")]
    [Description("Goes on with a migration that stopped, copying again the files that failed. Administrators only, with a recent authentication.")]
    public static Task<ArcanistImportJob> RetryArcanistImport([Id] Guid id, [FromServices] ArcanistImportService import, CancellationToken cancellationToken) =>
        import.RetryAsync(id, cancellationToken);

    [Name("deleteArcanistImport")]
    [Description("Removes a finished migration from the history; what it brought stays. Administrators only.")]
    public static Task<bool> DeleteArcanistImport([Id] Guid id, [FromServices] ArcanistImportService import, CancellationToken cancellationToken) =>
        import.DeleteAsync(id, cancellationToken);
}
