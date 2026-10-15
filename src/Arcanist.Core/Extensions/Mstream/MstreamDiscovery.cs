using System.Text.Json.Nodes;
using Arcanist.Core.Analysis;
using Arcanist.Core.Application.Analysis;
using Arcanist.Core.Application.Discovery;
using Arcanist.Core.Application.Libraries;
using Arcanist.Core.Application.Mstream;
using Arcanist.Core.Data;
using Arcanist.Core.Data.Entities;
using Microsoft.EntityFrameworkCore;
using TrackEntity = Arcanist.Core.Data.Entities.Track;

namespace Arcanist.Core.Extensions.Mstream;

/// <summary>
/// The mStream apps' own functions that Arcanist offers with what it has: waveforms from its analysis, scan progress,
/// and similar songs, similar artists and journeys between two songs. mStream finds those with audio embeddings;
/// here they come from tempo, key, genres and related artists. Each is switched on in the panel.
/// </summary>
internal sealed partial class MstreamExtension
{
    private const int WaveformBars = 800;
    private const int MaxSimilar = 100;
    private const int SimilarPool = 3000;

    private static async Task<bool> HasAsync(MstreamCall call, MstreamFeature feature) =>
        (await call.Service<MstreamSettings>().GetAsync(call.Cancellation)).Has(feature);

    private static async Task RequireAsync(MstreamCall call, MstreamFeature feature)
    {
        if (!await HasAsync(call, feature))
            throw new MstreamException(StatusCodes.Status403Forbidden, "Forbidden");
    }

    /// <summary>800 bars from 0 to 255. Off, 503: the apps take it as "this server has no waveforms" and stop asking.</summary>
    private static async Task<IResult> WaveformAsync(MstreamCall call)
    {
        if (!await HasAsync(call, MstreamFeature.Waveform))
            return MstreamCall.Error(StatusCodes.Status503ServiceUnavailable, "Waveforms are not available");

        var library = new MstreamLibrary(call);
        var file = await library.ResolveAsync(call.Value("filepath")) ?? throw MstreamException.NotFound();
        var peaks = await library.Db.AudioAnalyses.AsNoTracking()
                        .Where(a => a.ContentHash == file.ContentHash && a.Waveform != null)
                        .Select(a => a.Waveform)
                        .FirstOrDefaultAsync(call.Cancellation)
                    ?? throw MstreamException.NotFound();
        return MstreamCall.Json(new JsonObject { ["waveform"] = new JsonArray(Resample(peaks, WaveformBars).Select(b => (JsonNode?)b).ToArray()) });
    }

    /// <summary>Peaks resampled to <paramref name="bars"/>: each bar keeps the highest peak it covers.</summary>
    internal static int[] Resample(byte[] peaks, int bars)
    {
        if (peaks.Length == 0)
            return [];
        var result = new int[bars];
        for (var i = 0; i < bars; i++)
        {
            var from = i * peaks.Length / bars;
            var to = Math.Max(from + 1, (i + 1) * peaks.Length / bars);
            var max = 0;
            for (var j = from; j < to && j < peaks.Length; j++)
                max = Math.Max(max, peaks[j]);
            result[i] = max;
        }

        return result;
    }

    /// <summary>One entry per running scan of a library the user sees.</summary>
    private static async Task<IResult> ScanProgressAsync(MstreamCall call)
    {
        await RequireAsync(call, MstreamFeature.ScanStatus);
        var library = new MstreamLibrary(call);
        var vpaths = await library.VpathsAsync();
        var visible = LibraryService.Visible(library.Db, call.Access).Select(l => l.Id);
        var running = await library.Db.ScanJobs.AsNoTracking()
            .Where(j => visible.Contains(j.LibraryId) && j.Status == ScanStatus.Running)
            .Select(j => new { j.LibraryId, j.FilesSeen })
            .ToListAsync(call.Cancellation);
        return MstreamCall.Json(new JsonArray(running.Select(j => (JsonNode?)new JsonObject
        {
            ["vpath"] = vpaths.FirstOrDefault(v => v.LibraryId == j.LibraryId)?.Name ?? "",
            // Arcanist does not count a folder before scanning it.
            ["pct"] = null,
            ["scanned"] = j.FilesSeen,
            ["expected"] = null,
            ["currentFile"] = null,
        }).ToArray()));
    }

