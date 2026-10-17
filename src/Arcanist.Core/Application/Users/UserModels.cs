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
}

/// <param name="LibraryIds">Libraries granted explicitly. Administrators and libraries visible to all are not listed.</param>
public sealed record UserAccount(
    Guid Id,
    string UserName,
    string? DisplayName,
    UserRole Role,
    IReadOnlyList<Permission> Permissions,
    bool IsDisabled,
    DateTime CreatedAt,
    DateTime? LastLoginAt,
    IReadOnlyList<Guid> LibraryIds);

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
    IReadOnlyList<Guid>? LibraryIds);

/// <summary>Replaces the editable fields of a user.</summary>
public sealed record UpdateUserInput(
    string? DisplayName,
    UserRole Role,
    IReadOnlyList<Permission>? Permissions,
    bool IsDisabled);

public sealed record SetUserLibrariesInput(IReadOnlyList<Guid> LibraryIds);

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
        _ => throw new ArgumentOutOfRangeException(nameof(permission), permission, null),
    };

    public static UserAccount ToAccount(this User user, IReadOnlyList<Guid> libraryIds) => new(
        user.Id, user.UserName, user.DisplayName, user.Role, ToList(user.Permissions),
        user.IsDisabled, user.CreatedAt, user.LastLoginAt, libraryIds);
}
