using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Channels;
using Arcanist.Core.Application;
using ScanEvents = Arcanist.Core.Application.Scanning.ScanEvents;
using Arcanist.Core.Data;
using Arcanist.Core.Data.Entities;
using Arcanist.Core.Data.Search;
using Arcanist.Core.Organization;
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
    LibraryLocks locks,
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

        // The job stays queued while the library's files are being organized or imported.
        IDisposable libraryLock;
        try
        {
            libraryLock = await locks.AcquireAsync(library.Id, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            job.Status = ScanStatus.Cancelled;
            job.FinishedAt = Now();
            await db.SaveChangesAsync(CancellationToken.None);
            events.Publish(job);
            return;
        }

        using var _ = libraryLock;
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
        // Several rows per path when a cue sheet splits the file into tracks.
        var known = (await db.MediaFiles.AsNoTracking()
                .Where(f => f.LibrarySourceId == source.Id)
                .Select(f => new KnownFile(f.Id, f.RelativePath, f.CueTrack, f.Size, f.ModifiedAt, f.MissingSince, f.ScannedAt))
                .ToListAsync(cancellationToken))
            .GroupBy(f => f.RelativePath, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var toRead = new List<(StorageEntry Entry, List<KnownFile>? Known)>();
        var reappeared = new List<Guid>();
        var folderArtwork = new Dictionary<string, (int Rank, string Path)>(StringComparer.Ordinal);
        // Sidecar lyrics by the path of their audio without extension ("Album/01 Song.lrc" goes with "Album/01 Song.flac").
        var sidecars = new Dictionary<string, StorageEntry>(StringComparer.Ordinal);
        var cueFiles = new List<StorageEntry>();
        var unchanged = new List<(StorageEntry Entry, List<KnownFile> Known)>();

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

                if (AudioFormats.IsLyrics(entry.RelativePath))
                {
                    sidecars[AudioFormats.WithoutExtension(entry.RelativePath)] = entry;
                    continue;
                }

                if (AudioFormats.IsCueSheet(entry.RelativePath))
                {
                    cueFiles.Add(entry);
                    continue;
                }

                if (!AudioFormats.IsAudio(entry.RelativePath))
                    continue;

                seen.Add(entry.RelativePath);
                counter.Found(1);

                // Every row of a path describes the same physical file.
                if (known.TryGetValue(entry.RelativePath, out var existing)
                    && job.Kind == ScanKind.Incremental
                    && existing[0].Size == entry.Size
                    && (existing[0].ModifiedAt - entry.ModifiedAt).Duration() <= ModifiedTolerance)
                {
                    reappeared.AddRange(existing.Where(f => f.MissingSince is not null).Select(f => f.Id));
                    unchanged.Add((entry, existing));
                    continue;
                }

                toRead.Add((entry, existing));
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

        var cues = await PairCueSheetsAsync(location, storage, cueFiles, seen, logger, cancellationToken);

        // Sidecars added, edited or removed next to an unchanged file: read it again to pick the change up.
        foreach (var (entry, existing) in unchanged)
        {
            var scannedAt = existing.Min(f => f.ScannedAt);
            var lyricsChanged = sidecars.TryGetValue(AudioFormats.WithoutExtension(entry.RelativePath), out var lyrics) && lyrics.ModifiedAt > scannedAt;
            var hadCue = existing.Any(f => f.CueTrack > 0);
            var cueChanged = cues.TryGetValue(entry.RelativePath, out var cue) ? !hadCue || cue.Entry.ModifiedAt > scannedAt : hadCue;
            if (lyricsChanged || cueChanged)
                toRead.Add((entry, existing));
        }

        job.FilesSeen += seen.Count;
        counter.ToRead(toRead.Count);
        counter.Phase(ScanPhase.Reading);

        await ReadAndWriteAsync(job, source, location, storage, toRead, folderArtwork, sidecars, cues, writer, counter, cancellationToken);

        var now = Now();
        foreach (var chunk in reappeared.Chunk(UpdateChunk))
            await db.MediaFiles.Where(f => chunk.Contains(f.Id)).ExecuteUpdateAsync(s => s.SetProperty(f => f.MissingSince, (DateTime?)null), cancellationToken);

        var vanished = known.Values.SelectMany(f => f).Where(f => f.MissingSince is null && !seen.Contains(f.RelativePath)).Select(f => f.Id).ToList();
        foreach (var chunk in vanished.Chunk(UpdateChunk))
            await db.MediaFiles.Where(f => chunk.Contains(f.Id)).ExecuteUpdateAsync(s => s.SetProperty(f => f.MissingSince, now), cancellationToken);
        job.FilesMissing += vanished.Count;
    }

    private async Task ReadAndWriteAsync(
        ScanJob job,
        LibrarySource source,
        StorageLocation location,
        ILibraryStorage storage,
        List<(StorageEntry Entry, List<KnownFile>? Known)> toRead,
        Dictionary<string, (int Rank, string Path)> folderArtwork,
        Dictionary<string, StorageEntry> sidecars,
        Dictionary<string, (CueSheet Sheet, StorageEntry Entry)> cues,
        CatalogWriter writer,
        ScanProgressTracker.Counter counter,
        CancellationToken cancellationToken)
    {
        var batchSize = Math.Max(1, options.Value.BatchSize);
        // One item per file read: one entry, or one per track of a cue sheet, which stay together in a batch.
        var channel = Channel.CreateBounded<IReadOnlyList<ScannedFile>>(batchSize * 2);
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
                        var scanned = await ReadFileAsync(source, location, storage, item.Entry, item.Known, folderArtwork, folderImages, sidecars, cues, ct);
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
        var filesInBatch = 0;
        try
        {
            await foreach (var group in channel.Reader.ReadAllAsync(cancellationToken))
            {
                batch.AddRange(group);
                filesInBatch++;
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
            // Searchable as soon as written, not only once the whole scan is done.
            await using (var indexDb = databases.CreateDbContext())
                await SearchIndexer.RefreshAsync(indexDb, cancellationToken);
            job.FilesAdded += result.Added;
            job.FilesUpdated += result.Updated;
            counter.Read(filesInBatch);
            batch.Clear();
            filesInBatch = 0;
            events.Publish(job);
        }
    }

    private async Task<IReadOnlyList<ScannedFile>?> ReadFileAsync(
        LibrarySource source,
        StorageLocation location,
        ILibraryStorage storage,
        StorageEntry entry,
        List<KnownFile>? known,
        Dictionary<string, (int Rank, string Path)> folderArtwork,
        ConcurrentDictionary<string, Lazy<Task<StoredArtwork?>>> folderImages,
        Dictionary<string, StorageEntry> sidecars,
        Dictionary<string, (CueSheet Sheet, StorageEntry Entry)> cues,
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

            if (sidecars.TryGetValue(AudioFormats.WithoutExtension(entry.RelativePath), out var lyrics))
                metadata = await WithSidecarLyricsAsync(metadata, location, storage, lyrics, cancellationToken);

            var artwork = metadata.EmbeddedPicture is { } picture ? artworkStore.Store(picture) : null;
            if (artwork is null && folderArtwork.TryGetValue(AudioFormats.Folder(entry.RelativePath), out var image))
            {
                artwork = await folderImages.GetOrAdd(image.Path, path => new Lazy<Task<StoredArtwork?>>(
                    () => ReadFolderImageAsync(location, storage, path, cancellationToken))).Value;
            }

            metadata = metadata with { EmbeddedPicture = null };
            Guid? Existing(int cueTrack) => known?.FirstOrDefault(f => f.CueTrack == cueTrack)?.Id;

            if (cues.TryGetValue(entry.RelativePath, out var cue))
            {
                return CueTracks(cue.Sheet, metadata, hash)
                    .Select(t => new ScannedFile(source.Id, Existing(t.Number), entry.RelativePath, entry.Size, entry.ModifiedAt, t.Hash,
                        t.Metadata, artwork, t.Number, t.StartMs, t.EndMs))
                    .ToList();
            }

            return [new ScannedFile(source.Id, Existing(0), entry.RelativePath, entry.Size, entry.ModifiedAt, hash, metadata, artwork)];
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            logger.LogWarning(e, "Could not read {Path} in {Location}", entry.RelativePath, location.Location);
            return null;
        }
    }

    /// <summary>
    /// The tracks of an album stored as one file: the file's own tags for what the cue sheet does not say, a
    /// content hash per track derived from the file's, and each track running until the next one starts.
    /// </summary>
    private static IEnumerable<(int Number, int StartMs, int EndMs, string Hash, AudioMetadata Metadata)> CueTracks(
        CueSheet sheet, AudioMetadata file, string fileHash)
    {
        var tracks = sheet.Files[0].Tracks.OrderBy(t => t.StartMs).ToList();
        for (var i = 0; i < tracks.Count; i++)
        {
            var track = tracks[i];
            var end = i + 1 < tracks.Count ? tracks[i + 1].StartMs : file.DurationMs;
            if (end <= track.StartMs)
                continue;

            var performer = track.Performer ?? sheet.Performer;
            yield return (track.Number, track.StartMs, end,
                Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"{fileHash}:{track.StartMs}-{end}"))),
                file with
                {
                    Title = track.Title ?? $"Track {track.Number}",
                    Artists = performer is null ? file.Artists : TagReader.Split(performer),
                    FeaturedArtists = [],
                    Album = sheet.Title ?? file.Album,
                    AlbumArtist = sheet.Performer ?? file.AlbumArtist,
                    TrackNumber = track.Number,
                    DiscNumber = sheet.DiscNumber ?? file.DiscNumber,
                    Year = sheet.Year ?? file.Year,
                    Genres = sheet.Genre is { } genre ? [genre] : file.Genres,
                    DurationMs = end - track.StartMs,
                    // What the file's tags say about one recording, its gain or its words is about the whole album here.
                    RecordingMbid = null,
                    ReplayGainTrackGain = null,
                    ReplayGainTrackPeak = null,
                    Lyrics = null,
                    SyncedLyrics = null,
                    LyricsSource = null,
                });
        }
    }

    /// <summary>
    /// Pairs each cue sheet with the one audio file it describes in its folder: the FILE it names (rippers often
    /// name a .wav that was later compressed to .flac, so the extension is ignored), or the file with the cue
    /// sheet's own name. Cue sheets that name several files describe files that are scanned on their own.
    /// </summary>
    internal static async Task<Dictionary<string, (CueSheet Sheet, StorageEntry Entry)>> PairCueSheetsAsync(
        StorageLocation location, ILibraryStorage storage, List<StorageEntry> cueFiles, IReadOnlySet<string> audio, ILogger logger, CancellationToken cancellationToken)
    {
        var pairs = new Dictionary<string, (CueSheet, StorageEntry)>(StringComparer.Ordinal);
        if (cueFiles.Count == 0)
            return pairs;

        var byFolderAndName = audio
            .GroupBy(path => (AudioFormats.Folder(path), Path.GetFileNameWithoutExtension(path).ToLowerInvariant()))
            .ToDictionary(g => g.Key, g => g.First());

        foreach (var entry in cueFiles.Where(c => c.Size <= MaxCueBytes))
        {
            CueSheet? sheet;
            try
            {
                await using var stream = await storage.OpenReadAsync(location, entry.RelativePath, cancellationToken);
                using var buffer = new MemoryStream();
                await stream.CopyToAsync(buffer, cancellationToken);
                sheet = CueSheet.Parse(buffer.ToArray());
            }
            catch (IOException e)
            {
                logger.LogDebug(e, "Could not read {Path}", entry.RelativePath);
                continue;
            }

            if (sheet is not { Files: [{ Tracks.Count: > 1 } file] })
                continue;

            var folder = AudioFormats.Folder(entry.RelativePath);
            var named = Path.GetFileNameWithoutExtension(file.Name.Replace('\\', '/').Split('/')[^1]).ToLowerInvariant();
            var own = Path.GetFileNameWithoutExtension(entry.RelativePath).ToLowerInvariant();
            if (byFolderAndName.TryGetValue((folder, named), out var path) || byFolderAndName.TryGetValue((folder, own), out path))
                pairs.TryAdd(path, (sheet, entry));
        }

        return pairs;
    }

    private const long MaxCueBytes = 256 * 1024;

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

    /// <summary>Lyrics from a .lrc file next to the audio, which win over the ones in its tags.</summary>
    private async Task<AudioMetadata> WithSidecarLyricsAsync(
        AudioMetadata metadata, StorageLocation location, ILibraryStorage storage, StorageEntry lyrics, CancellationToken cancellationToken)
    {
        // Lyrics files are small; anything bigger is not one.
        if (lyrics.Size > MaxLyricsBytes)
            return metadata;

        try
        {
            await using var stream = await storage.OpenReadAsync(location, lyrics.RelativePath, cancellationToken);
            using var reader = new StreamReader(stream, detectEncodingFromByteOrderMarks: true);
            var text = (await reader.ReadToEndAsync(cancellationToken)).Replace("\r\n", "\n").Trim();
            if (text.Length == 0)
                return metadata;

            var lines = Lrc.Parse(text);
            return lines.Count > 0
                ? metadata with { SyncedLyrics = text, Lyrics = string.Join('\n', lines.Select(l => l.Text)), LyricsSource = LyricsSource.Sidecar }
                : metadata with { SyncedLyrics = null, Lyrics = text, LyricsSource = LyricsSource.Sidecar };
        }
        catch (IOException e)
        {
            logger.LogDebug(e, "Could not read {Path}", lyrics.RelativePath);
            return metadata;
        }
    }

    private const long MaxLyricsBytes = 256 * 1024;

    private sealed record KnownFile(Guid Id, string RelativePath, int CueTrack, long Size, DateTime ModifiedAt, DateTime? MissingSince, DateTime ScannedAt);
}