    /// <summary>The scan queue and the audio analysis, which gives waveforms, tempo and key in one pass.</summary>
    private static async Task<IResult> ScanStatusAsync(MstreamCall call)
    {
        await RequireAsync(call, MstreamFeature.ScanStatus);
        var library = new MstreamLibrary(call);
        var visible = LibraryService.Visible(library.Db, call.Access).Select(l => l.Id);
        var jobs = await library.Db.ScanJobs.AsNoTracking()
            .Where(j => visible.Contains(j.LibraryId) && (j.Status == ScanStatus.Running || j.Status == ScanStatus.Queued))
            .OrderBy(j => j.RequestedAt)
            .Select(j => j.Status)
            .ToListAsync(call.Cancellation);
        var tracks = await (await library.TracksAsync()).CountAsync(call.Cancellation);

        var analysis = call.Service<AnalysisMonitor>().Current;
        var scanning = jobs.Contains(ScanStatus.Running);
        var activeTask = scanning ? "scan" : analysis.Activity == AnalysisActivity.Running ? "audioanalysis" : null;
        var queued = jobs.Where(s => s == ScanStatus.Queued).Select(_ => (JsonNode?)"scan").ToList();
        if (analysis.Activity == AnalysisActivity.WaitingForScan)
            queued.Add("audioanalysis");

        return MstreamCall.Json(new JsonObject
        {
            ["queue"] = new JsonObject { ["scanning"] = scanning, ["activeTask"] = activeTask, ["queued"] = new JsonArray(queued.ToArray()) },
            ["totals"] = new JsonObject { ["tracks"] = tracks },
            ["enrichment"] = new JsonArray(Pass("waveform", analysis), Pass("audioanalysis", analysis)),
        });
    }

    private static JsonObject Pass(string name, AnalysisProgress analysis) => new()
    {
        ["pass"] = name,
        ["enabled"] = analysis.Activity is not (AnalysisActivity.Disabled or AnalysisActivity.Unavailable),
        ["disabledReason"] = analysis.Activity switch
        {
            AnalysisActivity.Disabled => "config",
            AnalysisActivity.Unavailable => "no-binary",
            _ => null,
        },
        ["state"] = analysis.Activity switch
        {
            AnalysisActivity.Running => "running",
            AnalysisActivity.WaitingForScan => "queued",
            AnalysisActivity.Idle => "idle",
            _ => "disabled",
        },
        ["progress"] = analysis.Activity == AnalysisActivity.Running
            ? new JsonObject { ["attempted"] = analysis.Analyzed, ["total"] = analysis.Analyzed + analysis.Pending }
            : null,
        ["lastRun"] = null,
        ["coverage"] = new JsonObject
        {
            ["scope"] = "global",
            ["unit"] = "tracks",
            ["done"] = analysis.Analyzed,
            ["remaining"] = analysis.Pending,
            ["outcomes"] = new JsonObject { ["undecodable"] = analysis.Failed, ["needsFfmpeg"] = analysis.NeedsFfmpeg },
        },
    };

    /// <summary>What a song sounds like, as far as Arcanist knows: tempo, key, genres, artist.</summary>
    private sealed record Sound(Guid TrackId, Guid AlbumId, Guid? ArtistId, IReadOnlyList<Guid> Genres, double? Bpm, (int Number, bool Minor)? Key)
    {
        public bool Analyzed => Bpm is not null || Key is not null;
    }

