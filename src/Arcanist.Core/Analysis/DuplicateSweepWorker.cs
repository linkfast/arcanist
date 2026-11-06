using Arcanist.Core.Application.Scanning;
using Arcanist.Core.Data;
using Arcanist.Core.Setup;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Arcanist.Core.Analysis;

/// <summary>Where the sweep of the catalog is, and how to send it back to the start or wake it.</summary>
public sealed class DuplicateSweep(SettingsStore settings)
{
    private readonly SemaphoreSlim _wake = new(0);

    public Task WaitAsync(TimeSpan delay, CancellationToken cancellationToken) => _wake.WaitAsync(delay, cancellationToken);

    /// <summary>There may be new files to look at.</summary>
    public void Wake()
    {
        if (_wake.CurrentCount == 0)
            _wake.Release();
    }

    /// <summary>Looks at the whole catalog again.</summary>
    public async Task RestartAsync(CancellationToken cancellationToken)
    {
        await settings.DeleteAsync(SettingKeys.DuplicatesCursor, cancellationToken);
        Wake();
    }
}

/// <summary>
/// Goes through the catalog's files, oldest first (UUIDv7 ids), looking for duplicates of their tracks. Analysis looks
/// only at audio it has just heard: a copy of audio already analyzed (the same song added again, on another album) is
/// not analyzed again, and a library older than a detection rule never met it. The cursor makes it one pass plus the new
/// files; starting over is <see cref="DuplicateSweep.RestartAsync"/>.
/// </summary>
internal sealed class DuplicateSweepWorker(
    IArcanistDbContextFactory databases,
    DuplicateDetector duplicates,
    DuplicateSweep sweep,
    ScanEvents scans,
    SettingsStore settings,
    IOptionsMonitor<SetupOptions> setup,
    IOptionsMonitor<AnalysisOptions> options,
    DatabaseMaintenance maintenance,
    ILogger<DuplicateSweepWorker> logger) : BackgroundService
{
    private const int BatchSize = 100;

    private static readonly TimeSpan IdleDelay = TimeSpan.FromMinutes(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();
        while (!stoppingToken.IsCancellationRequested)
        {
            TimeSpan delay;
            try
            {
                using (await maintenance.WorkAsync(stoppingToken))
                    delay = await RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception e)
            {
                logger.LogError(e, "Looking for duplicates failed");
                delay = TimeSpan.FromMinutes(5);
            }

            try
            {
                await sweep.WaitAsync(delay, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task<TimeSpan> RunOnceAsync(CancellationToken cancellationToken)
    {
        if (!options.CurrentValue.Enabled || !setup.CurrentValue.Completed || !databases.IsConfigured)
            return IdleDelay;
        if (scans.HasActiveJobs)
            return TimeSpan.FromMinutes(1);

        var cursor = await settings.GetAsync<Guid?>(SettingKeys.DuplicatesCursor, null, cancellationToken);
        await using var db = databases.CreateDbContext();
        var files = await db.MediaFiles.AsNoTracking()
            .Where(f => f.MissingSince == null && f.CueTrack >= 0 && (cursor == null || f.Id.CompareTo(cursor.Value) > 0))
            .OrderBy(f => f.Id)
            .Take(BatchSize)
            .Select(f => new { f.Id, f.TrackId })
            .ToListAsync(cancellationToken);
        if (files.Count == 0)
            return IdleDelay;

        var found = await duplicates.ProcessTracksAsync(db, files.Select(f => f.TrackId).Distinct().ToList(), cancellationToken);
        if (found != new DuplicateDetector.Outcome(0, 0, 0))
            logger.LogInformation("Duplicates: {Merged} merged, {Linked} linked, {Proposed} proposed", found.Merged, found.Linked, found.Proposed);

        await settings.SetAsync(SettingKeys.DuplicatesCursor, files[^1].Id, cancellationToken);
        return TimeSpan.FromSeconds(1);
    }
}
