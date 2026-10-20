using Arcanist.Core.Caching;
using Arcanist.Core.Data;
using Arcanist.Core.Data.Entities;
using Arcanist.Core.Scanning;
using Arcanist.Core.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Arcanist.Core.Media;

/// <summary>How far the versions of one format are. Part of the API contract.</summary>
/// <param name="Done">Songs that have it.</param>
/// <param name="Total">Songs that will have it (those that gain from it: never from a smaller lossy file).</param>
/// <param name="Bytes">What the versions made take.</param>
/// <param name="EstimatedBytes">What all of them will take, from the songs' lengths.</param>
public sealed record PregenerateProgress(TranscodeFormat Format, int BitRate, int Done, int Total, long Bytes, long EstimatedBytes);

/// <summary>
/// Passive transcoding: makes the versions the settings ask for of every song of the chosen libraries (local and
/// S3 ones; other servers' and Ultimate Cloud's are not this server's to copy), one at a time and below normal
/// priority, waiting while anyone is listening. Removes versions nobody asks for any more (a format switched off,
/// a song gone), and the conversion cache's old entries.
/// </summary>
public sealed class PretranscodeWorker(
    IArcanistDbContextFactory databases,
    TranscodingSettings settings,
    TranscodeStore store,
    Transcoder transcoder,
    FfmpegLocator ffmpeg,
    ILibraryStorageResolver storages,
    StreamActivity streams,
    IOptionsMonitor<Setup.SetupOptions> setup,
    TimeProvider time,
    ILogger<PretranscodeWorker> logger) : BackgroundService
{
    private readonly SemaphoreSlim _wake = new(0, 1);
    private volatile IReadOnlyList<PregenerateProgress> _progress = [];
    private volatile bool _working;
    private DateTime _purgedAt;

    public IReadOnlyList<PregenerateProgress> Progress => _progress;

    public bool Working => _working;

    /// <summary>Starts a pass now (the settings changed, or a library was scanned).</summary>
    public void Wake()
    {
        try
        {
            _wake.Release();
        }
        catch (SemaphoreFullException)
        {
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        settings.Changed += Wake;
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                if (setup.CurrentValue.Completed)
                {
                    try
                    {
                        await PassAsync(stoppingToken);
                    }
                    catch (Exception e) when (e is not OperationCanceledException)
                    {
                        logger.LogWarning(e, "Making the versions of the music failed; trying again later");
                    }
                    finally
                    {
                        _working = false;
                    }
                }

                await _wake.WaitAsync(TimeSpan.FromMinutes(30), stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        finally
        {
            settings.Changed -= Wake;
        }
    }

    private sealed record Song(string ContentHash, LibrarySourceKind Kind, string Location, string? Settings, string RelativePath,
        int? StartMs, int? EndMs, int DurationMs, int? BitRate, long Size);

    private async Task PassAsync(CancellationToken cancellationToken)
    {
        var config = await settings.GetAsync(cancellationToken);
        var now = time.GetUtcNow().UtcDateTime;
        if (now - _purgedAt > TimeSpan.FromHours(1))
        {
            store.Cache.Purge(TimeSpan.FromDays(config.CacheMaxAgeDays));
            store.Cache.Trim();
            _purgedAt = now;
        }

        if (config.Pregenerate.Count == 0)
        {
            store.Pregenerated.Clear();
            _progress = [];
            return;
        }

        var songs = await SongsAsync(config, cancellationToken);
        var work = config.Pregenerate
            .SelectMany(target => songs.Where(song => Gains(song, target, config)).Select(song => (Target: target, Song: song)))
            .ToList();
        var wanted = work.Select(w => KeyOf(w.Song, w.Target)).ToHashSet(StringComparer.Ordinal);
        store.Pregenerated.RemoveWhere(key => !wanted.Contains(key));
        Report(config, work);

        if (await ffmpeg.GetAsync(cancellationToken) is null)
            return;
        var failed = 0;
        foreach (var (target, song) in work)
        {
            if (store.Pregenerated.Contains(KeyOf(song, target)))
                continue;
            // Listeners first: it waits while anyone is playing music.
            while (streams.Active > 0)
                await Task.Delay(TimeSpan.FromSeconds(5), time, cancellationToken);
            if ((await settings.GetAsync(cancellationToken)) != config)
                return; // The settings changed: a new pass starts with them.

            _working = true;
            if (!await MakeAsync(song, target, cancellationToken) && ++failed >= 20)
            {
                logger.LogWarning("Stopped making versions of the music after {Count} failures in a row", failed);
                return;
            }

            Report(config, work);
        }
    }

    /// <summary>Lossless versions of lossy files, and smaller lossy versions of files that are already as small, would gain nothing.</summary>
    private static bool Gains(Song song, TranscodeTarget target, TranscodingConfig config)
    {
        var lossless = Scanning.AudioFormats.IsLossless(song.RelativePath);
        if (config.PregenerateOnlyLossless && !lossless)
            return false;
        if (target.Format == TranscodeFormat.Flac)
            return lossless;
        return lossless || song.BitRate is null || song.BitRate > target.BitRate;
    }

    private async Task<List<Song>> SongsAsync(TranscodingConfig config, CancellationToken cancellationToken)
    {
        await using var db = databases.CreateDbContext();
        var files = db.MediaFiles.AsNoTracking()
            .Where(f => f.MissingSince == null
                        && (f.LibrarySource.Kind == LibrarySourceKind.Local || f.LibrarySource.Kind == LibrarySourceKind.S3));
        if (config.PregenerateLibraries is { } libraries)
            files = files.Where(f => libraries.Contains(f.LibrarySource.LibraryId));
        var rows = await files
            .Select(f => new Song(f.ContentHash, f.LibrarySource.Kind, f.LibrarySource.Location, f.LibrarySource.Settings, f.RelativePath,
                f.StartMs, f.EndMs, f.DurationMs, f.BitRate, f.Size))
            .ToListAsync(cancellationToken);
        // The same audio in two places is converted once.
        return rows.GroupBy(r => r.ContentHash).Select(g => g.First()).OrderBy(r => r.ContentHash, StringComparer.Ordinal).ToList();
    }

    private static string KeyOf(Song song, TranscodeTarget target) =>
        TranscodeStore.Key(song.ContentHash, TranscodeProfile.For(target.Format), target.BitRate);

    private async Task<bool> MakeAsync(Song song, TranscodeTarget target, CancellationToken cancellationToken)
    {
        var storage = storages.For(song.Kind);
        var location = new StorageLocation(song.Kind, song.Location, song.Settings);
        var profile = TranscodeProfile.For(target.Format);
        var partial = store.Pregenerated.NewPartialPath();
        try
        {
            var session = await transcoder.StartAsync(
                storage.TryGetLocalPath(location, song.RelativePath),
                () => storage.OpenReadAsync(location, song.RelativePath, cancellationToken),
                profile, target.BitRate, (song.StartMs ?? 0) / 1000.0, cancellationToken,
                song.StartMs is null ? null : song.DurationMs / 1000.0);
            if (session is null)
                return false;
            await using (session)
            {
                session.Lower();
                await using (var output = new FileStream(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, true))
                    await session.Output.CopyToAsync(output, cancellationToken);
                if (!await session.FinishedCleanlyAsync() || new FileInfo(partial).Length == 0)
                {
                    logger.LogDebug("ffmpeg could not make the {Format} version of {Path}", target.Format, song.RelativePath);
                    return false;
                }
            }

            store.Pregenerated.Commit(partial, KeyOf(song, target));
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or FileNotFoundException or HttpRequestException)
        {
            logger.LogDebug("Could not make the {Format} version of {Path}: {Error}", target.Format, song.RelativePath, e.Message);
            return false;
        }
        finally
        {
            DiskCache.Discard(partial);
        }
    }

    private void Report(TranscodingConfig config, List<(TranscodeTarget Target, Song Song)> work)
    {
        _progress = config.Pregenerate.Select(target =>
        {
            var songs = work.Where(w => w.Target == target).Select(w => w.Song).ToList();
            var done = songs.Where(s => store.Pregenerated.Contains(KeyOf(s, target))).ToList();
            long Estimate(Song s) => target.Format == TranscodeFormat.Flac
                ? s.Size
                : (long)(s.DurationMs / 1000.0 * target.BitRate * 1000 / 8);
            return new PregenerateProgress(target.Format, target.BitRate, done.Count, songs.Count,
                done.Sum(s => store.Pregenerated.SizeOf(KeyOf(s, target))), songs.Sum(Estimate));
        }).ToList();
    }
}
