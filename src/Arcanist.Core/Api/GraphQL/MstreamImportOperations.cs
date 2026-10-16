using System.ComponentModel;
using Arcanist.Core.Application.Import;
using GraphQL;

namespace Arcanist.Core.Api.GraphQL;

public sealed partial class Query
{
    [Name("mstreamImports")]
    [Description("The latest imports from mStream, newest first. Administrators only.")]
    public static Task<IReadOnlyList<MstreamImportJob>> MstreamImports([FromServices] MstreamImportService imports, CancellationToken cancellationToken) =>
        imports.ListAsync(cancellationToken);
}

public sealed partial class Mutation
{
    [Name("analyzeMstreamFolder")]
    [Description("Reads an mStream installation on this server (its folder or configuration file) without changing it: its libraries, users and their data. Administrators only.")]
    public static Task<MstreamAnalysis> AnalyzeMstreamFolder(string folder, [FromServices] MstreamImportService imports, CancellationToken cancellationToken) =>
        imports.AnalyzeFolderAsync(folder, cancellationToken);

    [Name("analyzeMstreamServer")]
    [Description("Reads the libraries and users of an mStream server through its API, signing in with one of its administrators. Administrators only.")]
    public static Task<MstreamAnalysis> AnalyzeMstreamServer(MstreamServerInput input, [FromServices] MstreamImportService imports, CancellationToken cancellationToken) =>
        imports.AnalyzeServerAsync(input, cancellationToken);

    [Name("importMstream")]
    [Description("Imports from mStream: the chosen libraries, its users (who keep their passwords) and their playlists, ratings and play counts, which arrive once the new libraries are scanned (or, from a server, when each user first signs in). Needs a recent authentication.")]
    public static Task<MstreamImportJob> ImportMstream(ImportMstreamInput input, [FromServices] MstreamImportService imports, CancellationToken cancellationToken) =>
        imports.ImportAsync(input, cancellationToken);

    [Name("deleteMstreamImport")]
    [Description("Removes a finished import from mStream from the history; what it brought stays. An import from an mStream server stays while some of its users have not signed in. Administrators only.")]
    public static Task<bool> DeleteMstreamImport([Id] Guid id, [FromServices] MstreamImportService imports, CancellationToken cancellationToken) =>
        imports.DeleteAsync(id, cancellationToken);

    [Name("clearImportHistory")]
    [Description("Clears the history of migrations from mStream and DLNA servers (only finished ones, and not the mStream imports still needed to sign in); returns how many went. Administrators only.")]
    public static Task<int> ClearImportHistory([FromServices] MstreamImportService imports, CancellationToken cancellationToken) =>
        imports.ClearHistoryAsync(cancellationToken);
}