namespace Arcanist.Core.Data.Entities;

public sealed class Library : Entity
{
    public required string Name { get; set; }

    /// <summary>When set, every user can see the library without an explicit <see cref="LibraryAccess"/> row.</summary>
    public bool VisibleToAll { get; set; }

    /// <summary>Automatic incremental scan interval; null disables scheduled scans.</summary>
    public int? ScanIntervalHours { get; set; }

    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    public DateTime? LastScanAt { get; set; }

    public List<LibrarySource> Sources { get; } = [];

    public List<LibraryAccess> Access { get; } = [];
}

public enum LibrarySourceKind
{
    /// <summary>A folder reachable through the file system, including mounted network shares.</summary>
    Local,
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
