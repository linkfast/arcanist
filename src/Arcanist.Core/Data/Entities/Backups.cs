namespace Arcanist.Core.Data.Entities;

public enum BackupDestinationKind
{
    /// <summary>A folder of this server: a disk, a USB drive, a mounted network share.</summary>
    Folder,

    /// <summary>A bucket of an S3-compatible service (the Cloud plan).</summary>
    S3,

    /// <summary>Another Arcanist server, through the P2P network, that agreed to keep this server's backups.</summary>
    Peer,

    /// <summary>A kind an extension provides (such as Ultimate Cloud, with Ultimate Cloud Plus): <c>Location</c> starts with its id.</summary>
    Extension,
}

/// <summary>Where backups go. Removing it never touches what is already there.</summary>
public sealed class BackupDestination : Entity
{
    public required string Name { get; set; }

    public BackupDestinationKind Kind { get; init; }

    /// <summary>The folder's full path, <c>s3://bucket/prefix</c>, the other server's node id, or an extension's <c>&lt;provider&gt;:&lt;location&gt;</c>.</summary>
    public required string Location { get; set; }

    /// <summary>As JSON: the S3 connection (with its secret protected), or the other server's ticket.</summary>
    public string? Settings { get; set; }

    public DateTime CreatedAt { get; init; }
}

public enum BackupScope
{
    /// <summary>Every library of the server (not those shared by other servers nor Ultimate Cloud's), its users' data and, optionally, the server itself.</summary>
    Server,

    Library,

    Album,

    Playlist,
}

public enum BackupSchedule
{
    /// <summary>Only when an administrator runs it.</summary>
    Manual,

    /// <summary>Every <see cref="Backup.IntervalHours"/> hours.</summary>
    Hours,

    /// <summary>Every day at <see cref="Backup.TimeOfDay"/>.</summary>
    Daily,

    /// <summary>Every week, on <see cref="Backup.DayOfWeek"/> at <see cref="Backup.TimeOfDay"/>.</summary>
    Weekly,
}

/// <summary>What to back up, where and when.</summary>
public sealed class Backup : Entity
{
    public required string Name { get; set; }

    public Guid DestinationId { get; init; }

    public BackupDestination Destination { get; init; } = null!;

    /// <summary>Its folder in the destination, chosen once (renaming the backup does not move it).</summary>
    public required string Folder { get; init; }

    public BackupScope Scope { get; init; }

    /// <summary>The libraries, albums or playlists backed up, as a JSON array of ids (empty for the server).</summary>
    public required string ScopeIds { get; set; }

    public bool IncludeUserData { get; set; }

    /// <summary>The database, settings and keys, encrypted with the backup's password (server and library backups).</summary>
    public bool IncludeServer { get; set; }

    /// <summary>The password of <see cref="IncludeServer"/>, protected with Data Protection so scheduled runs can use it.</summary>
    public string? ProtectedPassword { get; set; }

    public BackupSchedule Schedule { get; set; }

    public int? IntervalHours { get; set; }

    /// <summary>Minutes after midnight, in <see cref="TimeZone"/>.</summary>
    public int? TimeOfDay { get; set; }

    public DayOfWeek? DayOfWeek { get; set; }

    /// <summary>IANA time zone of the schedule.</summary>
    public required string TimeZone { get; set; }

    /// <summary>Days old versions are kept; 0 keeps them forever.</summary>
    public int RetentionDays { get; set; }

    public bool Enabled { get; set; }

    public DateTime CreatedAt { get; init; }

    public DateTime? LastRunAt { get; set; }

    public DateTime? NextRunAt { get; set; }

    public List<BackupRun> Runs { get; init; } = [];
}

public enum BackupRunStatus
{
    Queued,

    Running,

    Completed,

    /// <summary>Finished, but some files could not be copied (see the failures).</summary>
    CompletedWithErrors,

    Failed,

    Cancelled,
}

public enum BackupTrigger
{
    Manual,

    Scheduled,
}

/// <summary>One run of a backup: a snapshot in its destination.</summary>
public sealed class BackupRun : Entity
{
    public Guid BackupId { get; init; }

