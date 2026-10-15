using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using Arcanist.Core.Application;
using Arcanist.Core.Capabilities;
using Arcanist.Core.Data;
using Arcanist.Core.Data.Entities;
using Arcanist.Core.Hosting;
using Arcanist.Core.Organization;
using Arcanist.Core.Scanning;
using Arcanist.Core.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Arcanist.Core.Cloud;

/// <summary>What a sync is doing now.</summary>
public sealed record CloudSyncProgress(Guid LibraryId, string Phase, int Done, int Total);

/// <summary>
/// Syncs libraries with the linked Ultimate Cloud account (Cloud plan), one library at a time: every 6 hours, and
/// when asked. A run pairs what is on both sides by Ultimate's audio hash (asking Ultimate Cloud, as its clients do),
/// uploads what only the library has (<see cref="CloudSyncMode.Upload"/>: a tag-free copy, as Ultimate clients upload)
/// and downloads what only the account has (<see cref="CloudSyncMode.Download"/>: tagged again, and placed with the
/// library's template). <b>Nothing is ever deleted on either side</b>: removing a song from one does not remove it
/// from the other. Pairings are kept (<see cref="CloudSyncItem"/>), so each song is hashed, uploaded or downloaded once.
/// </summary>
public sealed class CloudSyncWorker(
    IArcanistDbContextFactory databases,
    UltimateCloudClient client,
    IUltimateAudio audio,
    ICapabilityService capabilities,
    ILibraryStorageResolver storages,
    LibraryLocks locks,
    ScanCoordinator scans,
    ArtworkStore artworkStore,
    DatabaseMaintenance maintenance,
    IHttpClientFactory clients,
    CloudTrackDownloader downloader,
    IOptionsMonitor<Setup.SetupOptions> setup,
    ArcanistPaths paths,
    TimeProvider time,
    ILogger<CloudSyncWorker> logger) : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromHours(6);
    private const long MaxCoverBytes = 5 * 1024 * 1024;

    private const string CloudFields = CloudTrackDownloader.Fields;

    private readonly SemaphoreSlim _wake = new(0);

    public CloudSyncProgress? Progress { get; private set; }

    public void Wake()
    {
        if (_wake.CurrentCount == 0)
            _wake.Release();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (await NextAsync(stoppingToken) is { } libraryId)
                {
                    await RunAsync(libraryId, stoppingToken);
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
                logger.LogError(e, "Syncing with Ultimate Cloud failed");
                await Task.Delay(TimeSpan.FromMinutes(1), time, stoppingToken).ContinueWith(_ => { }, TaskScheduler.Default);
            }
        }
    }

    private async Task<Guid?> NextAsync(CancellationToken cancellationToken)
    {
        if (!databases.IsConfigured || !setup.CurrentValue.Completed || !capabilities.IsEnabled(Capability.CloudSync))
            return null;
        var due = time.GetUtcNow().UtcDateTime - Interval;
        return await DbAsync(db => db.LibraryCloudSyncs.AsNoTracking()
            .Where(s => s.Mode != CloudSyncMode.Off && (s.Requested || s.LastRunAt == null || s.LastRunAt < due))
            .OrderByDescending(s => s.Requested).ThenBy(s => s.LastRunAt)
            .Select(s => (Guid?)s.LibraryId)
            .FirstOrDefaultAsync(cancellationToken), cancellationToken);
    }

    private async Task RunAsync(Guid libraryId, CancellationToken cancellationToken)
    {
        var sync = await DbAsync(async db =>
        {
            var row = await db.LibraryCloudSyncs.SingleAsync(s => s.LibraryId == libraryId, cancellationToken);
            row.Requested = false;
            await db.SaveChangesAsync(cancellationToken);
            return row;
        }, cancellationToken);

        string? error;
        try
        {
            error = !capabilities.IsEnabled(Capability.CloudSync) ? ErrorCodes.CloudSyncUnavailable
                : !audio.IsInstalled ? ErrorCodes.CloudToolMissing
                : await SyncAsync(sync, cancellationToken);
        }
        catch (UltimateCloudException e)
        {
            error = e.Code;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            logger.LogError(e, "Syncing library {Library} with Ultimate Cloud failed", libraryId);
            error = ErrorCodes.CloudSyncFailed;
        }
        finally
        {
            Progress = null;
        }

        if (error is not null)
            logger.LogWarning("Syncing library {Library} with Ultimate Cloud stopped: {Error}", libraryId, error);
        await DbAsync(db => db.LibraryCloudSyncs.Where(s => s.LibraryId == libraryId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.LastRunAt, time.GetUtcNow().UtcDateTime).SetProperty(x => x.ErrorCode, error),
                CancellationToken.None), CancellationToken.None);
    }

    /// <summary>One run; returns why it stopped early, or null.</summary>
    private async Task<string?> SyncAsync(LibraryCloudSync sync, CancellationToken cancellationToken)
    {
        var library = await DbAsync(db => db.Libraries.AsNoTracking().Include(l => l.Sources)
            .SingleAsync(l => l.Id == sync.LibraryId, cancellationToken), cancellationToken);
        var sources = library.Sources.Where(s => s.Kind == LibrarySourceKind.Local).ToList();
        if (sources.Count == 0)
            return ErrorCodes.ReadOnly;
        var upload = sync.Mode is CloudSyncMode.Upload or CloudSyncMode.Both;
        var download = sync.Mode is CloudSyncMode.Download or CloudSyncMode.Both;

        Progress = new CloudSyncProgress(library.Id, "listing", 0, 0);
        var cloud = (await client.SendAsync($"query Library {{ library {{ {CloudFields} }} }}", null, cancellationToken))["library"]?.AsArray()
            .OfType<JsonNode>().ToList() ?? [];
        var items = await DbAsync(db => db.CloudSyncItems.Where(i => i.LibraryId == library.Id).ToListAsync(cancellationToken), cancellationToken);
        var locals = await LocalSongsAsync(library.Id, sources, cancellationToken);

        // The library's side: pair, or upload, what has not been paired yet (failures are tried again).
        string? stop = null;
        var byHash = items.Where(i => i.ContentHash is not null).GroupBy(i => i.ContentHash!).ToDictionary(g => g.Key, g => g.First());
        var pending = locals.Where(l => !byHash.TryGetValue(l.ContentHash, out var item)
                                        || item.Status == CloudSyncItemStatus.Failed
                                        || item.Status == CloudSyncItemStatus.LocalOnly && upload).ToList();
        Progress = new CloudSyncProgress(library.Id, upload ? "uploading" : "matching", 0, pending.Count);
        var uploadStopped = false;
        foreach (var song in pending)
        {
            cancellationToken.ThrowIfCancellationRequested();
            byHash.TryGetValue(song.ContentHash, out var item);
            item ??= await AddItemAsync(new CloudSyncItem { LibraryId = library.Id, ContentHash = song.ContentHash, Title = song.Title }, cancellationToken);
            var outcome = await LocalAsync(song, item, upload && !uploadStopped, cancellationToken);
            if (outcome is "cloud_quota_exceeded" or "cloud_read_only")
            {
                // No room left (or a lapsed subscription): nothing more goes up, the rest still pairs and comes down.
                uploadStopped = true;
                stop = outcome;
            }

            Progress = Progress with { Done = Progress.Done + 1 };
        }

        // The account's side: pair what the library has under another name, download the rest.
        items = await DbAsync(db => db.CloudSyncItems.AsNoTracking().Where(i => i.LibraryId == library.Id).ToListAsync(cancellationToken), cancellationToken);
        var paired = items.Where(i => i.CloudTrackId is not null).Select(i => i.CloudTrackId!).ToHashSet(StringComparer.Ordinal);
        var localOnly = items.Where(i => i.Status == CloudSyncItemStatus.LocalOnly && i.MatchKey is not null && i.AudioHash is not null)
            .GroupBy(i => i.MatchKey!).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
        var missing = cloud.Where(t => t["cloudTrackId"]?.GetValue<string>() is { } id && !paired.Contains(id)).ToList();
        var failedDownloads = items.Where(i => i.Status == CloudSyncItemStatus.Failed && i.ContentHash is null && i.CloudTrackId is not null)
            .ToDictionary(i => i.CloudTrackId!, StringComparer.Ordinal);
        Progress = new CloudSyncProgress(library.Id, download ? "downloading" : "matching", 0, missing.Count);
        var target = sources.FirstOrDefault(s => s.Id == sync.DownloadSourceId) ?? sources[0];
        var template = PathTemplate.TryParse(library.OrganizeTemplate, out var parsed, out _) ? parsed : PathTemplate.DefaultTemplate;
        var downloaded = 0;
        foreach (var track in missing)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var id = track["cloudTrackId"]!.GetValue<string>();
            if (!await PairByNameAsync(track, id, localOnly, cancellationToken) && download)
            {
                if (await DownloadAsync(library.Id, track, id, failedDownloads.GetValueOrDefault(id), target, template, cancellationToken))
                    downloaded++;
                if (downloaded > 0 && downloaded % 200 == 0)
                    await DbAsync(_ => scans.EnqueueAsync(library.Id, ScanKind.Incremental, null, cancellationToken), cancellationToken);
            }

            Progress = Progress with { Done = Progress.Done + 1 };
        }

        // The scan first: whoever sees the run finished finds the scan of what came down already queued.
        if (downloaded > 0)
            await DbAsync(_ => scans.EnqueueAsync(library.Id, ScanKind.Incremental, null, cancellationToken), cancellationToken);
        logger.LogInformation("Synced library {Library} with Ultimate Cloud: {Pending} songs checked here, {Missing} there", library.Name, pending.Count, missing.Count);
        return stop;
    }

    /// <summary>A song of the library, as the catalog knows it (one copy per audio: the best).</summary>
    private sealed record LocalSong(
        string ContentHash, Guid SourceId, string RelativePath, string Title, string? Artist, string? Album, string? Genre, int? Year,
        int? TrackNumber, int? DiscNumber, string? Format, int? BitRate, int? SampleRate, int? BitDepth, int? Channels, int DurationMs,
        Guid? RecordingMbid, Guid? AcoustId, string? ArtworkHash, string? ArtworkMime)
    {
        public string MatchKey => Key(Artist, Album, Title);
    }

    private static readonly System.Linq.Expressions.Expression<Func<MediaFile, LocalSong>> ToSong = f => new LocalSong(
        f.ContentHash, f.LibrarySourceId, f.RelativePath, f.Track.Title,
        f.Track.Artists.Where(a => a.Role == ArtistRole.Primary).OrderBy(a => a.Position).Select(a => a.Artist.Name).FirstOrDefault(),
        f.Track.Album.Title,
        f.Track.Genres.Select(g => g.Genre.Name).FirstOrDefault(),
        f.Track.Album.Year, f.Track.TrackNumber, f.Track.DiscNumber, f.Format, f.BitRate, f.SampleRate, f.BitDepth, f.Channels, f.DurationMs,
        f.Track.Recording.MusicBrainzId, f.Track.Recording.AcoustId,
        f.Track.Artwork != null ? f.Track.Artwork.Hash : f.Track.Album.Artwork != null ? f.Track.Album.Artwork.Hash : null,
        f.Track.Artwork != null ? f.Track.Artwork.MimeType : f.Track.Album.Artwork != null ? f.Track.Album.Artwork.MimeType : null);

    private async Task<List<LocalSong>> LocalSongsAsync(Guid libraryId, List<LibrarySource> sources, CancellationToken cancellationToken)
    {
        var sourceIds = sources.Select(s => s.Id).ToList();
        var rows = await DbAsync(db => db.MediaFiles.AsNoTracking()
            // Whole files only: a song of an album file split by a cue sheet cannot be uploaded on its own.
            .Where(f => sourceIds.Contains(f.LibrarySourceId) && f.MissingSince == null && f.CueTrack == 0)
            .OrderByDescending(f => f.BitDepth).ThenByDescending(f => f.SampleRate).ThenByDescending(f => f.BitRate)
            .Select(ToSong)
            .ToListAsync(cancellationToken), cancellationToken);
        return rows.DistinctBy(r => r.ContentHash).ToList();
    }

    /// <summary>
    /// Uploads one song of a library to the account's library, whatever the library's sync mode (as Cloud Drop's
    /// drop folder does with the songs it brought, once identified). Its best whole local copy goes, tag-free, with
    /// the catalog's description; one the account already has is only paired. Kept as the library's sync items, so
    /// syncing never uploads it again.
    /// </summary>
    /// <returns>The track's id in Ultimate Cloud, or why it was not uploaded.</returns>
    public async Task<(string? CloudTrackId, string? ErrorCode)> UploadTrackAsync(Guid trackId, CancellationToken cancellationToken)
    {
        if (!audio.IsInstalled)
            return (null, ErrorCodes.CloudToolMissing);
        var song = await DbAsync(db => db.MediaFiles.AsNoTracking()
            .Where(f => f.TrackId == trackId && f.MissingSince == null && f.CueTrack == 0 && f.LibrarySource.Kind == LibrarySourceKind.Local)
            .OrderByDescending(f => f.BitDepth).ThenByDescending(f => f.SampleRate).ThenByDescending(f => f.BitRate)
            .Select(ToSong)
            .FirstOrDefaultAsync(cancellationToken), cancellationToken);
        if (song is null)
            return (null, ErrorCodes.NotFound);

        var libraryId = await DbAsync(db => db.LibrarySources.Where(s => s.Id == song.SourceId).Select(s => s.LibraryId).SingleAsync(cancellationToken), cancellationToken);
        var item = await DbAsync(db => db.CloudSyncItems.AsNoTracking().FirstOrDefaultAsync(i => i.LibraryId == libraryId && i.ContentHash == song.ContentHash, cancellationToken),
            cancellationToken);
        if (item is { CloudTrackId: { } known, Status: CloudSyncItemStatus.Uploaded or CloudSyncItemStatus.Matched })
            return (known, null);

        item ??= await AddItemAsync(new CloudSyncItem { LibraryId = libraryId, ContentHash = song.ContentHash, Title = Cut(song.Title)! }, cancellationToken);
        var stop = await LocalAsync(song, item, upload: true, cancellationToken);
        return item.Status is CloudSyncItemStatus.Uploaded or CloudSyncItemStatus.Matched
            ? (item.CloudTrackId, null)
            : (null, stop ?? item.Reason ?? ErrorCodes.UploadFailed);
    }

    /// <summary>Pairs a song of the library with Ultimate Cloud, uploading it when asked to. Returns the stop reason of an upload refused for the whole account.</summary>
    private async Task<string?> LocalAsync(LocalSong song, CloudSyncItem item, bool upload, CancellationToken cancellationToken)
    {
        var storage = storages.For(LibrarySourceKind.Local);
        var location = await DbAsync(async db => StorageLocation.From(await db.LibrarySources.AsNoTracking().SingleAsync(s => s.Id == song.SourceId, cancellationToken)),
            cancellationToken);
        var path = storage.TryGetLocalPath(location, song.RelativePath);
        item.Title = Cut(song.Title)!;
        item.Path = song.RelativePath;
        item.MatchKey = song.MatchKey;
        if (path is null || !File.Exists(path))
            return await SaveAsync(item, CloudSyncItemStatus.Failed, ErrorCodes.NotFound, cancellationToken);

        var stripped = Path.Combine(paths.Cache, "sync", $"{Guid.NewGuid():N}{Path.GetExtension(path)}");
        Directory.CreateDirectory(Path.GetDirectoryName(stripped)!);
        try
        {
            string sha1 = "";
            long size = 0;
            try
            {
                if (upload)
                    (item.AudioHash, sha1, size) = await audio.StripAsync(path, stripped, cancellationToken);
                else
                    item.AudioHash ??= await audio.HashAsync(path, cancellationToken);
            }
            catch (UltimateCloudException e) when (e.Code.StartsWith("tool:", StringComparison.Ordinal))
            {
                return await SaveAsync(item, CloudSyncItemStatus.Failed, Cut(e.Code), cancellationToken);
            }

            var check = (await client.SendAsync(
                "mutation Check($h: String!, $a: String) { checkCloudFile(audioHash: $h, acoustId: $a) { blobExists cloudFileId alreadyInLibrary existingTrackId } }",
                new { h = item.AudioHash, a = song.AcoustId?.ToString() }, cancellationToken))["checkCloudFile"]!;
            if (check["alreadyInLibrary"]?.GetValue<bool>() == true && check["existingTrackId"]?.GetValue<string>() is { } existing)
            {
                item.CloudTrackId = existing;
                return await SaveAsync(item, CloudSyncItemStatus.Matched, null, cancellationToken);
            }

            if (!upload)
                return await SaveAsync(item, CloudSyncItemStatus.LocalOnly, null, cancellationToken);

            try
            {
                var cloudFileId = check["blobExists"]?.GetValue<bool>() == true ? check["cloudFileId"]?.GetValue<string>() : null;
                cloudFileId ??= await UploadBlobAsync(song, item.AudioHash!, stripped, sha1, size, cancellationToken);
                var cover = song.ArtworkHash is null ? null : artworkStore.PathFor(song.ArtworkHash, song.ArtworkMime!);
                var coverBytes = cover is not null && File.Exists(cover) && new FileInfo(cover).Length <= MaxCoverBytes ? await File.ReadAllBytesAsync(cover, cancellationToken) : null;
                var added = (await client.SendAsync(
                    "mutation Add($f: String!, $i: CloudTrackInput!) { addCloudTrack(cloudFileId: $f, input: $i) { cloudTrackId } }",
                    new
                    {
                        f = cloudFileId,
                        i = new
                        {
                            title = song.Title, artist = song.Artist, album = song.Album, genre = song.Genre, trackNumber = song.TrackNumber,
                            discNumber = song.DiscNumber, year = song.Year, acoustId = song.AcoustId?.ToString(),
                            musicBrainzRecordingId = song.RecordingMbid?.ToString(),
                            coverArt = coverBytes?.Select(b => (int)b).ToArray(),
                        },
                    }, cancellationToken))["addCloudTrack"]!;
                item.CloudTrackId = added["cloudTrackId"]!.GetValue<string>();
                return await SaveAsync(item, CloudSyncItemStatus.Uploaded, null, cancellationToken);
            }
            catch (UltimateCloudException e) when (e.Code is "QUOTA_EXCEEDED" or "INSUFFICIENT_FREE_SPACE" or "CLOUD_READ_ONLY")
            {
                var code = e.Code == "CLOUD_READ_ONLY" ? ErrorCodes.CloudReadOnly : ErrorCodes.CloudQuotaExceeded;
                await SaveAsync(item, CloudSyncItemStatus.LocalOnly, null, cancellationToken);
                return code;
            }
            catch (Exception e) when (e is IOException or HttpRequestException)
            {
                logger.LogWarning("Uploading {Path} to Ultimate Cloud failed: {Error}", song.RelativePath, e.Message);
                return await SaveAsync(item, CloudSyncItemStatus.Failed, ErrorCodes.UploadFailed, cancellationToken);
            }
        }
        finally
        {
            File.Delete(stripped);
        }
    }

    /// <summary>Uploads a tag-free copy to B2 as Ultimate clients do, and registers it. Returns its CloudFile id.</summary>
    private async Task<string> UploadBlobAsync(LocalSong song, string audioHash, string stripped, string sha1, long size, CancellationToken cancellationToken)
    {
        var target = (await client.SendAsync(
            "mutation Upload($h: String!, $s: Long!) { uploadUrl(audioHash: $h, declaredSizeBytes: $s) { uploadUrl authorizationToken filePrefix } }",
            new { h = audioHash, s = size }, cancellationToken))["uploadUrl"]!;
        var fileName = target["filePrefix"]!.GetValue<string>() + audioHash;
        var contentType = AudioFormats.MimeType(stripped) is var mime && mime != "application/octet-stream" ? mime : "b2/x-auto";

        await using var body = File.OpenRead(stripped);
        using var request = new HttpRequestMessage(HttpMethod.Post, target["uploadUrl"]!.GetValue<string>()) { Content = new StreamContent(body) };
        request.Headers.TryAddWithoutValidation("Authorization", target["authorizationToken"]!.GetValue<string>());
        request.Headers.Add("X-Bz-File-Name", Uri.EscapeDataString(fileName));
        request.Headers.Add("X-Bz-Content-Sha1", sha1);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        request.Content.Headers.ContentLength = size;
        using var response = await clients.CreateClient(UltimateCloudStorage.FilesClientName).SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new IOException($"B2 answered {(int)response.StatusCode}.");
        var b2FileId = JsonNode.Parse(await response.Content.ReadAsStringAsync(cancellationToken))?["fileId"]?.GetValue<string>()
                       ?? throw new IOException("B2 did not give the file's id.");

        var registered = (await client.SendAsync(
            "mutation Register($i: RegisterCloudUploadInput!) { registerCloudUpload(input: $i) { cloudFileId } }",
            new
            {
                i = new
                {
                    fileName, b2FileId, audioHash,
                    format = (song.Format ?? Path.GetExtension(song.RelativePath).TrimStart('.')).ToUpperInvariant(),
                    contentType,
                    bitrate = song.BitRate ?? 0, sampleRate = song.SampleRate ?? 0, bitDepth = song.BitDepth, channels = song.Channels,
                    isLossless = AudioFormats.IsLossless(song.RelativePath),
                    durationSeconds = song.DurationMs / 1000.0,
                    contentLength = size,
                    acoustId = song.AcoustId?.ToString(),
                    musicBrainzRecordingId = song.RecordingMbid?.ToString(),
                },
            }, cancellationToken))["registerCloudUpload"]!;
        return registered["cloudFileId"]!.GetValue<string>();
    }

    /// <summary>A track of the account that a song of the library already is: same artist, album and title, confirmed by Ultimate Cloud with the song's audio hash.</summary>
    private async Task<bool> PairByNameAsync(JsonNode track, string id, Dictionary<string, List<CloudSyncItem>> localOnly, CancellationToken cancellationToken)
    {
        if (!localOnly.TryGetValue(Key(Text(track, "artist"), Text(track, "album"), Text(track, "title")), out var candidates))
            return false;
        foreach (var candidate in candidates.ToList())
        {
            var check = (await client.SendAsync(
                "mutation Check($h: String!) { checkCloudFile(audioHash: $h) { alreadyInLibrary existingTrackId } }",
                new { h = candidate.AudioHash }, cancellationToken))["checkCloudFile"]!;
            if (check["existingTrackId"]?.GetValue<string>() != id)
                continue;
            candidate.CloudTrackId = id;
            await SaveAsync(candidate, CloudSyncItemStatus.Matched, null, cancellationToken);
            candidates.Remove(candidate);
            return true;
        }

        return false;
    }

    /// <summary>Downloads a track of the account into the library folder, tagged as Ultimate clients restore it.</summary>
    private async Task<bool> DownloadAsync(
        Guid libraryId, JsonNode track, string id, CloudSyncItem? item, LibrarySource target, PathTemplate template, CancellationToken cancellationToken)
    {
        item ??= await AddItemAsync(new CloudSyncItem { LibraryId = libraryId, CloudTrackId = id, Title = Cut(Text(track, "title") ?? id)! }, cancellationToken);
        var folder = Path.Combine(paths.Cache, "sync");
        Directory.CreateDirectory(folder);
        var file = Path.Combine(folder, $"{Guid.NewGuid():N}{UltimateCloudStorage.Extension(Text(track, "audioFormat"))}");
        try
        {
            if (await downloader.DownloadAsync(track, id, file, cancellationToken) is { } error)
            {
                await SaveAsync(item, CloudSyncItemStatus.Failed, error, cancellationToken);
                return false;
            }

            var artist = Text(track, "artist");
            var album = Text(track, "album");
            var title = Text(track, "title");
            int? Int(string name) => track[name] is JsonValue value && value.TryGetValue<int>(out var number) && number > 0 ? number : null;

            string contentHash;
            try
            {
                await using var stream = File.OpenRead(file);
                var metadata = TagReader.Read(stream, AudioFormats.MimeType(file));
                contentHash = await ContentHasher.HashAsync(stream, metadata.AudioDataOffset, metadata.AudioDataSize, cancellationToken);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                logger.LogWarning("{Title} from Ultimate Cloud cannot be read as audio: {Error}", title, e.Message);
                await SaveAsync(item, CloudSyncItemStatus.Failed, ErrorCodes.Unreadable, cancellationToken);
                return false;
            }

            var storage = storages.For(target.Kind);
            var location = StorageLocation.From(target);
            var disc = Int("discNumber");
            var wanted = template.Render(new PathValues(null, artist, album, Int("year"), disc, disc > 1, Int("trackNumber"), title, Text(track, "genre")));
            item.ContentHash = contentHash;
            item.MatchKey = Key(artist, album, title);
            using (await locks.AcquireForWritingAsync(libraryId, cancellationToken))
            {
                // The library has the same audio already (the scan has not paired it yet): nothing to copy.
                if (await DbAsync(db => db.MediaFiles.AnyAsync(f => f.ContentHash == contentHash && f.MissingSince == null && f.LibrarySource.LibraryId == libraryId, cancellationToken), cancellationToken))
                {
                    await SaveAsync(item, CloudSyncItemStatus.Matched, null, cancellationToken);
                    return false;
                }

                var extension = Path.GetExtension(file);
                var destination = wanted + extension;
                for (var copy = 2; await storage.ExistsAsync(location, destination, cancellationToken); copy++)
                    destination = $"{wanted} ({copy}){extension}";
                await storage.ImportAsync(location, destination, file, cancellationToken);
                item.Path = destination;
            }

            await SaveAsync(item, CloudSyncItemStatus.Downloaded, null, cancellationToken);
            return true;
        }
        catch (UltimateCloudException e) when (e.Message.Contains("not found", StringComparison.OrdinalIgnoreCase))
        {
            await SaveAsync(item, CloudSyncItemStatus.Failed, ErrorCodes.NotFound, cancellationToken);
            return false;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(e, "Could not place {Title} from Ultimate Cloud in the library", Text(track, "title"));
            await SaveAsync(item, CloudSyncItemStatus.Failed, ErrorCodes.WriteFailed, cancellationToken);
            return false;
        }
        finally
        {
            File.Delete(file);
        }
    }

    private async Task<CloudSyncItem> AddItemAsync(CloudSyncItem item, CancellationToken cancellationToken)
    {
        item.UpdatedAt = time.GetUtcNow().UtcDateTime;
        item.Status = CloudSyncItemStatus.Failed;
        item.Reason = ErrorCodes.Interrupted;
        await DbAsync(async db =>
        {
            db.CloudSyncItems.Add(item);
            await db.SaveChangesAsync(cancellationToken);
            return 0;
        }, cancellationToken);
        return item;
    }

    private async Task<string?> SaveAsync(CloudSyncItem item, CloudSyncItemStatus status, string? reason, CancellationToken cancellationToken)
    {
        item.Status = status;
        item.Reason = reason;
        item.UpdatedAt = time.GetUtcNow().UtcDateTime;
        await DbAsync(async db =>
        {
            db.CloudSyncItems.Update(item);
            await db.SaveChangesAsync(cancellationToken);
            return 0;
        }, cancellationToken);
        return null;
    }

    /// <summary>A short unit of database work, with its own context (a migration may switch databases in between).</summary>
    private async Task<T> DbAsync<T>(Func<ArcanistDbContext, Task<T>> work, CancellationToken cancellationToken)
    {
        using (await maintenance.WorkAsync(cancellationToken))
        {
            await using var db = databases.CreateDbContext();
            return await work(db);
        }
    }

    internal static string Key(string? artist, string? album, string? title) =>
        string.Join('\u001f', TextNormalizer.Normalize(artist ?? ""), TextNormalizer.Normalize(album ?? ""), TextNormalizer.Normalize(title ?? ""));

    private static string? Text(JsonNode track, string name) =>
        track[name]?.GetValue<string>() is { } value && !string.IsNullOrWhiteSpace(value) ? value.Trim() : null;

    private static string? Cut(string? text) => text is { Length: > 500 } ? text[..500] : text;
}
