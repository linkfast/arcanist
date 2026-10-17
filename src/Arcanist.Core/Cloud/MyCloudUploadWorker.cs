using Arcanist.Core.Application;
using Arcanist.Core.Capabilities;
using Arcanist.Core.Data;
using Arcanist.Core.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Arcanist.Core.Cloud;

/// <summary>
/// Sends the songs users asked for to their own Ultimate Cloud library (<see cref="UserUpload"/>s queued by
/// <see cref="Application.Uploads.MyUploadService"/>), one at a time and in order, as each user's account: through
/// <see cref="CloudSyncWorker.UploadFileAsync"/>, which uploads a tag-free copy with the catalog's description, or only
/// pairs it when the account already has it. A full account or a read-only one fails the rest of that user's queue.
/// Uploads a restart interrupted are queued again.
/// </summary>
public sealed class MyCloudUploadWorker(
    IArcanistDbContextFactory databases,
    CloudSyncWorker uploads,
    UltimateCloudAccount server,
    UserCloudAccounts accounts,
    DatabaseMaintenance maintenance,
    IOptionsMonitor<Setup.SetupOptions> setup,
    TimeProvider time,
    ILogger<MyCloudUploadWorker> logger) : BackgroundService
{
    private readonly SemaphoreSlim _wake = new(0);

    public void Wake()
    {
        if (_wake.CurrentCount == 0)
            _wake.Release();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();
        var restarted = false;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (databases.IsConfigured && setup.CurrentValue.Completed)
                {
                    if (!restarted)
                    {
                        await DbAsync(db => db.UserUploads.Where(u => u.Status == UserUploadStatus.Uploading)
                            .ExecuteUpdateAsync(s => s.SetProperty(u => u.Status, UserUploadStatus.Queued), stoppingToken), stoppingToken);
                        restarted = true;
                    }

                    if (await DbAsync(db => db.UserUploads.AsNoTracking()
                            .Where(u => u.Status == UserUploadStatus.Queued)
                            .OrderBy(u => u.CreatedAt).ThenBy(u => u.Id)
                            .FirstOrDefaultAsync(stoppingToken), stoppingToken) is { } next)
                    {
                        await RunAsync(next, stoppingToken);
                        continue;
                    }
                }

                await _wake.WaitAsync(TimeSpan.FromMinutes(1), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception e)
            {
                logger.LogError(e, "Uploading to Ultimate Cloud failed");
                await Task.Delay(TimeSpan.FromSeconds(30), time, stoppingToken).ContinueWith(_ => { }, TaskScheduler.Default);
            }
        }
    }

    private async Task RunAsync(UserUpload upload, CancellationToken cancellationToken)
    {
        await SetAsync(upload.Id, UserUploadStatus.Uploading, null, null, cancellationToken);
        string? error = !server.Enabled ? ErrorCodes.CloudDisabled
            : accounts.TierOf(upload.UserId) == CloudTier.None ? ErrorCodes.CloudNotLinked
            : upload.FileId is null ? ErrorCodes.NotFound
            : null;
        CloudSyncWorker.CloudUploadOutcome? outcome = null;
        if (error is null)
        {
            using var as_ = UltimateCloudClient.As(upload.UserId);
            try
            {
                outcome = await uploads.UploadFileAsync(upload.FileId!.Value, cancellationToken);
                error = outcome.ErrorCode;
            }
            catch (UltimateCloudException e)
            {
                logger.LogWarning("Uploading {Name} to Ultimate Cloud failed: {Error}", upload.Name, e.Code);
                error = e.Code is ErrorCodes.CloudNotLinked or ErrorCodes.CloudLinkExpired or ErrorCodes.CloudUnreachable ? e.Code : ErrorCodes.UploadFailed;
            }
        }

        if (error is null)
        {
            await SetAsync(upload.Id, outcome!.AlreadyInCloud ? UserUploadStatus.AlreadyInCloud : UserUploadStatus.Uploaded, null, outcome.CloudTrackId,
                cancellationToken);
            return;
        }

        await SetAsync(upload.Id, UserUploadStatus.Failed, error, null, cancellationToken);
        // The account takes nothing more for now: the rest of this user's queue would fail the same way.
        if (error is ErrorCodes.CloudQuotaExceeded or ErrorCodes.CloudReadOnly or ErrorCodes.CloudNotLinked or ErrorCodes.CloudDisabled
            or ErrorCodes.CloudLinkExpired or ErrorCodes.CloudToolMissing)
        {
            var now = time.GetUtcNow().UtcDateTime;
            await DbAsync(db => db.UserUploads.Where(u => u.UserId == upload.UserId && u.Status == UserUploadStatus.Queued)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(u => u.Status, UserUploadStatus.Failed)
                    .SetProperty(u => u.ErrorCode, error)
                    .SetProperty(u => u.UpdatedAt, now), cancellationToken), cancellationToken);
        }
    }

    private Task SetAsync(Guid id, UserUploadStatus status, string? error, string? cloudTrackId, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow().UtcDateTime;
        return DbAsync(db => db.UserUploads.Where(u => u.Id == id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(u => u.Status, status)
                .SetProperty(u => u.ErrorCode, error)
                .SetProperty(u => u.CloudTrackId, u => cloudTrackId ?? u.CloudTrackId)
                .SetProperty(u => u.UpdatedAt, now), cancellationToken), cancellationToken);
    }

    private async Task<T> DbAsync<T>(Func<ArcanistDbContext, Task<T>> work, CancellationToken cancellationToken)
    {
        using (await maintenance.WorkAsync(cancellationToken))
        {
            await using var db = databases.CreateDbContext();
            return await work(db);
        }
    }
}
