using Arcanist.Core.Application.Libraries;
using Arcanist.Core.Data.Entities;

namespace Arcanist.Core.Application.Backups;

// Types in this file are part of the public API contracts (GraphQL schema and OpenAPI document).

/// <summary>Where backups go.</summary>
/// <param name="Location">The folder's path, <c>s3://bucket/prefix</c>, or the other server's name.</param>
/// <param name="Backups">How many backups use it.</param>
public sealed record BackupDestinationInfo(
    Guid Id,
    string Name,
    BackupDestinationKind Kind,
    string Location,
    int Backups,
    DateTime CreatedAt);

/// <summary>A new destination: a folder of this server, an S3 bucket (Cloud plan) or another Arcanist server.</summary>
/// <param name="Path">For a folder: its full path on this server.</param>
/// <param name="S3">For S3: the connection, as for libraries in S3.</param>
/// <param name="Invitation">For another server: the link it gave (<c>arcanist://p2p?kind=backup…</c>).</param>
public sealed record CreateBackupDestinationInput(
    string Name,
    BackupDestinationKind Kind,
    string? Path,
    S3ConnectionInput? S3,
    string? Invitation);

/// <summary>Whether a destination can be written now.</summary>
/// <param name="ErrorCode">Why not, when it cannot.</param>
public sealed record BackupDestinationTest(bool Success, string? ErrorCode);

/// <summary>A library, album or playlist a backup takes.</summary>
public sealed record BackupScopeItem(Guid Id, string Name);

/// <summary>What is backed up, where and when.</summary>
/// <param name="Scope">The whole server, or some libraries, albums or playlists (<see cref="Items"/>).</param>
/// <param name="IncludeServer">The server's database, settings and keys, encrypted with the backup's password.</param>
/// <param name="TimeOfDay">For daily and weekly schedules: minutes after midnight in <see cref="TimeZone"/>.</param>
/// <param name="RetentionDays">Days old versions are kept (0: forever).</param>
/// <param name="Folder">Its folder in the destination.</param>
/// <param name="LastRun">The latest run, also when it is still going.</param>
public sealed record BackupDefinition(
    Guid Id,
    string Name,
    BackupDestinationInfo Destination,
    BackupScope Scope,
    IReadOnlyList<BackupScopeItem> Items,
    bool IncludeUserData,
    bool IncludeServer,
    BackupSchedule Schedule,
    int? IntervalHours,
    int? TimeOfDay,
    DayOfWeek? DayOfWeek,
    string TimeZone,
    int RetentionDays,
    bool Enabled,
    string Folder,
    DateTime CreatedAt,
    DateTime? NextRunAt,
    BackupJob? LastRun);

/// <summary>A new backup.</summary>
/// <param name="ScopeIds">The libraries, albums or playlists of the scope (none for the server).</param>
/// <param name="Password">Needed with <see cref="IncludeServer"/>, at least 8 characters: without it the server's
/// part cannot be restored, and Arcanist cannot recover it.</param>
/// <param name="TimeZone">IANA time zone of the schedule; the server's when missing.</param>
public sealed record CreateBackupInput(
    string Name,
    Guid DestinationId,
    BackupScope Scope,
    IReadOnlyList<Guid>? ScopeIds,
    bool IncludeUserData,
    bool IncludeServer,
    string? Password,
    BackupSchedule Schedule,
    int? IntervalHours,
    int? TimeOfDay,
    DayOfWeek? DayOfWeek,
    string? TimeZone,
    int RetentionDays,
    bool Enabled);

/// <summary>Changes to a backup (its destination and kind of scope stay).</summary>
/// <param name="Password">A new password for the server's part (later runs use it); none keeps the current one.</param>
public sealed record UpdateBackupInput(
    string Name,
    IReadOnlyList<Guid>? ScopeIds,
    bool IncludeUserData,
    bool IncludeServer,
    string? Password,
    BackupSchedule Schedule,
    int? IntervalHours,
    int? TimeOfDay,
    DayOfWeek? DayOfWeek,
    string? TimeZone,
    int RetentionDays,
    bool Enabled);

/// <summary>A file a run could not back up.</summary>
/// <param name="Code"><c>source_unavailable</c> (the whole library folder), <c>write_failed</c>, <c>backup_file_too_large</c>…</param>
public sealed record BackupJobFailure(string Path, string Code);

/// <summary>A run of a backup, and how far it got.</summary>
/// <param name="Snapshot">Its snapshot in the destination.</param>
/// <param name="Files">Files in the snapshot.</param>
/// <param name="Versioned">Changed or removed files whose previous copy became an old version.</param>
/// <param name="Skipped">Files that went away while the run read the library: the next run takes them.</param>
/// <param name="Failures">The first 50 files that could not be backed up.</param>
public sealed record BackupJob(
    Guid Id,
    Guid BackupId,
    string BackupName,
    BackupRunStatus Status,
    BackupTrigger Trigger,
    string? Snapshot,
    DateTime RequestedAt,
    DateTime? StartedAt,
    DateTime? FinishedAt,
    int Files,
    int Copied,
    int Unchanged,
    int Versioned,
    int Skipped,
    int Failed,
    long Bytes,
    string? ErrorCode,
    IReadOnlyList<BackupJobFailure> Failures);

/// <summary>An invitation for another Arcanist server to keep its backups in a folder of this one.</summary>
/// <param name="Name">What to call the other server.</param>
/// <param name="Folder">The folder of this server its backups go to.</param>
/// <param name="QuotaGb">How much they may take, in GB; no limit when null.</param>
public sealed record CreateBackupInvitationInput(string Name, string Folder, double? QuotaGb);

/// <summary>Another server that keeps its backups here.</summary>
/// <param name="PeerId">Its peer in the P2P network (revoking it stops its backups).</param>
/// <param name="UsedBytes">What its backups take; unknown when the folder cannot be read now.</param>
public sealed record BackupGuest(Guid PeerId, string Name, string Folder, long? UsedBytes, long? QuotaBytes, bool Revoked, DateTime? LastSeenAt);