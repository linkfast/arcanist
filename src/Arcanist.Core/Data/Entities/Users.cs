namespace Arcanist.Core.Data.Entities;

public enum UserRole
{
    User,

    /// <summary>Implies every permission and access to every library.</summary>
    Admin,
}

[Flags]
public enum Permissions : long
{
    None = 0,
    ManageLibraries = 1 << 0,
    ManageUsers = 1 << 1,
    ManageSettings = 1 << 2,
    RunScans = 1 << 3,
    Download = 1 << 4,
    ViewAllStats = 1 << 5,
    Upload = 1 << 6,
    PlayOnServer = 1 << 7,
    /// <summary>Sees everyone's wish lists (the server's), not only their own.</summary>
    ViewAllWishes = 1 << 8,
    /// <summary>Changes tracks' tags and covers: in the files themselves and in the catalog.</summary>
    EditMetadata = 1 << 9,
    /// <summary>Removes tracks from the libraries, their files going to the location's trash.</summary>
    DeleteTracks = 1 << 10,
}

public sealed class User : Entity
{
    public required string UserName { get; set; }

    /// <summary>Lookup key for <see cref="UserName"/>; see <c>TextNormalizer</c>.</summary>
    public required string NormalizedUserName { get; set; }

    public string? DisplayName { get; set; }

    public required string PasswordHash { get; set; }

    public UserRole Role { get; set; }

    public Permissions Permissions { get; set; }

    /// <summary>Changes whenever credentials or permissions change, invalidating issued tokens.</summary>
    public string SecurityStamp { get; set; } = Guid.NewGuid().ToString("N");

    public bool IsDisabled { get; set; }

    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    public DateTime? LastLoginAt { get; set; }

    public List<UserSession> Sessions { get; } = [];

    public List<LibraryAccess> LibraryAccess { get; } = [];
}

/// <summary>A signed-in device. Holds the hash of its current refresh token so it can be rotated and revoked.</summary>
public sealed class UserSession : Entity
{
    public Guid UserId { get; init; }

    public User User { get; init; } = null!;

    public required string RefreshTokenHash { get; set; }

    public string? DeviceName { get; set; }

    public string? UserAgent { get; set; }

    public string? IpAddress { get; set; }

    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    /// <summary>Last time the user entered their password on this session (login or step-up).</summary>
    public DateTime AuthenticatedAt { get; set; } = DateTime.UtcNow;

    public DateTime LastSeenAt { get; set; } = DateTime.UtcNow;

    public DateTime ExpiresAt { get; set; }

    public DateTime? RevokedAt { get; set; }
}

/// <summary>
/// A password a user creates for one app that cannot use Arcanist's sign-in (Subsonic and mStream clients). Kept
/// encrypted (Data Protection), since Subsonic's token authentication needs the password itself; its hash finds it
/// fast when sent as an OpenSubsonic API key. Never the user's main password.
/// </summary>
public sealed class AppPassword : Entity
{
    public Guid UserId { get; init; }

    public User User { get; init; } = null!;

    public required string Name { get; set; }

    public required string EncryptedSecret { get; init; }

    /// <summary>SHA-256 of the secret, hex.</summary>
    public required string SecretHash { get; init; }

    public DateTime CreatedAt { get; init; }

    public DateTime? LastUsedAt { get; set; }
}

/// <summary>Server-wide setting stored as JSON, for values edited from the admin panel at runtime.</summary>
public sealed class Setting
{
    public required string Key { get; init; }

    public required string Value { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
