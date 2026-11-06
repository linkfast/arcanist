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
    /// <summary>Shares outside the server: public links to playlists, Jukebox Jams open to guests without an account.</summary>
    ShareLinks = 1 << 11,
    /// <summary>Makes invitations to the server, with its public URL: to add it to Ultimate apps, and to ask for an account when the server allows it.</summary>
    Invite = 1 << 12,
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

    /// <summary>The authenticator app's secret (Data Protection); pending until <see cref="TotpEnabledAt"/> is set.</summary>
    public string? TotpSecret { get; set; }

    public DateTime? TotpEnabledAt { get; set; }

    /// <summary>The time step of the last code used: a code never works twice.</summary>
    public long? TotpLastStep { get; set; }

    /// <summary>The user has an authenticator app or a passkey. Kept by <c>SecondFactors</c> whenever they change.</summary>
    public bool HasSecondFactor { get; set; }

    /// <summary>Pairs a second factor at the next sign-in, whatever the server requires: an administrator reset theirs, or they had one on the server they came from.</summary>
    public bool MustSetUpSecondFactor { get; set; }

    /// <summary>Made with an invitation and waiting for an administrator: disabled, without permissions or libraries, until approved.</summary>
    public bool PendingApproval { get; set; }

    public List<UserSession> Sessions { get; } = [];

    public List<LibraryAccess> LibraryAccess { get; } = [];
}

/// <summary>A one-time code for signing in without the second factor at hand. Only its hash is kept.</summary>
public sealed class UserRecoveryCode : Entity
{
    public Guid UserId { get; init; }

    public User User { get; init; } = null!;

    /// <summary>SHA-256 of the code, hex.</summary>
    public required string CodeHash { get; init; }

    public DateTime? UsedAt { get; set; }
}

/// <summary>A passkey or security key (WebAuthn), valid only on the domain it was registered on.</summary>
public sealed class UserPasskey : Entity
{
    public Guid UserId { get; init; }

    public User User { get; init; } = null!;

    public required byte[] CredentialId { get; init; }

    /// <summary>COSE-encoded.</summary>
    public required byte[] PublicKey { get; init; }

    /// <summary>A long: PostgreSQL's provider would take a uint for a transaction id.</summary>
    public long SignCount { get; set; }

    public Guid AaGuid { get; init; }

    /// <summary>How the browser reaches it, comma separated, as the authenticator said.</summary>
    public string? Transports { get; init; }

    public required string RpId { get; init; }

    public required string Name { get; set; }

    public DateTime CreatedAt { get; init; }

    public DateTime? LastUsedAt { get; set; }
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

/// <summary>
/// An invitation to this server, of a single use: its link (the server's public URL and the code) adds the server to an
/// Ultimate app, and lets someone ask for an account when the server allows it. Only the code's hash is kept.
/// </summary>
public sealed class ServerInvitation : Entity
{
    public required string CodeHash { get; init; }

    public Guid CreatedByUserId { get; init; }

    public User CreatedBy { get; init; } = null!;

    /// <summary>Who it is for, as the one inviting wrote it: only for them to recognize it.</summary>
    public string? Note { get; init; }

    public DateTime CreatedAt { get; init; }

    public DateTime ExpiresAt { get; init; }

    public DateTime? RevokedAt { get; set; }

    /// <summary>When an account was asked for with it: it does not work again.</summary>
    public DateTime? UsedAt { get; set; }

    /// <summary>The account made with it, while it exists.</summary>
    public Guid? UsedByUserId { get; set; }
}

/// <summary>Server-wide setting stored as JSON, for values edited from the admin panel at runtime.</summary>
public sealed class Setting
{
    public required string Key { get; init; }

    public required string Value { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