    public Backup Backup { get; init; } = null!;

    public BackupRunStatus Status { get; set; }

    public BackupTrigger Trigger { get; init; }

    /// <summary>The snapshot's name in the destination (the run's start, UTC: <c>20261016T104500Z</c>).</summary>
    public string? Snapshot { get; set; }

    public Guid? RequestedByUserId { get; init; }

    public DateTime RequestedAt { get; init; }

    public DateTime? StartedAt { get; set; }

    public DateTime? FinishedAt { get; set; }

    /// <summary>Files in the snapshot.</summary>
    public int Files { get; set; }

    public int Copied { get; set; }

    public int Unchanged { get; set; }

    /// <summary>Files that changed or disappeared: their previous copy became an old version.</summary>
    public int Versioned { get; set; }

    /// <summary>Files that went away while they were read: the next run takes them.</summary>
    public int Skipped { get; set; }

    public int Failed { get; set; }

    public long Bytes { get; set; }

    /// <summary>Why it failed, as an <c>ErrorCodes</c> value.</summary>
    public string? ErrorCode { get; set; }

    public List<BackupRunFailure> Failures { get; init; } = [];
}

/// <summary>A file a run could not back up (the first ones of each run).</summary>
public sealed class BackupRunFailure : Entity
{
    public Guid RunId { get; init; }

    public required string Path { get; init; }

    /// <summary>An <c>ErrorCodes</c> value.</summary>
    public required string Code { get; init; }
}

public enum RestoreStatus
{
    Queued,

    /// <summary>Copying files from the backup into the libraries.</summary>
    Copying,

    /// <summary>Waiting for the libraries' scans, so the user data finds the restored music.</summary>
    WaitingForScans,

    /// <summary>Restoring ratings, play counts, history and playlists.</summary>
    Applying,

    Completed,

    /// <summary>Finished, but some files could not be restored (see the failures).</summary>
    CompletedWithErrors,

    Failed,

    Cancelled,
}

/// <summary>
/// Bringing back files and user data from a snapshot of a backup (this server's or another's). Nothing is overwritten:
/// a different file where one goes is kept, and the restored one gets another name.
/// </summary>
public sealed class RestoreJob : Entity
{
    public Guid DestinationId { get; init; }

    /// <summary>The backup's folder in the destination.</summary>
    public required string BackupFolder { get; init; }

    public required string BackupName { get; init; }

    public required string Snapshot { get; init; }

    public bool Files { get; init; }

    public bool UserData { get; init; }

    /// <summary>What to restore: paths (or folders) relative to the backup's <c>current/</c>, as a JSON array; all when empty.</summary>
    public required string Paths { get; init; }

    /// <summary>Everything into this library folder; null brings each library's files back to the folder they came from.</summary>
    public Guid? TargetSourceId { get; init; }

    /// <summary>Whose data goes to whom: <c>{"backup user name": "user id or null"}</c>.</summary>
    public required string UserMap { get; init; }

    public RestoreStatus Status { get; set; }

    public Guid? RequestedByUserId { get; init; }

    public DateTime RequestedAt { get; init; }

    public DateTime? StartedAt { get; set; }

    public DateTime? FinishedAt { get; set; }

    public int FilesTotal { get; set; }

    public int Restored { get; set; }

    /// <summary>Already there, the same.</summary>
    public int Identical { get; set; }

    /// <summary>Restored under another name: a different file was where it went.</summary>
    public int Renamed { get; set; }

    public int FilesFailed { get; set; }

    public int Ratings { get; set; }

    public int PlayCounts { get; set; }

    public int Plays { get; set; }

    public int Playlists { get; set; }

    /// <summary>Data of songs this server does not have (or did not recognize).</summary>
    public int Unmatched { get; set; }

    /// <summary>The libraries the restored files went to, as a JSON array of ids (their scans are awaited).</summary>
    public string Libraries { get; set; } = "[]";

    /// <summary>The first files that failed, as JSON (<c>[{"path": "…", "code": "…"}]</c>).</summary>
    public string Failures { get; set; } = "[]";

    public string? ErrorCode { get; set; }
}