    private static async Task<Dictionary<Guid, Sound>> SoundsAsync(MstreamLibrary library, IQueryable<TrackEntity> tracks)
    {
        var db = library.Db;
        var rows = await tracks.AsNoTracking()
            .Select(t => new
            {
                t.Id,
                t.AlbumId,
                Artist = t.Artists.Where(a => a.Role == ArtistRole.Primary).OrderBy(a => a.Position).Select(a => (Guid?)a.ArtistId).FirstOrDefault(),
                Genres = t.Genres.Select(g => g.GenreId).ToList(),
                Analysis = db.MediaFiles.Where(f => f.TrackId == t.Id && f.MissingSince == null)
                    .SelectMany(f => db.AudioAnalyses.Where(a => a.ContentHash == f.ContentHash && a.State == AnalysisState.Analyzed))
                    .Select(a => new { a.Bpm, a.BpmConfidence, a.KeyTonic, a.KeyMinor })
                    .FirstOrDefault(),
            })
            .ToListAsync(library.Cancellation);
        return rows.ToDictionary(r => r.Id, r => new Sound(
            r.Id,
            r.AlbumId,
            r.Artist,
            r.Genres,
            r.Analysis is { Bpm: { } bpm, BpmConfidence: >= 0.1f } ? bpm : null,
            r.Analysis is { KeyTonic: { } tonic, KeyMinor: { } minor } ? DiscoveryService.Camelot(tonic, minor) : null));
    }

    /// <summary>How alike two songs sound, from 0 to 1: genres, tempo, key and how related their artists are.</summary>
    private static double Similarity(Sound a, Sound b, IReadOnlySet<Guid> relatedArtists) =>
        Similarity(a.Genres, a.Bpm, a.Key, b, a.ArtistId is { } artist && b.ArtistId == artist ? 1 : b.ArtistId is { } other && relatedArtists.Contains(other) ? 0.7 : 0);

    private static double Similarity(IReadOnlyCollection<Guid> genres, double? bpm, (int Number, bool Minor)? key, Sound b, double artistTie)
    {
        var union = genres.Union(b.Genres).Count();
        var genre = union == 0 ? 0.5 : (double)genres.Intersect(b.Genres).Count() / union;
        var tempo = bpm is { } x && b.Bpm is { } y ? Math.Max(0, 1 - DiscoveryService.TempoGap(x, y) / 16) : 0.5;
        var harmony = key is { } k && b.Key is { } l
            ? DiscoveryService.CamelotSteps(k, l) switch { 0 => 1, 1 => 0.8, 2 => 0.4, _ => 0.1 }
            : 0.5;
        return Math.Round(0.35 * genre + 0.3 * tempo + 0.2 * harmony + 0.15 * artistTie, 3);
    }

    /// <summary>Songs that sound like this one, most alike first; no more than three from one album.</summary>
    private static async Task<IResult> SimilarTracksAsync(MstreamCall call)
    {
        await RequireAsync(call, MstreamFeature.Similar);
        var library = new MstreamLibrary(call);
        var file = await library.ResolveAsync(call.Body.String("filePath") ?? call.Body.String("filepath")) ?? throw MstreamException.NotFound();
        var limit = Math.Clamp(call.Body.Int("limit") ?? 20, 1, MaxSimilar);

        var visible = await library.TracksAsync();
        var seed = (await SoundsAsync(library, visible.Where(t => t.Id == file.TrackId))).Values.SingleOrDefault() ?? throw MstreamException.NotFound();
        var related = seed.ArtistId is { } artist
            ? (await call.Service<DiscoveryService>().RelatedArtistIdsAsync(call.Access, artist, 10, call.Cancellation)).ToHashSet()
            : [];
        var artists = related.Append(seed.ArtistId ?? Guid.Empty).ToList();
        var pool = visible.Where(t => t.Id != seed.TrackId
                                      && (t.Genres.Any(g => seed.Genres.Contains(g.GenreId)) || t.Artists.Any(a => artists.Contains(a.ArtistId))))
            .OrderBy(_ => EF.Functions.Random())
            .Take(SimilarPool);
        var candidates = await SoundsAsync(library, pool);

        var perAlbum = new Dictionary<Guid, int>();
        var picked = new List<(Guid TrackId, double Score)>();
        foreach (var (sound, score) in candidates.Values
                     .Select(c => (Sound: c, Score: Similarity(seed, c, related)))
                     .OrderByDescending(c => c.Score).ThenBy(c => c.Sound.TrackId))
        {
            if (perAlbum.GetValueOrDefault(sound.AlbumId) >= 3)
                continue;
            perAlbum[sound.AlbumId] = perAlbum.GetValueOrDefault(sound.AlbumId) + 1;
            picked.Add((sound.TrackId, score));
            if (picked.Count == limit)
                break;
        }

        var results = await ResultsAsync(library, picked);
        return MstreamCall.Json(new JsonObject { ["notAnalyzed"] = !seed.Analyzed, ["results"] = new JsonArray(results.ToArray()) });
    }

