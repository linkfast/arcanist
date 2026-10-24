using Arcanist.Core.Data.Entities;

namespace Arcanist.Core.Application.Users;

// Types in this file are part of the public API contracts (GraphQL schema and OpenAPI document).
// Renaming or removing members is a breaking change for third-party players.

/// <summary>A permission that can be granted to a user. Administrators implicitly have all of them.</summary>
public enum Permission
{
    ManageLibraries,
    ManageUsers,
    ManageSettings,
    RunScans,
    Download,
    ViewAllStats,

    /// <summary>Add music to the libraries the user can access (uploads, and migrating an app's library).</summary>
    Upload,

    /// <summary>Play on the server's own audio output (server audio), from "play to" and Ultimate Connect.</summary>
    PlayOnServer,

    /// <summary>Sees everyone's wish lists, not only their own.</summary>
    ViewAllWishes,

    /// <summary>
    /// Edits tracks' tags and covers (title, artists, album…) in the libraries the user can add to: written to the files
    /// themselves and to the catalog.
    /// </summary>
    EditMetadata,

    /// <summary>Removes tracks from the libraries the user can add to; their files go to the location's trash.</summary>
    DeleteTracks,
}

/// <param name="LibraryIds">Libraries granted explicitly. Administrators and libraries visible to all are not listed,
/// unless the user is only a listener there.</param>
/// <param name="ReadOnlyLibraryIds">Of those, the ones the user listens to but adds nothing to (no uploads).</param>
public sealed record UserAccount(
    Guid Id,
    string UserName,
    string? DisplayName,
    UserRole Role,
    IReadOnlyList<Permission> Permissions,
    bool IsDisabled,
    DateTime CreatedAt,
    DateTime? LastLoginAt,
    IReadOnlyList<Guid> LibraryIds,
    IReadOnlyList<Guid> ReadOnlyLibraryIds);

/// <summary>A signed-in device of the current user.</summary>
public sealed record Session(
    Guid Id,
    string? DeviceName,
    string? UserAgent,
    string? IpAddress,
    DateTime CreatedAt,
    DateTime LastSeenAt,
    DateTime ExpiresAt,
    bool IsCurrent);

public sealed record CreateUserInput(
    string UserName,
    string Password,
    string? DisplayName,
    UserRole Role,
    IReadOnlyList<Permission>? Permissions,
    IReadOnlyList<Guid>? LibraryIds,
    IReadOnlyList<Guid>? ReadOnlyLibraryIds = null);

/// <summary>Replaces the editable fields of a user.</summary>
public sealed record UpdateUserInput(
    string? DisplayName,
    UserRole Role,
    IReadOnlyList<Permission>? Permissions,
    bool IsDisabled);

/// <param name="LibraryIds">The libraries the user is given (beyond those visible to all).</param>
/// <param name="ReadOnlyLibraryIds">Libraries the user only listens to, adding nothing (no uploads): given ones, or
/// ones visible to all. Null keeps nobody read only.</param>
public sealed record SetUserLibrariesInput(IReadOnlyList<Guid> LibraryIds, IReadOnlyList<Guid>? ReadOnlyLibraryIds = null);

public sealed record ResetUserPasswordInput(string NewPassword);

public sealed record ChangeMyPasswordInput(string CurrentPassword, string NewPassword);

public static class PermissionMapping
{
    public static IReadOnlyList<Permission> ToList(Permissions flags) =>
        Enum.GetValues<Permission>().Where(p => flags.HasFlag(ToFlag(p))).ToList();

    public static Permissions ToFlags(IEnumerable<Permission>? permissions) =>
        permissions?.Aggregate(Permissions.None, (flags, p) => flags | ToFlag(p)) ?? Permissions.None;

    public static Permissions ToFlag(Permission permission) => permission switch
    {
        Permission.ManageLibraries => Permissions.ManageLibraries,
        Permission.ManageUsers => Permissions.ManageUsers,
        Permission.ManageSettings => Permissions.ManageSettings,
        Permission.RunScans => Permissions.RunScans,
        Permission.Download => Permissions.Download,
        Permission.ViewAllStats => Permissions.ViewAllStats,
        Permission.Upload => Permissions.Upload,
        Permission.PlayOnServer => Permissions.PlayOnServer,
        Permission.ViewAllWishes => Permissions.ViewAllWishes,
        Permission.EditMetadata => Permissions.EditMetadata,
        Permission.DeleteTracks => Permissions.DeleteTracks,
        _ => throw new ArgumentOutOfRangeException(nameof(permission), permission, null),
    };

    public static UserAccount ToAccount(this User user, IReadOnlyList<Guid> libraryIds, IReadOnlyList<Guid>? readOnlyLibraryIds = null) => new(
        user.Id, user.UserName, user.DisplayName, user.Role, ToList(user.Permissions),
        user.IsDisabled, user.CreatedAt, user.LastLoginAt, libraryIds, readOnlyLibraryIds ?? []);

    /// <summary>The account with its grants as stored: every row is a library, and some are read only.</summary>
    public static UserAccount ToAccount(this User user, IEnumerable<LibraryAccess> grants)
    {
        var list = grants.ToList();
        return user.ToAccount(list.Select(g => g.LibraryId).Order().ToList(), list.Where(g => g.ReadOnly).Select(g => g.LibraryId).Order().ToList());
    }
}
