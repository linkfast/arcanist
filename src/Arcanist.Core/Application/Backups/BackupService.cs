using System.Text.Json;
using Arcanist.Core.Application.Libraries;
using Arcanist.Core.Backups;
using Arcanist.Core.Backups.Targets;
using Arcanist.Core.Data;
using Arcanist.Core.Data.Entities;
using Arcanist.Core.Hosting;
using Arcanist.Core.Organization;
using Arcanist.Core.Storage;
using Arcanist.Core.Storage.S3;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace Arcanist.Core.Application.Backups;

/// <summary>
/// Backups of the server, its libraries, albums or playlists, into folders, S3 buckets or other Arcanist servers.
/// Administrators only; creating, changing or removing destinations and backups needs a recent authentication
/// (they read every library and may carry the server's database).
/// </summary>
public sealed class BackupService(
    ArcanistDbContext db,
    AccessGuard guard,
    BackupEvents events,
    ArcanistPaths paths,
    IDataProtectionProvider protection,
    TimeProvider time,
    IServiceProvider services)
{
    public const int MinPasswordLength = 8;
    internal const string PasswordPurpose = "Arcanist.Backup.Password";

    private BackupWorker Worker => services.GetRequiredService<BackupWorker>();
    private BackupTargets Targets => services.GetRequiredService<BackupTargets>();
    private ILibraryStorageResolver Storages => services.GetRequiredService<ILibraryStorageResolver>();

    public IObservable<BackupJob> Watch()
    {
        guard.Admin();
        return events;
    }

    // ── Destinations ────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<BackupDestinationInfo>> DestinationsAsync(CancellationToken cancellationToken)
    {
        guard.Admin();
        var destinations = await db.BackupDestinations.AsNoTracking().OrderBy(d => d.Name).ToListAsync(cancellationToken);
        var counts = await db.Backups.AsNoTracking().GroupBy(b => b.DestinationId).Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Key, g => g.Count, cancellationToken);
        return destinations.Select(d => ToModel(d, counts.GetValueOrDefault(d.Id))).ToList();
    }

    public async Task<BackupDestinationInfo> CreateDestinationAsync(CreateBackupDestinationInput input, CancellationToken cancellationToken)
    {
        guard.Admin();
        guard.RecentAuthentication();
        var name = input.Name?.Trim() ?? "";
        if (name.Length == 0)
            throw ArcanistException.Field("name", ErrorCodes.Required);

        var destination = input.Kind switch
        {
            BackupDestinationKind.Folder => await FolderDestinationAsync(name, input.Path, cancellationToken),
            BackupDestinationKind.S3 => S3Destination(name, input.S3),
            BackupDestinationKind.Peer => await services.GetRequiredService<BackupPeers>().JoinAsync(name, input.Invitation, cancellationToken),
            _ => throw ArcanistException.Field("kind", ErrorCodes.InvalidFormat),
        };

        if (await Targets.Open(destination).ProbeAsync(cancellationToken) is { } problem)
            throw new ArcanistException.Conflict(problem);
        db.BackupDestinations.Add(destination);
        await db.SaveChangesAsync(cancellationToken);
        return ToModel(destination, 0);
    }

    public async Task<BackupDestinationTest> TestDestinationAsync(Guid id, CancellationToken cancellationToken)
    {
        guard.Admin();
        var destination = await db.BackupDestinations.AsNoTracking().SingleOrDefaultAsync(d => d.Id == id, cancellationToken)
                          ?? throw new ArcanistException.NotFound();
        var problem = await Targets.Open(destination).ProbeAsync(cancellationToken);
        return new BackupDestinationTest(problem is null, problem);
    }

    /// <summary>Removes a destination and its backups from this server; what is in the destination stays.</summary>
    public async Task<bool> DeleteDestinationAsync(Guid id, CancellationToken cancellationToken)
    {
        guard.Admin();
        guard.RecentAuthentication();
        var destination = await db.BackupDestinations.SingleOrDefaultAsync(d => d.Id == id, cancellationToken) ?? throw new ArcanistException.NotFound();
        if (await db.BackupRuns.AnyAsync(r => r.Backup.DestinationId == id && (r.Status == BackupRunStatus.Queued || r.Status == BackupRunStatus.Running), cancellationToken))
            throw new ArcanistException.Conflict(ErrorCodes.BackupBusy);
        db.BackupDestinations.Remove(destination);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private async Task<BackupDestination> FolderDestinationAsync(string name, string? path, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path.Trim()))
            throw ArcanistException.Field("path", ErrorCodes.InvalidFormat);
        var local = Storages.For(LibrarySourceKind.Local);
        var location = local.NormalizeLocation(path);
        if (!Directory.Exists(location))
            throw ArcanistException.Field("path", ErrorCodes.NotFound);

        // Inside a library its scans would find the copies; inside the data folder a factory reset would take them.
        var libraries = await db.LibrarySources.AsNoTracking().Where(s => s.Kind == LibrarySourceKind.Local).Select(s => s.Location).ToListAsync(cancellationToken);
        if (libraries.Any(l => local.Contains(l, location)) || local.Contains(paths.Root, location))
            throw ArcanistException.Field("path", ErrorCodes.BackupDestinationOverlaps);

        return new BackupDestination { Name = name, Kind = BackupDestinationKind.Folder, Location = location, CreatedAt = Now };
    }

    private BackupDestination S3Destination(string name, S3ConnectionInput? input)
    {
        if (!services.GetRequiredService<Capabilities.ICapabilityService>().IsEnabled(Capabilities.Capability.ThirdPartyStorage))
            throw new ArcanistException.Conflict(ErrorCodes.S3Unavailable);
        var (connection, prefix) = S3LibraryService.Validate(input, null, "s3.");
        var storage = (S3Storage)Storages.For(LibrarySourceKind.S3);
        return new BackupDestination
        {
            Name = name,
            Kind = BackupDestinationKind.S3,
            Location = storage.NormalizeLocation($"{S3Storage.Scheme}{connection.Bucket}/{prefix}"),
            Settings = S3LibraryService.SettingsJson(storage, connection),
            CreatedAt = Now,
        };
    }

    // ── Backups ─────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<BackupDefinition>> ListAsync(CancellationToken cancellationToken)
    {
        guard.Admin();
        var backups = await db.Backups.AsNoTracking().Include(b => b.Destination).OrderBy(b => b.Name).ToListAsync(cancellationToken);
        return await ToModelsAsync(backups, cancellationToken);
    }

    public async Task<BackupDefinition> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        guard.Admin();
        var backup = await db.Backups.AsNoTracking().Include(b => b.Destination).SingleOrDefaultAsync(b => b.Id == id, cancellationToken)
                     ?? throw new ArcanistException.NotFound();
        return (await ToModelsAsync([backup], cancellationToken))[0];
    }

    public async Task<BackupDefinition> CreateAsync(CreateBackupInput input, CancellationToken cancellationToken)
    {
        guard.Admin();
        guard.RecentAuthentication();
        var destination = await db.BackupDestinations.SingleOrDefaultAsync(d => d.Id == input.DestinationId, cancellationToken)
                          ?? throw ArcanistException.Field("destinationId", ErrorCodes.NotFound);

        var name = input.Name?.Trim() ?? "";
        if (name.Length == 0)
            throw ArcanistException.Field("name", ErrorCodes.Required);

        // Its own folder in the destination, named once: renaming the backup does not move it.
        var id = Guid.CreateVersion7();
        var backup = new Backup
        {
            Id = id,
            Name = name,
            DestinationId = destination.Id,
            Scope = input.Scope,
            Folder = $"{PathTemplate.SanitizeName(name)}-{id.ToString("N")[^8..]}",
            ScopeIds = "[]",
            TimeZone = "",
            CreatedAt = Now,
        };
        await ApplyAsync(backup, name, input.ScopeIds, input.IncludeUserData, input.IncludeServer, input.Password, input.Schedule,
            input.IntervalHours, input.TimeOfDay, input.DayOfWeek, input.TimeZone, input.RetentionDays, input.Enabled, cancellationToken);
        db.Backups.Add(backup);
        await db.SaveChangesAsync(cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    public async Task<BackupDefinition> UpdateAsync(Guid id, UpdateBackupInput input, CancellationToken cancellationToken)
    {
        guard.Admin();
        guard.RecentAuthentication();
        var backup = await db.Backups.SingleOrDefaultAsync(b => b.Id == id, cancellationToken) ?? throw new ArcanistException.NotFound();
        await ApplyAsync(backup, input.Name?.Trim() ?? "", input.ScopeIds, input.IncludeUserData, input.IncludeServer, input.Password, input.Schedule,
            input.IntervalHours, input.TimeOfDay, input.DayOfWeek, input.TimeZone, input.RetentionDays, input.Enabled, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    /// <summary>Removes a backup from this server; its copies in the destination stay.</summary>
    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        guard.Admin();
        guard.RecentAuthentication();
        var backup = await db.Backups.SingleOrDefaultAsync(b => b.Id == id, cancellationToken) ?? throw new ArcanistException.NotFound();
        if (await db.BackupRuns.AnyAsync(r => r.BackupId == id && (r.Status == BackupRunStatus.Queued || r.Status == BackupRunStatus.Running), cancellationToken))
            throw new ArcanistException.Conflict(ErrorCodes.BackupBusy);
        db.Backups.Remove(backup);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private async Task ApplyAsync(
        Backup backup, string name, IReadOnlyList<Guid>? scopeIds, bool includeUserData, bool includeServer, string? password,
        BackupSchedule schedule, int? intervalHours, int? timeOfDay, DayOfWeek? dayOfWeek, string? timeZone, int retentionDays, bool enabled,
        CancellationToken cancellationToken)
    {
        var errors = new ValidationErrors();
        if (name.Length == 0)
            errors.Add("name", ErrorCodes.Required);

        var ids = (scopeIds ?? []).Distinct().ToList();
        if (backup.Scope != BackupScope.Server && ids.Count == 0)
            errors.Add("scopeIds", ErrorCodes.Required);
        else if (!await ScopeExistsAsync(backup.Scope, ids, cancellationToken))
            errors.Add("scopeIds", ErrorCodes.NotFound);

        // The server's part is the database: only the whole server or libraries carry it.
        includeServer &= backup.Scope is BackupScope.Server or BackupScope.Library;
        var hasPassword = !string.IsNullOrEmpty(password);
        if (includeServer && !hasPassword && backup.ProtectedPassword is null)
            errors.Add("password", ErrorCodes.BackupPasswordRequired);
        if (hasPassword && password!.Length < MinPasswordLength)
            errors.Add("password", ErrorCodes.BackupPasswordRequired);

        switch (schedule)
        {
            case BackupSchedule.Hours when intervalHours is not (>= 1 and <= 24 * 30):
                errors.Add("intervalHours", ErrorCodes.InvalidFormat);
                break;
            case BackupSchedule.Daily or BackupSchedule.Weekly when timeOfDay is not (>= 0 and < 24 * 60):
                errors.Add("timeOfDay", ErrorCodes.InvalidFormat);
                break;
        }

        if (schedule == BackupSchedule.Weekly && dayOfWeek is null)
            errors.Add("dayOfWeek", ErrorCodes.Required);
        var zone = string.IsNullOrWhiteSpace(timeZone) ? TimeZoneInfo.Local.Id : timeZone.Trim();
        if (!BackupSchedules.IsZone(zone))
            errors.Add("timeZone", ErrorCodes.InvalidFormat);
        if (retentionDays is < 0 or > 36500)
            errors.Add("retentionDays", ErrorCodes.InvalidFormat);
        errors.ThrowIfAny();

        backup.Name = name;
        backup.ScopeIds = JsonSerializer.Serialize(backup.Scope == BackupScope.Server ? [] : ids, JsonSerializerOptions.Web);
        backup.IncludeUserData = includeUserData;
        backup.IncludeServer = includeServer;
        if (hasPassword)
            backup.ProtectedPassword = protection.CreateProtector(PasswordPurpose).Protect(password!);
        backup.Schedule = schedule;
        backup.IntervalHours = schedule == BackupSchedule.Hours ? intervalHours : null;
        backup.TimeOfDay = schedule is BackupSchedule.Daily or BackupSchedule.Weekly ? timeOfDay : null;
        backup.DayOfWeek = schedule == BackupSchedule.Weekly ? dayOfWeek : null;
        backup.TimeZone = zone;
        backup.RetentionDays = retentionDays;
        backup.Enabled = enabled;
        backup.NextRunAt = BackupSchedules.Next(backup, Now);
    }

    private async Task<bool> ScopeExistsAsync(BackupScope scope, List<Guid> ids, CancellationToken cancellationToken) => scope switch
    {
        BackupScope.Server => true,
        BackupScope.Library => await db.Libraries.CountAsync(l => ids.Contains(l.Id) && l.OwnerUserId == null, cancellationToken) == ids.Count,
        BackupScope.Album => await db.Albums.CountAsync(a => ids.Contains(a.Id), cancellationToken) == ids.Count,
        BackupScope.Playlist => await db.Playlists.CountAsync(p => ids.Contains(p.Id), cancellationToken) == ids.Count,
        _ => false,
    };

    // ── Runs ────────────────────────────────────────────────────────────────

    public async Task<BackupJob> RunAsync(Guid id, CancellationToken cancellationToken)
    {
        var actor = guard.Admin();
        var backup = await db.Backups.SingleOrDefaultAsync(b => b.Id == id, cancellationToken) ?? throw new ArcanistException.NotFound();
        if (await db.BackupRuns.AnyAsync(r => r.BackupId == id && (r.Status == BackupRunStatus.Queued || r.Status == BackupRunStatus.Running), cancellationToken))
            throw new ArcanistException.Conflict(ErrorCodes.BackupBusy);
        var run = new BackupRun
        {
            BackupId = backup.Id,
            Status = BackupRunStatus.Queued,
            Trigger = BackupTrigger.Manual,
            RequestedByUserId = actor.UserId,
            RequestedAt = Now,
        };
        db.BackupRuns.Add(run);
        await db.SaveChangesAsync(cancellationToken);
        Worker.Wake();
        var job = await RunModelAsync(run.Id, cancellationToken);
        events.Publish(job);
        return job;
    }

    public async Task<BackupJob> CancelRunAsync(Guid runId, CancellationToken cancellationToken)
    {
        guard.Admin();
        var run = await db.BackupRuns.SingleOrDefaultAsync(r => r.Id == runId, cancellationToken) ?? throw new ArcanistException.NotFound();
        if (run.Status is not (BackupRunStatus.Queued or BackupRunStatus.Running))
            throw new ArcanistException.Conflict(ErrorCodes.BackupNotActive);

        // The worker marks the one it is running when it stops; one still waiting is marked here.
        if (!Worker.Cancel(runId))
        {
            run.Status = BackupRunStatus.Cancelled;
            run.FinishedAt = Now;
            await db.SaveChangesAsync(cancellationToken);
        }
        else
        {
            for (var i = 0; i < 50 && await db.BackupRuns.AsNoTracking().AnyAsync(r => r.Id == runId && r.FinishedAt == null, cancellationToken); i++)
                await Task.Delay(100, cancellationToken);
        }

        var job = await RunModelAsync(runId, cancellationToken);
        events.Publish(job);
        return job;
    }

    /// <summary>The 20 latest runs of a backup.</summary>
    public async Task<IReadOnlyList<BackupJob>> RunsAsync(Guid backupId, CancellationToken cancellationToken)
    {
        guard.Admin();
        if (!await db.Backups.AnyAsync(b => b.Id == backupId, cancellationToken))
            throw new ArcanistException.NotFound();
        var ids = await db.BackupRuns.AsNoTracking().Where(r => r.BackupId == backupId).OrderByDescending(r => r.RequestedAt).Take(20)
            .Select(r => r.Id).ToListAsync(cancellationToken);
        return await RunModelsAsync(db, ids, cancellationToken);
    }

    public async Task<BackupJob> RunInfoAsync(Guid runId, CancellationToken cancellationToken)
    {
        guard.Admin();
        return await RunModelAsync(runId, cancellationToken);
    }

    private async Task<BackupJob> RunModelAsync(Guid runId, CancellationToken cancellationToken) =>
        (await RunModelsAsync(db, [runId], cancellationToken)).SingleOrDefault() ?? throw new ArcanistException.NotFound();

    internal static async Task<IReadOnlyList<BackupJob>> RunModelsAsync(ArcanistDbContext db, IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
    {
        var runs = await db.BackupRuns.AsNoTracking().Where(r => ids.Contains(r.Id))
            .Select(r => new { Run = r, r.Backup.Name })
            .ToListAsync(cancellationToken);
        var failures = await db.BackupRunFailures.AsNoTracking().Where(f => ids.Contains(f.RunId))
            .OrderBy(f => f.Path)
            .Select(f => new { f.RunId, f.Path, f.Code })
            .ToListAsync(cancellationToken);
        return runs.OrderByDescending(r => r.Run.RequestedAt)
            .Select(r => ToModel(r.Run, r.Name, failures.Where(f => f.RunId == r.Run.Id).Take(50).Select(f => new BackupJobFailure(f.Path, f.Code)).ToList()))
            .ToList();
    }

    internal static BackupJob ToModel(BackupRun run, string backupName, IReadOnlyList<BackupJobFailure> failures) => new(
        run.Id, run.BackupId, backupName, run.Status, run.Trigger, run.Snapshot, run.RequestedAt, run.StartedAt, run.FinishedAt,
        run.Files, run.Copied, run.Unchanged, run.Versioned, run.Skipped, run.Failed, run.Bytes, run.ErrorCode, failures);

    // ── Models ──────────────────────────────────────────────────────────────

    private async Task<IReadOnlyList<BackupDefinition>> ToModelsAsync(IReadOnlyList<Backup> backups, CancellationToken cancellationToken)
    {
        var backupIds = backups.Select(b => b.Id).ToList();
        var lastRuns = await db.BackupRuns.AsNoTracking()
            .Where(r => backupIds.Contains(r.BackupId))
            .GroupBy(r => r.BackupId)
            .Select(g => g.OrderByDescending(r => r.RequestedAt).Select(r => r.Id).First())
            .ToListAsync(cancellationToken);
        var runs = (await RunModelsAsync(db, lastRuns, cancellationToken)).ToDictionary(r => r.BackupId);
        var counts = await db.Backups.AsNoTracking().GroupBy(b => b.DestinationId).Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Key, g => g.Count, cancellationToken);

        var ids = backups.SelectMany(BackupPlanner.ScopeIds).Distinct().ToList();
        var names = new Dictionary<Guid, string>();
        foreach (var library in await db.Libraries.AsNoTracking().Where(l => ids.Contains(l.Id)).Select(l => new { l.Id, l.Name }).ToListAsync(cancellationToken))
            names[library.Id] = library.Name;
        foreach (var album in await db.Albums.AsNoTracking().Where(a => ids.Contains(a.Id)).Select(a => new { a.Id, a.Title }).ToListAsync(cancellationToken))
            names[album.Id] = album.Title;
        foreach (var playlist in await db.Playlists.AsNoTracking().Where(p => ids.Contains(p.Id)).Select(p => new { p.Id, p.Name }).ToListAsync(cancellationToken))
            names[playlist.Id] = playlist.Name;

        return backups.Select(b => new BackupDefinition(
                b.Id,
                b.Name,
                ToModel(b.Destination, counts.GetValueOrDefault(b.DestinationId)),
                b.Scope,
                BackupPlanner.ScopeIds(b).Where(names.ContainsKey).Select(id => new BackupScopeItem(id, names[id])).ToList(),
                b.IncludeUserData,
                b.IncludeServer,
                b.Schedule,
                b.IntervalHours,
                b.TimeOfDay,
                b.DayOfWeek,
                b.TimeZone,
                b.RetentionDays,
                b.Enabled,
                b.Folder,
                b.CreatedAt,
                b.NextRunAt,
                runs.GetValueOrDefault(b.Id)))
            .ToList();
    }

    private static BackupDestinationInfo ToModel(BackupDestination destination, int backups) => new(
        destination.Id,
        destination.Name,
        destination.Kind,
        destination.Kind == BackupDestinationKind.Peer ? destination.Name : destination.Location,
        backups,
        destination.CreatedAt);

    private DateTime Now => time.GetUtcNow().UtcDateTime;
}
