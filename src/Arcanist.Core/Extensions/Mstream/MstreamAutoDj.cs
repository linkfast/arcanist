using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Arcanist.Core.Application.Mstream;
using Arcanist.Core.Data;
using Microsoft.EntityFrameworkCore;

namespace Arcanist.Core.Extensions.Mstream;

/// <summary>
/// mStream's auto-DJ (<c>db/random-songs</c>): random songs within always-on limits (rating, genres, length, a
/// similarity pool), and a waterfall that relaxes tempo and key until something matches, preferring similar artists
/// and avoiding artists on cooldown and songs served recently.
/// </summary>
internal sealed partial class MstreamExtension
{
    private const int MaxAutoDjPool = 5000;
    private const int MaxAutoDjBatch = 25;
    private const int IgnoreListLength = 50;

    /// <summary>
    /// The integer mStream apps know a track by in <c>ignoreList</c>: from the random half of its id, so it is stable
    /// and only collides by chance.
    /// </summary>
    internal static int ShortId(Guid id)
    {
        var random = BitConverter.ToInt64(id.ToByteArray(), 8);
        return (int)(random ^ (random >> 32)) & int.MaxValue;
    }

    private static async Task<IResult> RandomSongsAsync(MstreamCall call)
    {
        var body = call.Body;
        var library = new MstreamLibrary(call);
        var db = library.Db;
        var limit = Math.Clamp(body.Int("limit") ?? 1, 1, MaxAutoDjBatch);
        var tracks = await library.TracksAsync();
        var user = call.Access.UserId;

        // Always-on limits: never relaxed.
        if (body.Int("minRating") is > 0 and var minRating)
        {
            var stars = (minRating + 1) / 2;
            tracks = tracks.Where(t => db.TrackRatings.Any(r => r.UserId == user && r.TrackId == t.Id && r.Rating >= stars));
        }

        var genres = body.Strings("genres").Select(TextNormalizer.Normalize).Where(g => g.Length > 0).Distinct().ToList();
        if (genres.Count > 0)
        {
            tracks = body.String("genreMode") == "blacklist"
                ? tracks.Where(t => !t.Genres.Any(g => genres.Contains(g.Genre.NormalizedName)))
                : tracks.Where(t => t.Genres.Any(g => genres.Contains(g.Genre.NormalizedName)));
        }

        var minMs = (int)Math.Round((Number(body, "minDuration") ?? 0) * 1000);
        var maxMs = (int)Math.Round((Number(body, "maxDuration") ?? 0) * 1000);
        if (minMs > 0 && maxMs > 0 && minMs > maxMs)
            throw MstreamException.Invalid("minDuration");
        if (minMs > 0 || maxMs > 0)
        {
            var unknownToo = body.Bool("allowUnknownDuration");
            tracks = tracks.Where(t => t.DurationMs == 0 ? unknownToo : (minMs == 0 || t.DurationMs >= minMs) && (maxMs == 0 || t.DurationMs <= maxMs));
        }

        var tight = Ranges(body, "bpmRanges");
        var wide = Ranges(body, "bpmRangesWide");
        var codes = body.Strings("musicalKeys");
        var keys = codes.Select(CamelotKey).OfType<(int Number, bool Minor)>().ToHashSet();
        if (codes.Count > 0 && keys.Count == 0)
            throw MstreamException.Invalid("musicalKeys");
        var requireBpm = body.Bool("requireBpm");
        var requireKey = body.Bool("requireMusicalKey");

        // A similarity pool around one to eight seeds: a hard limit, like the genres.
        var seedPaths = body.Strings("similarTo").Take(8).ToList();
        var minSimilarity = Number(body, "minSimilarity");
        List<Sound> seeds = [];
        if (seedPaths.Count > 0 || body.Has("similarToVector"))
        {
            await RequireAsync(call, MstreamFeature.Similar);
            if (body.Has("similarToVector"))
                throw new MstreamException(StatusCodes.Status400BadRequest, "Sonic vectors are not supported by this server");
            if (minSimilarity is null)
                throw MstreamException.Invalid("minSimilarity");
            var seedIds = new List<Guid>();
            foreach (var path in seedPaths)
                seedIds.Add((await library.ResolveAsync(path) ?? throw MstreamException.NotFound()).TrackId);
            seeds = (await SoundsAsync(library, (await library.TracksAsync()).Where(t => seedIds.Contains(t.Id)))).Values.ToList();
            if (seeds.All(s => !s.Analyzed))
                throw new MstreamException(StatusCodes.Status400BadRequest, "Sonic seed track has not been analyzed yet");
            tracks = tracks.Where(t => !seedIds.Contains(t.Id));
        }
        else if (minSimilarity is not null)
            throw MstreamException.Invalid("similarTo");

        var pool = (await SoundsAsync(library, tracks.OrderBy(_ => EF.Functions.Random()).Take(MaxAutoDjPool))).Values.ToList();
        if (requireBpm && tight.Count == 0)
            pool = pool.Where(s => s.Bpm is not null).ToList();
        if (requireKey || keys.Count > 0)
            pool = pool.Where(s => s.Key is not null).ToList();

        Dictionary<Guid, double> similarity = [];
        var poolSize = 0;
        if (seeds.Count > 0)
        {
            similarity = pool.ToDictionary(s => s.TrackId, s => seeds.Max(seed => Similarity(seed, s, new HashSet<Guid>())));
            pool = pool.Where(s => similarity[s.TrackId] >= minSimilarity).ToList();
            poolSize = pool.Count;
            if (pool.Count == 0)
                throw new MstreamException(StatusCodes.Status400BadRequest, "No songs within the similarity range match criteria");
        }

        var ids = pool.Select(s => s.TrackId).ToList();
        var similarArtists = await ArtistTracksAsync(library, ids, body.Strings("artists"));
        var cooldown = await ArtistTracksAsync(library, ids, body.Strings("ignoreArtists"));

        // The waterfall: tempo and key together, then the wide tempo, then tempo alone, then nothing.
        bool InBpm(Sound s, List<(double Min, double Max)> ranges) => s.Bpm is { } bpm && ranges.Any(r => bpm >= r.Min && bpm <= r.Max);
        bool InKey(Sound s) => s.Key is { } key && keys.Contains(key);
        var steps = new List<Func<Sound, bool>>();
        if (tight.Count > 0 && keys.Count > 0)
            steps.Add(s => InBpm(s, tight) && InKey(s));
        if (wide.Count > 0 && keys.Count > 0)
            steps.Add(s => InBpm(s, wide) && InKey(s));
        if (tight.Count > 0)
            steps.Add(s => InBpm(s, tight));
        if (wide.Count > 0)
            steps.Add(s => InBpm(s, wide));
        if (tight.Count == 0 && keys.Count > 0)
            steps.Add(InKey);
        steps.Add(_ => true);

        // Similar artists first (then even those on cooldown), then everyone off cooldown, then anyone.
        var groups = new List<Func<Sound, bool>>();
        if (similarArtists is not null)
        {
            groups.Add(s => similarArtists.Contains(s.TrackId) && cooldown?.Contains(s.TrackId) != true);
            if (cooldown is not null)
                groups.Add(s => similarArtists.Contains(s.TrackId));
        }

        groups.Add(s => cooldown?.Contains(s.TrackId) != true);
        if (cooldown is not null)
            groups.Add(_ => true);

        List<Sound> matches = [];
        foreach (var group in groups)
        {
            foreach (var step in steps)
            {
                matches = pool.Where(s => group(s) && step(s)).ToList();
                if (matches.Count > 0)
                    break;
            }

            if (matches.Count > 0)
                break;
        }

        if (matches.Count == 0)
            throw new MstreamException(StatusCodes.Status400BadRequest, "No songs that match criteria");

        // Fresh songs before the ones served recently (a soft cooldown: repeats fill in when there is nothing else).
        var recent = new List<int>();
        if (body.Root is { ValueKind: JsonValueKind.Object } root && root.TryGetProperty("ignoreList", out var list) && list.ValueKind == JsonValueKind.Array)
            recent.AddRange(list.EnumerateArray().Where(e => e.TryGetInt32(out _)).Select(e => e.GetInt32()).TakeLast(500));
        var ignored = recent.ToHashSet();
        var picked = matches
            .OrderBy(s => ignored.Contains(ShortId(s.TrackId)) ? 1 : 0)
            .ThenBy(s => seeds.Count > 0 ? -similarity[s.TrackId] : 0)
            .ThenBy(_ => Random.Shared.Next())
            .Take(limit)
            .ToList();

        var songs = await ResultsAsync(library, picked.Select(s => (s.TrackId, similarity.GetValueOrDefault(s.TrackId))).ToList(), (song, _) => song.Remove("similarity"));
        // Songs of other servers' libraries are fetched while the current one plays: Auto-DJ never waits for the network.
        await call.Service<P2P.PeerCache>().PrefetchTracksAsync(db, picked.Select(s => s.TrackId).ToList(), MaxAutoDjBatch, call.Cancellation);
        recent.AddRange(picked.Select(s => ShortId(s.TrackId)));
        var result = new JsonObject
        {
            ["songs"] = new JsonArray(songs.ToArray()),
            ["ignoreList"] = new JsonArray(recent.TakeLast(IgnoreListLength).Select(id => (JsonNode?)id).ToArray()),
        };
        if (seeds.Count > 0)
        {
            var scores = picked.Select(s => (JsonNode?)Math.Round(similarity[s.TrackId], 4)).ToArray();
            result["sonic"] = new JsonObject
            {
                ["similarity"] = scores.Length > 0 ? scores[0]?.DeepClone() : null,
                ["similarities"] = new JsonArray(scores),
                ["poolSize"] = poolSize,
            };
        }

        return MstreamCall.Json(result);
    }

