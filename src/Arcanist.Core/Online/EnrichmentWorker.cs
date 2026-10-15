using System.Collections.Concurrent;
using Arcanist.Core.Analysis;
using Arcanist.Core.Application.Analysis;
using Arcanist.Core.Application.Online;
using Arcanist.Core.Data;
using Arcanist.Core.Data.Entities;
using Arcanist.Core.Scanning;
using Arcanist.Core.Setup;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Arcanist.Core.Online;

/// <summary>Bound from <c>Arcanist:Online</c>.</summary>
public sealed class OnlineOptions
{
    public const string Section = "Arcanist:Online";

    /// <summary>
    /// The project's AcoustID application key, used when the administrator has not set their own. Application keys
    /// identify the program, not its users, and are meant to ship with it.
    /// </summary>
    public const string? ProjectAcoustIdKey = "hXVwtfLtdR";

    /// <summary>Overrides <see cref="ProjectAcoustIdKey"/> (for forks and packagers).</summary>
    public string? AcoustIdKey { get; set; }

    /// <summary>Time between two requests to the same service. MusicBrainz allows one per second.</summary>
    public TimeSpan RequestInterval { get; set; } = TimeSpan.FromSeconds(1);
}

/// <summary>The online services settings, as stored by the admin panel or the setup wizard.</summary>
public sealed class OnlineSettings(SettingsStore store, IOptionsMonitor<OnlineOptions> options)
{
    public Task<OnlineServicesSettings> GetAsync(CancellationToken cancellationToken) =>
        store.GetAsync(SettingKeys.OnlineServices, OnlineServicesSettings.None, cancellationToken);

    public Task<string?> OwnAcoustIdKeyAsync(CancellationToken cancellationToken) =>
        store.GetAsync<string?>(SettingKeys.AcoustIdKey, null, cancellationToken);

    public string? ProjectAcoustIdKey => string.IsNullOrWhiteSpace(options.CurrentValue.AcoustIdKey) ? OnlineOptions.ProjectAcoustIdKey : options.CurrentValue.AcoustIdKey;

    public async Task<string?> AcoustIdKeyAsync(CancellationToken cancellationToken) =>
        await OwnAcoustIdKeyAsync(cancellationToken) is { Length: > 0 } own ? own : ProjectAcoustIdKey;
}

/// <summary>What each service did last, for the admin panel.</summary>
public sealed class OnlineMonitor
{
    private readonly ConcurrentDictionary<OnlineService, (DateTime? At, string? Error)> _last = new();

    internal event Action? RunRequested;

    /// <summary>Asks the services to look for work now (settings changed, new music analyzed).</summary>
    public void RequestRun() => RunRequested?.Invoke();

    internal void Record(OnlineService service, DateTime at, string? error) => _last[service] = (at, error);

    public (DateTime? At, string? Error) Last(OnlineService service) => _last.GetValueOrDefault(service);
}

