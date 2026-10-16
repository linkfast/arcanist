using Arcanist.Core.Application.Backups;

namespace Arcanist.Core.Api.Rest;

public static class BackupEndpoints
{
    public static IEndpointRouteBuilder MapBackupEndpoints(this IEndpointRouteBuilder rest)
    {
        var backups = rest.MapGroup("/backups").WithTags("Backups");

        backups.MapGet("/destinations", (BackupService service, CancellationToken ct) => service.DestinationsAsync(ct))
            .WithName("BackupDestinations")
            .WithSummary("Where backups go: folders of this server, S3 buckets and other Arcanist servers. Administrators only.");

        backups.MapPost("/destinations", (CreateBackupDestinationInput input, BackupService service, CancellationToken ct) => service.CreateDestinationAsync(input, ct))
            .WithName("CreateBackupDestination")
            .WithSummary("Adds a destination for backups (it must be writable). Administrators only, with a recent authentication.");

        backups.MapPost("/destinations/{id:guid}/test", (Guid id, BackupService service, CancellationToken ct) => service.TestDestinationAsync(id, ct))
            .WithName("TestBackupDestination")
            .WithSummary("Whether a destination can be written now. Administrators only.");

        backups.MapDelete("/destinations/{id:guid}", (Guid id, BackupService service, CancellationToken ct) => service.DeleteDestinationAsync(id, ct))
            .WithName("DeleteBackupDestination")
            .WithSummary("Removes a destination and its backups from this server; what is in the destination stays. " +
                         "Administrators only, with a recent authentication.");

        backups.MapGet("", (BackupService service, CancellationToken ct) => service.ListAsync(ct))
            .WithName("Backups")
            .WithSummary("The backups of the server, its libraries, albums or playlists, with their latest run. Administrators only.");

        backups.MapGet("/{id:guid}", (Guid id, BackupService service, CancellationToken ct) => service.GetAsync(id, ct))
            .WithName("Backup")
            .WithSummary("A backup, with its latest run. Administrators only.");

        backups.MapPost("", (CreateBackupInput input, BackupService service, CancellationToken ct) => service.CreateAsync(input, ct))
            .WithName("CreateBackup")
            .WithSummary("Creates a backup. Administrators only, with a recent authentication.");

        backups.MapPut("/{id:guid}", (Guid id, UpdateBackupInput input, BackupService service, CancellationToken ct) => service.UpdateAsync(id, input, ct))
            .WithName("UpdateBackup")
            .WithSummary("Changes a backup (not its destination nor its kind of scope). Administrators only, with a recent authentication.");

        backups.MapDelete("/{id:guid}", (Guid id, BackupService service, CancellationToken ct) => service.DeleteAsync(id, ct))
            .WithName("DeleteBackup")
            .WithSummary("Removes a backup from this server; its copies in the destination stay. Administrators only, with a recent authentication.");

        backups.MapPost("/{id:guid}/run", (Guid id, BackupService service, CancellationToken ct) => service.RunAsync(id, ct))
            .WithName("RunBackup")
            .WithSummary("Runs a backup now, in the background. Administrators only.");

        backups.MapGet("/{id:guid}/runs", (Guid id, BackupService service, CancellationToken ct) => service.RunsAsync(id, ct))
            .WithName("BackupRuns")
            .WithSummary("The 20 latest runs of a backup. Administrators only.");

        backups.MapGet("/runs/{id:guid}", (Guid id, BackupService service, CancellationToken ct) => service.RunInfoAsync(id, ct))
            .WithName("BackupRun")
            .WithSummary("A run of a backup and how far it got. Administrators only.");

        backups.MapPost("/runs/{id:guid}/cancel", (Guid id, BackupService service, CancellationToken ct) => service.CancelRunAsync(id, ct))
            .WithName("CancelBackupRun")
            .WithSummary("Stops a run; what it copied stays, and the next run finishes its snapshot. Administrators only.");

        return rest;
    }
}
