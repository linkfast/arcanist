using System.ComponentModel;
using Arcanist.Core.Application.Libraries;
using Arcanist.Core.Application.Organization;
using GraphQL;
using ImportStatus = Arcanist.Core.Data.Entities.ImportStatus;

namespace Arcanist.Core.Api.GraphQL;

public sealed partial class Query
{
    [Name("organizePreview")]
    [Description("What organizing the library would move, with the given template or the library's own. Requires MANAGE_LIBRARIES.")]
    public static Task<OrganizePreview> OrganizePreview([Id] Guid libraryId, string? template,
        [FromServices] OrganizationService organization, CancellationToken cancellationToken) =>
        organization.PreviewAsync(libraryId, template, cancellationToken);

    [Name("organizeRuns")]
    [Description("The library's latest organizing runs, newest first. Requires MANAGE_LIBRARIES.")]
    public static Task<IReadOnlyList<OrganizeRun>> OrganizeRuns([Id] Guid libraryId,
        [FromServices] OrganizationService organization, CancellationToken cancellationToken) =>
        organization.RunsAsync(libraryId, cancellationToken);

    [Name("dropFolders")]
    [Description("Folders whose files are imported into libraries. Administrators only.")]
    public static Task<IReadOnlyList<DropFolder>> DropFolders([FromServices] ImportService imports, CancellationToken cancellationToken) =>
        imports.DropFoldersAsync(cancellationToken);

    [Name("importItems")]
    [Description("Files found in drop folders, most recently changed first: in the given state, or else those still in a drop folder. Requires MANAGE_LIBRARIES.")]
    public static Task<IReadOnlyList<ImportItem>> ImportItems(ImportStatus? status, [Id] Guid? dropFolderId, int? first,
        [FromServices] ImportService imports, CancellationToken cancellationToken) =>
        imports.ItemsAsync(status, dropFolderId, first, cancellationToken);

    [Name("importSummary")]
    [Description("How many files of the drop folders are in each state. Requires MANAGE_LIBRARIES.")]
    public static Task<ImportSummary> ImportSummary([FromServices] ImportService imports, CancellationToken cancellationToken) =>
        imports.SummaryAsync(cancellationToken);
}

public sealed partial class Mutation
{
    [Name("updateLibraryOrganization")]
    [Description("Sets the library's path template and whether files added to it are organized automatically. Requires MANAGE_LIBRARIES.")]
    public static Task<Library> UpdateLibraryOrganization([Id] Guid libraryId, UpdateLibraryOrganizationInput input,
        [FromServices] OrganizationService organization, CancellationToken cancellationToken) =>
        organization.UpdateAsync(libraryId, input, cancellationToken);

    [Name("organizeLibrary")]
    [Description("Moves the library's files to the paths its template gives them, in the background. Requires MANAGE_LIBRARIES and a recent authentication.")]
    public static Task<OrganizeRun> OrganizeLibrary([Id] Guid libraryId, [FromServices] OrganizationService organization, CancellationToken cancellationToken) =>
        organization.StartAsync(libraryId, cancellationToken);

    [Name("undoOrganizeRun")]
    [Description("Moves the files of a finished organizing run back where they were, in the background. Requires MANAGE_LIBRARIES and a recent authentication.")]
    public static Task<OrganizeRun> UndoOrganizeRun([Id] Guid id, [FromServices] OrganizationService organization, CancellationToken cancellationToken) =>
        organization.UndoAsync(id, cancellationToken);

    [Name("createDropFolder")]
    [Description("Administrators only.")]
    public static Task<DropFolder> CreateDropFolder(DropFolderInput input, [FromServices] ImportService imports, CancellationToken cancellationToken) =>
        imports.CreateDropFolderAsync(input, cancellationToken);

    [Name("updateDropFolder")]
    [Description("Administrators only.")]
    public static Task<DropFolder> UpdateDropFolder([Id] Guid id, DropFolderInput input, [FromServices] ImportService imports, CancellationToken cancellationToken) =>
        imports.UpdateDropFolderAsync(id, input, cancellationToken);

    [Name("deleteDropFolder")]
    [Description("Stops importing from the folder and forgets its history; its files are not touched. Administrators only.")]
    public static async Task<bool> DeleteDropFolder([Id] Guid id, [FromServices] ImportService imports, CancellationToken cancellationToken)
    {
        await imports.DeleteDropFolderAsync(id, cancellationToken);
        return true;
    }

    [Name("acceptImportItem")]
    [Description("Imports a file even though it is incomplete or a duplicate (then as another copy of its track), first writing the given tags into it. Requires MANAGE_LIBRARIES.")]
    public static Task<ImportItem> AcceptImportItem([Id] Guid id, ImportTagsInput? tags, [FromServices] ImportService imports, CancellationToken cancellationToken) =>
        imports.AcceptAsync(id, tags, cancellationToken);

    [Name("retryImportItem")]
    [Description("Looks at a file again, as if it had just been dropped. Requires MANAGE_LIBRARIES.")]
    public static Task<ImportItem> RetryImportItem([Id] Guid id, [FromServices] ImportService imports, CancellationToken cancellationToken) =>
        imports.RetryAsync(id, cancellationToken);

    [Name("ignoreImportItem")]
    [Description("Leaves a file in its drop folder; it is looked at again only if it changes. Requires MANAGE_LIBRARIES.")]
    public static Task<ImportItem> IgnoreImportItem([Id] Guid id, [FromServices] ImportService imports, CancellationToken cancellationToken) =>
        imports.IgnoreAsync(id, cancellationToken);

    [Name("discardImportItem")]
    [Description("Moves a file to its drop folder's trash, from which it is deleted after a while. Requires MANAGE_LIBRARIES.")]
    public static Task<ImportItem> DiscardImportItem([Id] Guid id, [FromServices] ImportService imports, CancellationToken cancellationToken) =>
        imports.DiscardAsync(id, cancellationToken);
}