/// <summary>
/// Completes the catalog from online services, each at its own pace (one request per second): AcoustID identifies
/// recordings from fingerprints, MusicBrainz identifies albums, the Cover Art Archive adds missing covers and
/// LRCLib missing lyrics. Only what an administrator turned on runs, and only fingerprints, identifiers and
/// artist, album and track names are sent.
/// </summary>
internal sealed class EnrichmentWorker(
    IArcanistDbContextFactory databases,
    OnlineSettings settings,
    OnlineMonitor monitor,
    AnalysisMonitor analysis,
    AcoustIdClient acoustId,
    MusicBrainzClient musicBrainz,
    CoverArtClient coverArt,
    LrcLibClient lrcLib,
    ArtworkStore artworkStore,
    IOptionsMonitor<SetupOptions> setup,
    IOptionsMonitor<OnlineOptions> options,
    TimeProvider time,
    DatabaseMaintenance maintenance,
    ILogger<EnrichmentWorker> logger) : BackgroundService, IObserver<AnalysisProgress>
{
    private static readonly TimeSpan IdleDelay = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan NotFoundRetry = TimeSpan.FromDays(90);

    /// <summary>A match must be at least this sure; AcoustID scores below it are often other versions.</summary>
    private const double AcoustIdMinScore = 0.85;

    private readonly Dictionary<OnlineService, SemaphoreSlim> _wake =
        Enum.GetValues<OnlineService>().ToDictionary(s => s, _ => new SemaphoreSlim(0));

    public void Wake()
    {
        foreach (var signal in _wake.Values)
        {
            if (signal.CurrentCount == 0)
                signal.Release();
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();
        monitor.RunRequested += Wake;
        using var subscription = analysis.Subscribe(this);
        await Task.WhenAll(Enum.GetValues<OnlineService>().Select(service => RunServiceAsync(service, stoppingToken)));
    }

    private async Task RunServiceAsync(OnlineService service, CancellationToken stoppingToken)
    {
        var gate = new RateGate(options.CurrentValue.RequestInterval, time);
        while (!stoppingToken.IsCancellationRequested)
        {
            TimeSpan delay;
            try
            {
                using (await maintenance.WorkAsync(stoppingToken))
                    delay = await StepAsync(service, gate, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception e)
            {
                logger.LogError(e, "{Service} lookup failed", service);
                monitor.Record(service, time.GetUtcNow().UtcDateTime, "unexpected_error");
                delay = TimeSpan.FromMinutes(1);
            }

            if (delay <= TimeSpan.Zero)
                continue;
            try
            {
                await _wake[service].WaitAsync(delay, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>Looks up one subject, and says how long to wait before the next.</summary>
    internal async Task<TimeSpan> StepAsync(OnlineService service, RateGate gate, CancellationToken cancellationToken)
    {
        if (!setup.CurrentValue.Completed || !databases.IsConfigured)
            return TimeSpan.FromMinutes(1);
        if (!(await settings.GetAsync(cancellationToken)).IsEnabled(service))
            return IdleDelay;

        var key = service == OnlineService.AcoustId ? await settings.AcoustIdKeyAsync(cancellationToken) : null;
        if (service == OnlineService.AcoustId && key is null)
            return IdleDelay;

        await using var db = databases.CreateDbContext();
        var now = time.GetUtcNow().UtcDateTime;
        if (await Pending(db, service, now).FirstOrDefaultAsync(cancellationToken) is not { } subject)
            return IdleDelay;

        await gate.WaitAsync(cancellationToken);
        LookupOutcome outcome;
        try
        {
            outcome = service switch
            {
                OnlineService.AcoustId => await IdentifyRecordingAsync(db, subject, key!, cancellationToken),
                OnlineService.MusicBrainz => await IdentifyAlbumAsync(db, Guid.Parse(subject), cancellationToken),
                OnlineService.CoverArtArchive => await AddCoverAsync(db, Guid.Parse(subject), cancellationToken),
                _ => await AddLyricsAsync(db, Guid.Parse(subject), cancellationToken),
            };
            monitor.Record(service, time.GetUtcNow().UtcDateTime, null);
        }
        catch (OnlineServiceException e)
        {
            monitor.Record(service, time.GetUtcNow().UtcDateTime, e.Code);
            logger.LogInformation("{Service} lookup of {Subject} failed: {Code}", service, subject, e.Code);

            // Problems of the service or the connection, not of this subject: pause the service, record nothing.
            switch (e.Code)
            {
                case "invalid_key":
                    return IdleDelay;
                case "rate_limited":
                    return TimeSpan.FromMinutes(1);
                case "connection_failed" or "timeout":
                    return TimeSpan.FromMinutes(5);
            }

            outcome = LookupOutcome.Failed;
        }

        await RecordAsync(db, service, subject, outcome, cancellationToken);
        return TimeSpan.Zero;
    }

    /// <summary>What each service still has to look up, oldest first.</summary>
    internal static IQueryable<string> Pending(ArcanistDbContext db, OnlineService service, DateTime now)
    {
        var blocked = db.OnlineLookups.Where(l => l.Service == service && (l.Outcome == LookupOutcome.Found || l.RetryAfter > now)).Select(l => l.Subject);

        return service switch
        {
            // Audio of recordings MusicBrainz does not identify yet.
            OnlineService.AcoustId => db.AudioAnalyses
                .Where(a => a.State == AnalysisState.Analyzed && a.AcoustIdFingerprint != null && a.DurationMs > 0)
                .Where(a => db.MediaFiles.Any(f => f.ContentHash == a.ContentHash && f.MissingSince == null && f.Track.Recording.MusicBrainzId == null))
                .Where(a => !blocked.Contains(a.ContentHash))
                .OrderBy(a => a.AnalyzedAt)
                .Select(a => a.ContentHash),
            OnlineService.MusicBrainz => db.Albums
                .Where(a => a.MusicBrainzReleaseId == null && a.Title != CatalogWriter.UnknownAlbum && a.AlbumArtist.Name != CatalogWriter.UnknownArtist)
                .Where(a => a.Tracks.Any(t => t.Files.Any(f => f.MissingSince == null)))
                .Where(a => !blocked.Contains(a.Id.ToString()))
                .OrderBy(a => a.Id)
                .Select(a => a.Id.ToString()),
            OnlineService.CoverArtArchive => db.Albums
                .Where(a => a.ArtworkId == null && a.MusicBrainzReleaseId != null)
                .Where(a => a.Tracks.Any(t => t.Files.Any(f => f.MissingSince == null)))
                .Where(a => !blocked.Contains(a.Id.ToString()))
                .OrderBy(a => a.Id)
                .Select(a => a.Id.ToString()),
            _ => db.Tracks
                .Where(t => !db.Lyrics.Any(l => l.TrackId == t.Id) && t.Files.Any(f => f.MissingSince == null))
                .Where(t => t.Artists.Any(a => a.Role == ArtistRole.Primary && a.Artist.Name != CatalogWriter.UnknownArtist))
                .Where(t => !blocked.Contains(t.Id.ToString()))
                .OrderBy(t => t.Id)
                .Select(t => t.Id.ToString()),
        };
    }

    private async Task<LookupOutcome> IdentifyRecordingAsync(ArcanistDbContext db, string contentHash, string key, CancellationToken cancellationToken)
    {
        var audio = await db.AudioAnalyses.AsNoTracking().Where(a => a.ContentHash == contentHash)
            .Select(a => new { a.AcoustIdFingerprint, a.DurationMs }).SingleAsync(cancellationToken);
        var matches = await acoustId.LookupAsync(key, audio.AcoustIdFingerprint!, (int)Math.Round(audio.DurationMs / 1000.0), cancellationToken);
        if (matches.Where(m => m.Score >= AcoustIdMinScore).MaxBy(m => m.Score) is not { } best)
            return LookupOutcome.NotFound;

        // Only an unambiguous answer identifies the recording; several recordings share some fingerprints.
        var mbid = best.RecordingMbids.Count == 1 ? best.RecordingMbids[0] : (Guid?)null;
        var recordingIds = await db.MediaFiles.Where(f => f.ContentHash == contentHash && f.MissingSince == null)
            .Select(f => f.Track.RecordingId).Distinct().ToListAsync(cancellationToken);

        foreach (var recording in await db.Recordings.Where(r => recordingIds.Contains(r.Id)).ToListAsync(cancellationToken))
        {
            recording.AcoustId ??= best.AcoustId;
            if (mbid is not { } id || recording.MusicBrainzId is not null)
                continue;

            // Another recording already carries that identifier: it is the same one, so its tracks join it.
            var existing = await db.Recordings.Where(r => r.MusicBrainzId == id).Select(r => (Guid?)r.Id).FirstOrDefaultAsync(cancellationToken);
            if (existing is { } other && other != recording.Id)
                await db.Tracks.Where(t => t.RecordingId == recording.Id).ExecuteUpdateAsync(s => s.SetProperty(t => t.RecordingId, other), cancellationToken);
            else
                recording.MusicBrainzId = id;
        }

        await db.SaveChangesAsync(cancellationToken);
        return LookupOutcome.Found;
    }

    private async Task<LookupOutcome> IdentifyAlbumAsync(ArcanistDbContext db, Guid albumId, CancellationToken cancellationToken)
    {
        var album = await db.Albums.Include(a => a.AlbumArtist).SingleAsync(a => a.Id == albumId, cancellationToken);
        var trackCount = await db.Tracks.CountAsync(t => t.AlbumId == albumId, cancellationToken);
        var releases = await musicBrainz.SearchReleasesAsync(album.Title, album.AlbumArtist.Name, cancellationToken);

        // Same title and album artist (ignoring case and accents), a confident score, and the same number of tracks
        // when possible: a reissue with bonus tracks is a different release.
        var matching = releases
            .Where(r => r.Score >= 90 && TextNormalizer.Normalize(r.Title) == album.NormalizedTitle)
            .Where(r => r.Artists.Count > 0 && TextNormalizer.Normalize(string.Join(" ", r.Artists.Select(a => a.Name))) == TextNormalizer.Normalize(album.AlbumArtist.Name))
            .ToList();
        if ((matching.FirstOrDefault(r => r.TrackCount == trackCount) ?? matching.FirstOrDefault()) is not { } release)
            return LookupOutcome.NotFound;

        album.MusicBrainzReleaseId = release.Id;
        if (album.Year is null && release.Date is { Length: >= 4 } date && int.TryParse(date[..4], out var year))
            album.Year = year;

        if (album.AlbumArtist.MusicBrainzId is null && release.Artists is [{ } artist]
            && !await db.Artists.AnyAsync(a => a.MusicBrainzId == artist.Id, cancellationToken))
        {
            album.AlbumArtist.MusicBrainzId = artist.Id;
        }

        await db.SaveChangesAsync(cancellationToken);
        return LookupOutcome.Found;
    }

    private async Task<LookupOutcome> AddCoverAsync(ArcanistDbContext db, Guid albumId, CancellationToken cancellationToken)
    {
        var album = await db.Albums.SingleAsync(a => a.Id == albumId, cancellationToken);
        if (album.ArtworkId is not null || album.MusicBrainzReleaseId is not { } release)
            return LookupOutcome.Found;

        if (await coverArt.FrontAsync(release, cancellationToken) is not { } image)
            return LookupOutcome.NotFound;
        if (artworkStore.Store(image) is not { } stored)
            throw new OnlineServiceException("unexpected_response");

        var artwork = await db.Artworks.FirstOrDefaultAsync(a => a.Hash == stored.Hash, cancellationToken);
        if (artwork is null)
        {
            artwork = new Artwork { Hash = stored.Hash, MimeType = stored.MimeType, Width = stored.Width, Height = stored.Height, Source = ArtworkSource.Online };
            db.Artworks.Add(artwork);
        }

        album.ArtworkId = artwork.Id;
        await db.SaveChangesAsync(cancellationToken);
        return LookupOutcome.Found;
    }

    private async Task<LookupOutcome> AddLyricsAsync(ArcanistDbContext db, Guid trackId, CancellationToken cancellationToken)
    {
        var track = await db.Tracks.AsNoTracking().Where(t => t.Id == trackId)
            .Select(t => new
            {
                t.Title,
                t.DurationMs,
                Album = t.Album.Title,
                Artist = t.Artists.Where(a => a.Role == ArtistRole.Primary).OrderBy(a => a.Position).Select(a => a.Artist.Name).FirstOrDefault(),
            })
            .SingleAsync(cancellationToken);
        if (track.Artist is null)
            return LookupOutcome.NotFound;

        var album = track.Album == CatalogWriter.UnknownAlbum ? null : track.Album;
        if (await lrcLib.GetAsync(track.Artist, track.Title, album, (int)Math.Round(track.DurationMs / 1000.0), cancellationToken) is not { } found
            || (found.Plain is null && found.Synced is null && !found.Instrumental))
        {
            return LookupOutcome.NotFound;
        }

        // The scanner may have found the track's own lyrics meanwhile; those win.
        if (!await db.Lyrics.AnyAsync(l => l.TrackId == trackId, cancellationToken))
        {
            db.Lyrics.Add(new Lyrics
            {
                TrackId = trackId,
                Plain = found.Plain,
                Synced = found.Synced,
                Instrumental = found.Instrumental,
                Source = LyricsSource.Online,
                UpdatedAt = time.GetUtcNow().UtcDateTime,
            });
            await db.SaveChangesAsync(cancellationToken);
        }

        return LookupOutcome.Found;
    }

    private async Task RecordAsync(ArcanistDbContext db, OnlineService service, string subject, LookupOutcome outcome, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var lookup = await db.OnlineLookups.SingleOrDefaultAsync(l => l.Service == service && l.Subject == subject, cancellationToken);
        if (lookup is null)
        {
            lookup = new OnlineLookup { Service = service, Subject = subject };
            db.OnlineLookups.Add(lookup);
        }

        lookup.Attempts = outcome == LookupOutcome.Failed ? lookup.Attempts + 1 : 0;
        lookup.Outcome = outcome;
        lookup.CheckedAt = now;
        lookup.RetryAfter = outcome switch
        {
            LookupOutcome.Found => null,
            LookupOutcome.NotFound => now + NotFoundRetry,
            // An hour, doubling with each failure, up to a week.
            _ => now + TimeSpan.FromHours(Math.Min(Math.Pow(2, lookup.Attempts - 1), 24 * 7)),
        };
        await db.SaveChangesAsync(cancellationToken);
    }

    // Newly analyzed audio may have something to look up.
    void IObserver<AnalysisProgress>.OnNext(AnalysisProgress value)
    {
        if (value.Activity == AnalysisActivity.Idle)
            Wake();
    }

    void IObserver<AnalysisProgress>.OnCompleted()
    {
    }

    void IObserver<AnalysisProgress>.OnError(Exception error)
    {
    }
}
