using Arcanist.Core.Auth;
using Arcanist.Core.Data;
using Arcanist.Core.Data.Entities;
using Arcanist.Core.Scanning;
using Microsoft.EntityFrameworkCore;
using ScanJobEntity = Arcanist.Core.Data.Entities.ScanJob;

namespace Arcanist.Core.Application.Scanning;

/// <summary>A library scan. Part of the API contract.</summary>
/// <param name="FilesSeen">Audio files found in the library's sources.</param>
/// <param name="Errors">Files that could not be read.</param>
/// <param name="ErrorCode">Why the scan failed or skipped a source (e.g. <c>source_unavailable</c>).</param>
/// <param name="Progress">Live progress while the scan is running.</param>
public sealed record ScanJob(
    Guid Id,
    Guid LibraryId,
    ScanKind Kind,
    ScanStatus Status,
    DateTime RequestedAt,
    DateTime? StartedAt,
    DateTime? FinishedAt,
    int FilesSeen,
    int FilesAdded,
    int FilesUpdated,
    int FilesMissing,
    int Errors,
    string? ErrorCode,
    ScanProgress? Progress);

/// <summary>Starting, cancelling and following scans. Requires RUN_SCANS (or MANAGE_LIBRARIES to follow them).</summary>
public sealed class ScanService(
    ArcanistDbContext db,
    AccessGuard guard,
    ScanCoordinator coordinator,
    ScanProgressTracker progress,
    ScanEvents events)
{
    public const int MaxJobs = 100;

    public async Task<ScanJob> StartAsync(Guid libraryId, bool full, CancellationToken cancellationToken)
    {
        var access = guard.Permission(Permissions.RunScans);
        await EnsureVisibleAsync(access, libraryId, cancellationToken);

        var job = await coordinator.EnqueueAsync(libraryId, full ? ScanKind.Full : ScanKind.Incremental, access.UserId, cancellationToken);
        return ToModel(job);
    }

    public async Task CancelAsync(Guid jobId, CancellationToken cancellationToken)
    {
        var access = guard.Permission(Permissions.RunScans);
        var job = await db.ScanJobs.AsNoTracking().SingleOrDefaultAsync(j => j.Id == jobId, cancellationToken)
                  ?? throw new ArcanistException.NotFound();
        await EnsureVisibleAsync(access, job.LibraryId, cancellationToken);

        if (!await coordinator.CancelAsync(jobId, cancellationToken))
            throw new ArcanistException.Conflict(ErrorCodes.ScanFinished);
    }

    /// <summary>Most recent scans first, of one library or of all the caller can see.</summary>
    public async Task<IReadOnlyList<ScanJob>> ListAsync(Guid? libraryId, int? limit, CancellationToken cancellationToken)
    {
        var access = Follower();
        var take = Math.Clamp(limit ?? 20, 1, MaxJobs);

        var jobs = db.ScanJobs.AsNoTracking();
        if (!access.IsAdmin)
        {
            var visible = access.LibraryIds.ToList();
            jobs = jobs.Where(j => visible.Contains(j.LibraryId));
        }

        if (libraryId is { } id)
            jobs = jobs.Where(j => j.LibraryId == id);

        var result = await jobs.OrderByDescending(j => j.RequestedAt).Take(take).ToListAsync(cancellationToken);
        return result.Select(ToModel).ToList();
    }

    public async Task<ScanJob> GetAsync(Guid jobId, CancellationToken cancellationToken)
    {
        var access = Follower();
        var job = await db.ScanJobs.AsNoTracking().SingleOrDefaultAsync(j => j.Id == jobId, cancellationToken);
        if (job is null || !access.CanAccessLibrary(job.LibraryId))
            throw new ArcanistException.NotFound();

        return ToModel(job);
    }

    /// <summary>Live updates of scans of one library, or of all the caller can see.</summary>
    public IObservable<ScanJob> Watch(Guid? libraryId)
    {
        var access = Follower();
        return new FilteredObservable<ScanJob>(events,
            job => access.CanAccessLibrary(job.LibraryId) && (libraryId is null || job.LibraryId == libraryId));
    }

    private UserAccess Follower()
    {
        var access = guard.Authenticated();
        return access.Has(Permissions.RunScans) || access.Has(Permissions.ManageLibraries)
            ? access
            : throw new ArcanistException.Forbidden();
    }

    private async Task EnsureVisibleAsync(UserAccess access, Guid libraryId, CancellationToken cancellationToken)
    {
        if (!access.CanAccessLibrary(libraryId) || !await db.Libraries.AnyAsync(l => l.Id == libraryId, cancellationToken))
            throw new ArcanistException.NotFound();
    }

    private ScanJob ToModel(ScanJobEntity job) =>
        ScanJobMapping.ToModel(job, job.Status == ScanStatus.Running ? progress.Get(job.Id) : null);
}
