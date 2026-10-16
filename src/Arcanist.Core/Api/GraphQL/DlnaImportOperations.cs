using System.ComponentModel;
using Arcanist.Core.Application.Import;
using GraphQL;

namespace Arcanist.Core.Api.GraphQL;

public sealed partial class Query
{
    [Name("dlnaMediaServers")]
    [Description("Searches the local networks for DLNA/UPnP media servers for a few seconds. Administrators only.")]
    public static Task<IReadOnlyList<DlnaMediaServer>> DlnaMediaServers([FromServices] DlnaImportService import, CancellationToken cancellationToken) =>
        import.SearchAsync(cancellationToken);

    [Name("dlnaMediaServer")]
    [Description("The media server at a description URL, for servers the search does not find. Administrators only.")]
    public static Task<DlnaMediaServer> DlnaMediaServer(string location, [FromServices] DlnaImportService import, CancellationToken cancellationToken) =>
        import.DescribeAsync(location, cancellationToken);

    [Name("browseDlnaMediaServer")]
    [Description("A folder of a media server (0 or none is the root): its subfolders and how much music it holds. Administrators only.")]
    public static Task<DlnaFolderContents> BrowseDlnaMediaServer(
        string location, string? folderId, [FromServices] DlnaImportService import, CancellationToken cancellationToken) =>
        import.BrowseAsync(location, folderId, cancellationToken);

    [Name("dlnaImports")]
    [Description("The latest copies from media servers and how far they got. Administrators only.")]
    public static Task<IReadOnlyList<DlnaImportJob>> DlnaImports([FromServices] DlnaImportService import, CancellationToken cancellationToken) =>
        import.ListAsync(cancellationToken);
}

public sealed partial class Mutation
{
    [Name("startDlnaImport")]
    [Description("Copies the music of folders of a media server into a library folder, in the background. Administrators only, with a recent authentication.")]
    public static Task<DlnaImportJob> StartDlnaImport(StartDlnaImportInput input, [FromServices] DlnaImportService import, CancellationToken cancellationToken) =>
        import.StartAsync(input, cancellationToken);

    [Name("cancelDlnaImport")]
    [Description("Stops a copy from a media server; what was copied stays. Administrators only.")]
    public static Task<DlnaImportJob> CancelDlnaImport([Id] Guid id, [FromServices] DlnaImportService import, CancellationToken cancellationToken) =>
        import.CancelAsync(id, cancellationToken);

    [Name("deleteDlnaImport")]
    [Description("Removes a finished copy from a media server from the history; what it copied stays. Administrators only.")]
    public static Task<bool> DeleteDlnaImport([Id] Guid id, [FromServices] DlnaImportService import, CancellationToken cancellationToken) =>
        import.DeleteAsync(id, cancellationToken);

    [Name("retryDlnaImport")]
    [Description("Walks the media server again and copies what is missing. Administrators only, with a recent authentication.")]
    public static Task<DlnaImportJob> RetryDlnaImport([Id] Guid id, [FromServices] DlnaImportService import, CancellationToken cancellationToken) =>
        import.RetryAsync(id, cancellationToken);
}
