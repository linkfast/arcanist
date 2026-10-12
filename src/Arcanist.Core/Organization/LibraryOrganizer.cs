using Arcanist.Core.Application;
using Arcanist.Core.Data;
using Arcanist.Core.Data.Entities;
using Arcanist.Core.Scanning;
using Arcanist.Core.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Arcanist.Core.Organization;

/// <summary>Bound from <c>Arcanist:Organization</c>.</summary>
public sealed class OrganizationOptions
{
    public const string Section = "Arcanist:Organization";

    /// <summary>
    /// Seconds a file must stay unchanged before it is imported or organized automatically: a file still being
    /// copied keeps changing.
    /// </summary>
    public int StableSeconds { get; set; } = 30;

    /// <summary>How often drop folders and watched libraries are listed, for changes watching missed.</summary>
    public int PollSeconds { get; set; } = 60;

    /// <summary>Days removed files stay in the trash of their location before being deleted.</summary>
    public int TrashRetentionDays { get; set; } = 30;
}

/// <summary>A file to move: an audio file, or something that goes with it (lyrics, cue sheet, artwork, booklet…).</summary>
public sealed record PlannedFileMove(Guid SourceId, string From, string To, bool IsCompanion);

public sealed record OrganizePlan(IReadOnlyList<PlannedFileMove> Moves, int InPlace, IReadOnlyList<Guid> UnavailableSources);

