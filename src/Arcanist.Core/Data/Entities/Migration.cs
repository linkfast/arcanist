namespace Arcanist.Core.Data.Entities;

/// <summary>
/// A migration code of this server: another Arcanist uses it to read this one through the export API
/// (<c>/rest/v1/export</c>) and bring its users, libraries, music and data over. Only its hash is kept.
/// </summary>
public sealed class MigrationGrant : Entity
{
    public required string CodeHash { get; init; }

    public Guid CreatedByUserId { get; init; }

    public DateTime CreatedAt { get; init; }

    public DateTime ExpiresAt { get; init; }

    public DateTime? RevokedAt { get; set; }

    public DateTime? LastUsedAt { get; set; }

    /// <summary>The name of the server that used it last, as it said.</summary>
    public string? LastUsedBy { get; set; }
}

public enum ArcanistImportStatus
{
    /// <summary>Waiting for the worker (or for setup to finish).</summary>
    Queued,

    /// <summary>Applying settings and installing extensions and players.</summary>
    Preparing,

    /// <summary>Listing and copying the music.</summary>
    Copying,

    /// <summary>Waiting for the libraries' scans before the user data.</summary>
    Waiting,

    /// <summary>Bringing ratings, plays, playlists and wishes.</summary>
    Applying,

    Completed,
    Failed,
    Cancelled,
}

public enum ArcanistImportFileStatus
{
    Pending,
    Copied,

    /// <summary>The library had it already.</summary>
    Present,

    Failed,
}

/// <summary>
/// A migration from another Arcanist: its users and libraries (created or mapped when it started), its settings,
/// extensions and players, its music copied into this server's libraries, and its users' data. Resumable.
/// </summary>
public sealed class ArcanistImport : Entity
{
    public required string SourceName { get; set; }

    public string? SourceVersion { get; set; }

    /// <summary>The other server's address, when it was reached by URL.</summary>
    public string? SourceUrl { get; set; }

    /// <summary>Its P2P node ticket (JSON), when it was reached through the P2P network.</summary>
    public string? SourceTicket { get; set; }

    /// <summary>The migration code, protected with Data Protection.</summary>
    public required string ProtectedCode { get; set; }

    /// <summary>What to bring (JSON <c>ArcanistImportPlan</c>).</summary>
    public required string Plan { get; set; }

    /// <summary>JSON <c>{sourceLibraryId: libraryId}</c>.</summary>
    public string LibraryMap { get; set; } = "{}";

    /// <summary>JSON <c>{sourceUserName: userId}</c>.</summary>
    public string UserMap { get; set; } = "{}";

    public ArcanistImportStatus Status { get; set; }

    public Guid RequestedByUserId { get; init; }

    public DateTime CreatedAt { get; init; }

    public DateTime? FinishedAt { get; set; }

    public string? ErrorCode { get; set; }

    public int UsersCreated { get; set; }

    public int UsersMapped { get; set; }

    public int LibrariesCreated { get; set; }

    public bool SettingsApplied { get; set; }

    public int ExtensionsInstalled { get; set; }

    public int PlayersInstalled { get; set; }

    public int FilesTotal { get; set; }

    public int FilesCopied { get; set; }

    public int FilesPresent { get; set; }

    public int FilesFailed { get; set; }

    public long CopiedBytes { get; set; }

    public int Ratings { get; set; }

    public int PlayCounts { get; set; }

    public int Plays { get; set; }

    public int Playlists { get; set; }

    public int Wishes { get; set; }

    /// <summary>References to songs this server does not have.</summary>
    public int Unmatched { get; set; }

    public List<ArcanistImportFile> Files { get; init; } = [];
}

/// <summary>One file of the other server's libraries, to copy (one row each, so a migration resumes).</summary>
public sealed class ArcanistImportFile : Entity
{
    public Guid ImportId { get; init; }

    public Guid SourceLibraryId { get; init; }

    public Guid LibraryId { get; init; }

    public required string Path { get; init; }

    public long Size { get; init; }

    public ArcanistImportFileStatus Status { get; set; }

    public string? Reason { get; set; }
}