    /// <summary>Artists like this one (sharing its genres or its albums), each with two of its songs closest to it.</summary>
    private static async Task<IResult> SimilarArtistsAsync(MstreamCall call)
    {
        await RequireAsync(call, MstreamFeature.Similar);
        var library = new MstreamLibrary(call);
        var name = call.Body.Required("artist");
        var limit = Math.Clamp(call.Body.Int("limit") ?? 10, 1, MaxSimilar);
        var visible = await library.TracksAsync();
        var normalized = TextNormalizer.Normalize(name);
        var artistId = await library.Db.Artists.AsNoTracking()
                           .Where(a => a.NormalizedName == normalized && visible.Any(t => t.Artists.Any(ta => ta.ArtistId == a.Id)))
                           .Select(a => (Guid?)a.Id)
                           .FirstOrDefaultAsync(call.Cancellation)
                       ?? throw MstreamException.NotFound();

        var own = (await SoundsAsync(library, visible.Where(t => t.Artists.Any(a => a.ArtistId == artistId && a.Role == ArtistRole.Primary)))).Values.ToList();
        var genres = own.SelectMany(s => s.Genres).GroupBy(g => g).OrderByDescending(g => g.Count()).Take(3).Select(g => g.Key).ToList();
        var bpms = own.Where(s => s.Bpm is not null).Select(s => s.Bpm!.Value).Order().ToList();
        var bpm = bpms.Count > 0 ? bpms[bpms.Count / 2] : (double?)null;
        var key = own.Where(s => s.Key is not null).GroupBy(s => s.Key).OrderByDescending(g => g.Count()).Select(g => g.Key).FirstOrDefault();

        var related = await call.Service<DiscoveryService>().RelatedArtistIdsAsync(call.Access, artistId, limit, call.Cancellation);
        var names = await library.Db.Artists.AsNoTracking().Where(a => related.Contains(a.Id)).ToDictionaryAsync(a => a.Id, a => a.Name, call.Cancellation);
        var genreNames = await library.Db.Genres.AsNoTracking().ToDictionaryAsync(g => g.Id, g => g.Name, call.Cancellation);

        var results = new JsonArray();
        foreach (var other in related.Where(names.ContainsKey))
        {
            var sounds = (await SoundsAsync(library, visible.Where(t => t.Artists.Any(a => a.ArtistId == other && a.Role == ArtistRole.Primary)))).Values.ToList();
            if (sounds.Count == 0)
                continue;
            var scored = sounds.Select(s => (Sound: s, Score: Similarity(genres, bpm, key, s, 0.7))).OrderByDescending(s => s.Score).ThenBy(s => s.Sound.TrackId).ToList();
            var entryPoints = await ResultsAsync(library, scored.Take(2).Select(s => (s.Sound.TrackId, s.Score)).ToList(),
                (entry, _) => entry.Remove("similarity"));
            results.Add(new JsonObject
            {
                ["artist"] = names[other],
                ["similarity"] = Math.Round(scored.Take(5).Average(s => s.Score), 3),
                ["analyzedCount"] = sounds.Count(s => s.Analyzed),
                ["genreTags"] = new JsonArray(sounds.SelectMany(s => s.Genres).GroupBy(g => g).OrderByDescending(g => g.Count()).Take(3)
                    .Where(g => genreNames.ContainsKey(g.Key)).Select(g => (JsonNode?)genreNames[g.Key]).ToArray()),
                ["entryPoints"] = new JsonArray(entryPoints.ToArray()),
            });
        }

        return MstreamCall.Json(new JsonObject { ["notAnalyzed"] = own.All(s => !s.Analyzed), ["capped"] = false, ["results"] = results });
    }