    /// <summary>
    /// The tracks of the pool credited to any of these artists (as main or featured artist, or on their albums); null
    /// when no artist is named.
    /// </summary>
    private static async Task<HashSet<Guid>?> ArtistTracksAsync(MstreamLibrary library, IReadOnlyList<Guid> pool, IReadOnlyList<string> names)
    {
        var normalized = names.Select(TextNormalizer.Normalize).Where(n => n.Length > 0).Distinct().ToList();
        if (normalized.Count == 0)
            return null;
        var artists = library.Db.Artists.Where(a => normalized.Contains(a.NormalizedName)).Select(a => a.Id);
        return (await library.Db.Tracks.AsNoTracking()
                .Where(t => pool.Contains(t.Id) && (t.Artists.Any(a => artists.Contains(a.ArtistId)) || artists.Contains(t.Album.AlbumArtistId)))
                .Select(t => t.Id)
                .ToListAsync(library.Cancellation))
            .ToHashSet();
    }

    /// <summary>A Camelot code (<c>8A</c> is A minor, <c>8B</c> C major); null when it is not one.</summary>
    internal static (int Number, bool Minor)? CamelotKey(string code)
    {
        code = code.Trim().ToUpperInvariant();
        return code.Length is 2 or 3 && code[^1] is 'A' or 'B'
                                     && int.TryParse(code[..^1], NumberStyles.None, CultureInfo.InvariantCulture, out var number) && number is >= 1 and <= 12
            ? (number, code[^1] == 'A')
            : null;
    }

    private static List<(double Min, double Max)> Ranges(MstreamBody body, string name)
    {
        var ranges = new List<(double, double)>();
        if (body.Root is not { ValueKind: JsonValueKind.Object } root || !root.TryGetProperty(name, out var array) || array.ValueKind != JsonValueKind.Array)
            return ranges;
        foreach (var range in array.EnumerateArray().Take(16))
        {
            if (range.ValueKind != JsonValueKind.Object || !range.TryGetProperty("min", out var min) || !range.TryGetProperty("max", out var max)
                || !min.TryGetDouble(out var low) || !max.TryGetDouble(out var high) || low > high)
                throw MstreamException.Invalid(name);
            ranges.Add((low, high));
        }

        return ranges;
    }

    private static double? Number(MstreamBody body, string name) =>
        body.Root is { ValueKind: JsonValueKind.Object } root && root.TryGetProperty(name, out var value) && value.TryGetDouble(out var number) ? number : null;
}
