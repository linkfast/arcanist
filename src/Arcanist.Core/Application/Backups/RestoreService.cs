using System.Text.Json;
using Arcanist.Core.Backups;
using Arcanist.Core.Backups.Targets;
using Arcanist.Core.Data;
using Arcanist.Core.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Arcanist.Core.Application.Backups;

/// <summary>
/// Restoring from backups: what a destination holds (this server's backups or another's), what a snapshot holds, and
/// bringing back its files and user data. Administrators only; starting a restore needs a recent authentication.
/// </summary>
public sealed class RestoreService(
    ArcanistDbContext db,
    AccessGuard guard,
    RestoreEvents events,
    TimeProvider time,
    IServiceProvider services)
{
    private BackupTargets Targets => services.GetRequiredService<BackupTargets>();
    private RestoreWorker Worker => services.GetRequiredService<RestoreWorker>();

    public IObservable<RestoreJobInfo> Watch()
    {
        guard.Admin();
        return events;
    }

    /// <summary>The backups in a destination, with their snapshots.</summary>
    public async Task<IReadOnlyList<StoredBackup>> StoredBackupsAsync(Guid destinationId, CancellationToken cancellationToken)
    {
        guard.Admin();
        var target = await TargetAsync(destinationId, cancellationToken);
        var found = await BackupReader.FindAsync(target, cancellationToken);
        var sourceIds = found.SelectMany(b => b.Manifest.Sources).Select(s => s.SourceId).Distinct().ToList();
        var here = (await db.LibrarySources.AsNoTracking().Where(s => sourceIds.Contains(s.Id)).Select(s => s.Id).ToListAsync(cancellationToken)).ToHashSet();
        return found
            .OrderByDescending(b => b.Manifest.UpdatedAt)
            .Select(b => new StoredBackup(
                b.Folder,
                b.Manifest.Id,
                b.Manifest.Name,
                b.Manifest.Scope,
                b.Manifest.Machine,
                b.Manifest.ArcanistVersion,
                b.Manifest.UpdatedAt,
                b.Manifest.Snapshots.Select(s => new StoredSnapshot(s.Name, s.CreatedAt, s.Files, s.Bytes, s.UserData, s.Server)).ToList(),
                b.Manifest.Sources.Select(s => new StoredLibrary(s.Folder, s.Library, here.Contains(s.SourceId) ? s.SourceId : null)).ToList()))
            .ToList();
    }

    /// <summary>What a snapshot holds: its libraries, albums, playlists and users.</summary>
    public async Task<BackupSnapshotContents> SnapshotContentsAsync(Guid destinationId, string folder, string snapshot, CancellationToken cancellationToken)
    {
        guard.Admin();
        var target = await TargetAsync(destinationId, cancellationToken);
        var manifest = await BackupReader.ManifestAsync(target, folder, cancellationToken);
        var index = await BackupReader.IndexAsync(target, folder, manifest, snapshot, cancellationToken);
        var catalog = await BackupReader.SnapshotFileAsync<BackupCatalog>(target, folder, snapshot, BackupDataExtra.CatalogFile, cancellationToken);
        var userData = await BackupReader.SnapshotFileAsync<BackupUserData>(target, folder, snapshot, BackupDataExtra.UserDataFile, cancellationToken);

        var libraries = index.Files.GroupBy(f => f.Path[..Math.Max(0, f.Path.IndexOf('/'))])
            .Select(g => new SnapshotLibrary(g.Key, manifest.Sources.FirstOrDefault(s => s.Folder == g.Key)?.Library ?? g.Key, g.Count(), g.Sum(f => f.Size)))
            .OrderBy(l => l.Name)
            .ToList();

        var tracks = catalog?.Tracks.ToDictionary(t => t.Id) ?? [];
        var albums = (catalog?.Albums ?? [])
            .Select(a => new SnapshotAlbum(a.Id, a.Title, a.Artist, a.Year,
                tracks.Values.Where(t => t.AlbumId == a.Id).SelectMany(t => t.Files).Select(BackupPaths.Folder).Distinct().Order(StringComparer.Ordinal).ToList()))
            .Where(a => a.Paths.Count > 0)
            .OrderBy(a => a.Artist).ThenBy(a => a.Year).ThenBy(a => a.Title)
            .ToList();

        var local = (await db.Users.AsNoTracking().Select(u => new { u.Id, u.NormalizedUserName }).ToListAsync(cancellationToken))
            .ToDictionary(u => u.NormalizedUserName, u => u.Id);
        var users = (userData?.Users ?? [])
            .Select(u => new SnapshotUser(u.UserName, u.DisplayName, u.Ratings.Count, u.Plays.Count, u.Playlists.Count,
                local.TryGetValue(TextNormalizer.Normalize(u.UserName), out var id) ? id : null))
            .ToList();
        var playlists = (userData?.Users ?? [])
            .SelectMany(u => u.Playlists.Select(p => new SnapshotPlaylist(u.UserName, p.Name, p.Entries.Count,
                p.Entries.Select(e => tracks.GetValueOrDefault(e.Track)?.Files.FirstOrDefault()).OfType<string>().Distinct().ToList())))
            .ToList();
        return new BackupSnapshotContents(libraries, albums, playlists, users);
    }

    public async Task<RestoreJobInfo> StartAsync(StartRestoreInput input, CancellationToken cancellationToken)
    {
        var actor = guard.Admin();
        guard.RecentAuthentication();
        var errors = new ValidationErrors();
        if (!input.Files && !input.UserData)
            errors.Add("files", ErrorCodes.Required);
        var paths = (input.Paths ?? []).Select(p => p.Trim().Trim('/')).Where(p => p.Length > 0).Distinct().ToList();
        if (paths.Any(p => !BackupPaths.IsSafe(p)))
            errors.Add("paths", ErrorCodes.InvalidFormat);
        if (input.TargetSourceId is { } targetId)
        {
            var source = await db.LibrarySources.AsNoTracking().SingleOrDefaultAsync(s => s.Id == targetId, cancellationToken);
            if (source is null || !BackupPlanner.IsBackedUp(source.Kind))
                errors.Add("targetSourceId", ErrorCodes.NotFound);
        }

        var userIds = (input.Users ?? []).Select(u => u.UserId).OfType<Guid>().Distinct().ToList();
        if (userIds.Count > 0 && await db.Users.CountAsync(u => userIds.Contains(u.Id), cancellationToken) != userIds.Count)
            errors.Add("users", ErrorCodes.NotFound);
        errors.ThrowIfAny();

        var target = await TargetAsync(input.DestinationId, cancellationToken);
        var manifest = await BackupReader.ManifestAsync(target, input.Folder, cancellationToken);
        if (manifest.Snapshots.FirstOrDefault(s => s.Name == input.Snapshot) is not { } snapshot)
            throw new ArcanistException.Conflict(ErrorCodes.BackupNotFound);

        var job = new RestoreJob
        {
            DestinationId = input.DestinationId,
            BackupFolder = input.Folder,
            BackupName = manifest.Name,
            Snapshot = snapshot.Name,
            Files = input.Files,
            UserData = input.UserData && snapshot.UserData,
            Paths = JsonSerializer.Serialize(paths, JsonSerializerOptions.Web),
            TargetSourceId = input.TargetSourceId,
            UserMap = JsonSerializer.Serialize((input.Users ?? []).GroupBy(u => u.UserName).ToDictionary(g => g.Key, g => g.Last().UserId), JsonSerializerOptions.Web),
            Status = RestoreStatus.Queued,
            RequestedByUserId = actor.UserId,
            RequestedAt = time.GetUtcNow().UtcDateTime,
        };
        db.RestoreJobs.Add(job);
        await db.SaveChangesAsync(cancellationToken);
        Worker.Wake();
        var model = ToModel(job);
        events.Publish(model);
        return model;
    }

    /// <summary>The 20 latest restores.</summary>
    public async Task<IReadOnlyList<RestoreJobInfo>> JobsAsync(CancellationToken cancellationToken)
    {
        guard.Admin();
        return (await db.RestoreJobs.AsNoTracking().OrderByDescending(j => j.RequestedAt).Take(20).ToListAsync(cancellationToken)).Select(ToModel).ToList();
    }

    public async Task<RestoreJobInfo> JobAsync(Guid id, CancellationToken cancellationToken)
    {
        guard.Admin();
        return ToModel(await db.RestoreJobs.AsNoTracking().SingleOrDefaultAsync(j => j.Id == id, cancellationToken) ?? throw new ArcanistException.NotFound());
    }

    /// <summary>Stops a restore; what it restored stays.</summary>
    public async Task<RestoreJobInfo> CancelAsync(Guid id, CancellationToken cancellationToken)
    {
        guard.Admin();
        var job = await db.RestoreJobs.SingleOrDefaultAsync(j => j.Id == id, cancellationToken) ?? throw new ArcanistException.NotFound();
        if (job.FinishedAt is not null)
            throw new ArcanistException.Conflict(ErrorCodes.BackupNotActive);
        if (!Worker.Cancel(id))
        {
            job.Status = RestoreStatus.Cancelled;
            job.FinishedAt = time.GetUtcNow().UtcDateTime;
            await db.SaveChangesAsync(cancellationToken);
        }
        else
        {
            for (var i = 0; i < 50 && await db.RestoreJobs.AsNoTracking().AnyAsync(j => j.Id == id && j.FinishedAt == null, cancellationToken); i++)
                await Task.Delay(100, cancellationToken);
        }

        return await JobAsync(id, cancellationToken);
    }

    private async Task<IBackupTarget> TargetAsync(Guid destinationId, CancellationToken cancellationToken)
    {
        var destination = await db.BackupDestinations.AsNoTracking().SingleOrDefaultAsync(d => d.Id == destinationId, cancellationToken)
                          ?? throw new ArcanistException.NotFound();
        var target = Targets.Open(destination);
        if (await target.ProbeAsync(cancellationToken) is { } problem)
            throw new ArcanistException.Conflict(problem);
        return target;
    }

    internal static RestoreJobInfo ToModel(RestoreJob job) => new(
        job.Id, job.BackupName, job.Snapshot, job.Status, job.Files, job.UserData, job.FilesTotal, job.Restored, job.Identical, job.Renamed,
        job.FilesFailed, job.Ratings, job.PlayCounts, job.Plays, job.Playlists, job.Unmatched, job.RequestedAt, job.FinishedAt, job.ErrorCode,
        JsonSerializer.Deserialize<List<BackupJobFailure>>(job.Failures, JsonSerializerOptions.Web) ?? []);
}