    /// <summary>
    /// A journey from one song to another: each stop moves the tempo, the key and the genres a step closer to the
    /// destination. Both ends are included, so the answer is the queue.
    /// </summary>
    private static async Task<IResult> JourneyAsync(MstreamCall call)
    {
        await RequireAsync(call, MstreamFeature.Similar);
        var library = new MstreamLibrary(call);
        var startFile = await library.ResolveAsync(call.Body.String("startFilePath")) ?? throw MstreamException.NotFound();
        var endFile = await library.ResolveAsync(call.Body.String("endFilePath")) ?? throw MstreamException.NotFound();
        var length = Math.Clamp(call.Body.Int("length") ?? 14, 4, 32);

        var visible = await library.TracksAsync();
        var ends = await SoundsAsync(library, visible.Where(t => t.Id == startFile.TrackId || t.Id == endFile.TrackId));
        if (!ends.TryGetValue(startFile.TrackId, out var start) || !ends.TryGetValue(endFile.TrackId, out var end))
            throw MstreamException.NotFound();
        var notAnalyzed = new JsonObject { ["start"] = start.Bpm is null, ["end"] = end.Bpm is null };
        if (start.Bpm is null || end.Bpm is null)
            return MstreamCall.Json(new JsonObject { ["notAnalyzed"] = notAnalyzed, ["results"] = new JsonArray() });

        var pool = await SoundsAsync(library, visible.Where(t => t.Id != start.TrackId && t.Id != end.TrackId).OrderBy(_ => EF.Functions.Random()).Take(SimilarPool));
        var remaining = pool.Values.Where(s => s.Bpm is not null).ToList();
        var stops = new List<(Guid TrackId, double T, double Score)> { (start.TrackId, 0, 1) };
        var albums = new HashSet<Guid> { start.AlbumId, end.AlbumId };
        for (var i = 1; i < length - 1 && remaining.Count > 0; i++)
        {
            var t = (double)i / (length - 1);
            var bpm = start.Bpm.Value + (end.Bpm.Value - start.Bpm.Value) * t;
            var key = t < 0.5 ? start.Key : end.Key;
            var genres = t < 0.5 ? start.Genres : end.Genres;
            var best = remaining
                .Select(s => (Sound: s, Score: Similarity(genres, bpm, key, s, 0) - (albums.Contains(s.AlbumId) ? 0.2 : 0)))
                .OrderByDescending(s => s.Score).ThenBy(s => s.Sound.TrackId)
                .First();
            stops.Add((best.Sound.TrackId, Math.Round(t, 3), Math.Max(0, best.Score)));
            albums.Add(best.Sound.AlbumId);
            remaining.Remove(best.Sound);
        }

        stops.Add((end.TrackId, 1, 1));
        var results = await ResultsAsync(library, stops.Select(s => (s.TrackId, s.Score)).ToList(), (stop, index) => stop["t"] = stops[index].T);
        return MstreamCall.Json(new JsonObject { ["notAnalyzed"] = notAnalyzed, ["results"] = new JsonArray(results.ToArray()) });
    }

    /// <summary>Track objects with their similarity, in the given order (tracks the user can no longer see are left out).</summary>
    /// <param name="decorate">Adds to each result, given its index in <paramref name="tracks"/>.</param>
    private static async Task<List<JsonNode?>> ResultsAsync(
        MstreamLibrary library, IReadOnlyList<(Guid TrackId, double Score)> tracks, Action<JsonObject, int>? decorate = null)
    {
        var models = (await library.TracksAsync(tracks.Select(t => t.TrackId).ToList())).Where(t => t.Files.Count > 0).ToDictionary(t => t.Id);
        var items = tracks.Where(t => models.ContainsKey(t.TrackId)).Select(t => (models[t.TrackId], models[t.TrackId].Files[0].Id)).ToList();
        var objects = await library.ObjectsByFileAsync(items);
        var results = new List<JsonNode?>();
        for (var i = 0; i < tracks.Count; i++)
        {
            var (trackId, score) = tracks[i];
            if (!models.TryGetValue(trackId, out var track) || !objects.TryGetValue(track.Files[0].Id, out var found))
                continue;
            var result = (JsonObject)found.DeepClone();
            result["similarity"] = score;
            decorate?.Invoke(result, i);
            results.Add(result);
        }

        return results;
    }
}
