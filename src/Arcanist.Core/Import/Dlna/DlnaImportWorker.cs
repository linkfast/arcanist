using System.Text.Json;
using Arcanist.Core.Application;
using Arcanist.Core.Application.Organization;
using Arcanist.Core.Data;
using Arcanist.Core.Data.Entities;
using Arcanist.Core.Dlna;
using Arcanist.Core.Hosting;
using Arcanist.Core.Organization;
using Arcanist.Core.Scanning;
using Arcanist.Core.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Arcanist.Core.Import.Dlna;

/// <summary>A container of a media server chosen for an import.</summary>
public sealed record DlnaContainerChoice(string Id, string Title);

/// <summary>
/// Copies music from DLNA/UPnP media servers into library folders, one import at a time and one file at a time:
/// first it walks the chosen containers of the server (each piece of music once, however many views list it), then
/// downloads each file (the original when the server offers it), skips audio the library has already, writes the
/// server's description into the tags the file lacks, and places the rest where the library's template says, as
/// organizing would, with the album's artwork when the file has none.
/// Imports interrupted by a restart go on where they were.
/// </summary>
internal sealed class DlnaImportWorker(
    IArcanistDbContextFactory databases,
    MediaServerClient client,
    IHttpClientFactory clients,
    ILibraryStorageResolver storages,
    LibraryLocks locks,
    ScanCoordinator scans,
    DatabaseMaintenance maintenance,
    IOptionsMonitor<Setup.SetupOptions> setup,
    ArcanistPaths paths,
    TimeProvider time,
    ILogger<DlnaImportWorker> logger) : BackgroundService
{
    private const int PageSize = 200;
    private const int MaxDepth = 32;
    private const int MaxItems = 500_000;
    private const int ScanEvery = 500;
    private const long MaxArtworkBytes = 20 * 1024 * 1024;

    private readonly SemaphoreSlim _wake = new(0);
    private readonly Lock _lock = new();
    private (Guid Id, CancellationTokenSource Cancel)? _running;

    public void Wake()
    {
        if (_wake.CurrentCount == 0)
            _wake.Release();
    }

    /// <summary>Stops the import if it is the one running; returns whether it was.</summary>
    public bool Cancel(Guid importId)
    {
        lock (_lock)
        {
            if (_running is not { } running || running.Id != importId)
                return false;
            running.Cancel.Cancel();
            return true;
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (await NextAsync(stoppingToken) is { } importId)
                {
                    await RunAsync(importId, stoppingToken);
                    continue;
                }

                await _wake.WaitAsync(TimeSpan.FromMinutes(1), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception e)
            {
                logger.LogError(e, "DLNA import failed");
                await Task.Delay(TimeSpan.FromSeconds(30), time, stoppingToken).ContinueWith(_ => { }, TaskScheduler.Default);
            }
        }
    }

    private async Task<Guid?> NextAsync(CancellationToken cancellationToken)
    {
        if (!databases.IsConfigured || !setup.CurrentValue.Completed)
            return null;
        using (await maintenance.WorkAsync(cancellationToken))
        {
            await using var db = databases.CreateDbContext();
            return await db.DlnaImports
                .Where(i => i.Status == DlnaImportStatus.Queued || i.Status == DlnaImportStatus.Listing || i.Status == DlnaImportStatus.Copying)
                .OrderBy(i => i.CreatedAt)
                .Select(i => (Guid?)i.Id)
                .FirstOrDefaultAsync(cancellationToken);
        }
    }

    private async Task RunAsync(Guid importId, CancellationToken stoppingToken)
    {
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        lock (_lock)
            _running = (importId, cancel);
        try
        {
            var import = await UpdateAsync(importId, _ => { }, cancel.Token);
            var server = await client.DescribeAsync(new Uri(import.Location), cancel.Token);
            if (import.Status is DlnaImportStatus.Queued or DlnaImportStatus.Listing)
            {
                await UpdateAsync(importId, i =>
                {
                    i.Status = DlnaImportStatus.Listing;
                    i.ServerName = server.Name;
                }, cancel.Token);
                await ListAsync(import, server, cancel.Token);
            }

            await UpdateAsync(importId, i => i.Status = DlnaImportStatus.Copying, cancel.Token);
            await CopyAsync(import, cancel.Token);
            await FinishAsync(importId, DlnaImportStatus.Completed, null);
            logger.LogInformation("Imported the music of {Server}", server.Name);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Goes on after the restart.
        }
        catch (OperationCanceledException)
        {
            await FinishAsync(importId, DlnaImportStatus.Cancelled, null);
        }
        catch (ArcanistException e)
        {
            logger.LogWarning("DLNA import {Import} failed: {Error}", importId, e.Code);
            await FinishAsync(importId, DlnaImportStatus.Failed, e.Code);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            logger.LogError(e, "DLNA import {Import} failed", importId);
            await FinishAsync(importId, DlnaImportStatus.Failed, ErrorCodes.ImportFailed);
        }
        finally
        {
            lock (_lock)
                _running = null;
            await DeleteTemporaryFilesAsync();
        }
    }

    /// <summary>
    /// Walks the chosen containers and records their music. Items copied before (by a run that was interrupted, or
    /// before a retry) stay as they are; the others get the server's current URL.
    /// </summary>
    private async Task ListAsync(DlnaImport import, MediaServerDevice server, CancellationToken cancellationToken)
    {
        var chosen = JsonSerializer.Deserialize<List<DlnaContainerChoice>>(import.Containers, JsonSerializerOptions.Web) ?? [];
        Dictionary<string, DlnaImportItem> known;
        using (await maintenance.WorkAsync(cancellationToken))
        {
            await using var db = databases.CreateDbContext();
            known = await db.DlnaImportItems.AsNoTracking().Where(i => i.ImportId == import.Id).ToDictionaryAsync(i => i.ObjectId, StringComparer.Ordinal, cancellationToken);
        }

        var seenUrls = new HashSet<string>(StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<(string Id, int Depth)>(chosen.Select(c => (c.Id, 0)).Reverse());
        var found = 0;
        while (pending.TryPop(out var container))
        {
            if (!visited.Add(container.Id))
                continue;

            for (var start = 0; ; start += PageSize)
            {
                var page = await client.BrowseAsync(server, container.Id, start, PageSize, cancellationToken);
                if (container.Depth < MaxDepth)
                {
                    foreach (var child in page.Containers.AsEnumerable().Reverse())
                        pending.Push((child.Id, container.Depth + 1));
                }

                var added = new List<DlnaImportItem>();
                var updated = new List<DlnaImportItem>();
                foreach (var track in page.Tracks)
                {
                    // The same file appears in several views (albums, artists, genres, folders…).
                    if (!seenUrls.Add(track.Url.ToString()) || found >= MaxItems)
                        continue;
                    found++;
                    if (known.TryGetValue(track.Id, out var item))
                    {
                        if (item.Status is DlnaImportItemStatus.Copied or DlnaImportItemStatus.Duplicate)
                            continue;
                        Describe(item, track);
                        item.Status = DlnaImportItemStatus.Pending;
                        item.Reason = null;
                        updated.Add(item);
                    }
                    else
                    {
                        item = new DlnaImportItem
                        {
                            ImportId = import.Id, ObjectId = track.Id, Url = "", Extension = "", Title = "", Status = DlnaImportItemStatus.Pending,
                        };
                        Describe(item, track);
                        known[track.Id] = item;
                        added.Add(item);
                    }
                }

                if (added.Count > 0 || updated.Count > 0)
                {
                    using (await maintenance.WorkAsync(cancellationToken))
                    {
                        await using var db = databases.CreateDbContext();
                        db.DlnaImportItems.AddRange(added);
                        db.DlnaImportItems.UpdateRange(updated);
                        await db.SaveChangesAsync(cancellationToken);
                    }
                }

                if (page.Returned == 0 || start + page.Returned >= page.Total)
                    break;
            }
        }
    }

    private static void Describe(DlnaImportItem item, RemoteTrack track)
    {
        item.Url = track.Url.ToString();
        item.Extension = track.Extension;
        item.Size = track.Size;
        item.Title = Cut(track.Title)!;
        item.Artist = Cut(track.Artist);
        item.AlbumArtist = Cut(track.AlbumArtist);
        item.Album = Cut(track.Album);
        item.Genre = Cut(track.Genre);
        item.Year = track.Year;
        item.TrackNumber = track.Track;
        item.DiscNumber = track.Disc;
        item.ArtUrl = track.ArtUrl?.ToString();
    }

    private static string? Cut(string? text) => text is { Length: > 500 } ? text[..500] : text;

    /// <summary>Downloads every pending item into the library folder.</summary>
    private async Task CopyAsync(DlnaImport import, CancellationToken cancellationToken)
    {
        LibrarySource source;
        PathTemplate template;
        HashSet<string> multiDisc;
        using (await maintenance.WorkAsync(cancellationToken))
        {
            await using var db = databases.CreateDbContext();
            source = await db.LibrarySources.AsNoTracking().Include(s => s.Library).SingleAsync(s => s.Id == import.LibrarySourceId, cancellationToken);
            template = PathTemplate.TryParse(source.Library.OrganizeTemplate, out var parsed, out _) ? parsed : PathTemplate.DefaultTemplate;
            // Albums with several discs, as the server describes them: their files get the disc in their name.
            multiDisc = (await db.DlnaImportItems.AsNoTracking()
                    .Where(i => i.ImportId == import.Id && i.DiscNumber > 1)
                    .Select(i => new { i.AlbumArtist, i.Artist, i.Album })
                    .Distinct()
                    .ToListAsync(cancellationToken))
                .Select(a => AlbumKey(a.AlbumArtist ?? a.Artist, a.Album))
                .ToHashSet(StringComparer.Ordinal);
        }

        var storage = storages.For(source.Kind);
        var location = StorageLocation.From(source);
        if (!await storage.CanWriteAsync(location, cancellationToken))
            throw new ArcanistException.Conflict(ErrorCodes.ReadOnly);

        var batch = new CopyBatch(import, source, storage, location, template, multiDisc);
        while (true)
        {
            List<DlnaImportItem> items;
            using (await maintenance.WorkAsync(cancellationToken))
            {
                await using var db = databases.CreateDbContext();
                items = await db.DlnaImportItems.AsNoTracking()
                    .Where(i => i.ImportId == import.Id && i.Status == DlnaImportItemStatus.Pending)
                    .OrderBy(i => i.Album).ThenBy(i => i.DiscNumber).ThenBy(i => i.TrackNumber).ThenBy(i => i.Id)
                    .Take(50)
                    .ToListAsync(cancellationToken);
            }

            if (items.Count == 0)
                break;

            foreach (var item in items)
            {
                await CopyOneAsync(batch, item, cancellationToken);
                if (batch.WriteFailures >= 5)
                    throw new ArcanistException.Conflict(ErrorCodes.WriteFailed);
                if (batch.CopiedSinceScan >= ScanEvery)
                {
                    // Long imports show up in the library as they go.
                    batch.CopiedSinceScan = 0;
                    using (await maintenance.WorkAsync(cancellationToken))
                        await scans.EnqueueAsync(source.LibraryId, ScanKind.Incremental, import.RequestedByUserId, cancellationToken);
                }
            }
        }
    }

    private async Task CopyOneAsync(CopyBatch batch, DlnaImportItem item, CancellationToken cancellationToken)
    {
        var temporary = TemporaryPath(item.Extension);
        try
        {
            if (!await DownloadAsync(new Uri(item.Url), temporary, null, cancellationToken))
            {
                await SettleAsync(item, DlnaImportItemStatus.Failed, ErrorCodes.DownloadFailed, null, 0, cancellationToken);
                return;
            }

            AudioMetadata metadata;
            string hash;
            try
            {
                await using var stream = File.OpenRead(temporary);
                metadata = TagReader.Read(stream, AudioFormats.MimeType(temporary));
                hash = await ContentHasher.HashAsync(stream, metadata.AudioDataOffset, metadata.AudioDataSize, cancellationToken);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                logger.LogDebug(e, "Could not read {Title} from the media server", item.Title);
                await SettleAsync(item, DlnaImportItemStatus.Failed, ErrorCodes.Unreadable, null, 0, cancellationToken);
                return;
            }

            // The server's description fills the gaps of the file's tags, in the copy, before it enters the library.
            var missing = new ImportTagsInput(
                metadata.Title is null ? item.Title : null,
                metadata.Artists.Count == 0 ? item.Artist : null,
                metadata.Album is null ? item.Album : null,
                metadata.AlbumArtist is null ? item.AlbumArtist : null,
                metadata.Year is null ? item.Year : null,
                metadata.TrackNumber is null ? item.TrackNumber : null,
                metadata.DiscNumber is null ? item.DiscNumber : null,
                metadata.Genres.Count == 0 ? item.Genre : null);
            if (missing != NoTags)
            {
                try
                {
                    TagWriter.Write(temporary, missing);
                }
                catch (Exception e) when (e is IOException or InvalidOperationException or NotSupportedException)
                {
                    // Some formats take no tags: the file is copied as it is.
                    logger.LogDebug(e, "Could not write the server's tags into {Title}", item.Title);
                }
            }

            // What the file says first; the server's description fills the gaps.
            var album = metadata.Album ?? item.Album;
            var albumArtist = metadata.AlbumArtist ?? item.AlbumArtist;
            var artist = metadata.Artists.Count > 0 ? string.Join(", ", metadata.Artists) : item.Artist;
            var disc = metadata.DiscNumber ?? item.DiscNumber;
            var values = new PathValues(albumArtist, artist, album, metadata.Year ?? item.Year, disc,
                disc > 1 || batch.MultiDisc.Contains(AlbumKey(item.AlbumArtist ?? item.Artist, item.Album)),
                metadata.TrackNumber ?? item.TrackNumber, metadata.Title ?? item.Title, metadata.Genres.FirstOrDefault() ?? item.Genre);
            var wanted = batch.Template.Render(values);
            var length = new FileInfo(temporary).Length;

            // The library's lock first: a scan may hold it for a while, and maintenance must not wait for that.
            var libraryId = batch.Source.LibraryId;
            using (await locks.AcquireForWritingAsync(libraryId, cancellationToken))
            using (await maintenance.WorkAsync(cancellationToken))
            {
                await using var db = databases.CreateDbContext();
                if (batch.Hashes.Contains(hash)
                    || await db.MediaFiles.AnyAsync(f => f.ContentHash == hash && f.MissingSince == null && f.LibrarySource.LibraryId == libraryId, cancellationToken))
                {
                    await SettleAsync(item, DlnaImportItemStatus.Duplicate, null, null, 0, cancellationToken);
                    return;
                }

                string destination;
                try
                {
                    destination = await FreePathAsync(batch, wanted, item.Extension, cancellationToken);
                    await batch.Storage.ImportAsync(batch.Location, destination, temporary, cancellationToken);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    logger.LogWarning(e, "Could not write {Title} from the media server into the library", item.Title);
                    batch.WriteFailures++;
                    await SettleAsync(item, DlnaImportItemStatus.Failed, ErrorCodes.WriteFailed, null, 0, cancellationToken);
                    return;
                }

                batch.WriteFailures = 0;
                batch.CopiedSinceScan++;
                batch.Hashes.Add(hash);
                await SettleAsync(item, DlnaImportItemStatus.Copied, null, destination, length, cancellationToken);
                if (metadata.EmbeddedPicture is null && item.ArtUrl is not null)
                    await ArtworkAsync(batch, AudioFormats.Folder(destination), new Uri(item.ArtUrl), cancellationToken);
            }
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    /// <summary>The album's artwork from the server, as cover.jpg (or .png) in its folder, when the folder has none.</summary>
    private async Task ArtworkAsync(CopyBatch batch, string folder, Uri url, CancellationToken cancellationToken)
    {
        if (!batch.ArtworkFolders.Add(folder))
            return;
        foreach (var name in new[] { "cover", "folder", "front" })
        {
            foreach (var extension in new[] { ".jpg", ".png" })
            {
                if (await batch.Storage.ExistsAsync(batch.Location, Join(folder, name + extension), cancellationToken))
                    return;
            }
        }

        var temporary = TemporaryPath(".img");
        try
        {
            string? contentType = null;
            if (!await DownloadAsync(url, temporary, type => contentType = type, cancellationToken) || new FileInfo(temporary).Length > MaxArtworkBytes)
                return;
            var extension = contentType switch
            {
                "image/jpeg" or "image/jpg" => ".jpg",
                "image/png" => ".png",
                _ => null,
            };
            if (extension is null)
                return;
            // Called with the library's lock held.
            await batch.Storage.ImportAsync(batch.Location, Join(folder, "cover" + extension), temporary, cancellationToken);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            logger.LogDebug(e, "Could not save the artwork of {Folder}", folder);
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    /// <summary>Downloads to a local file; false when the server answers with an error or stops sending.</summary>
    private async Task<bool> DownloadAsync(Uri url, string path, Action<string?>? contentType, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await clients.CreateClient(MediaServerClient.DownloadClientName)
                .GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogDebug("The media server answered {Status} for {Url}", (int)response.StatusCode, url);
                return false;
            }

            contentType?.Invoke(response.Content.Headers.ContentType?.MediaType?.ToLowerInvariant());
            await using var file = File.Create(path);
            await response.Content.CopyToAsync(file, cancellationToken);
            return true;
        }
        catch (Exception e) when (e is HttpRequestException or IOException || e is TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            logger.LogDebug("Downloading {Url} failed: {Error}", url, e.Message);
            return false;
        }
    }

    private static async Task<string> FreePathAsync(CopyBatch batch, string wanted, string extension, CancellationToken cancellationToken)
    {
        for (var copy = 1; ; copy++)
        {
            var candidate = copy == 1 ? wanted + extension : $"{wanted} ({copy}){extension}";
            if (!await batch.Storage.ExistsAsync(batch.Location, candidate, cancellationToken))
                return candidate;
        }
    }

    private async Task SettleAsync(
        DlnaImportItem item, DlnaImportItemStatus status, string? reason, string? destination, long bytes, CancellationToken cancellationToken)
    {
        item.Status = status;
        item.Reason = reason;
        item.DestinationPath = destination;
        await using var db = databases.CreateDbContext();
        db.DlnaImportItems.Update(item);
        if (bytes > 0)
        {
            await db.DlnaImports.Where(i => i.Id == item.ImportId)
                .ExecuteUpdateAsync(s => s.SetProperty(i => i.CopiedBytes, i => i.CopiedBytes + bytes), cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<DlnaImport> UpdateAsync(Guid importId, Action<DlnaImport> change, CancellationToken cancellationToken)
    {
        using (await maintenance.WorkAsync(cancellationToken))
        {
            await using var db = databases.CreateDbContext();
            var import = await db.DlnaImports.SingleAsync(i => i.Id == importId, cancellationToken);
            change(import);
            await db.SaveChangesAsync(cancellationToken);
            return import;
        }
    }

    /// <summary>Ends an import (also when the server is stopping its other work) and scans its library.</summary>
    private async Task FinishAsync(Guid importId, DlnaImportStatus status, string? errorCode)
    {
        using (await maintenance.WorkAsync(CancellationToken.None))
        {
            await using var db = databases.CreateDbContext();
            var import = await db.DlnaImports.Include(i => i.LibrarySource).SingleOrDefaultAsync(i => i.Id == importId);
            if (import is null)
                return;
            // The scan first: whoever sees the import finished must find the scan of its music already queued.
            if (await db.DlnaImportItems.AnyAsync(i => i.ImportId == importId && i.Status == DlnaImportItemStatus.Copied))
                await scans.EnqueueAsync(import.LibrarySource.LibraryId, ScanKind.Incremental, import.RequestedByUserId, CancellationToken.None);
            import.Status = status;
            import.ErrorCode = errorCode;
            import.FinishedAt = time.GetUtcNow().UtcDateTime;
            await db.SaveChangesAsync();
        }
    }

    private string TemporaryPath(string extension)
    {
        var folder = Path.Combine(paths.Cache, "import");
        Directory.CreateDirectory(folder);
        return Path.Combine(folder, $"dlna-{Guid.NewGuid():N}{extension}");
    }

    /// <summary>Leftovers of a stop in the middle of a download.</summary>
    private Task DeleteTemporaryFilesAsync()
    {
        try
        {
            var folder = Path.Combine(paths.Cache, "import");
            if (Directory.Exists(folder))
            {
                foreach (var file in Directory.EnumerateFiles(folder, "dlna-*"))
                    File.Delete(file);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            logger.LogDebug(e, "Could not delete the temporary files of DLNA imports");
        }

        return Task.CompletedTask;
    }

    private static readonly ImportTagsInput NoTags = new(null, null, null, null, null, null, null, null);

    private static string AlbumKey(string? artist, string? album) =>
        TextNormalizer.Normalize(artist ?? "") + '\u001f' + TextNormalizer.Normalize(album ?? "");

    private static string Join(string folder, string name) => folder.Length == 0 ? name : $"{folder}/{name}";

    private sealed record CopyBatch(
        DlnaImport Import,
        LibrarySource Source,
        ILibraryStorage Storage,
        StorageLocation Location,
        PathTemplate Template,
        HashSet<string> MultiDisc)
    {
        /// <summary>Audio copied in this run, to recognize it before the library is scanned.</summary>
        public HashSet<string> Hashes { get; } = new(StringComparer.Ordinal);

        public HashSet<string> ArtworkFolders { get; } = new(StringComparer.Ordinal);

        public int WriteFailures { get; set; }

        public int CopiedSinceScan { get; set; }
    }
}
