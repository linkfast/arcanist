using System.ComponentModel;
using Arcanist.Core.Application.Backups;
using GraphQL;

namespace Arcanist.Core.Api.GraphQL;

public sealed partial class Query
{
    [Name("backupDestinations")]
    [Description("Where backups go: folders of this server, S3 buckets and other Arcanist servers. Administrators only.")]
    public static Task<IReadOnlyList<BackupDestinationInfo>> BackupDestinations([FromServices] BackupService backups, CancellationToken cancellationToken) =>
        backups.DestinationsAsync(cancellationToken);

    [Name("backups")]
    [Description("The backups of the server, its libraries, albums or playlists, with their latest run. Administrators only.")]
    public static Task<IReadOnlyList<BackupDefinition>> Backups([FromServices] BackupService backups, CancellationToken cancellationToken) =>
        backups.ListAsync(cancellationToken);

    [Name("backup")]
    [Description("A backup, with its latest run. Administrators only.")]
    public static Task<BackupDefinition> Backup([Id] Guid id, [FromServices] BackupService backups, CancellationToken cancellationToken) =>
        backups.GetAsync(id, cancellationToken);

    [Name("backupRuns")]
    [Description("The 20 latest runs of a backup. Administrators only.")]
    public static Task<IReadOnlyList<BackupJob>> BackupRuns([Id] Guid backupId, [FromServices] BackupService backups, CancellationToken cancellationToken) =>
        backups.RunsAsync(backupId, cancellationToken);

    [Name("backupGuests")]
    [Description("Other Arcanist servers that keep their backups here, with the space they take. Administrators only.")]
    public static Task<IReadOnlyList<BackupGuest>> BackupGuests([FromServices] BackupService backups, CancellationToken cancellationToken) =>
        backups.GuestsAsync(cancellationToken);

    [Name("storedBackups")]
    [Description("The backups in a destination (this server's or another's), with the snapshots that can be restored. Administrators only.")]
    public static Task<IReadOnlyList<StoredBackup>> StoredBackups([Id] Guid destinationId, [FromServices] RestoreService restores, CancellationToken cancellationToken) =>
        restores.StoredBackupsAsync(destinationId, cancellationToken);

    [Name("backupSnapshotContents")]
    [Description("What a snapshot holds (libraries, albums, playlists and users), to choose what to restore. Administrators only.")]
    public static Task<BackupSnapshotContents> BackupSnapshotContents(
        [Id] Guid destinationId, string folder, string snapshot, [FromServices] RestoreService restores, CancellationToken cancellationToken) =>
        restores.SnapshotContentsAsync(destinationId, folder, snapshot, cancellationToken);

    [Name("restoreJobs")]
    [Description("The latest restores and how far they got. Administrators only.")]
    public static Task<IReadOnlyList<RestoreJobInfo>> RestoreJobs([FromServices] RestoreService restores, CancellationToken cancellationToken) =>
        restores.JobsAsync(cancellationToken);

    [Name("restoreJob")]
    [Description("A restore and how far it got. Administrators only.")]
    public static Task<RestoreJobInfo> RestoreJob([Id] Guid id, [FromServices] RestoreService restores, CancellationToken cancellationToken) =>
        restores.JobAsync(id, cancellationToken);

    [Name("backupRun")]
    [Description("A run of a backup and how far it got. Administrators only.")]
    public static Task<BackupJob> BackupRun([Id] Guid id, [FromServices] BackupService backups, CancellationToken cancellationToken) =>
        backups.RunInfoAsync(id, cancellationToken);
}

public sealed partial class Mutation
{
    [Name("createBackupDestination")]
    [Description("Adds a destination for backups (it must be writable). Administrators only, with a recent authentication.")]
    public static Task<BackupDestinationInfo> CreateBackupDestination(
        CreateBackupDestinationInput input, [FromServices] BackupService backups, CancellationToken cancellationToken) =>
        backups.CreateDestinationAsync(input, cancellationToken);

