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
/// <param name="Disabled">Its music is in Ultimate Cloud or S3 and the Ultimate Cloud extension is off: its tracks are
/// hidden and it can only be deleted (<c>library_disabled</c>) until the extension is on again.</param>
/// <param name="CanUpload">The caller may upload files to it: they have the UPLOAD permission and it has a folder Arcanist
/// writes to (on this server or in S3). A folder that turns out read-only still answers <c>read_only</c>.</param>
/// <param name="Virtual">A view, not a folder: "All" (<c>VirtualLibraries.All</c>), every library the caller can access.
/// It has no sources, nothing is uploaded to it, and as a <c>libraryId</c> filter it means no filter.</param>
/// <param name="SharedBy">For a library another server shares with this one: that server's name.</param>
/// <param name="Personal">The caller's own Ultimate Cloud music (<c>myUltimateCloud</c>): only they see it, and it goes
/// when they unlink their account.</param>
public sealed record Library(
    Guid Id,
    string Name,
    bool VisibleToAll,
    int? ScanIntervalMinutes,
    bool WatchForChanges,
    DateTime CreatedAt,
    DateTime? LastScanAt,
    IReadOnlyList<LibrarySource>? Sources,
    LibraryOrganization? Organization,
    bool CanUpload = false,
    bool Disabled = false,
    bool Personal = false,
    bool Virtual = false,
    string? SharedBy = null,
    string? ManagedBy = null);

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
    public static Library ToModel(this LibraryEntity library, bool includeSources, Auth.UserAccess? access = null, CloudLibraries? cloud = null) => new(
        library.Id,
        library.Name,
        library.VisibleToAll,
        library.ScanIntervalMinutes,
        library.WatchForChanges,
        library.CreatedAt,
        library.LastScanAt,
        includeSources ? library.Sources.OrderBy(s => s.Location).Select(ToModel).ToList() : null,
        includeSources ? library.ToOrganization() : null,
        access is not null && access.Has(Permissions.Upload)
                            && library.Sources.Any(s => s.Kind is LibrarySourceKind.Local or LibrarySourceKind.S3)
                            && cloud?.IsDisabled(library.Sources.Select(s => s.Kind), library.ManagedBy is not null) != true,
        cloud?.IsDisabled(library.Sources.Select(s => s.Kind), library.ManagedBy is not null) == true,
        library.OwnerUserId is not null,
        ManagedBy: library.ManagedBy);

    public static LibraryOrganization ToOrganization(this LibraryEntity library) => new(
        string.IsNullOrEmpty(library.OrganizeTemplate) ? PathTemplate.Default : library.OrganizeTemplate,
        string.IsNullOrEmpty(library.OrganizeTemplate),
        library.AutoOrganize,
        library.AutoOrganize ? library.AutoOrganizeSince : null);

    public static LibrarySource ToModel(this LibrarySourceEntity source) => new(source.Id, source.Kind, source.Location);
}