/// <summary>
/// Moves a library's files to the paths its template gives them. Lyrics and cue sheets follow their audio; a
/// folder's other files (artwork, booklets, logs) follow when all of its audio goes to one folder. Every move is
/// recorded as it happens, so a run can be undone even if it was interrupted.
/// </summary>
public sealed class LibraryOrganizer(
    IArcanistDbContextFactory databases,
    ILibraryStorageResolver storages,
    LibraryLocks locks,
    IOptions<OrganizationOptions> options,
    TimeProvider time,
    ILogger<LibraryOrganizer> logger)
{
    private const int SaveEvery = 50;

    /// <summary>The moves organizing would make. <paramref name="addedSince"/> limits it to files added since then.</summary>
    public async Task<OrganizePlan> PlanAsync(Guid libraryId, PathTemplate template, DateTime? addedSince, CancellationToken cancellationToken)
    {
        await using var db = databases.CreateDbContext();
        var sources = await db.LibrarySources.AsNoTracking().Where(s => s.LibraryId == libraryId).OrderBy(s => s.Location).ToListAsync(cancellationToken);

        var moves = new List<PlannedFileMove>();
        var unavailable = new List<Guid>();
        var inPlace = 0;
        foreach (var source in sources)
        {
            var storage = storages.For(source.Kind);
            var location = StorageLocation.From(source);
            if (!(await storage.ProbeAsync(location, cancellationToken)).IsAvailable)
            {
                unavailable.Add(source.Id);
                continue;
            }

            var (sourceMoves, sourceInPlace) = await PlanSourceAsync(db, source, storage, location, template, addedSince, cancellationToken);
            moves.AddRange(sourceMoves);
            inPlace += sourceInPlace;
        }

        return new OrganizePlan(moves, inPlace, unavailable);
    }

    private async Task<(List<PlannedFileMove> Moves, int InPlace)> PlanSourceAsync(
        ArcanistDbContext db, LibrarySource source, ILibraryStorage storage, StorageLocation location, PathTemplate template,
        DateTime? addedSince, CancellationToken cancellationToken)
    {
        var query = db.MediaFiles.AsNoTracking().Where(f => f.LibrarySourceId == source.Id && f.MissingSince == null);
        if (addedSince is { } since)
        {
            // A file still being copied is left for the next time.
            var settled = time.GetUtcNow().UtcDateTime.AddSeconds(-options.Value.StableSeconds);
            query = query.Where(f => f.AddedAt >= since && f.ModifiedAt <= settled);
        }

        var rows = await query
            .Select(f => new FileRow(
                f.RelativePath,
                f.CueTrack,
                f.Track.Title,
                f.Track.DiscNumber,
                f.Track.TrackNumber,
                f.Track.Album.Title,
                f.Track.Album.Year,
                f.Track.Album.AlbumArtist.Name,
                f.Track.Album.Tracks.Any(t => t.DiscNumber > 1),
                f.Track.Artists.Where(a => a.Role == ArtistRole.Primary).OrderBy(a => a.Position).Select(a => a.Artist.Name).ToList(),
                f.Track.Genres.Select(g => g.Genre.Name).OrderBy(name => name).FirstOrDefault()))
            .ToListAsync(cancellationToken);
        if (rows.Count == 0)
            return ([], 0);

        var comparer = storage.PathComparer;
        var entries = new List<string>();
        await foreach (var entry in storage.EnumerateAsync(location, cancellationToken))
            entries.Add(entry.RelativePath);

        var occupied = new HashSet<string>(entries, comparer);
        var audio = entries.Where(AudioFormats.IsAudio).ToHashSet(comparer);
        var byStem = entries.GroupBy(e => AudioFormats.WithoutExtension(e), comparer).ToDictionary(g => g.Key, g => g.ToList(), comparer);

        var moves = new List<PlannedFileMove>();
        var claimed = new HashSet<string>(comparer);
        // The folder each audio file ends up in, to decide whether the other files of its folder follow it. Files
        // the catalog does not know (yet) stay, and keep their folder's other files with them.
        var destinations = audio.ToDictionary(a => a, _ => (string?)null, comparer);
        var inPlace = 0;

        foreach (var file in rows.GroupBy(r => r.RelativePath, StringComparer.Ordinal).Select(g => g.OrderBy(r => r.CueTrack).First()))
        {
            if (!audio.Contains(file.RelativePath))
                continue;

            var isCue = file.CueTrack > 0;
            var values = new PathValues(file.AlbumArtist, string.Join(", ", file.Artists), file.Album, file.Year, file.Disc, file.MultiDisc,
                file.Track, isCue ? file.Album : file.Title, file.Genre);
            var extension = Path.GetExtension(file.RelativePath);
            // A file split by a cue sheet is a whole album: it keeps its name, which the cue sheet refers to.
            var wanted = isCue
                ? Join(template.RenderFolder(values), Path.GetFileNameWithoutExtension(file.RelativePath))
                : template.Render(values);

            var target = Choose(wanted, extension, file.RelativePath, occupied, comparer);
            if (target is null)
            {
                inPlace++;
                destinations[file.RelativePath] = AudioFormats.Folder(file.RelativePath);
                continue;
            }

            occupied.Add(target);
            claimed.Add(file.RelativePath);
            destinations[file.RelativePath] = AudioFormats.Folder(target);
            moves.Add(new PlannedFileMove(source.Id, file.RelativePath, target, IsCompanion: false));

            // Lyrics named like the audio, and the cue sheet of an album file.
            var stem = AudioFormats.WithoutExtension(file.RelativePath);
            foreach (var sidecar in byStem.GetValueOrDefault(stem, []).Where(e => !comparer.Equals(e, file.RelativePath)))
            {
                if (!AudioFormats.IsLyrics(sidecar) && !(isCue && AudioFormats.IsCueSheet(sidecar)))
                    continue;

                var sidecarTarget = AudioFormats.WithoutExtension(target) + Path.GetExtension(sidecar);
                if (claimed.Add(sidecar) && occupied.Add(sidecarTarget))
                    moves.Add(new PlannedFileMove(source.Id, sidecar, sidecarTarget, IsCompanion: true));
            }
        }

        foreach (var (from, to) in Companions.Plan(entries, destinations, claimed, comparer))
        {
            // Never over something already there; the file then stays with whatever audio is left.
            if (occupied.Add(to))
                moves.Add(new PlannedFileMove(source.Id, from, to, IsCompanion: true));
        }

        return (moves, inPlace);
    }

    /// <summary>
    /// The free path for a file, adding " (2)", " (3)"… when taken; null when the file is already where it goes
    /// (also with a suffix, so organizing twice moves nothing).
    /// </summary>
    internal static string? Choose(string wanted, string extension, string current, HashSet<string> occupied, StringComparer comparer)
    {
        for (var copy = 1; ; copy++)
        {
            var candidate = copy == 1 ? wanted + extension : $"{wanted} ({copy}){extension}";
            if (comparer.Equals(candidate, current))
                return null;
            if (!occupied.Contains(candidate))
                return candidate;
        }
    }

    private static string Join(string folder, string name) => folder.Length == 0 ? name : $"{folder}/{name}";

    /// <summary>
    /// Organizes the library now. Returns the run, or null when an automatic run found nothing to move (no run is
    /// recorded then). <paramref name="runId"/> is a run created beforehand to report progress on.
    /// </summary>
    public async Task<Guid?> RunAsync(Guid libraryId, OrganizeRunKind kind, Guid? runId, CancellationToken cancellationToken)
    {
        using var writing = await locks.AcquireForWritingAsync(libraryId, cancellationToken);
        await using var db = databases.CreateDbContext();
        var library = await db.Libraries.AsNoTracking().SingleOrDefaultAsync(l => l.Id == libraryId, cancellationToken);
        if (library is null)
            return null;

        var template = PathTemplate.TryParse(library.OrganizeTemplate, out var parsed, out _) ? parsed : PathTemplate.DefaultTemplate;
        var addedSince = kind == OrganizeRunKind.Automatic ? library.AutoOrganizeSince ?? DateTime.MaxValue : (DateTime?)null;
        var plan = await PlanAsync(libraryId, template, addedSince, cancellationToken);

        OrganizeRun run;
        if (runId is { } id)
            run = await db.OrganizeRuns.SingleAsync(r => r.Id == id, cancellationToken);
        else if (plan.Moves.Count == 0)
            return null;
        else
        {
            run = new OrganizeRun { LibraryId = libraryId, Kind = kind, Status = OrganizeRunStatus.Running, Template = template.Text, StartedAt = Now() };
            db.OrganizeRuns.Add(run);
        }

        run.Total = plan.Moves.Count;
        if (plan.UnavailableSources.Count > 0)
            run.ErrorCode = ErrorCodes.SourceUnavailable;
        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Organizing library {Library}: {Count} files to move", library.Name, plan.Moves.Count);

        var sources = await db.LibrarySources.AsNoTracking().Where(s => s.LibraryId == libraryId).ToDictionaryAsync(s => s.Id, cancellationToken);
        var vacated = new HashSet<(Guid Source, string Folder)>();
        var sequence = 0;
        try
        {
            foreach (var move in plan.Moves)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var source = sources[move.SourceId];
                try
                {
                    await storages.For(source.Kind).MoveAsync(StorageLocation.From(source), move.From, move.To, cancellationToken);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    logger.LogWarning("Could not move {From} to {To}: {Error}", move.From, move.To, e.Message);
                    run.Failed++;
                    continue;
                }

                // Recorded at once: if the server stops now, the move can still be undone.
                db.OrganizeMoves.Add(new OrganizeMove
                {
                    RunId = run.Id, Sequence = ++sequence, LibrarySourceId = source.Id, FromPath = move.From, ToPath = move.To,
                });
                await RepointAsync(db, source.Id, move.From, move.To, cancellationToken);
                run.Moved++;
                vacated.Add((source.Id, AudioFormats.Folder(move.From)));
                if (sequence % SaveEvery == 0)
                    await db.SaveChangesAsync(cancellationToken);
            }

            await db.SaveChangesAsync(cancellationToken);
            await RemoveEmptyFoldersAsync(sources, vacated, cancellationToken);
            run.Status = OrganizeRunStatus.Completed;
        }
        catch (OperationCanceledException)
        {
            run.Status = OrganizeRunStatus.Failed;
            run.ErrorCode = ErrorCodes.Interrupted;
        }
        catch (Exception e)
        {
            logger.LogError(e, "Organizing library {Library} failed", library.Name);
            run.Status = OrganizeRunStatus.Failed;
            run.ErrorCode ??= ErrorCodes.WriteFailed;
        }

        run.FinishedAt = Now();
        await db.SaveChangesAsync(CancellationToken.None);
        logger.LogInformation("Organized library {Library}: {Moved} moved, {Failed} failed", library.Name, run.Moved, run.Failed);
        return run.Id;
    }

    /// <summary>After a scan completes, moves the files it added to a library that organizes itself.</summary>
    public async Task AfterScanAsync(Guid jobId, CancellationToken cancellationToken)
    {
        await using var db = databases.CreateDbContext();
        var library = await db.ScanJobs.AsNoTracking()
            .Where(j => j.Id == jobId && j.Status == ScanStatus.Completed && j.Library.AutoOrganize)
            .Select(j => (Guid?)j.LibraryId)
            .SingleOrDefaultAsync(cancellationToken);

        if (library is { } libraryId)
            await RunAsync(libraryId, OrganizeRunKind.Automatic, runId: null, cancellationToken);
    }

    /// <summary>Moves a run's files back, newest first. Files moved again since, or whose old place is taken, stay.</summary>
    public async Task UndoAsync(Guid runId, CancellationToken cancellationToken)
    {
        await using var db = databases.CreateDbContext();
        var run = await db.OrganizeRuns.SingleOrDefaultAsync(r => r.Id == runId, cancellationToken);
        if (run is null)
            return;

        using var writing = await locks.AcquireForWritingAsync(run.LibraryId, cancellationToken);
        var sources = await db.LibrarySources.AsNoTracking().Where(s => s.LibraryId == run.LibraryId).ToDictionaryAsync(s => s.Id, cancellationToken);
        var moves = await db.OrganizeMoves.Where(m => m.RunId == runId && !m.Undone).OrderByDescending(m => m.Sequence).ToListAsync(cancellationToken);
        var vacated = new HashSet<(Guid Source, string Folder)>();
        var skipped = 0;

        foreach (var move in moves)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!sources.TryGetValue(move.LibrarySourceId, out var source))
            {
                skipped++;
                continue;
            }

            var storage = storages.For(source.Kind);
            var location = StorageLocation.From(source);
            try
            {
                if (!await storage.ExistsAsync(location, move.ToPath, cancellationToken) || await storage.ExistsAsync(location, move.FromPath, cancellationToken))
                {
                    skipped++;
                    continue;
                }

                await storage.MoveAsync(location, move.ToPath, move.FromPath, cancellationToken);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning("Could not move {From} back to {To}: {Error}", move.ToPath, move.FromPath, e.Message);
                skipped++;
                continue;
            }

            move.Undone = true;
            await RepointAsync(db, source.Id, move.ToPath, move.FromPath, cancellationToken);
            vacated.Add((source.Id, AudioFormats.Folder(move.ToPath)));
            await db.SaveChangesAsync(cancellationToken);
        }

        await RemoveEmptyFoldersAsync(sources, vacated, cancellationToken);
        run.Status = OrganizeRunStatus.Undone;
        run.UndoneAt = Now();
        run.Failed = skipped;
        await db.SaveChangesAsync(CancellationToken.None);
        logger.LogInformation("Undid organizing run {Run}: {Count} moved back, {Skipped} left", runId, moves.Count - skipped, skipped);
    }

    /// <summary>Points the catalog rows of a moved file at its new path (several rows when a cue sheet splits it).</summary>
    private static async Task RepointAsync(ArcanistDbContext db, Guid sourceId, string from, string to, CancellationToken cancellationToken)
    {
        // The move succeeded, so no file was at the new path: a row still pointing there is of a file that vanished
        // since the last scan, and would collide with the moved one.
        await db.MediaFiles.Where(f => f.LibrarySourceId == sourceId && f.RelativePath == to)
            .ExecuteDeleteAsync(cancellationToken);
        await db.MediaFiles.Where(f => f.LibrarySourceId == sourceId && f.RelativePath == from)
            .ExecuteUpdateAsync(s => s.SetProperty(f => f.RelativePath, to), cancellationToken);
    }

    private async Task RemoveEmptyFoldersAsync(
        Dictionary<Guid, LibrarySource> sources, HashSet<(Guid Source, string Folder)> folders, CancellationToken cancellationToken)
    {
        // Deepest first, so a parent is looked at once its subfolders are gone.
        foreach (var (sourceId, folder) in folders.OrderByDescending(f => f.Folder.Length))
        {
            var source = sources[sourceId];
            try
            {
                await storages.For(source.Kind).RemoveEmptyFoldersAsync(StorageLocation.From(source), folder, cancellationToken);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                logger.LogDebug(e, "Could not remove empty folder {Folder}", folder);
            }
        }
    }

    private DateTime Now() => time.GetUtcNow().UtcDateTime;

    private sealed record FileRow(
        string RelativePath,
        int CueTrack,
        string Title,
        int? Disc,
        int? Track,
        string Album,
        int? Year,
        string AlbumArtist,
        bool MultiDisc,
        List<string> Artists,
        string? Genre);
}
