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
/// <param name="Provider">For an extension's kind (<see cref="BackupDestinationKind.Extension"/>): its id, from <c>backupProviders</c>.</param>
/// <param name="ProviderSettings">For an extension's kind: what it asks for, as JSON (may be empty).</param>
public sealed record CreateBackupDestinationInput(
    string Name,
    BackupDestinationKind Kind,
    string? Path,
    S3ConnectionInput? S3,
    string? Invitation,
    string? Provider = null,
    string? ProviderSettings = null);

/// <summary>A kind of destination an extension provides (Ultimate Cloud, with Ultimate Cloud Plus).</summary>
public sealed record BackupProvider(string Id, string Name);

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
/// <summary>A backup found in a destination (made by this server or another).</summary>
/// <param name="Folder">Its folder in the destination, which names it in the other operations.</param>
/// <param name="Machine">The computer of the server that made it.</param>
/// <param name="Snapshots">What can be restored, oldest first.</param>
public sealed record StoredBackup(
    string Folder,
    Guid Id,
    string Name,
    string Scope,
    string Machine,
    string ArcanistVersion,
    DateTime UpdatedAt,
    IReadOnlyList<StoredSnapshot> Snapshots,
    IReadOnlyList<StoredLibrary> Libraries);

/// <param name="Name">The run's start in UTC (<c>20261016T104500Z</c>), which names it.</param>
/// <param name="Server">It holds the server's encrypted database, settings and keys.</param>
public sealed record StoredSnapshot(string Name, DateTime CreatedAt, int Files, long Bytes, bool UserData, bool Server);

/// <summary>A library folder in a backup.</summary>
/// <param name="Folder">Its folder in the backup's <c>current/</c>.</param>
/// <param name="SourceId">The library folder of this server it came from, when this server still has it.</param>
public sealed record StoredLibrary(string Folder, string Name, Guid? SourceId);

/// <summary>What a snapshot holds, to choose what to restore.</summary>
public sealed record BackupSnapshotContents(
    IReadOnlyList<SnapshotLibrary> Libraries,
    IReadOnlyList<SnapshotAlbum> Albums,
    IReadOnlyList<SnapshotPlaylist> Playlists,
    IReadOnlyList<SnapshotUser> Users);

public sealed record SnapshotLibrary(string Folder, string Name, int Files, long Bytes);

/// <param name="Paths">What to restore to bring it back: its folders in the backup.</param>
public sealed record SnapshotAlbum(Guid Id, string Title, string Artist, int? Year, IReadOnlyList<string> Paths);

/// <param name="Paths">Its songs' files in the backup.</param>
public sealed record SnapshotPlaylist(string Owner, string Name, int Tracks, IReadOnlyList<string> Paths);

/// <param name="UserId">The user of this server with the same name, when there is one.</param>
public sealed record SnapshotUser(string UserName, string? DisplayName, int Ratings, int Plays, int Playlists, Guid? UserId);

/// <summary>Whose data from the backup goes to which user of this server; none skips it.</summary>
public sealed record RestoreUserInput(string UserName, Guid? UserId);

/// <summary>What to restore from a snapshot.</summary>
/// <param name="Folder">The backup's folder in the destination.</param>
/// <param name="Paths">Paths (or folders) of the backup's <c>current/</c>; everything when empty.</param>
/// <param name="TargetSourceId">Everything into this library folder; none brings each library's files back where they came from.</param>
/// <param name="Users">Whose data goes to whom; users not listed go to the user of the same name, when there is one.</param>
public sealed record StartRestoreInput(
    Guid DestinationId,
    string Folder,
    string Snapshot,
    bool Files,
    bool UserData,
    IReadOnlyList<string>? Paths,
    Guid? TargetSourceId,
    IReadOnlyList<RestoreUserInput>? Users);

/// <summary>A restore, and how far it got.</summary>
/// <param name="Identical">Files already there, the same.</param>
/// <param name="Renamed">Files restored under another name, because a different one was where they went.</param>
/// <param name="Unmatched">User data of songs this server does not have.</param>
public sealed record RestoreJobInfo(
    Guid Id,
    string BackupName,
    string Snapshot,
    RestoreStatus Status,
    bool Files,
    bool UserData,
    int FilesTotal,
    int Restored,
    int Identical,
    int Renamed,
    int FilesFailed,
    int Ratings,
    int PlayCounts,
    int Plays,
    int Playlists,
    int Unmatched,
    DateTime RequestedAt,
    DateTime? FinishedAt,
    string? ErrorCode,
    IReadOnlyList<BackupJobFailure> Failures);