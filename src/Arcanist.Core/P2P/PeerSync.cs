using System.Text.Json;
using Arcanist.Core.Analysis;
using Arcanist.Core.Application;
using Arcanist.Core.Application.Listening;
using Arcanist.Core.Data;
using Arcanist.Core.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Arcanist.Core.P2P;

/// <summary>
/// Brings the analyses of shared songs from the servers that share them (loudness, tempo, key, fingerprints,
/// waveform), so this server has them without hearing the songs: mixes, mStream's waveforms and Auto-DJ, duplicates
/// and AcoustID work on shared libraries too. Asks for what is missing a few seconds after starting and then every
/// 15 minutes; what the other server has not analyzed yet is asked for again some hours later.
/// </summary>
public sealed class PeerAnalysisImporter(
    IArcanistDbContextFactory databases,
    DatabaseMaintenance maintenance,
    PeerClient client,
    DuplicateDetector duplicates,
    P2PSettings p2p,
    IOptionsMonitor<Setup.SetupOptions> setup,
    TimeProvider time,
    ILogger<PeerAnalysisImporter> logger) : BackgroundService
{
    private readonly Dictionary<string, DateTime> _asked = new(StringComparer.Ordinal);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(30), time, stoppingToken);
            while (!stoppingToken.IsCancellationRequested)
            {
                if (setup.CurrentValue.Completed && databases.IsConfigured && (await p2p.GetAsync(stoppingToken)).Enabled)
                {
                    try
                    {
                        await RunAsync(stoppingToken);
                    }
                    catch (Exception e) when (e is not OperationCanceledException)
                    {
                        logger.LogWarning(e, "Bringing the analyses of shared libraries failed");
                    }
                }

                await Task.Delay(TimeSpan.FromMinutes(15), time, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    /// <summary>One pass over every active share.</summary>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await using var db = databases.CreateDbContext();
        var remotes = await db.P2PRemotes.AsNoTracking().Where(r => r.Status == P2PRemoteStatus.Active).ToListAsync(cancellationToken);
        var now = time.GetUtcNow().UtcDateTime;
        foreach (var remote in remotes)
        {
            var location = remote.Id.ToString();
            var missing = await db.MediaFiles.AsNoTracking()
                .Where(f => f.MissingSince == null && f.LibrarySource.Kind == LibrarySourceKind.Peer && f.LibrarySource.Location == location
                            && !db.AudioAnalyses.Any(a => a.ContentHash == f.ContentHash))
                .Select(f => f.ContentHash).Distinct().ToListAsync(cancellationToken);
            missing = missing.Where(h => !_asked.TryGetValue(h, out var at) || now - at > TimeSpan.FromHours(6)).ToList();
            if (missing.Count == 0)
                continue;

            var ticket = JsonSerializer.Deserialize<NodeTicket>(remote.Ticket, NativeP2P.Json)!;
            foreach (var batch in missing.Chunk(PeerApi.MaxAnalyses))
            {
                List<PeerAnalysis> analyses;
                try
                {
                    analyses = await client.PostAsync<List<PeerAnalysis>>(ticket, "/rest/v1/peer/analyses", new PeerAnalysesRequest(batch), cancellationToken);
                }
                catch (ArcanistException e)
                {
                    logger.LogDebug("{Server} did not send its analyses: {Error}", remote.Name, e.Code);
                    break;
                }

                foreach (var hash in batch)
                    _asked[hash] = now;
                if (analyses.Count > 0)
                    await SaveAsync(analyses, cancellationToken);
            }
        }
    }

    private async Task SaveAsync(IReadOnlyList<PeerAnalysis> analyses, CancellationToken cancellationToken)
    {
        using (await maintenance.WorkAsync(cancellationToken))
        {
            await using var db = databases.CreateDbContext();
            var hashes = analyses.Select(a => a.ContentHash).ToList();
            // This server's own analysis of the same audio (a local copy) wins.
            var existing = (await db.AudioAnalyses.Where(a => hashes.Contains(a.ContentHash)).Select(a => a.ContentHash).ToListAsync(cancellationToken))
                .ToHashSet(StringComparer.Ordinal);
            foreach (var a in analyses.Where(a => !existing.Contains(a.ContentHash)))
            {
                db.AudioAnalyses.Add(new AudioAnalysis
                {
                    ContentHash = a.ContentHash,
                    AnalyzerVersion = a.AnalyzerVersion,
                    State = AnalysisState.Analyzed,
                    AnalyzedAt = a.AnalyzedAt,
                    DurationMs = a.DurationMs,
                    Loudness = a.Loudness,
                    LoudnessRange = a.LoudnessRange,
                    TruePeak = a.TruePeak,
                    LoudnessHistogram = a.LoudnessHistogram,
                    Bpm = a.Bpm,
                    BpmConfidence = a.BpmConfidence,
                    KeyTonic = a.KeyTonic,
                    KeyMinor = a.KeyMinor,
                    KeyConfidence = a.KeyConfidence,
                    AcoustIdFingerprint = a.AcoustIdFingerprint,
                    Fingerprint = a.Fingerprint,
                    FingerprintDurationMs = a.FingerprintDurationMs,
                    Waveform = a.Waveform,
                });
            }

            await db.SaveChangesAsync(cancellationToken);
            // As after this server's own analyses: duplicates by fingerprint, and the albums' loudness.
            await duplicates.ProcessAsync(db, analyses.Where(a => a.Fingerprint is not null).Select(a => a.ContentHash).ToList(), cancellationToken);
            var albums = await db.MediaFiles.Where(f => hashes.Contains(f.ContentHash)).Select(f => f.Track.AlbumId).Distinct().ToListAsync(cancellationToken);
            await AlbumLoudness.UpdateAsync(db, albums, cancellationToken);
        }
    }
}

