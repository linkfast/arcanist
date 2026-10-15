using Arcanist.Core.Application.Organization;
using Arcanist.Core.Data.Entities;
using Arcanist.Core.Organization;
using LibraryEntity = Arcanist.Core.Data.Entities.Library;
using LibrarySourceEntity = Arcanist.Core.Data.Entities.LibrarySource;

namespace Arcanist.Core.Application.Libraries;

// Types in this file are part of the public API contracts (GraphQL schema and OpenAPI document).

/// <param name="Sources">Where the files live. Null unless the caller has the MANAGE_LIBRARIES permission, since
/// locations reveal the server's file system layout.</param>
/// <param name="Organization">How files are organized. Null unless the caller has the MANAGE_LIBRARIES permission.</param>
public sealed record Library(
    Guid Id,
    string Name,
    bool VisibleToAll,
    int? ScanIntervalMinutes,
    bool WatchForChanges,
    DateTime CreatedAt,
    DateTime? LastScanAt,
    IReadOnlyList<LibrarySource>? Sources,
    LibraryOrganization? Organization);

public sealed record LibrarySource(Guid Id, LibrarySourceKind Kind, string Location);

/// <param name="ScanIntervalMinutes">Minutes between automatic scans (5 to 43200, 30 days); null disables them.</param>
/// <param name="Sources">The library's folder: exactly one.</param>
/// <param name="WatchForChanges">Watch the folder and scan what changes as it happens.</param>
public sealed record CreateLibraryInput(
    string Name,
    bool VisibleToAll,
    int? ScanIntervalMinutes,
    IReadOnlyList<LibrarySourceInput> Sources,
    bool WatchForChanges = false);

/// <summary>Replaces the editable fields of a library. Sources are changed with their own operations.</summary>
/// <param name="ScanIntervalMinutes">Minutes between automatic scans (5 to 43200, 30 days); null disables them.</param>
/// <param name="WatchForChanges">Notice changes as they happen: local folders are watched, and Ultimate Cloud tells
/// about its own. Libraries shared by another server or in S3 cannot (<c>watch_unsupported</c>).</param>
public sealed record UpdateLibraryInput(string Name, bool VisibleToAll, int? ScanIntervalMinutes, bool WatchForChanges);

/// <param name="Location">For local sources, an absolute folder path on the server. Only a library without a
/// folder (its folder was removed) can get one.</param>
public sealed record LibrarySourceInput(string Location);

/// <summary>A folder on the server, offered when choosing library locations.</summary>
public sealed record ServerDirectory(string Name, string Path, bool IsHidden);

/// <param name="Parent">Absolute path of the folder to create it in.</param>
/// <param name="Name">The new folder's name; it must be valid on Windows too.</param>
public sealed record CreateServerDirectoryInput(string Parent, string Name);

/// <param name="Path">Null when listing the file system roots (drives on Windows).</param>
public sealed record ServerDirectoryListing(string? Path, string? Parent, IReadOnlyList<ServerDirectory> Directories);

internal static class LibraryMapping
{
    /// <param name="includeSources">Whether the caller manages libraries, and sees their locations and organization.</param>
    public static Library ToModel(this LibraryEntity library, bool includeSources) => new(
        library.Id,
        library.Name,
        library.VisibleToAll,
        library.ScanIntervalMinutes,
        library.WatchForChanges,
        library.CreatedAt,
        library.LastScanAt,
        includeSources ? library.Sources.OrderBy(s => s.Location).Select(ToModel).ToList() : null,
        includeSources ? library.ToOrganization() : null);

    public static LibraryOrganization ToOrganization(this LibraryEntity library) => new(
        string.IsNullOrEmpty(library.OrganizeTemplate) ? PathTemplate.Default : library.OrganizeTemplate,
        string.IsNullOrEmpty(library.OrganizeTemplate),
        library.AutoOrganize,
        library.AutoOrganize ? library.AutoOrganizeSince : null);

    public static LibrarySource ToModel(this LibrarySourceEntity source) => new(source.Id, source.Kind, source.Location);
}
