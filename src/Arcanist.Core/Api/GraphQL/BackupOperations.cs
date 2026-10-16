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

    [Name("runBackup")]
    [Description("Runs a backup now, in the background. Administrators only.")]
    public static Task<BackupJob> RunBackup([Id] Guid id, [FromServices] BackupService backups, CancellationToken cancellationToken) =>
        backups.RunAsync(id, cancellationToken);

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
}
