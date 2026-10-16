using System.Text.Json;
using Arcanist.Core.Application;
using Arcanist.Core.Backups.Targets;
using Arcanist.Core.Data;
using Arcanist.Core.Data.Entities;
using Arcanist.Core.Scanning;
using Arcanist.Core.Storage;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace Arcanist.Core.Backups;

/// <summary>Opens a destination for writing or reading.</summary>
internal sealed class BackupTargets(IServiceProvider services)
{
    public IBackupTarget Open(BackupDestination destination) => destination.Kind switch
    {
        BackupDestinationKind.Folder => new FolderTarget(destination.Location),
        BackupDestinationKind.S3 => services.GetRequiredService<S3BackupTargets>().Open(destination),
        BackupDestinationKind.Peer => services.GetRequiredService<PeerBackupTargets>().Open(destination),
        _ => throw new ArcanistException.Conflict(ErrorCodes.BackupDestinationUnavailable),
    };
}

/// <summary>Turns a backup's definition into what a run copies.</summary>
internal sealed class BackupPlanner(ILibraryStorageResolver storages, IDataProtectionProvider protection)
{
    /// <summary>Library folders backups can read: those on this server or in S3 (not other servers' nor Ultimate Cloud's).</summary>
    public static bool IsBackedUp(LibrarySourceKind kind) => kind is LibrarySourceKind.Local or LibrarySourceKind.S3;

    public async Task<BackupPlan> PlanAsync(ArcanistDbContext db, Backup backup, CancellationToken cancellationToken)
    {
        var ids = ScopeIds(backup);
        var sources = await db.LibrarySources.AsNoTracking().Include(s => s.Library)
            .Where(s => s.Library.OwnerUserId == null && (s.Kind == LibrarySourceKind.Local || s.Kind == LibrarySourceKind.S3))
            .OrderBy(s => s.Library.Name).ThenBy(s => s.Id)
            .ToListAsync(cancellationToken);

        Dictionary<Guid, Func<string, bool>>? filters = null;
        switch (backup.Scope)
        {
            case BackupScope.Library:
                sources = sources.Where(s => ids.Contains(s.LibraryId)).ToList();
                break;
            case BackupScope.Album:
                filters = await AlbumFiltersAsync(db, ids, cancellationToken);
                sources = sources.Where(s => filters.ContainsKey(s.Id)).ToList();
                break;
            case BackupScope.Playlist:
                filters = await PlaylistFiltersAsync(db, ids, cancellationToken);
                sources = sources.Where(s => filters.ContainsKey(s.Id)).ToList();
                break;
        }

        var sourceIds = sources.Select(s => s.Id).ToList();
        var hashes = (await db.MediaFiles.AsNoTracking()
                .Where(f => sourceIds.Contains(f.LibrarySourceId) && f.CueTrack == 0 && f.MissingSince == null)
                .Select(f => new { f.LibrarySourceId, f.RelativePath, f.ContentHash })
                .ToListAsync(cancellationToken))
            .DistinctBy(f => (f.LibrarySourceId, f.RelativePath))
            .ToDictionary(f => (f.LibrarySourceId, f.RelativePath), f => f.ContentHash);

        return new BackupPlan(
            backup.Id,
            backup.Name,
            backup.Folder,
            backup.Scope.ToString().ToUpperInvariant(),
            backup.RetentionDays,
            sources.Select(s => new BackupPlanSource(s.Id, s.LibraryId, s.Library.Name, storages.For(s.Kind), StorageLocation.From(s),
                filters?.GetValueOrDefault(s.Id) is { } filter ? new PathFilter(filter) : null)).ToList(),
            hashes)
        {
            ScopeKind = backup.Scope,
            ScopeIds = ids,
            IncludeUserData = backup.IncludeUserData,
            ServerPassword = backup.IncludeServer && backup.ProtectedPassword is { } password
                ? protection.CreateProtector(Application.Backups.BackupService.PasswordPurpose).Unprotect(password)
                : null,
        };
    }

    public static IReadOnlyList<Guid> ScopeIds(Backup backup) =>
        JsonSerializer.Deserialize<List<Guid>>(backup.ScopeIds, JsonSerializerOptions.Web) ?? [];

    /// <summary>An album's files: every present copy of its tracks, and what else is in their folders (artwork, booklets, logs, lyrics, cue sheets).</summary>
    private static async Task<Dictionary<Guid, Func<string, bool>>> AlbumFiltersAsync(ArcanistDbContext db, IReadOnlyList<Guid> albumIds, CancellationToken cancellationToken)
    {
        var files = await db.MediaFiles.AsNoTracking()
            .Where(f => albumIds.Contains(f.Track.AlbumId) && f.MissingSince == null)
            .Select(f => new { f.LibrarySourceId, f.RelativePath })
            .ToListAsync(cancellationToken);
        return files.GroupBy(f => f.LibrarySourceId).ToDictionary(g => g.Key, g =>
        {
            var audio = g.Select(f => f.RelativePath).ToHashSet(StringComparer.Ordinal);
            var folders = audio.Select(BackupPaths.Folder).ToHashSet(StringComparer.Ordinal);
            return (Func<string, bool>)(path => audio.Contains(path) || (!AudioFormats.IsAudio(path) && folders.Contains(BackupPaths.Folder(path))));
        });
    }

