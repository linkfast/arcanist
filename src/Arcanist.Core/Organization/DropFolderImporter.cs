using System.Text.Json;
using Arcanist.Core.Application.Organization;
using Arcanist.Core.Data;
using DropFolder = Arcanist.Core.Data.Entities.DropFolder;
using ImportItem = Arcanist.Core.Data.Entities.ImportItem;
using Arcanist.Core.Data.Entities;
using Arcanist.Core.Hosting;
using Arcanist.Core.Media;
using Arcanist.Core.Scanning;
using Arcanist.Core.Storage;
using Microsoft.EntityFrameworkCore;

namespace Arcanist.Core.Organization;

/// <summary>What an administrator asked for a dropped file: tags to write first, and whether to import it anyway.</summary>
internal sealed record ImportRequest(ImportTagsInput? Tags, bool Force);

/// <summary>
/// Imports the files of a drop folder into its library: reads their tags, checks the library for the same song,
/// converts them if asked, and moves them (with their lyrics, cue sheet and the rest of their folder) to the path
/// the library's template gives them. What cannot be placed waits in the drop folder for an administrator.
/// </summary>
public sealed class DropFolderImporter(
    IArcanistDbContextFactory databases,
    ILibraryStorageResolver storages,
    LibraryLocks locks,
    Transcoder transcoder,
    ArcanistPaths paths,
    TimeProvider time,
    ILogger<DropFolderImporter> logger)
{
    private static readonly JsonSerializerOptions Json = JsonSerializerOptions.Web;

    /// <summary>
    /// Processes <paramref name="ready"/> (audio files of the drop folder that stopped changing).
    /// <paramref name="entries"/> lists all of the drop folder's files. Returns whether anything reached the library,
    /// which then needs a scan.
    /// </summary>
    public async Task<bool> ImportAsync(Guid dropFolderId, IReadOnlyList<StorageEntry> entries, IReadOnlyCollection<string> ready, CancellationToken cancellationToken)
    {
        await using var db = databases.CreateDbContext();
        var drop = await db.DropFolders.Include(d => d.LibrarySource).ThenInclude(s => s.Library).SingleOrDefaultAsync(d => d.Id == dropFolderId, cancellationToken);
        if (drop is null || ready.Count == 0)
            return false;

        var library = drop.LibrarySource.Library;
        using var writing = await locks.AcquireForWritingAsync(library.Id, cancellationToken);

        var batch = new Batch(
            drop,
            new StorageLocation(LibrarySourceKind.Local, drop.Path),
            storages.For(LibrarySourceKind.Local),
            StorageLocation.From(drop.LibrarySource),
            storages.For(drop.LibrarySource.Kind),
            PathTemplate.TryParse(library.OrganizeTemplate, out var template, out _) ? template : PathTemplate.DefaultTemplate,
            await db.LibrarySources.AsNoTracking().Where(s => s.LibraryId == library.Id).ToDictionaryAsync(s => s.Id, cancellationToken));

        var copying = drop.Mode == DropFolderMode.Copy;
        var items = (await db.ImportItems
                .Where(i => i.DropFolderId == drop.Id && (i.Status != ImportStatus.Imported || copying) && i.Status != ImportStatus.Discarded)
                .ToListAsync(cancellationToken))
            .GroupBy(i => i.RelativePath, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(i => i.UpdatedAt).First(), StringComparer.Ordinal);

        var candidates = new List<Candidate>();
        var cues = await LibraryScanner.PairCueSheetsAsync(batch.DropLocation, batch.DropStorage,
            entries.Where(e => AudioFormats.IsCueSheet(e.RelativePath)).ToList(), ready.ToHashSet(StringComparer.Ordinal), logger, cancellationToken);

        foreach (var entry in entries.Where(e => ready.Contains(e.RelativePath)).OrderBy(e => e.RelativePath, StringComparer.Ordinal))
        {
            if (!items.TryGetValue(entry.RelativePath, out var item))
            {
                item = new ImportItem { DropFolderId = drop.Id, RelativePath = entry.RelativePath, CreatedAt = Now() };
                db.ImportItems.Add(item);
            }

            candidates.Add(await ReadAsync(batch, item, entry, cues.TryGetValue(entry.RelativePath, out var cue) ? cue : null, cancellationToken));
        }

        if (!await batch.LibraryStorage.CanWriteAsync(batch.LibraryLocation, cancellationToken))
        {
            foreach (var candidate in candidates.Where(c => c.Item.Status == ImportStatus.Pending))
                Settle(candidate.Item, ImportStatus.Failed, ImportReason.WriteFailed);
            await db.SaveChangesAsync(cancellationToken);
            return false;
        }

        // Albums with several discs, as far as this batch tells: their files get the disc in their name.
        var multiDisc = candidates.Where(c => c.Metadata?.DiscNumber > 1).Select(c => AlbumKey(c.Album)).ToHashSet(StringComparer.Ordinal);

        // The best copy of a song first, so that a worse one dropped with it counts as its duplicate.
        foreach (var candidate in candidates.Where(c => c.Item.Status == ImportStatus.Pending).OrderByDescending(c => c.Quality))
        {
            try
            {
                await ImportOneAsync(db, batch, candidate, multiDisc.Contains(AlbumKey(candidate.Album)), entries, cancellationToken);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(e, "Could not import {Path} from {DropFolder}", candidate.Item.RelativePath, drop.Path);
                Settle(candidate.Item, ImportStatus.Failed, ImportReason.WriteFailed);
            }

            await db.SaveChangesAsync(cancellationToken);
        }

        await MoveCompanionsAsync(batch, entries, cancellationToken);
        await TidyDropFolderAsync(batch, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return batch.Imported.Count > 0;
    }

    /// <summary>Applies what an administrator asked for, then reads the file's tags and audio hash.</summary>
    private async Task<Candidate> ReadAsync(Batch batch, ImportItem item, StorageEntry entry, (CueSheet Sheet, StorageEntry Entry)? cue, CancellationToken cancellationToken)
    {
        var request = item.Request is { } json ? JsonSerializer.Deserialize<ImportRequest>(json, Json) : null;
        item.Request = null;
        item.Status = ImportStatus.Pending;
        item.Reason = null;
        item.Size = entry.Size;
        item.ModifiedAt = entry.ModifiedAt;
        item.UpdatedAt = Now();

        var localPath = batch.DropStorage.TryGetLocalPath(batch.DropLocation, entry.RelativePath)!;
        try
        {
            if (request?.Tags is { } tags)
            {
                TagWriter.Write(localPath, tags);
                // Writing tags changed the file; the item must describe it as it is now.
                var written = new FileInfo(localPath);
                item.Size = written.Length;
                item.ModifiedAt = written.LastWriteTimeUtc;
            }

            AudioMetadata metadata;
            await using (var stream = await batch.DropStorage.OpenReadAsync(batch.DropLocation, entry.RelativePath, cancellationToken))
            {
                metadata = TagReader.Read(stream, AudioFormats.MimeType(entry.RelativePath));
                item.ContentHash = await ContentHasher.HashAsync(stream, metadata.AudioDataOffset, metadata.AudioDataSize, cancellationToken);
            }

            metadata = metadata with { EmbeddedPicture = null };
            item.Tags = JsonSerializer.Serialize(Details(metadata, cue?.Sheet), Json);
            return new Candidate(item, entry, localPath, metadata, cue, request?.Force ?? false);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            logger.LogWarning(e, "Could not read {Path} in {DropFolder}", entry.RelativePath, batch.Drop.Path);
            Settle(item, ImportStatus.Failed, ImportReason.Unreadable);
            return new Candidate(item, entry, localPath, null, cue, false);
        }
    }

    private async Task ImportOneAsync(
        ArcanistDbContext db, Batch batch, Candidate candidate, bool multiDisc, IReadOnlyList<StorageEntry> entries, CancellationToken cancellationToken)
    {
        var (item, entry, localPath, metadata, cue, force) = candidate;
        var album = candidate.Album;
        if (!force && (album.Title is null || album.Album is null || album.MainArtist is null))
        {
            Settle(item, ImportStatus.NeedsReview, ImportReason.MissingTags);
            return;
        }

        var conversion = Conversion(batch.Drop, entry.RelativePath);
        var quality = conversion is { } profile ? QualityAfter(profile, batch.Drop, metadata!) : candidate.Quality;

        // Copies of the same song already in the library (or imported a moment ago) that this file replaces.
        List<LibraryCopy> replaced = [];
        if (!force)
        {
            var sameAudio = await db.MediaFiles.AsNoTracking()
                .Where(f => f.ContentHash == item.ContentHash && f.MissingSince == null && f.LibrarySource.LibraryId == batch.Drop.LibrarySource.LibraryId)
                .Select(f => (Guid?)f.TrackId)
                .FirstOrDefaultAsync(cancellationToken);
            if (sameAudio is not null || batch.Imported.Any(i => i.Hash == item.ContentHash))
            {
                item.TrackId = sameAudio;
                Settle(item, ImportStatus.Duplicate, ImportReason.SameAudio);
                return;
            }

            if (cue is null)
            {
                var (trackId, copies) = await SameTrackAsync(db, batch, album, cancellationToken);
                if (trackId is not null || copies.Count > 0)
                {
                    item.TrackId = trackId;
                    switch (batch.Drop.DuplicatePolicy)
                    {
                        case DuplicatePolicy.Skip:
                            Settle(item, ImportStatus.Duplicate, ImportReason.SameTrack);
                            return;
                        case DuplicatePolicy.KeepBest when copies.Count == 0 || copies.Any(c => !(quality > c.Quality)):
                            Settle(item, ImportStatus.Duplicate, ImportReason.NotBetter);
                            return;
                        case DuplicatePolicy.KeepBest:
                            replaced = copies;
                            break;
                    }
                }
            }
        }

        // Converted next to the data directory, then moved into the library like any other file.
        string? converted = null;
        if (conversion is { } target)
        {
            converted = await ConvertAsync(item, localPath, target, batch.Drop.ConvertBitRate, cancellationToken);
            if (converted is null)
                return;
        }

        try
        {
            var extension = converted is not null ? Path.GetExtension(converted) : Path.GetExtension(entry.RelativePath);
            var values = new PathValues(album.AlbumArtist, album.Artist, album.Album, metadata!.Year, metadata.DiscNumber, multiDisc,
                metadata.TrackNumber, cue is null ? album.Title : album.Album, metadata.Genres.FirstOrDefault());
            // An album file split by a cue sheet keeps its name, which the cue sheet refers to.
            var wanted = cue is null
                ? batch.Template.Render(values)
                : Join(batch.Template.RenderFolder(values), Path.GetFileNameWithoutExtension(entry.RelativePath));

            var destination = await FreePathAsync(batch, wanted, extension, cancellationToken);
            await batch.LibraryStorage.ImportAsync(batch.LibraryLocation, destination, converted ?? Take(batch, localPath), cancellationToken);
            converted = null;

            // Worse copies go only once the better one is in, then it takes the place of the one at its path.
            foreach (var copy in replaced)
                await TrashCopyAsync(batch, copy, cancellationToken);
            if (replaced.Count > 0 && destination != wanted + extension
                && !await batch.LibraryStorage.ExistsAsync(batch.LibraryLocation, wanted + extension, cancellationToken))
            {
                await batch.LibraryStorage.MoveAsync(batch.LibraryLocation, destination, wanted + extension, cancellationToken);
                destination = wanted + extension;
            }

            if (conversion is not null && batch.Drop.Mode == DropFolderMode.Move)
                await batch.DropStorage.TrashAsync(batch.DropLocation, entry.RelativePath, cancellationToken);

            await ImportSidecarsAsync(batch, entry.RelativePath, destination, cue?.Entry, entries, cancellationToken);

            item.DestinationSourceId = batch.Drop.LibrarySourceId;
            item.DestinationPath = destination;
            Settle(item, ImportStatus.Imported, replaced.Count > 0 ? ImportReason.ReplacedWorse : null);
            batch.Imported.Add(new BatchCopy(item.ContentHash, Key(album), entry.RelativePath, new LibraryCopy(batch.Drop.LibrarySourceId, destination, quality)));
            logger.LogInformation("Imported {Path} from {DropFolder} as {Destination}", entry.RelativePath, batch.Drop.Path, destination);
        }
        finally
        {
            if (converted is not null)
                File.Delete(converted);
        }
    }

    /// <summary>The format a dropped file must be converted to, or null to keep it as it is.</summary>
    internal static TranscodeProfile? Conversion(DropFolder drop, string path)
    {
        if (drop.ConvertTo is not { } format)
            return null;

        var extension = Path.GetExtension(path).ToLowerInvariant();
        var targetExtension = Transcoder.FileFormat(format).Extension;
        if (extension == targetExtension)
            return null;

        // FLAC is lossless: converting a lossy file to it only makes the file bigger.
        var lossless = AudioFormats.IsLossless(path);
        if (format == TranscodeFormat.Flac ? !lossless : !lossless && !drop.ConvertLossy)
            return null;

        return TranscodeProfile.For(format);
    }

    private static AudioQuality QualityAfter(TranscodeProfile profile, DropFolder drop, AudioMetadata metadata) =>
        profile.IsLossless
            ? new AudioQuality(true, metadata.BitDepth ?? 0, metadata.SampleRate ?? 0, metadata.BitRate ?? 0)
            : new AudioQuality(false, 0, profile.Format == TranscodeFormat.Opus ? 48_000 : metadata.SampleRate ?? 0, profile.Clamp(drop.ConvertBitRate));

    private async Task<string?> ConvertAsync(ImportItem item, string localPath, TranscodeProfile profile, int? bitRate, CancellationToken cancellationToken)
    {
        var folder = Path.Combine(paths.Cache, "import");
        Directory.CreateDirectory(folder);
        var output = Path.Combine(folder, Guid.NewGuid().ToString("N") + Transcoder.FileFormat(profile.Format ?? TranscodeFormat.Flac).Extension);

        var result = await transcoder.ConvertAsync(localPath, output, profile, profile.Clamp(bitRate), cancellationToken);
        if (!result.Succeeded)
        {
            File.Delete(output);
            Settle(item, ImportStatus.Failed, result.Available ? ImportReason.TranscodingFailed : ImportReason.TranscodingUnavailable);
            return null;
        }

        try
        {
            TagWriter.Copy(localPath, output);
            return output;
        }
        catch (Exception e) when (e is IOException or InvalidOperationException or NotSupportedException)
        {
            logger.LogWarning(e, "Could not copy tags to the converted {Path}", item.RelativePath);
            File.Delete(output);
            Settle(item, ImportStatus.Failed, ImportReason.TranscodingFailed);
            return null;
        }
    }

    /// <summary>
    /// The library's track that is this song (same album, disc, number and title, or the same MusicBrainz
    /// recording), with its copies in the library, plus copies imported earlier in this batch.
    /// </summary>
    private static async Task<(Guid? TrackId, List<LibraryCopy> Copies)> SameTrackAsync(
        ArcanistDbContext db, Batch batch, AlbumInfo album, CancellationToken cancellationToken)
    {
        var copies = batch.Imported.Where(i => i.Key == Key(album)).Select(i => i.Copy).ToList();

        var title = TextNormalizer.Normalize(album.Title!);
        var albumTitle = TextNormalizer.Normalize(album.Album!);
        var libraryId = batch.Drop.LibrarySource.LibraryId;
        var tracks = await db.Tracks.AsNoTracking()
            .Where(t => t.NormalizedTitle == title && t.Album.NormalizedTitle == albumTitle)
            .Select(t => new
            {
                t.Id,
                t.DiscNumber,
                t.TrackNumber,
                AlbumArtist = t.Album.AlbumArtist.NormalizedName,
                t.Album.IsCompilation,
                Recording = t.Recording.MusicBrainzId,
                Artist = t.Artists.Where(a => a.Role == ArtistRole.Primary).OrderBy(a => a.Position).Select(a => a.Artist.NormalizedName).FirstOrDefault(),
                Files = t.Files
                    .Where(f => f.MissingSince == null && f.LibrarySource.LibraryId == libraryId)
                    .Select(f => new { f.LibrarySourceId, f.RelativePath, f.CueTrack, f.BitDepth, f.SampleRate, f.BitRate })
                    .ToList(),
            })
            .ToListAsync(cancellationToken);

        var artist = TextNormalizer.Normalize(album.MainArtist!);
        var match = tracks.FirstOrDefault(t =>
            t.Files.Count > 0
            && (album.RecordingMbid is { } mbid && t.Recording == mbid
                || (t.DiscNumber ?? 1) == (album.Disc ?? 1) && t.TrackNumber == album.Track
                && (t.IsCompilation || t.AlbumArtist == artist || t.Artist == artist)));
        if (match is null)
            return (null, copies);

        // A track of an album file split by a cue sheet cannot be replaced on its own; it counts as better.
        copies.AddRange(match.Files.Select(f => new LibraryCopy(f.LibrarySourceId, f.RelativePath,
            f.CueTrack > 0 ? new AudioQuality(true, int.MaxValue, int.MaxValue, int.MaxValue) : AudioQuality.Of(f.RelativePath, f.BitDepth, f.SampleRate, f.BitRate))));
        return (match.Id, copies);
    }

    private async Task TrashCopyAsync(Batch batch, LibraryCopy copy, CancellationToken cancellationToken)
    {
        if (!batch.Sources.TryGetValue(copy.SourceId, out var source))
            return;

        var storage = storages.For(source.Kind);
        var location = StorageLocation.From(source);
        if (await storage.ExistsAsync(location, copy.Path, cancellationToken))
        {
            await storage.TrashAsync(location, copy.Path, cancellationToken);
            logger.LogInformation("Moved {Path} to the trash: a better copy was dropped", copy.Path);
        }
    }

    private static async Task<string> FreePathAsync(Batch batch, string wanted, string extension, CancellationToken cancellationToken)
    {
        for (var copy = 1; ; copy++)
        {
            var candidate = copy == 1 ? wanted + extension : $"{wanted} ({copy}){extension}";
            if (!await batch.LibraryStorage.ExistsAsync(batch.LibraryLocation, candidate, cancellationToken))
                return candidate;
        }
    }

    /// <summary>Lyrics named like the audio, and the cue sheet of an album file, go with it.</summary>
    private async Task ImportSidecarsAsync(
        Batch batch, string path, string destination, StorageEntry? cue, IReadOnlyList<StorageEntry> entries, CancellationToken cancellationToken)
    {
        var comparer = batch.DropStorage.PathComparer;
        var stem = AudioFormats.WithoutExtension(path);
        var sidecars = entries
            .Where(e => AudioFormats.IsLyrics(e.RelativePath) && comparer.Equals(AudioFormats.WithoutExtension(e.RelativePath), stem))
            .Select(e => (e.RelativePath, Target: AudioFormats.WithoutExtension(destination) + Path.GetExtension(e.RelativePath)))
            .ToList();
        if (cue is not null)
            sidecars.Add((cue.RelativePath, Join(AudioFormats.Folder(destination), Path.GetFileName(cue.RelativePath))));

        foreach (var (from, to) in sidecars)
        {
            try
            {
                if (batch.DropStorage.TryGetLocalPath(batch.DropLocation, from) is { } local && File.Exists(local))
                    await batch.LibraryStorage.ImportAsync(batch.LibraryLocation, to, Take(batch, local), cancellationToken);
                batch.Moved.Add(from);
            }
            catch (IOException e)
            {
                logger.LogDebug(e, "Could not import {Path}", from);
            }
        }
    }

    /// <summary>The rest of a folder whose audio all went to one album folder goes there too.</summary>
    private async Task MoveCompanionsAsync(Batch batch, IReadOnlyList<StorageEntry> entries, CancellationToken cancellationToken)
    {
        if (batch.Imported.Count == 0)
            return;

        var comparer = batch.DropStorage.PathComparer;
        var imported = batch.Imported.ToDictionary(i => i.DropPath, i => AudioFormats.Folder(i.Copy.Path), comparer);
        // Audio that stays (duplicates, files to review, files still arriving) keeps its folder's other files.
        var destinations = entries.Where(e => AudioFormats.IsAudio(e.RelativePath))
            .ToDictionary(e => e.RelativePath, e => imported.TryGetValue(e.RelativePath, out var folder) ? folder : null, comparer);
        var claimed = batch.Moved.ToHashSet(comparer);

        foreach (var (from, to) in Companions.Plan(entries.Select(e => e.RelativePath), destinations, claimed, comparer))
        {
            try
            {
                if (await batch.LibraryStorage.ExistsAsync(batch.LibraryLocation, to, cancellationToken))
                    continue;
                await batch.LibraryStorage.ImportAsync(batch.LibraryLocation, to, Take(batch, batch.DropStorage.TryGetLocalPath(batch.DropLocation, from)!), cancellationToken);
                batch.Moved.Add(from);
            }
            catch (IOException e)
            {
                logger.LogDebug(e, "Could not import {Path}", from);
            }
        }
    }

    /// <summary>
    /// The file to hand to the library: the drop folder's own (importing moves it), or, when the folder copies, a copy
    /// of it next to the data directory.
    /// </summary>
    private string Take(Batch batch, string localPath)
    {
        if (batch.Drop.Mode == DropFolderMode.Move)
            return localPath;
        var folder = Path.Combine(paths.Cache, "import");
        Directory.CreateDirectory(folder);
        var copy = Path.Combine(folder, Guid.NewGuid().ToString("N") + Path.GetExtension(localPath));
        File.Copy(localPath, copy);
        return copy;
    }

    /// <summary>Removes the drop folder's subfolders that importing emptied.</summary>
    private async Task TidyDropFolderAsync(Batch batch, CancellationToken cancellationToken)
    {
        if (batch.Drop.Mode == DropFolderMode.Copy)
            return;
        var folders = batch.Imported.Select(i => i.DropPath).Concat(batch.Moved).Select(AudioFormats.Folder).Distinct().OrderByDescending(f => f.Length);
        foreach (var folder in folders)
        {
            try
            {
                await batch.DropStorage.RemoveEmptyFoldersAsync(batch.DropLocation, folder, cancellationToken);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                logger.LogDebug(e, "Could not remove empty folder {Folder}", folder);
            }
        }
    }

    private void Settle(ImportItem item, ImportStatus status, ImportReason? reason)
    {
        item.Status = status;
        item.Reason = reason;
        item.UpdatedAt = Now();
    }

    internal static ImportFileDetails Details(AudioMetadata metadata, CueSheet? cue) => new(
        cue is null ? metadata.Title : null,
        metadata.Artists.Count > 0 ? string.Join("; ", metadata.Artists) : null,
        cue?.Title ?? metadata.Album,
        cue?.Performer ?? metadata.AlbumArtist,
        cue?.Year ?? metadata.Year,
        cue is null ? metadata.TrackNumber : null,
        cue?.DiscNumber ?? metadata.DiscNumber,
        metadata.Genres.FirstOrDefault(),
        metadata.Format,
        metadata.BitRate,
        metadata.SampleRate,
        metadata.BitDepth,
        metadata.DurationMs);

    /// <summary>Identifies the same song of the same album within a batch.</summary>
    private static string Key(AlbumInfo album) => string.Join('\u001f', AlbumKey(album), album.Disc ?? 1, album.Track ?? 0,
        TextNormalizer.Normalize(album.Title ?? ""));

    private static string AlbumKey(AlbumInfo album) =>
        TextNormalizer.Normalize(album.MainArtist ?? "") + '\u001f' + TextNormalizer.Normalize(album.Album ?? "");

    private static string Join(string folder, string name) => folder.Length == 0 ? name : $"{folder}/{name}";

    private DateTime Now() => time.GetUtcNow().UtcDateTime;

    /// <summary>A copy of a song in the library: where it is and how good it is.</summary>
    private sealed record LibraryCopy(Guid SourceId, string Path, AudioQuality Quality);

    /// <summary>A file imported in this batch, to recognize its duplicates before the library is scanned.</summary>
    private sealed record BatchCopy(string? Hash, string Key, string DropPath, LibraryCopy Copy);

    /// <summary>What identifies a file's song and where it goes. For an album file split by a cue sheet, the album.</summary>
    /// <param name="Artist">All of the song's artists, for paths.</param>
    /// <param name="MainArtist">Who the album is by: its album artist, or else the song's first artist.</param>
    private sealed record AlbumInfo(
        string? Title, string? Artist, string? MainArtist, string? Album, string? AlbumArtist, int? Disc, int? Track, Guid? RecordingMbid);

    private sealed record Candidate(
        ImportItem Item, StorageEntry Entry, string LocalPath, AudioMetadata? Metadata, (CueSheet Sheet, StorageEntry Entry)? Cue, bool Force)
    {
        public AlbumInfo Album { get; } = Metadata is null
            ? new AlbumInfo(null, null, null, null, null, null, null, null)
            : Cue is { Sheet: var sheet }
                ? new AlbumInfo(sheet.Title ?? Metadata.Album, sheet.Performer ?? Metadata.Artists.FirstOrDefault(),
                    sheet.Performer ?? Metadata.AlbumArtist ?? Metadata.Artists.FirstOrDefault(), sheet.Title ?? Metadata.Album,
                    sheet.Performer ?? Metadata.AlbumArtist, sheet.DiscNumber ?? Metadata.DiscNumber, null, null)
                : new AlbumInfo(Metadata.Title, Metadata.Artists.Count > 0 ? string.Join(", ", Metadata.Artists) : null,
                    Metadata.AlbumArtist ?? Metadata.Artists.FirstOrDefault(), Metadata.Album, Metadata.AlbumArtist, Metadata.DiscNumber,
                    Metadata.TrackNumber, Metadata.RecordingMbid);

        public AudioQuality Quality { get; } = Metadata is null
            ? default
            : AudioQuality.Of(Entry.RelativePath, Metadata.BitDepth, Metadata.SampleRate, Metadata.BitRate);
    }

    private sealed record Batch(
        DropFolder Drop,
        StorageLocation DropLocation,
        ILibraryStorage DropStorage,
        StorageLocation LibraryLocation,
        ILibraryStorage LibraryStorage,
        PathTemplate Template,
        Dictionary<Guid, LibrarySource> Sources)
    {
        public List<BatchCopy> Imported { get; } = [];

        /// <summary>Other files moved out of the drop folder (lyrics, cue sheets, artwork…).</summary>
        public List<string> Moved { get; } = [];
    }
}
