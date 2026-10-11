using System.Collections.Concurrent;
using System.Threading.Channels;
using Arcanist.Core.Application;
using ScanEvents = Arcanist.Core.Application.Scanning.ScanEvents;
using Arcanist.Core.Data;
using Arcanist.Core.Data.Entities;
using Arcanist.Core.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Arcanist.Core.Scanning;

/// <summary>
/// Runs one scan job: for each source, lists files, reads new and changed ones in parallel, writes them through
/// a single <see cref="CatalogWriter"/>, marks vanished files as missing and finally cleans the catalog.
/// <para>
/// A source that cannot be reached, or that suddenly looks empty while the catalog has files from it (an
/// unmounted share), is skipped without marking anything missing, so a NAS that is off does not empty the library.
/// </para>
/// </summary>
public sealed class LibraryScanner(
    IArcanistDbContextFactory databases,
    ILibraryStorageResolver storages,
    ArtworkStore artworkStore,
    ScanProgressTracker progress,
    ScanEvents events,
    IOptions<ScannerOptions> options,
    TimeProvider time,
    ILogger<LibraryScanner> logger)
{
    /// <summary>Modification times within this window are considered unchanged (FAT and SMB round them).</summary>
    private static readonly TimeSpan ModifiedTolerance = TimeSpan.FromSeconds(2);

    private const int UpdateChunk = 500;

    public async Task RunAsync(Guid jobId, CancellationToken cancellationToken)
    {
        await using var db = databases.CreateDbContext();
        var job = await db.ScanJobs.SingleOrDefaultAsync(j => j.Id == jobId, cancellationToken);
        if (job is not { Status: ScanStatus.Queued })
            return;

        var library = await db.Libraries.AsNoTracking().Include(l => l.Sources).SingleAsync(l => l.Id == job.LibraryId, cancellationToken);
        var counter = progress.Start(job.Id);

        job.Status = ScanStatus.Running;
        job.StartedAt = Now();
        await db.SaveChangesAsync(cancellationToken);
        events.Publish(job);
        logger.LogInformation("Scanning library {Library} ({Kind})", library.Name, job.Kind);

        try
        {
            await using var writerDb = databases.CreateDbContext();
            var writer = new CatalogWriter(writerDb, time);
            await writer.InitializeAsync(cancellationToken);

            foreach (var source in library.Sources)
                await ScanSourceAsync(job, source, writer, counter, cancellationToken);

            counter.Phase(ScanPhase.CleaningUp);
            await using (var cleanerDb = databases.CreateDbContext())
            {
                await new CatalogCleaner(cleanerDb, artworkStore, time)
                    .CleanAsync(TimeSpan.FromDays(options.Value.MissingFileRetentionDays), cancellationToken);
            }

            job.Status = ScanStatus.Completed;
            await db.Libraries.Where(l => l.Id == library.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(l => l.LastScanAt, Now()), CancellationToken.None);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            job.Status = ScanStatus.Cancelled;
        }
        catch (Exception e)
        {
            logger.LogError(e, "Scan of library {Library} failed", library.Name);
            job.Status = ScanStatus.Failed;
            job.ErrorCode ??= ErrorCodes.ScanFailed;
        }
        job.FinishedAt = Now();
        progress.Stop(job.Id);
        try
        {
            await db.SaveChangesAsync(CancellationToken.None);
        }
        catch (DbUpdateConcurrencyException)
        {
            // The library (and with it this job) was deleted while scanning.
        }

        events.Publish(job);

        logger.LogInformation(
            "Scan of {Library} {Status}: {Seen} files, {Added} added, {Updated} updated, {Missing} missing, {Errors} unreadable",
            library.Name, job.Status, job.FilesSeen, job.FilesAdded, job.FilesUpdated, job.FilesMissing, job.Errors);
    }

    private async Task ScanSourceAsync(
        ScanJob job, LibrarySource source, CatalogWriter writer, ScanProgressTracker.Counter counter, CancellationToken cancellationToken)
    {
        var storage = storages.For(source.Kind);
        var location = StorageLocation.From(source);
        counter.Phase(ScanPhase.Discovering);

        if (!(await storage.ProbeAsync(location, cancellationToken)).IsAvailable)
        {
            logger.LogWarning("Library source {Location} is not available; skipped", source.Location);
            job.ErrorCode = ErrorCodes.SourceUnavailable;
            return;
        }

        await using var db = databases.CreateDbContext();
        var known = await db.MediaFiles.AsNoTracking()
            .Where(f => f.LibrarySourceId == source.Id)
            .Select(f => new KnownFile(f.Id, f.RelativePath, f.Size, f.ModifiedAt, f.MissingSince))
            .ToDictionaryAsync(f => f.RelativePath, StringComparer.Ordinal, cancellationToken);

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var toRead = new List<(StorageEntry Entry, Guid? ExistingId)>();
        var reappeared = new List<Guid>();
        var folderArtwork = new Dictionary<string, (int Rank, string Path)>(StringComparer.Ordinal);

        try
        {
            await foreach (var entry in storage.EnumerateAsync(location, cancellationToken))
            {
                if (AudioFormats.ArtworkRank(entry.RelativePath) is { } rank)
                {
                    var folder = AudioFormats.Folder(entry.RelativePath);
                    if (!folderArtwork.TryGetValue(folder, out var best) || rank < best.Rank)
                        folderArtwork[folder] = (rank, entry.RelativePath);
                    continue;
                }

                if (!AudioFormats.IsAudio(entry.RelativePath))
                    continue;

                seen.Add(entry.RelativePath);
                counter.Found(1);

                if (known.TryGetValue(entry.RelativePath, out var existing)
                    && job.Kind == ScanKind.Incremental
                    && existing.Size == entry.Size
                    && (existing.ModifiedAt - entry.ModifiedAt).Duration() <= ModifiedTolerance)
                {
                    if (existing.MissingSince is not null)
                        reappeared.Add(existing.Id);
                    continue;
                }

                toRead.Add((entry, existing?.Id));
            }
        }
        catch (IOException e)
        {
            logger.LogWarning(e, "Listing {Location} failed; skipped", source.Location);
            job.ErrorCode = ErrorCodes.SourceUnavailable;
            return;
        }

        if (seen.Count == 0 && known.Count > 0)
        {
            logger.LogWarning("Library source {Location} looks empty but had {Count} files; treated as unavailable", source.Location, known.Count);
            job.ErrorCode = ErrorCodes.SourceUnavailable;
            return;
        }

        job.FilesSeen += seen.Count;
        counter.ToRead(toRead.Count);
        counter.Phase(ScanPhase.Reading);

        await ReadAndWriteAsync(job, source, location, storage, toRead, folderArtwork, writer, counter, cancellationToken);

        var now = Now();
        foreach (var chunk in reappeared.Chunk(UpdateChunk))
            await db.MediaFiles.Where(f => chunk.Contains(f.Id)).ExecuteUpdateAsync(s => s.SetProperty(f => f.MissingSince, (DateTime?)null), cancellationToken);

        var vanished = known.Values.Where(f => f.MissingSince is null && !seen.Contains(f.RelativePath)).Select(f => f.Id).ToList();
        foreach (var chunk in vanished.Chunk(UpdateChunk))
            await db.MediaFiles.Where(f => chunk.Contains(f.Id)).ExecuteUpdateAsync(s => s.SetProperty(f => f.MissingSince, now), cancellationToken);
        job.FilesMissing += vanished.Count;
    }

    private async Task ReadAndWriteAsync(
        ScanJob job,
        LibrarySource source,
        StorageLocation location,
        ILibraryStorage storage,
        List<(StorageEntry Entry, Guid? ExistingId)> toRead,
        Dictionary<string, (int Rank, string Path)> folderArtwork,
        CatalogWriter writer,
        ScanProgressTracker.Counter counter,
        CancellationToken cancellationToken)
    {
        var batchSize = Math.Max(1, options.Value.BatchSize);
        var channel = Channel.CreateBounded<ScannedFile>(batchSize * 2);
        var folderImages = new ConcurrentDictionary<string, Lazy<Task<StoredArtwork?>>>(StringComparer.Ordinal);
        var errors = 0;

        // If writing fails, readers blocked on a full channel must stop too.
        using var readCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var readToken = readCancellation.Token;

        var readers = Task.Run(async () =>
        {
            try
            {
                await Parallel.ForEachAsync(toRead,
                    new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, options.Value.Parallelism), CancellationToken = readToken },
                    async (item, ct) =>
                    {
                        var scanned = await ReadFileAsync(source, location, storage, item.Entry, item.ExistingId, folderArtwork, folderImages, ct);
                        if (scanned is null)
                        {
                            Interlocked.Increment(ref errors);
                            counter.Read(1);
                            return;
                        }

                        await channel.Writer.WriteAsync(scanned, ct);
                    });
            }
            finally
            {
                channel.Writer.Complete();
            }
        }, readToken);

        var batch = new List<ScannedFile>(batchSize);
        try
        {
            await foreach (var file in channel.Reader.ReadAllAsync(cancellationToken))
            {
                batch.Add(file);
                if (batch.Count >= batchSize)
                    await FlushAsync();
            }

            await FlushAsync();
        }
        catch
        {
            await readCancellation.CancelAsync();
            throw;
        }

        await readers;
        job.Errors += errors;

        async Task FlushAsync()
        {
            if (batch.Count == 0)
                return;

            var result = await writer.ApplyAsync(batch, cancellationToken);
            job.FilesAdded += result.Added;
            job.FilesUpdated += result.Updated;
            counter.Read(batch.Count);
            batch.Clear();
            events.Publish(job);
        }
    }

    private async Task<ScannedFile?> ReadFileAsync(
        LibrarySource source,
        StorageLocation location,
        ILibraryStorage storage,
        StorageEntry entry,
        Guid? existingId,
        Dictionary<string, (int Rank, string Path)> folderArtwork,
        ConcurrentDictionary<string, Lazy<Task<StoredArtwork?>>> folderImages,
        CancellationToken cancellationToken)
    {
        try
        {
            AudioMetadata metadata;
            string hash;
            await using (var stream = await storage.OpenReadAsync(location, entry.RelativePath, cancellationToken))
            {
                metadata = TagReader.Read(stream, AudioFormats.MimeType(entry.RelativePath));
                hash = await ContentHasher.HashAsync(stream, metadata.AudioDataOffset, metadata.AudioDataSize, cancellationToken);
            }

            var artwork = metadata.EmbeddedPicture is { } picture ? artworkStore.Store(picture) : null;
            if (artwork is null && folderArtwork.TryGetValue(AudioFormats.Folder(entry.RelativePath), out var image))
            {
                artwork = await folderImages.GetOrAdd(image.Path, path => new Lazy<Task<StoredArtwork?>>(
                    () => ReadFolderImageAsync(location, storage, path, cancellationToken))).Value;
            }

            return new ScannedFile(source.Id, existingId, entry.RelativePath, entry.Size, entry.ModifiedAt, hash,
                metadata with { EmbeddedPicture = null }, artwork);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            logger.LogWarning(e, "Could not read {Path} in {Location}", entry.RelativePath, location.Location);
            return null;
        }
    }

    private async Task<StoredArtwork?> ReadFolderImageAsync(StorageLocation location, ILibraryStorage storage, string path, CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = await storage.OpenReadAsync(location, path, cancellationToken);
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, cancellationToken);
            return artworkStore.Store(buffer.ToArray());
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            logger.LogWarning(e, "Could not read folder image {Path}", path);
            return null;
        }
    }

    private DateTime Now() => time.GetUtcNow().UtcDateTime;

    private sealed record KnownFile(Guid Id, string RelativePath, long Size, DateTime ModifiedAt, DateTime? MissingSince);
}
