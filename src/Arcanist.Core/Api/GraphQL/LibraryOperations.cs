using System.ComponentModel;
using Arcanist.Core.Application.Libraries;
using GraphQL;

namespace Arcanist.Core.Api.GraphQL;

public sealed partial class Query
{
    [Name("libraries")]
    [Description("Libraries the signed-in user can access.")]
    public static Task<IReadOnlyList<Library>> Libraries([FromServices] LibraryService libraries, CancellationToken cancellationToken) =>
        libraries.ListAsync(cancellationToken);

    [Name("library")]
    [Description("A library by id. Libraries the user cannot access are reported as not found.")]
    public static Task<Library> Library([Id] Guid id, [FromServices] LibraryService libraries, CancellationToken cancellationToken) =>
        libraries.GetAsync(id, cancellationToken);

    [Name("serverDirectories")]
    [Description("Folders on the server, for choosing library locations. Lists the file system roots when path is omitted. Administrators only.")]
    public static ServerDirectoryListing ServerDirectories(string? path, [FromServices] LibraryService libraries) =>
        libraries.BrowseServerDirectories(path);
}

public sealed partial class Mutation
{
    [Name("createLibrary")]
    [Description("Requires MANAGE_LIBRARIES.")]
    public static Task<Library> CreateLibrary(CreateLibraryInput input, [FromServices] LibraryService libraries, CancellationToken cancellationToken) =>
        libraries.CreateAsync(input, cancellationToken);

    [Name("updateLibrary")]
    [Description("Requires MANAGE_LIBRARIES.")]
    public static Task<Library> UpdateLibrary([Id] Guid id, UpdateLibraryInput input, [FromServices] LibraryService libraries, CancellationToken cancellationToken) =>
        libraries.UpdateAsync(id, input, cancellationToken);

    [Name("deleteLibrary")]
    [Description("Removes the library and its tracks from the catalog; files on disk are not touched. Requires MANAGE_LIBRARIES and a recent authentication.")]
    public static async Task<bool> DeleteLibrary([Id] Guid id, [FromServices] LibraryService libraries, CancellationToken cancellationToken)
    {
        await libraries.DeleteAsync(id, cancellationToken);
        return true;
    }

    [Name("addLibrarySource")]
    [Description("Adds a location to a library. Requires MANAGE_LIBRARIES.")]
    public static Task<Library> AddLibrarySource([Id] Guid libraryId, LibrarySourceInput input, [FromServices] LibraryService libraries, CancellationToken cancellationToken) =>
        libraries.AddSourceAsync(libraryId, input, cancellationToken);

    [Name("createServerDirectory")]
    [Description("Creates a folder on the server, to pick it as a location. Administrators only.")]
    public static ServerDirectory CreateServerDirectory(CreateServerDirectoryInput input, [FromServices] LibraryService libraries) =>
        libraries.CreateServerDirectory(input);

    [Name("removeLibrarySource")]
    [Description("Removes a location and its tracks from the catalog; files on disk are not touched. Requires MANAGE_LIBRARIES.")]
    public static Task<Library> RemoveLibrarySource([Id] Guid id, [FromServices] LibraryService libraries, CancellationToken cancellationToken) =>
        libraries.RemoveSourceAsync(id, cancellationToken);
}