    /// <summary>A playlist's files: the best copy of each of its tracks, their lyrics and cue sheets, and their folders' artwork.</summary>
    private static async Task<Dictionary<Guid, Func<string, bool>>> PlaylistFiltersAsync(ArcanistDbContext db, IReadOnlyList<Guid> playlistIds, CancellationToken cancellationToken)
    {
        var trackIds = await db.PlaylistEntries.AsNoTracking()
            .Where(e => playlistIds.Contains(e.PlaylistId))
            .Select(e => e.TrackId)
            .Distinct()
            .ToListAsync(cancellationToken);
        var files = await db.MediaFiles.AsNoTracking()
            .Where(f => trackIds.Contains(f.TrackId) && f.MissingSince == null
                        && (f.LibrarySource.Kind == LibrarySourceKind.Local || f.LibrarySource.Kind == LibrarySourceKind.S3)
                        && f.LibrarySource.Library.OwnerUserId == null)
            .Select(f => new { f.TrackId, f.LibrarySourceId, f.RelativePath, f.BitDepth, f.SampleRate, f.BitRate })
            .ToListAsync(cancellationToken);
        var best = files.GroupBy(f => f.TrackId)
            .Select(g => g.OrderByDescending(f => f.BitDepth ?? 0).ThenByDescending(f => f.SampleRate ?? 0).ThenByDescending(f => f.BitRate ?? 0).First());
        return best.GroupBy(f => f.LibrarySourceId).ToDictionary(g => g.Key, g =>
        {
            var audio = g.Select(f => f.RelativePath).ToHashSet(StringComparer.Ordinal);
            var stems = audio.Select(AudioFormats.WithoutExtension).ToHashSet(StringComparer.Ordinal);
            var folders = audio.Select(BackupPaths.Folder).ToHashSet(StringComparer.Ordinal);
            return (Func<string, bool>)(path =>
                audio.Contains(path)
                || ((AudioFormats.IsLyrics(path) || AudioFormats.IsCueSheet(path)) && stems.Contains(AudioFormats.WithoutExtension(path)))
                || (AudioFormats.ArtworkRank(path) is not null && folders.Contains(BackupPaths.Folder(path))));
        });
    }

    /// <summary>A set-like view over a predicate, for <see cref="BackupPlanSource.Paths"/>.</summary>
    private sealed class PathFilter(Func<string, bool> include) : IReadOnlySet<string>
    {
        public bool Contains(string item) => include(item);

        public int Count => throw new NotSupportedException();
        public IEnumerator<string> GetEnumerator() => throw new NotSupportedException();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
        public bool IsProperSubsetOf(IEnumerable<string> other) => throw new NotSupportedException();
        public bool IsProperSupersetOf(IEnumerable<string> other) => throw new NotSupportedException();
        public bool IsSubsetOf(IEnumerable<string> other) => throw new NotSupportedException();
        public bool IsSupersetOf(IEnumerable<string> other) => throw new NotSupportedException();
        public bool Overlaps(IEnumerable<string> other) => throw new NotSupportedException();
        public bool SetEquals(IEnumerable<string> other) => throw new NotSupportedException();
    }
}

/// <summary>When a scheduled backup runs next.</summary>
internal static class BackupSchedules
{
    /// <summary>The first time after <paramref name="afterUtc"/> it is due, or null when it only runs by hand.</summary>
    public static DateTime? Next(Backup backup, DateTime afterUtc)
    {
        if (!backup.Enabled)
            return null;
        switch (backup.Schedule)
        {
            case BackupSchedule.Hours when backup.IntervalHours is > 0 and var hours:
                return afterUtc.AddHours(hours);
            case BackupSchedule.Daily or BackupSchedule.Weekly when backup.TimeOfDay is { } minutes:
            {
                var zone = Zone(backup.TimeZone);
                var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(afterUtc, DateTimeKind.Utc), zone);
                for (var day = 0; day <= 8; day++)
                {
                    var date = local.Date.AddDays(day);
                    if (backup.Schedule == BackupSchedule.Weekly && date.DayOfWeek != backup.DayOfWeek)
                        continue;
                    var candidate = date.AddMinutes(minutes);
                    // A time skipped by a daylight saving change happens an hour later that day.
                    if (zone.IsInvalidTime(candidate))
                        candidate = candidate.AddHours(1);
                    var utc = TimeZoneInfo.ConvertTimeToUtc(candidate, zone);
                    if (utc > afterUtc)
                        return utc;
                }

                return null;
            }
            default:
                return null;
        }
    }

    public static TimeZoneInfo Zone(string id)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return TimeZoneInfo.Utc;
        }
    }

    public static bool IsZone(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return false;
        try
        {
            TimeZoneInfo.FindSystemTimeZoneById(id);
            return true;
        }
        catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return false;
        }
    }
}
