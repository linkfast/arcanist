using System.Text.Json;
using Arcanist.Core.Analysis;
using Arcanist.Core.Application.Online;
using Arcanist.Core.Application.Organization;
using Arcanist.Core.Data;
using Arcanist.Core.Data.Entities;
using Arcanist.Core.Media;
using Arcanist.Core.Online;
using Arcanist.Core.Scanning;
using Arcanist.Core.Setup;
using Arcanist.Core.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ImportItem = Arcanist.Core.Data.Entities.ImportItem;

namespace Arcanist.Core.Organization;

/// <summary>
/// Suggests tags for the files of drop folders that lack them: takes the fingerprint of each (in the drop folder,
/// before anything is imported), looks it up in AcoustID and keeps the recordings and releases it matched as
/// suggestions on the item, for an administrator to accept with a click. Only with AcoustID switched on, at the rate of
/// the online services; only fingerprints and their duration are sent. A file whose audio changes is looked up again.
/// </summary>
internal sealed class ImportSuggester(
    IArcanistDbContextFactory databases,
    ILibraryStorageResolver storages,
    OnlineSettings settings,
    AcoustIdClient acoustId,
    FfmpegLocator ffmpeg,
    DatabaseMaintenance maintenance,
    IOptionsMonitor<SetupOptions> setup,
    IOptionsMonitor<OnlineOptions> online,
    TimeProvider time,
    ILogger<ImportSuggester> logger) : BackgroundService
{
    /// <summary>Matches less likely than this are not suggested.</summary>
    internal const double MinScore = 0.5;

    internal const int MaxSuggestions = 10;

    /// <summary>How often it looks for files to look up when nothing wakes it.</summary>
    internal static TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(30);

    private static readonly JsonSerializerOptions Json = JsonSerializerOptions.Web;

    private readonly SemaphoreSlim _wake = new(0, 1);
    private TimeSpan _failureWait = TimeSpan.Zero;

    /// <summary>Looks for files to look up now (the drop folders just found some).</summary>
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
        var gate = new RateGate(online.CurrentValue.RequestInterval, time);
        while (!stoppingToken.IsCancellationRequested)
        {
            TimeSpan wait;
            try
            {
                wait = await StepAsync(gate, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception e)
            {
                logger.LogWarning(e, "Suggesting tags for dropped files failed");
                wait = TimeSpan.FromMinutes(1);
            }

            if (wait <= TimeSpan.Zero)
                continue;
            try
            {
                await _wake.WaitAsync(wait, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>Looks up the next file; returns how long to wait before the next one (zero: at once).</summary>
    internal async Task<TimeSpan> StepAsync(RateGate gate, CancellationToken cancellationToken)
    {
        if (!setup.CurrentValue.Completed || !databases.IsConfigured
            || !(await settings.GetAsync(cancellationToken)).IsEnabled(OnlineService.AcoustId)
            || await settings.AcoustIdKeyAsync(cancellationToken) is not { Length: > 0 } key)
            return PollInterval;

        // Files missing tags whose audio has not been looked up (or changed since).
        ImportItem? item;
        await using (var db = databases.CreateDbContext())
        {
            item = await db.ImportItems.AsNoTracking().Include(i => i.DropFolder)
                .Where(i => i.Status == ImportStatus.NeedsReview && i.Reason == ImportReason.MissingTags && i.ContentHash != null)
                .Where(i => i.SuggestionsFor == null || i.SuggestionsFor != i.ContentHash)
                .OrderBy(i => i.UpdatedAt)
                .FirstOrDefaultAsync(cancellationToken);
        }

        if (item is null)
            return PollInterval;

        var fingerprint = await FingerprintAsync(item, cancellationToken);
        ImportSuggestionState state;
        List<ImportSuggestion> suggestions = [];
        if (fingerprint is null)
        {
            state = ImportSuggestionState.Unidentifiable;
        }
        else
        {
            IReadOnlyList<AcoustIdClient.RecordingDetails> matches;
            try
            {
                await gate.WaitAsync(cancellationToken);
                matches = await acoustId.LookupDetailsAsync(key, fingerprint.Value.Fingerprint, fingerprint.Value.Seconds, cancellationToken);
                _failureWait = TimeSpan.Zero;
            }
            catch (OnlineServiceException e)
            {
                // Nothing is marked: the file is looked up again once AcoustID answers.
                _failureWait = e.Message == "invalid_key"
                    ? TimeSpan.FromHours(1)
                    : TimeSpan.FromTicks(Math.Clamp(_failureWait.Ticks * 2, TimeSpan.FromMinutes(1).Ticks, TimeSpan.FromMinutes(30).Ticks));
                logger.LogInformation("AcoustID could not suggest tags ({Error}); trying again in {Wait}", e.Message, _failureWait);
                return _failureWait;
            }

            var details = item.Tags is { } tags ? JsonSerializer.Deserialize<ImportFileDetails>(tags, Json) : null;
            suggestions = Rank(matches, details);
            state = suggestions.Count > 0 ? ImportSuggestionState.Found : ImportSuggestionState.NotFound;
        }

        // Only if the file is still the one looked up (the drop folder may have changed it meanwhile).
        using (await maintenance.WorkAsync(cancellationToken))
        {
            await using var db = databases.CreateDbContext();
            var json = JsonSerializer.Serialize(suggestions, Json);
            await db.ImportItems.Where(i => i.Id == item.Id && i.ContentHash == item.ContentHash)
                .ExecuteUpdateAsync(set => set
                    .SetProperty(i => i.SuggestionState, state)
                    .SetProperty(i => i.SuggestionsFor, item.ContentHash)
                    .SetProperty(i => i.Suggestions, json), cancellationToken);
        }

        logger.LogInformation("AcoustID suggestions for {Path}: {State} ({Count})", item.RelativePath, state, suggestions.Count);
        return TimeSpan.Zero;
    }

    /// <summary>The file's AcoustID fingerprint and its duration in seconds; null when none can be taken.</summary>
    private async Task<(string Fingerprint, int Seconds)?> FingerprintAsync(ImportItem item, CancellationToken cancellationToken)
    {
        var storage = storages.For(LibrarySourceKind.Local);
        var location = new StorageLocation(LibrarySourceKind.Local, item.DropFolder.Path);
        try
        {
            // An album in one file with its cue sheet: a fingerprint of its start says nothing of its tracks.
            if (await storage.ExistsAsync(location, Path.ChangeExtension(item.RelativePath, ".cue"), cancellationToken))
                return null;

            var extension = Path.GetExtension(item.RelativePath);
            AnalysisOutcome outcome;
            await using (var stream = await storage.OpenReadAsync(location, item.RelativePath, cancellationToken))
                outcome = await Task.Run(() => AudioAnalyzer.Analyze(stream, extension, AnalysisParts.Fingerprint), cancellationToken);

            // Formats the built-in decoder does not read go through ffmpeg when there is one.
            if (outcome.Status == AnalyzerStatus.Unsupported && (await ffmpeg.GetAsync(cancellationToken))?.Path is { } ffmpegPath)
            {
                await using var stream = await storage.OpenReadAsync(location, item.RelativePath, cancellationToken);
                outcome = await Task.Run(() => FfmpegDecoder.Analyze(ffmpegPath, stream, cancellationToken), cancellationToken);
            }

            if (outcome.Analysis is { Fingerprint: { } fingerprint } analysis && analysis.DurationMs > 0)
                return (fingerprint.Compressed, (int)Math.Round(analysis.DurationMs / 1000.0));
            logger.LogInformation("No fingerprint of {Path}: {Status} {Error}", item.RelativePath, outcome.Status, outcome.Error);
            return null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            logger.LogInformation("Could not read {Path} to suggest its tags: {Error}", item.RelativePath, e.Message);
            return null;
        }
    }

    /// <summary>
    /// One suggestion per recording and release it is on (or the recording alone when it is on none), those agreeing
    /// with the tags the file has first, then the likeliest and the earliest releases; the same tags only once.
    /// </summary>
    internal static List<ImportSuggestion> Rank(IReadOnlyList<AcoustIdClient.RecordingDetails> matches, ImportFileDetails? file)
    {
        static string Key(string? text) => TextNormalizer.Normalize(text ?? "");

        var candidates = new List<ImportSuggestion>();
        foreach (var recording in matches.Where(m => m.Score >= MinScore && !string.IsNullOrWhiteSpace(m.Title)))
        {
            if (recording.Releases.Count == 0)
            {
                candidates.Add(new ImportSuggestion(recording.Title!, recording.Artist, null, null, null, null, null,
                    recording.Score, recording.AcoustId, recording.RecordingMbid, null));
                continue;
            }

            foreach (var release in recording.Releases)
            {
                candidates.Add(new ImportSuggestion(
                    string.IsNullOrWhiteSpace(release.TrackTitle) ? recording.Title! : release.TrackTitle,
                    recording.Artist,
                    release.Title,
                    release.Artist ?? recording.Artist,
                    release.Year,
                    release.TrackNumber,
                    release.DiscCount > 1 ? release.DiscNumber : null,
                    recording.Score,
                    recording.AcoustId,
                    recording.RecordingMbid,
                    release.ReleaseMbid));
            }
        }

        int Agreement(ImportSuggestion s) =>
            (file?.Album is { } album && Key(album) == Key(s.Album) ? 4 : 0)
            + (file?.Artist is { } artist && (Key(artist) == Key(s.Artist) || Key(artist) == Key(s.AlbumArtist)) ? 2 : 0)
            + (file?.Title is { } title && Key(title) == Key(s.Title) ? 1 : 0);

        return candidates
            .OrderByDescending(Agreement)
            .ThenByDescending(s => Math.Round(s.Score, 2))
            .ThenBy(s => s.Year ?? int.MaxValue)
            .ThenBy(s => s.Album, StringComparer.Ordinal)
            .DistinctBy(s => (Key(s.Title), Key(s.Artist), Key(s.Album), Key(s.AlbumArtist), s.Year, s.TrackNumber, s.DiscNumber))
            .Take(MaxSuggestions)
            .ToList();
    }
}
