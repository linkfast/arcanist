using System.Text.Json.Nodes;
using Arcanist.Core.Application;
using Arcanist.Core.Capabilities;
using Arcanist.Core.Data;
using Arcanist.Core.Data.Entities;
using Arcanist.Core.Organization;
using Arcanist.Core.Setup;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Arcanist.Core.Cloud;

/// <summary>
/// Cloud Drop as a drop folder: the songs of the linked account's Cloud Drop are downloaded (tagged again, as Ultimate
/// clients restore them) to the folder's staging folder, where the drop folder imports them like any other file. Once
/// a song is in the library (or the library had it), it leaves Cloud Drop when the folder moves, or stays there when
/// it copies; either way it is not brought again. Runs every <see cref="OrganizationOptions.PollSeconds"/>, and at
/// once when Ultimate Cloud tells of a change (<see cref="Wake"/>).
/// </summary>
public sealed class CloudDropWorker(
    IArcanistDbContextFactory databases,
    UltimateCloudClient client,
    ICapabilityService capabilities,
    CloudTrackDownloader downloader,
    CloudSyncWorker uploads,
    UltimateCloudAuth auth,
    ImportMonitor imports,
    DatabaseMaintenance maintenance,
    IOptions<OrganizationOptions> options,
    IOptionsMonitor<SetupOptions> setup,
    TimeProvider time,
    ILogger<CloudDropWorker> logger) : BackgroundService
{
    /// <summary>Songs downloaded per run, so a full Cloud Drop arrives in steps the drop folder can keep up with.</summary>
    public const int MaxPerRun = 50;

    private readonly SemaphoreSlim _wake = new(0);

    /// <summary>Looks at Cloud Drop now.</summary>
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
                if (setup.CurrentValue.Completed && databases.IsConfigured && capabilities.IsEnabled(Capability.UltimateCloudSource))
                    await RunAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (UltimateCloudException e)
            {
                logger.LogInformation("Cloud Drop could not be read: {Error}", e.Code);
            }
            catch (Exception e)
            {
                logger.LogError(e, "Bringing songs from Cloud Drop failed");
            }

            try
            {
                await _wake.WaitAsync(TimeSpan.FromSeconds(Math.Max(1, options.Value.PollSeconds)), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private readonly SemaphoreSlim _run = new(1, 1);

    /// <summary>One pass at a time: two at once could bring or upload the same song twice.</summary>
    internal async Task RunAsync(CancellationToken cancellationToken)
    {
        await _run.WaitAsync(cancellationToken);
        try
        {
            await RunOnceAsync(cancellationToken);
        }
        finally
        {
            _run.Release();
        }
    }

    private async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        List<DropFolder> folders;
        await using (var db = databases.CreateDbContext())
        {
            folders = await db.DropFolders.AsNoTracking()
                .Where(d => d.Kind == DropFolderKind.UltimateCloudDrop && d.Enabled)
                .ToListAsync(cancellationToken);
        }

        if (folders.Count == 0)
            return;

        var tracks = (await client.SendAsync($"query CloudDrop {{ cloudDropTracks {{ {CloudTrackDownloader.Fields} }} }}", null, cancellationToken))
            ["cloudDropTracks"]?.AsArray().OfType<JsonNode>().ToList() ?? [];
        var present = tracks.Select(t => CloudTrackDownloader.Text(t, "cloudTrackId")).OfType<string>().ToHashSet(StringComparer.Ordinal);
        foreach (var folder in folders)
        {
            await SettleAsync(folder, present, cancellationToken);
            if (await FetchAsync(folder, tracks, cancellationToken))
                imports.RequestRun();
            if (folder.UploadToCloud)
                await UploadAsync(folder, cancellationToken);
        }
    }

    /// <summary>Songs brought from Cloud Drop, now in the library and identified, go to the account's library.</summary>
    private async Task UploadAsync(DropFolder folder, CancellationToken cancellationToken)
    {
        // Only to the account chosen: with another one linked, the songs wait.
        if (folder.UploadAccount is { } account && (await auth.LinkAsync(cancellationToken))?.Subject != account)
            return;

        List<CloudDropFetch> waiting;
        await using (var db = databases.CreateDbContext())
        {
            waiting = await db.CloudDropFetches.AsNoTracking()
                .Where(f => f.DropFolderId == folder.Id && (f.Upload == CloudDropUpload.Waiting || f.Upload == CloudDropUpload.Failed))
                .OrderBy(f => f.UpdatedAt)
                .Take(MaxPerRun)
                .ToListAsync(cancellationToken);
        }

        foreach (var fetch in waiting)
        {
            Guid? trackId;
            bool identified;
            await using (var db = databases.CreateDbContext())
            {
                trackId = fetch.TrackId ?? await db.MediaFiles
                    .Where(f => f.LibrarySourceId == fetch.LibrarySourceId && f.RelativePath == fetch.LibraryPath && f.MissingSince == null)
                    .Select(f => (Guid?)f.TrackId)
                    .FirstOrDefaultAsync(cancellationToken);
                identified = trackId is not null && await db.Tracks
                    .AnyAsync(t => t.Id == trackId && (t.Recording.AcoustId != null || t.Recording.MusicBrainzId != null), cancellationToken);
            }

            // Not catalogued yet, or not identified yet: looked at again on the next run.
            if (!identified)
                continue;

            var (cloudTrackId, error) = await uploads.UploadTrackAsync(trackId!.Value, cancellationToken);
            using var work = await maintenance.WorkAsync(cancellationToken);
            await using var save = databases.CreateDbContext();
            var row = await save.CloudDropFetches.SingleAsync(f => f.Id == fetch.Id, cancellationToken);
            row.TrackId = trackId;
            row.Upload = cloudTrackId is not null ? CloudDropUpload.Uploaded : CloudDropUpload.Failed;
            row.UploadedTrackId = cloudTrackId;
            row.ErrorCode = error;
            row.UpdatedAt = Now();
            await save.SaveChangesAsync(cancellationToken);
            if (cloudTrackId is not null)
                logger.LogInformation("{Title} was identified and uploaded to the account's library", fetch.Title);
        }
    }

    /// <summary>What the drop folder made of the songs it was given: off Cloud Drop, kept there, or discarded.</summary>
    private async Task SettleAsync(DropFolder folder, IReadOnlySet<string> present, CancellationToken cancellationToken)
    {
        using var work = await maintenance.WorkAsync(cancellationToken);
        await using var db = databases.CreateDbContext();
        var staged = await db.CloudDropFetches.Where(f => f.DropFolderId == folder.Id && f.State == CloudDropFetchState.Staged).ToListAsync(cancellationToken);
        if (staged.Count == 0)
            return;

        var names = staged.Select(f => f.FileName).ToList();
        var items = (await db.ImportItems.Where(i => i.DropFolderId == folder.Id && names.Contains(i.RelativePath)).ToListAsync(cancellationToken))
            .GroupBy(i => i.RelativePath, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(i => i.UpdatedAt).First(), StringComparer.Ordinal);

        foreach (var fetch in staged)
        {
            var file = Path.Combine(folder.Path, fetch.FileName);
            items.TryGetValue(fetch.FileName, out var item);
            switch (item?.Status)
            {
                case ImportStatus.Imported:
                case ImportStatus.Duplicate:
                    (fetch.LibrarySourceId, fetch.LibraryPath, fetch.TrackId) = (item.DestinationSourceId, item.DestinationPath, item.TrackId);
                    if (folder.UploadToCloud)
                        fetch.Upload = CloudDropUpload.Waiting;
                    if (item.Status == ImportStatus.Duplicate)
                    {
                        // The library has the song: the downloaded copy is not needed.
                        File.Delete(file);
                        item.Status = ImportStatus.Discarded;
                        item.UpdatedAt = Now();
                    }

                    await LeaveCloudDropAsync(folder, fetch, cancellationToken);
                    break;
                case ImportStatus.Discarded:
                    fetch.State = CloudDropFetchState.Discarded;
                    break;
                case null when !present.Contains(fetch.CloudTrackId):
                    // Taken off Cloud Drop elsewhere before it was imported.
                    File.Delete(file);
                    db.CloudDropFetches.Remove(fetch);
                    continue;
                default:
                    continue;
            }

            fetch.UpdatedAt = Now();
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task LeaveCloudDropAsync(DropFolder folder, CloudDropFetch fetch, CancellationToken cancellationToken)
    {
        if (folder.Mode == DropFolderMode.Copy)
        {
            fetch.State = CloudDropFetchState.Kept;
            return;
        }

        try
        {
            await client.SendAsync("mutation Remove($id: String!) { removeCloudTrack(trackId: $id) }", new { id = fetch.CloudTrackId }, cancellationToken);
        }
        catch (UltimateCloudException e) when (e.Message.Contains("not found", StringComparison.OrdinalIgnoreCase))
        {
            // Gone already.
        }

        fetch.State = CloudDropFetchState.Removed;
        logger.LogInformation("{Title} is in the library and left Cloud Drop", fetch.Title);
    }

    /// <summary>Downloads the songs of Cloud Drop the folder was not given yet (and retries those that failed).</summary>
    private async Task<bool> FetchAsync(DropFolder folder, IReadOnlyList<JsonNode> tracks, CancellationToken cancellationToken)
    {
        Dictionary<string, CloudDropFetch> known;
        await using (var db = databases.CreateDbContext())
        {
            known = await db.CloudDropFetches.AsNoTracking().Where(f => f.DropFolderId == folder.Id)
                .ToDictionaryAsync(f => f.CloudTrackId, StringComparer.Ordinal, cancellationToken);
        }

        var wanted = tracks
            .Select(t => (Track: t, Id: CloudTrackDownloader.Text(t, "cloudTrackId")))
            .Where(t => t.Id is not null && (!known.TryGetValue(t.Id, out var fetch) || fetch.State == CloudDropFetchState.Failed))
            .Take(MaxPerRun)
            .ToList();
        if (wanted.Count == 0)
            return false;

        Directory.CreateDirectory(folder.Path);
        var fetched = false;
        foreach (var (track, id) in wanted)
        {
            var title = CloudTrackDownloader.Text(track, "title");
            var name = FileName(track, id!);
            // Written under a hidden name, then renamed: the drop folder never sees half a file.
            var partial = Path.Combine(folder.Path, $".arcanist-partial-{Guid.NewGuid():N}{Path.GetExtension(name)}");
            var error = await downloader.DownloadAsync(track, id!, partial, cancellationToken);
            if (error is null)
            {
                File.Move(partial, Path.Combine(folder.Path, name), overwrite: true);
                fetched = true;
            }
            else
                File.Delete(partial);

            using var work = await maintenance.WorkAsync(cancellationToken);
            await using var db = databases.CreateDbContext();
            var fetch = await db.CloudDropFetches.SingleOrDefaultAsync(f => f.DropFolderId == folder.Id && f.CloudTrackId == id, cancellationToken);
            if (fetch is null)
                db.CloudDropFetches.Add(fetch = new CloudDropFetch { DropFolderId = folder.Id, CloudTrackId = id!, FileName = name, CreatedAt = Now() });
            fetch.Title = title is { Length: > 500 } ? title[..500] : title;
            fetch.State = error is null ? CloudDropFetchState.Staged : CloudDropFetchState.Failed;
            fetch.ErrorCode = error;
            fetch.UpdatedAt = Now();
            await db.SaveChangesAsync(cancellationToken);
        }

        return fetched;
    }

    /// <summary>"Artist - Title [id].ext": readable in the review queue, and never the same for two songs.</summary>
    private static string FileName(JsonNode track, string id)
    {
        var label = string.Join(" - ", new[] { CloudTrackDownloader.Text(track, "artist"), CloudTrackDownloader.Text(track, "title") }.OfType<string>());
        var safe = new string((label.Length > 0 ? label : "Cloud Drop").Select(c => Path.GetInvalidFileNameChars().Contains(c) || c is '/' or '\\' ? '_' : c).ToArray()).Trim(' ', '.');
        if (safe.Length > 120)
            safe = safe[..120];
        var shortId = id.Length > 8 ? id[..8] : id;
        return $"{safe} [{shortId}]{UltimateCloudStorage.Extension(CloudTrackDownloader.Text(track, "audioFormat"))}";
    }

    private DateTime Now() => time.GetUtcNow().UtcDateTime;
}
