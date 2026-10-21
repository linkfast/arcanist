namespace Arcanist.Core.Data.Entities;

public sealed class Library : Entity
{
    public required string Name { get; set; }

    /// <summary>
    /// A small number that never changes or repeats, for clients that need integer ids (Subsonic music folders).
    /// </summary>
    public int Number { get; set; }

    /// <summary>When set, every user can see the library without an explicit <see cref="LibraryAccess"/> row.</summary>
    public bool VisibleToAll { get; set; }

    /// <summary>Automatic incremental scan interval, in minutes; null disables scheduled scans.</summary>
    public int? ScanIntervalMinutes { get; set; }

    /// <summary>
    /// Changes are noticed as they happen: local folders are watched (<c>LibraryWatcher</c>), and Ultimate Cloud
    /// tells about its own. Works alongside <see cref="ScanIntervalMinutes"/>, which catches what watching misses.
    /// </summary>
    public bool WatchForChanges { get; set; }

    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    public DateTime? LastScanAt { get; set; }

    /// <summary>Where files go when organized (<c>PathTemplate</c> syntax); null for the default template.</summary>
    public string? OrganizeTemplate { get; set; }

    /// <summary>Files added to the library are moved to their template path after each scan.</summary>
    public bool AutoOrganize { get; set; }

    /// <summary>
    /// When <see cref="AutoOrganize"/> was turned on: only files added since then are moved automatically. The
    /// rest are organized on request, after reviewing a preview.
    /// </summary>
    public DateTime? AutoOrganizeSince { get; set; }

    /// <summary>
    /// A personal library: the music of this user's own Ultimate Cloud account, which only they see (administrators
    /// included). Null for the server's libraries. It goes with the user, and when they unlink the account.
    /// </summary>
    public Guid? OwnerUserId { get; set; }

    public User? Owner { get; set; }

    /// <summary>
    /// The managed extension that made and keeps this library (its id), such as a hosted service's storage: an
    /// administrator uses it but cannot delete it or change its sources. Null for the others.
    /// </summary>
    public string? ManagedBy { get; set; }

    /// <summary>The extension's own name for the library, unique among its libraries.</summary>
    public string? ManagedKey { get; set; }

    public List<LibrarySource> Sources { get; } = [];

    public List<LibraryAccess> Access { get; } = [];
}

public enum LibrarySourceKind
{
    /// <summary>A folder reachable through the file system, including mounted network shares.</summary>
    Local,

    /// <summary>
    /// Libraries another Arcanist server shares through the P2P network. The location is the <c>P2PRemote</c> id;
    /// files are described by that server and streamed from it on demand.
    /// </summary>
    Peer,

    /// <summary>
    /// The music of the linked Ultimate Cloud account. The location is the collection (<c>Library</c> or
    /// <c>CloudDrop</c>); tracks are described by Ultimate Cloud and their audio fetched when played.
    /// </summary>
    UltimateCloud,

    /// <summary>
    /// A bucket of an S3-compatible service, under a prefix: the location is <c>s3://bucket/prefix</c>, and
    /// <see cref="LibrarySource.Settings"/> holds the endpoint and the credentials (the secret protected).
    /// </summary>
    S3,
}

/// <summary>A location whose audio files belong to a library. Always read through <c>ILibraryStorage</c>.</summary>
public sealed class LibrarySource : Entity
{
    public Guid LibraryId { get; init; }

    public Library Library { get; init; } = null!;

    public LibrarySourceKind Kind { get; init; }

    /// <summary>Where the files are: an absolute path for <see cref="LibrarySourceKind.Local"/>.</summary>
    public required string Location { get; set; }

    /// <summary>Kind-specific options as JSON (credentials references, prefixes, …).</summary>
    public string? Settings { get; set; }
}

public sealed class LibraryAccess
{
    public Guid UserId { get; init; }

    public User User { get; init; } = null!;

    public Guid LibraryId { get; init; }

    public Library Library { get; init; } = null!;

    /// <summary>
    /// The user listens to the library but adds nothing to it (no uploads, also from extensions), even with the
    /// UPLOAD permission. On a library visible to all, a row with it only takes that away.
    /// </summary>
    public bool ReadOnly { get; set; }
}

public enum ScanKind
{
    /// <summary>Only files whose size or modification time changed are read again.</summary>
    Incremental,

    /// <summary>Every file is read again.</summary>
    Full,
}

public enum ScanStatus
{
    Queued,
    Running,
    Completed,
    Cancelled,
    Failed,
}

public sealed class ScanJob : Entity
{
    public Guid LibraryId { get; init; }

    public Library Library { get; init; } = null!;

    public ScanKind Kind { get; init; }

    public ScanStatus Status { get; set; }

    public Guid? RequestedByUserId { get; init; }

    public DateTime RequestedAt { get; init; } = DateTime.UtcNow;

    public DateTime? StartedAt { get; set; }

    public DateTime? FinishedAt { get; set; }

    public int FilesSeen { get; set; }

    public int FilesAdded { get; set; }

    public int FilesUpdated { get; set; }

    public int FilesMissing { get; set; }

    /// <summary>Files that could not be read.</summary>
    public int Errors { get; set; }

    /// <summary>Why the scan failed or was incomplete, as an <c>ErrorCodes</c> value.</summary>
    public string? ErrorCode { get; set; }
}
