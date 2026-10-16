using Arcanist.Core.Application;
using Arcanist.Core.Application.Backups;
using Arcanist.Core.Backups;
using Arcanist.Core.Data.Entities;
using Arcanist.Core.P2P;
using Arcanist.Core.Storage;
using Microsoft.AspNetCore.Http.Features;

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

        backups.MapPost("/invitations", (CreateBackupInvitationInput input, BackupService service, CancellationToken ct) => service.CreateInvitationAsync(input, ct))
            .WithName("CreateBackupInvitation")
            .WithSummary("An invitation for another Arcanist server to keep its backups in a folder of this one, valid 7 days. Needs the P2P network. " +
                         "Administrators only, with a recent authentication.");

        backups.MapGet("/guests", (BackupService service, CancellationToken ct) => service.GuestsAsync(ct))
            .WithName("BackupGuests")
            .WithSummary("Other Arcanist servers that keep their backups here, with the space they take. Administrators only.");

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

        backups.MapGet("/destinations/{id:guid}/stored", (Guid id, RestoreService service, CancellationToken ct) => service.StoredBackupsAsync(id, ct))
            .WithName("StoredBackups")
            .WithSummary("The backups in a destination (this server's or another's), with the snapshots that can be restored. Administrators only.");

        backups.MapGet("/destinations/{id:guid}/stored/{folder}/{snapshot}", (Guid id, string folder, string snapshot, RestoreService service, CancellationToken ct) =>
                service.SnapshotContentsAsync(id, folder, snapshot, ct))
            .WithName("BackupSnapshotContents")
            .WithSummary("What a snapshot holds (libraries, albums, playlists and users), to choose what to restore. Administrators only.");

        backups.MapGet("/restores", (RestoreService service, CancellationToken ct) => service.JobsAsync(ct))
            .WithName("RestoreJobs")
            .WithSummary("The latest restores and how far they got. Administrators only.");

        backups.MapGet("/restores/{id:guid}", (Guid id, RestoreService service, CancellationToken ct) => service.JobAsync(id, ct))
            .WithName("RestoreJob")
            .WithSummary("A restore and how far it got. Administrators only.");

        backups.MapPost("/restores", (StartRestoreInput input, RestoreService service, CancellationToken ct) => service.StartAsync(input, ct))
            .WithName("StartRestore")
            .WithSummary("Restores files and user data from a snapshot, in the background, without overwriting anything. " +
                         "Administrators only, with a recent authentication.");

        backups.MapPost("/restores/{id:guid}/cancel", (Guid id, RestoreService service, CancellationToken ct) => service.CancelAsync(id, ct))
            .WithName("CancelRestore")
            .WithSummary("Stops a restore; what it restored stays. Administrators only.");

        backups.MapGet("/runs/{id:guid}", (Guid id, BackupService service, CancellationToken ct) => service.RunInfoAsync(id, ct))
            .WithName("BackupRun")
            .WithSummary("A run of a backup and how far it got. Administrators only.");

        backups.MapPost("/runs/{id:guid}/cancel", (Guid id, BackupService service, CancellationToken ct) => service.CancelRunAsync(id, ct))
            .WithName("CancelBackupRun")
            .WithSummary("Stops a run; what it copied stays, and the next run finishes its snapshot. Administrators only.");

        return rest;
    }

    /// <summary>
    /// Server to server: another Arcanist server writing its backups into the folder this server keeps for it. Only
    /// server peers reach it (see P2PGateMiddleware), and only those that accepted an invitation to keep backups.
    /// </summary>
    internal static void MapPeerBackups(RouteGroupBuilder peer)
    {
        // A path that would leave the folder is refused, wherever the folder notices it.
        var backups = peer.MapGroup("/backups").ExcludeFromDescription().AddEndpointFilter(async (context, next) =>
        {
            try
            {
                return await next(context);
            }
            catch (ArgumentException)
            {
                return Problems.Create(StatusCodes.Status400BadRequest, ErrorCodes.InvalidFormat);
            }
            catch (StorageConflictException)
            {
                return Problems.Create(StatusCodes.Status409Conflict, ErrorCodes.StorageConflict);
            }
            catch (FileNotFoundException)
            {
                return Problems.Create(StatusCodes.Status404NotFound, ErrorCodes.NotFound);
            }
        });

        backups.MapGet("/space", async (HttpContext context, PeerBackupStore store, CancellationToken ct) =>
        {
            var (target, peerId, quota) = await store.OpenAsync(Caller(context), ct);
            return Results.Ok(new PeerBackupSpace(await store.UsedAsync(peerId, target, ct), quota));
        });

        backups.MapGet("/stat", async (HttpContext context, string path, PeerBackupStore store, CancellationToken ct) =>
        {
            var (target, _, _) = await store.OpenAsync(Caller(context), ct);
            return await target.StatAsync(path, ct) is { } file ? Results.Ok(file) : Problems.Create(StatusCodes.Status404NotFound, ErrorCodes.NotFound);
        });

        backups.MapGet("/files", async (HttpContext context, string? folder, PeerBackupStore store, CancellationToken ct) =>
        {
            var (target, _, _) = await store.OpenAsync(Caller(context), ct);
            var files = new List<Backups.Targets.BackupObject>();
            await foreach (var file in target.ListAsync(folder ?? "", ct))
                files.Add(file);
            return Results.Ok(files);
        });

        backups.MapGet("/folders", async (HttpContext context, string? folder, PeerBackupStore store, CancellationToken ct) =>
        {
            var (target, _, _) = await store.OpenAsync(Caller(context), ct);
            return Results.Ok(await target.FoldersAsync(folder ?? "", ct));
        });

        backups.MapGet("/file", async (HttpContext context, string path, PeerBackupStore store, CancellationToken ct) =>
        {
            var (target, _, _) = await store.OpenAsync(Caller(context), ct);
            return await target.StatAsync(path, ct) is null
                ? Problems.Create(StatusCodes.Status404NotFound, ErrorCodes.NotFound)
                : Results.Stream(await target.OpenReadAsync(path, ct), "application/octet-stream");
        });

        backups.MapPut("/file", async (HttpContext context, string path, PeerBackupStore store, CancellationToken ct) =>
        {
            var (target, peerId, quota) = await store.OpenAsync(Caller(context), ct);
            if (context.Request.ContentLength is not { } length)
                return Problems.Create(StatusCodes.Status411LengthRequired, ErrorCodes.Required);
            if (quota is { } limit && await store.UsedAsync(peerId, target, ct) + length > limit)
                return Problems.Create(StatusCodes.Status409Conflict, ErrorCodes.BackupQuotaExceeded);
            // Backups carry whole albums and databases: no limit on the body.
            if (context.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } size)
                size.MaxRequestBodySize = null;
            await target.WriteAsync(path, context.Request.Body, length, ct);
            store.Add(peerId, length);
            return Results.NoContent();
        });

        backups.MapPost("/move", async (HttpContext context, string from, string to, PeerBackupStore store, CancellationToken ct) =>
        {
            var (target, _, _) = await store.OpenAsync(Caller(context), ct);
            await target.MoveAsync(from, to, ct);
            return Results.NoContent();
        });

        backups.MapDelete("/file", async (HttpContext context, string path, PeerBackupStore store, CancellationToken ct) =>
        {
            var (target, peerId, _) = await store.OpenAsync(Caller(context), ct);
            var size = await target.StatAsync(path, ct) is { } file ? file.Size : 0;
            await target.DeleteAsync(path, ct);
            store.Add(peerId, -size);
            return Results.NoContent();
        });

        backups.MapDelete("/folder", async (HttpContext context, string path, PeerBackupStore store, CancellationToken ct) =>
        {
            var (target, peerId, _) = await store.OpenAsync(Caller(context), ct);
            await target.DeleteFolderAsync(path, ct);
            store.Forget(peerId);
            return Results.NoContent();
        });
    }

    private static P2PPeerSnapshot Caller(HttpContext context) =>
        P2PRequest.Peer(context) is { Kind: P2PPeerKind.Server } peer ? peer : throw new ArcanistException.NotFound();
}