/// <summary>
/// Sends the plays of shared songs to the servers that count them (<see cref="P2PRules.CountPlays"/>), unless an
/// administrator here keeps them home (<see cref="P2PRemote.SharePlays"/>): with the user's name, never anything
/// else of them. Only plays made since; a play of a song this server also has itself is not sent (it was played
/// from here). A server that cannot be reached gets them later, up to 14 days.
/// </summary>
public sealed class PeerPlayReporter(
    IArcanistDbContextFactory databases,
    DatabaseMaintenance maintenance,
    SettingsStore settings,
    PeerClient client,
    ListeningSignals signals,
    IOptionsMonitor<Setup.SetupOptions> setup,
    TimeProvider time,
    ILogger<PeerPlayReporter> logger) : BackgroundService
{
    private const int Batch = PeerApi.MaxPlays;

    private readonly SemaphoreSlim _wake = new(0, 1);

    public void Wake()
    {
        if (_wake.CurrentCount == 0)
            _wake.Release();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        signals.Played += Wake;
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                if (setup.CurrentValue.Completed && databases.IsConfigured)
                {
                    try
                    {
                        await RunAsync(stoppingToken);
                    }
                    catch (Exception e) when (e is not OperationCanceledException)
                    {
                        logger.LogWarning(e, "Sending plays of shared libraries failed");
                    }
                }

                await _wake.WaitAsync(TimeSpan.FromMinutes(5), stoppingToken);
                // Plays come in bursts: let them gather.
                await Task.Delay(TimeSpan.FromSeconds(2), time, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        finally
        {
            signals.Played -= Wake;
        }
    }

    private sealed record Row(Guid Id, string UserName, Guid TrackId, DateTime PlayedAt, int PlayedMs, bool Counted, bool Skipped);

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await using var db = databases.CreateDbContext();
        var wanting = await db.P2PRemotes.AsNoTracking()
            .Where(r => r.Status == P2PRemoteStatus.Active && r.SharePlays && r.Rules.CountPlays)
            .ToListAsync(cancellationToken);
        var cursor = await settings.GetAsync<Guid?>(SettingKeys.P2PPlaysCursor, null, cancellationToken);
        var last = await db.PlayEvents.OrderByDescending(e => e.Id).Select(e => (Guid?)e.Id).FirstOrDefaultAsync(cancellationToken) ?? Guid.Empty;
        // Nobody counts them: the plays made until now stay here, also when a server starts counting later.
        if (cursor is null || wanting.Count == 0)
        {
            if (cursor != last)
                await SaveCursorAsync(last, cancellationToken);
            return;
        }

        var remotes = wanting.ToDictionary(r => r.Id.ToString(), StringComparer.OrdinalIgnoreCase);
        var oldest = time.GetUtcNow().UtcDateTime.AddDays(-14);
        while (true)
        {
            var after = cursor.Value;
            var rows = await db.PlayEvents.AsNoTracking()
                .Where(e => e.Id.CompareTo(after) > 0 && e.TrackId != null)
                .OrderBy(e => e.Id).Take(Batch)
                .Select(e => new Row(e.Id, e.User.UserName, e.TrackId!.Value, e.PlayedAt, e.PlayedMs, e.Counted, e.Skipped))
                .ToListAsync(cancellationToken);
            if (rows.Count == 0)
                return;

            var trackIds = rows.Select(r => r.TrackId).Distinct().ToList();
            var files = await db.MediaFiles.AsNoTracking()
                .Where(f => f.MissingSince == null && trackIds.Contains(f.TrackId))
                .Select(f => new { f.TrackId, f.LibrarySource.Kind, f.LibrarySource.Location, f.RelativePath, f.CueTrack })
                .ToListAsync(cancellationToken);
            var byTrack = files.ToLookup(f => f.TrackId);

            var sends = new Dictionary<string, List<PeerPlay>>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in rows.Where(r => r.PlayedAt >= oldest))
            {
                var copies = byTrack[row.TrackId].ToList();
                // A song this server has itself was played from here.
                if (copies.Count == 0 || copies.Any(c => c.Kind != LibrarySourceKind.Peer))
                    continue;
                foreach (var copy in copies.Where(c => remotes.ContainsKey(c.Location)).GroupBy(c => c.Location).Select(g => g.First()))
                {
                    if (!sends.TryGetValue(copy.Location, out var list))
                        sends[copy.Location] = list = [];
                    list.Add(new PeerPlay(row.Id.ToString(), copy.RelativePath, copy.CueTrack, row.UserName, row.PlayedAt, row.PlayedMs, row.Counted, row.Skipped));
                }
            }

            foreach (var (location, plays) in sends)
            {
                var remote = remotes[location];
                try
                {
                    await client.PostAsync<PeerPlaysResult>(JsonSerializer.Deserialize<NodeTicket>(remote.Ticket, NativeP2P.Json)!,
                        "/rest/v1/peer/plays", new PeerPlaysRequest(plays), cancellationToken);
                }
                catch (ArcanistException e) when (e.Code == ErrorCodes.PlaysNotAccepted)
                {
                    // It stopped counting them; its rules here catch up at the next scan.
                }
                catch (ArcanistException e)
                {
                    // Not reachable now: these plays go next time (the cursor stays).
                    logger.LogDebug("Plays for {Server} wait: {Error}", remote.Name, e.Code);
                    return;
                }
            }

            cursor = rows[^1].Id;
            await SaveCursorAsync(cursor.Value, cancellationToken);
        }
    }

    private async Task SaveCursorAsync(Guid cursor, CancellationToken cancellationToken)
    {
        using (await maintenance.WorkAsync(cancellationToken))
            await settings.SetAsync<Guid?>(SettingKeys.P2PPlaysCursor, cursor, cancellationToken);
    }
}