    [Name("testBackupDestination")]
    [Description("Whether a destination can be written now. Administrators only.")]
    public static Task<BackupDestinationTest> TestBackupDestination([Id] Guid id, [FromServices] BackupService backups, CancellationToken cancellationToken) =>
        backups.TestDestinationAsync(id, cancellationToken);

    [Name("deleteBackupDestination")]
    [Description("Removes a destination and its backups from this server; what is in the destination stays. Administrators only, with a recent authentication.")]
    public static Task<bool> DeleteBackupDestination([Id] Guid id, [FromServices] BackupService backups, CancellationToken cancellationToken) =>
        backups.DeleteDestinationAsync(id, cancellationToken);

    [Name("createBackup")]
    [Description("Creates a backup. Administrators only, with a recent authentication.")]
    public static Task<BackupDefinition> CreateBackup(CreateBackupInput input, [FromServices] BackupService backups, CancellationToken cancellationToken) =>
        backups.CreateAsync(input, cancellationToken);

    [Name("updateBackup")]
    [Description("Changes a backup (not its destination nor its kind of scope). Administrators only, with a recent authentication.")]
    public static Task<BackupDefinition> UpdateBackup([Id] Guid id, UpdateBackupInput input, [FromServices] BackupService backups, CancellationToken cancellationToken) =>
        backups.UpdateAsync(id, input, cancellationToken);

    [Name("deleteBackup")]
    [Description("Removes a backup from this server; its copies in the destination stay. Administrators only, with a recent authentication.")]
    public static Task<bool> DeleteBackup([Id] Guid id, [FromServices] BackupService backups, CancellationToken cancellationToken) =>
        backups.DeleteAsync(id, cancellationToken);

    [Name("createBackupInvitation")]
    [Description("An invitation for another Arcanist server to keep its backups in a folder of this one, valid 7 days. Needs the P2P network. " +
                 "Administrators only, with a recent authentication.")]
    public static Task<Application.P2P.P2PInvitationLink> CreateBackupInvitation(
        CreateBackupInvitationInput input, [FromServices] BackupService backups, CancellationToken cancellationToken) =>
        backups.CreateInvitationAsync(input, cancellationToken);

    [Name("runBackup")]
    [Description("Runs a backup now, in the background. Administrators only.")]
    public static Task<BackupJob> RunBackup([Id] Guid id, [FromServices] BackupService backups, CancellationToken cancellationToken) =>
        backups.RunAsync(id, cancellationToken);

    [Name("startRestore")]
    [Description("Restores files and user data from a snapshot, in the background, without overwriting anything. " +
                 "Administrators only, with a recent authentication.")]
    public static Task<RestoreJobInfo> StartRestore(StartRestoreInput input, [FromServices] RestoreService restores, CancellationToken cancellationToken) =>
        restores.StartAsync(input, cancellationToken);

    [Name("cancelRestore")]
    [Description("Stops a restore; what it restored stays. Administrators only.")]
    public static Task<RestoreJobInfo> CancelRestore([Id] Guid id, [FromServices] RestoreService restores, CancellationToken cancellationToken) =>
        restores.CancelAsync(id, cancellationToken);

    [Name("cancelBackupRun")]
    [Description("Stops a run; what it copied stays, and the next run finishes its snapshot. Administrators only.")]
    public static Task<BackupJob> CancelBackupRun([Id] Guid id, [FromServices] BackupService backups, CancellationToken cancellationToken) =>
        backups.CancelRunAsync(id, cancellationToken);
}

public sealed partial class Subscription
{
    [Name("backupJobUpdated")]
    [Description("Runs of backups as they change: their state at once, their progress about once a second. Administrators only.")]
    public static IObservable<BackupJob> BackupJobUpdated([FromServices] BackupService backups) => backups.Watch();

    [Name("restoreJobUpdated")]
    [Description("Restores as they change: their state at once, their progress about once a second. Administrators only.")]
    public static IObservable<RestoreJobInfo> RestoreJobUpdated([FromServices] RestoreService restores) => restores.Watch();
}
