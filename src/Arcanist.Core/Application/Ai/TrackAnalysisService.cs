using Arcanist.Core.Ai;
using Arcanist.Core.Analysis;
using Arcanist.Core.Application.Catalog;
using Arcanist.Core.Application.Listening;
using Arcanist.Core.Auth;
using Arcanist.Core.Data;
using Arcanist.Core.Data.Entities;
using Arcanist.Core.Extensions;
using Arcanist.Core.Extensions.Ai;
using Microsoft.EntityFrameworkCore;

namespace Arcanist.Core.Application.Ai;

/// <summary>
/// Songs' DJ analyses for clients (the AI extension): one song's, asking for it when it is not there yet (and, for
/// songs of S3, other servers or Ultimate Cloud, analyzing it on request), putting a song first in line, pages of
/// the analyses of the songs the user sees, and a subscription to finished ones. Any user who can see the song.
/// </summary>
public sealed class TrackAnalysisService(
    ArcanistDbContext db,
    AccessGuard guard,
    CatalogService catalog,
    ExtensionHost extensions,
    AiSettings settings,
    AiMonitor monitor,
    TrackStatsService stats,
    TrackAnalysisEvents events,
    IServiceScopeFactory scopes,
    TimeProvider time)
{
    public const int MaxPageSize = 200;

    public async Task<TrackAnalysisResult> AnalysisAsync(Guid trackId, string? include, CancellationToken cancellationToken)
    {
        var access = guard.Authenticated();
        RequireEnabled();
        var blocks = DjJson.Parse(include);
        var file = await BestFileAsync(access, trackId, cancellationToken);
        var trackStats = (blocks & AnalysisBlocks.Stats) != 0 ? (await stats.ForTracksAsync(access, [trackId], cancellationToken))[trackId] : null;
        var version = DjAudioAnalyzer.Version;

        var row = await db.TrackAnalyses.AsNoTracking().FirstOrDefaultAsync(a => a.ContentHash == file.ContentHash, cancellationToken);
        var current = row is not null && row.AnalysisVersion == version;
        if (row?.Json is { } json && DjJson.Read(json) is { } output)
        {
            var stale = !current || row.State != TrackAnalysisState.Analyzed;
            // An old analysis of a song elsewhere is redone only on request.
            if (stale && !file.Local && (await settings.GetAsync(cancellationToken)).OnDemandSources)
                await RequestAsync(file, null, cancellationToken);
            return DjJson.Ready(trackId, row.ContentHash, row.AnalysisVersion, row.AnalyzedAt, stale, output, blocks, trackStats);
        }

        if (current && row!.State == TrackAnalysisState.Failed)
            return DjJson.Failed(trackId, row.ContentHash, row.AnalysisVersion, row.AnalyzedAt, trackStats);

        if (!file.Local)
        {
            if (!(await settings.GetAsync(cancellationToken)).OnDemandSources)
                throw new ArcanistException.Conflict(ErrorCodes.AnalysisUnavailable);
            await RequestAsync(file, null, cancellationToken);
        }

        return DjJson.Pending(trackId, await PositionAsync(file, cancellationToken), trackStats);
    }

    /// <summary>Puts a song first in line (it is going to be played soon). Returns its analysis or its place.</summary>
    public async Task<TrackAnalysisResult> PrioritizeAsync(Guid trackId, CancellationToken cancellationToken)
    {
        var access = guard.Authenticated();
        RequireEnabled();
        var file = await BestFileAsync(access, trackId, cancellationToken);
        if (!file.Local && !(await settings.GetAsync(cancellationToken)).OnDemandSources)
            throw new ArcanistException.Conflict(ErrorCodes.AnalysisUnavailable);

        var version = DjAudioAnalyzer.Version;
        var done = await db.TrackAnalyses.AnyAsync(a => a.ContentHash == file.ContentHash && a.AnalysisVersion == version
                                                        && a.State != TrackAnalysisState.NeedsFfmpeg, cancellationToken);
        if (!done)
        {
            var top = await db.TrackAnalysisRequests.MaxAsync(r => (int?)r.Priority, cancellationToken) ?? 0;
            await RequestAsync(file, top + 1, cancellationToken);
            monitor.RequestRun();
        }

        return await AnalysisAsync(trackId, null, cancellationToken);
    }

    /// <summary>
    /// The analyses of the songs the user sees, from the least to the most recently analyzed, so a client keeps its
    /// cache with <paramref name="updatedSince"/> set to the last <c>analyzedAt</c> it has.
    /// </summary>
    public async Task<TrackAnalysisPage> AnalysesAsync(DateTime? updatedSince, string? after, int? first, string? include, CancellationToken cancellationToken)
    {
        var access = guard.Authenticated();
        RequireEnabled();
        var blocks = DjJson.Parse(include);
        var analyzed = from file in catalog.VisibleFiles(access)
                       join analysis in db.TrackAnalyses on file.ContentHash equals analysis.ContentHash
                       where analysis.Json != null
                       select new { file.TrackId, analysis.AnalyzedAt };
        var tracks = analyzed.GroupBy(a => a.TrackId).Select(g => new { TrackId = g.Key, At = g.Max(a => a.AnalyzedAt) });
        if (updatedSince is { } since)
        {
            var utc = since.ToUniversalTime();
            tracks = tracks.Where(t => t.At > utc);
        }

        if (after is not null)
        {
            var (at, id) = TimeCursor.Decode(after);
            tracks = tracks.Where(t => t.At > at || (t.At == at && t.TrackId.CompareTo(id) > 0));
        }

        var size = Math.Clamp(first ?? 50, 1, MaxPageSize);
        var page = await tracks.OrderBy(t => t.At).ThenBy(t => t.TrackId).Take(size + 1).ToListAsync(cancellationToken);
        var next = page.Count > size ? TimeCursor.Encode(page[size - 1].At, page[size - 1].TrackId) : null;
        page = page.Take(size).ToList();

        var ids = page.Select(p => p.TrackId).ToList();
        var rows = await (from file in catalog.VisibleFiles(access)
                          where ids.Contains(file.TrackId)
                          join analysis in db.TrackAnalyses on file.ContentHash equals analysis.ContentHash
                          where analysis.Json != null
                          select new { file.TrackId, analysis.ContentHash, analysis.AnalysisVersion, analysis.State, analysis.AnalyzedAt, analysis.Json })
            .ToListAsync(cancellationToken);
        var trackStats = (blocks & AnalysisBlocks.Stats) != 0 ? await stats.ForTracksAsync(access, ids, cancellationToken) : null;
        var version = DjAudioAnalyzer.Version;

        var items = new List<TrackAnalysisResult>();
        foreach (var entry in page)
        {
            var row = rows.Where(r => r.TrackId == entry.TrackId).OrderByDescending(r => r.AnalyzedAt).First();
            if (DjJson.Read(row.Json) is not { } output)
                continue;
            items.Add(DjJson.Ready(entry.TrackId, row.ContentHash, row.AnalysisVersion, row.AnalyzedAt,
                row.AnalysisVersion != version || row.State != TrackAnalysisState.Analyzed, output, blocks, trackStats?.GetValueOrDefault(entry.TrackId)));
        }

        return new TrackAnalysisPage(items, next);
    }

    /// <summary>Songs the user sees whose analysis just finished.</summary>
    public IObservable<TrackAnalysisReady> WatchReady()
    {
        var access = guard.Authenticated();
        RequireEnabled();
        return new ReadyObservable(events, access, scopes);
    }

    /// <summary>Finished audio (by content hash) as the songs with it this user sees.</summary>
    private sealed class ReadyObservable(TrackAnalysisEvents events, UserAccess access, IServiceScopeFactory scopes) : IObservable<TrackAnalysisReady>
    {
        public IDisposable Subscribe(IObserver<TrackAnalysisReady> observer) => events.Subscribe(new Mapper(observer, access, scopes));

        private sealed class Mapper(IObserver<TrackAnalysisReady> inner, UserAccess access, IServiceScopeFactory scopes) : IObserver<(string ContentHash, int Version)>
        {
            public void OnNext((string ContentHash, int Version) value)
            {
                List<Guid> tracks;
                try
                {
                    using var scope = scopes.CreateScope();
                    var catalog = scope.ServiceProvider.GetRequiredService<CatalogService>();
                    tracks = catalog.VisibleFiles(access).Where(f => f.ContentHash == value.ContentHash).Select(f => f.TrackId).Distinct().ToList();
                }
                catch (Exception e) when (e is InvalidOperationException or DbUpdateException)
                {
                    return;
                }

                foreach (var id in tracks)
                    inner.OnNext(new TrackAnalysisReady(id, value.Version));
            }

            public void OnError(Exception error) => inner.OnError(error);

            public void OnCompleted() => inner.OnCompleted();
        }
    }

    internal void RequireEnabled()
    {
        if (!extensions.IsEnabled(AiExtension.Id))
            throw new ArcanistException.Conflict(ErrorCodes.AiDisabled);
    }

    private sealed record BestFile(Guid Id, string ContentHash, bool Local);

    /// <summary>The copy analyses use: a local one if there is (any copy of the same audio has the same analysis).</summary>
    private async Task<BestFile> BestFileAsync(UserAccess access, Guid trackId, CancellationToken cancellationToken) =>
        await catalog.VisibleFiles(access)
            .Where(f => f.TrackId == trackId)
            .OrderBy(f => f.LibrarySource.Kind == LibrarySourceKind.Local ? 0 : 1).ThenByDescending(f => f.BitDepth).ThenByDescending(f => f.SampleRate)
            .Select(f => new BestFile(f.Id, f.ContentHash, f.LibrarySource.Kind == LibrarySourceKind.Local))
            .FirstOrDefaultAsync(cancellationToken)
        ?? throw new ArcanistException.NotFound();

    /// <summary>Asks for the analysis of a song (at <paramref name="priority"/>, or keeping the one it has).</summary>
    private async Task RequestAsync(BestFile file, int? priority, CancellationToken cancellationToken)
    {
        var request = await db.TrackAnalysisRequests.FirstOrDefaultAsync(r => r.ContentHash == file.ContentHash, cancellationToken);
        if (request is null)
        {
            db.TrackAnalysisRequests.Add(new TrackAnalysisRequest
            {
                ContentHash = file.ContentHash, FileId = file.Id, Priority = priority ?? 0, RequestedAt = time.GetUtcNow().UtcDateTime,
            });
        }
        else if (priority is { } value)
        {
            request.Priority = value;
            request.FileId = file.Id;
        }
        else
        {
            return;
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Asked for at the same time by another request: it is in line either way.
            db.ChangeTracker.Clear();
        }

        monitor.RequestRun();
    }

    /// <summary>How many songs are ahead: requests before it, then (for local audio) the local songs before it.</summary>
    private async Task<int> PositionAsync(BestFile file, CancellationToken cancellationToken)
    {
        var request = await db.TrackAnalysisRequests.AsNoTracking().FirstOrDefaultAsync(r => r.ContentHash == file.ContentHash, cancellationToken);
        if (request is not null)
        {
            return await db.TrackAnalysisRequests.CountAsync(r => r.Priority > request.Priority
                || (r.Priority == request.Priority && (r.RequestedAt < request.RequestedAt || (r.RequestedAt == request.RequestedAt && r.Id.CompareTo(request.Id) < 0))),
                cancellationToken);
        }

        var requests = await db.TrackAnalysisRequests.CountAsync(cancellationToken);
        var before = await TrackAnalysisWorker.Pending(db, DjAudioAnalyzer.Version, false)
            .Where(f => f.Id.CompareTo(file.Id) < 0)
            .Select(f => f.ContentHash)
            .Distinct()
            .CountAsync(cancellationToken);
        return requests + before;
    }
}
