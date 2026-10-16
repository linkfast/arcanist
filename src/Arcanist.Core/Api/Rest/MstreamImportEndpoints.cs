using Arcanist.Core.Application.Import;

namespace Arcanist.Core.Api.Rest;

public sealed record AnalyzeMstreamFolderInput(string Folder);

public static class MstreamImportEndpoints
{
    public static IEndpointRouteBuilder MapMstreamImportEndpoints(this IEndpointRouteBuilder rest)
    {
        var mstream = rest.MapGroup("/mstream").WithTags("Import");

        mstream.MapGet("/imports", (MstreamImportService service, CancellationToken ct) => service.ListAsync(ct))
            .WithName("MstreamImports")
            .WithSummary("The latest imports from mStream, newest first. Administrators only.");

        mstream.MapPost("/analyze-folder", (AnalyzeMstreamFolderInput input, MstreamImportService service, CancellationToken ct) =>
                service.AnalyzeFolderAsync(input.Folder, ct))
            .WithName("AnalyzeMstreamFolder")
            .WithSummary("Reads an mStream installation on this server (its folder or configuration file) without changing it: its libraries, users and their data. Administrators only.");

        mstream.MapPost("/analyze-server", (MstreamServerInput input, MstreamImportService service, CancellationToken ct) =>
                service.AnalyzeServerAsync(input, ct))
            .WithName("AnalyzeMstreamServer")
            .WithSummary("Reads the libraries and users of an mStream server through its API, signing in with one of its administrators. Administrators only.");

        mstream.MapDelete("/imports/{id:guid}", (Guid id, MstreamImportService service, CancellationToken ct) => service.DeleteAsync(id, ct))
            .WithName("DeleteMstreamImport")
            .WithSummary("Removes a finished import from mStream from the history; what it brought stays. An import from an mStream server stays " +
                         "while some of its users have not signed in. Administrators only.");

        mstream.MapPost("/import-history/clear", (MstreamImportService service, CancellationToken ct) => service.ClearHistoryAsync(ct))
            .WithName("ClearImportHistory")
            .WithSummary("Clears the history of migrations from mStream and DLNA servers (only finished ones, and not the mStream imports still " +
                         "needed to sign in); returns how many went. Administrators only.");

        mstream.MapPost("/imports", (ImportMstreamInput input, MstreamImportService service, CancellationToken ct) => service.ImportAsync(input, ct))
            .WithName("ImportMstream")
            .WithSummary("Imports from mStream: the chosen libraries, its users (who keep their passwords) and their playlists, ratings and play counts, " +
                         "which arrive once the new libraries are scanned (or, from a server, when each user first signs in). Needs a recent authentication.");

        return rest;
    }
}
