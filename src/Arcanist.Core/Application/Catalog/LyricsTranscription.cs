using System.Collections.Concurrent;
using System.Text.Json;
using Arcanist.Core.Application.Online;
using Arcanist.Core.Data;
using Arcanist.Core.Data.Entities;
using Arcanist.Core.Online;
using Arcanist.Core.Storage;
using Microsoft.EntityFrameworkCore;

namespace Arcanist.Core.Application.Catalog;

/// <summary>A song without lyrics whose words a model can recognize, with a copy on this server's disks.</summary>
/// <param name="Forced">Someone asked for it: it is tried whatever was tried before.</param>
public sealed record UntranscribedSong(Guid TrackId, string Title, string? Artist, int DurationMs, string AudioPath, int? StartMs, int? EndMs, bool Forced);

/// <summary>A line a model recognized: when it starts, its words and how sure the model is of them (0–1).</summary>
public sealed record TranscribedLine(int TimeMs, string Text, double? Confidence);

/// <summary>How a recognition the extension took ended, when it saved nothing.</summary>
public enum TranscriptionOutcome
{
    /// <summary>Nothing sung was heard: an instrumental, or a voice the model cannot make out.</summary>
    NoVoice,
    /// <summary>Something failed (the file, ffmpeg, the model).</summary>
    Failed,
}

/// <summary>Where a song's recognition stands, as this server knows it (in memory: a restart forgets it).</summary>
public enum TranscriptionProgress
{
    None,
    Queued,
    Working,
    NoVoice,
    Failed,
}

/// <summary>
/// The songs without lyrics a recognition extension (Lyrics Sync) can write the words of: those a user asks for
/// (from a player, <c>requestLyricsTranscription</c>), and in the background, if the extension's administrator turns
/// it on, all the others (with LRCLib on, only those LRCLib does not have). What it recognizes is saved as a
/// <see cref="LyricsDraft"/>, which nobody sees as the song's lyrics until a person reviews and accepts it.
/// </summary>
public sealed class LyricsTranscription(
    IArcanistDbContextFactory databases, DatabaseMaintenance maintenance, ILibraryStorageResolver storages, OnlineSettings online, TimeProvider time)
{
    /// <summary>How long the end of a recognition (no voice, failed) is remembered for players.</summary>
    public static readonly TimeSpan OutcomeKept = TimeSpan.FromDays(1);

    /// <summary>How long a recognition the extension took counts as in progress when it never says how it ended.</summary>
    public static readonly TimeSpan WorkLimit = TimeSpan.FromHours(1);

    private readonly ConcurrentDictionary<Guid, DateTime> _requested = new();
    private readonly ConcurrentDictionary<Guid, (TranscriptionProgress Progress, DateTime At)> _progress = new();

    /// <summary>Raised when someone asks for a song's words.</summary>
    public event Action? Requested;

    /// <summary>Asks for a song's words to be recognized, before the background work (and whatever was tried before).</summary>
    public void Request(Guid trackId)
    {
        var now = time.GetUtcNow().UtcDateTime;
        _requested[trackId] = now;
        _progress[trackId] = (TranscriptionProgress.Queued, now);
        Requested?.Invoke();
    }

    /// <summary>The songs asked for, oldest first, and forgets the asking (they stay queued until taken).</summary>
    public IReadOnlyList<Guid> TakeRequested()
    {
        var ids = _requested.OrderBy(r => r.Value).Select(r => r.Key).ToList();
        foreach (var id in ids)
            _requested.TryRemove(id, out _);
        return ids;
    }

    /// <summary>The extension started on a song.</summary>
    public void Start(Guid trackId) => _progress[trackId] = (TranscriptionProgress.Working, time.GetUtcNow().UtcDateTime);

    /// <summary>The extension saved nothing for a song: nothing sung was heard, or it failed.</summary>
    public void End(Guid trackId, TranscriptionOutcome outcome) =>
        _progress[trackId] = (outcome == TranscriptionOutcome.NoVoice ? TranscriptionProgress.NoVoice : TranscriptionProgress.Failed, time.GetUtcNow().UtcDateTime);

    /// <summary>The extension cannot work now (not set up): nothing asked for stays queued.</summary>
    public void EndAll()
    {
        _requested.Clear();
        foreach (var (id, state) in _progress)
            if (state.Progress is TranscriptionProgress.Queued or TranscriptionProgress.Working)
                _progress.TryRemove(id, out _);
    }

    /// <summary>Where a song's recognition stands (queued ones wait a day at most, and outcomes are kept a day).</summary>
    public TranscriptionProgress ProgressOf(Guid trackId)
    {
        if (!_progress.TryGetValue(trackId, out var state))
            return TranscriptionProgress.None;
        var age = time.GetUtcNow().UtcDateTime - state.At;
        var current = state.Progress switch
        {
            TranscriptionProgress.Working when age >= WorkLimit => TranscriptionProgress.None,
            _ when age >= OutcomeKept => TranscriptionProgress.None,
            _ => state.Progress,
        };
        if (current == TranscriptionProgress.None)
            _progress.TryRemove(new KeyValuePair<Guid, (TranscriptionProgress, DateTime)>(trackId, state));
        return current;
    }

    /// <summary>Forgets where a song stood (its draft was saved, or it got lyrics).</summary>
    public void Forget(Guid trackId) => _progress.TryRemove(trackId, out _);

    /// <summary>
    /// The next songs for the background, in track id order after <paramref name="after"/> (null: from the start), at
    /// most <paramref name="take"/>. The caller skips what it already tried and pages on with the last id.
    /// </summary>
    public async Task<IReadOnlyList<UntranscribedSong>> PendingAsync(Guid? after, int take, CancellationToken cancellationToken)
    {
        await using var db = databases.CreateDbContext();
        var query = await WithoutLyricsAsync(db, lrcLibFirst: true, cancellationToken);
        if (after is { } last)
            query = query.Where(t => t.Id.CompareTo(last) > 0);
        var ids = await query.OrderBy(t => t.Id).Select(t => t.Id).Take(take).ToListAsync(cancellationToken);
        return await DescribeAsync(db, ids, forced: false, cancellationToken);
    }

    /// <summary>These songs (asked for by someone), if they still have no lyrics and no draft, in the same order.</summary>
    public async Task<IReadOnlyList<UntranscribedSong>> FindAsync(IReadOnlyCollection<Guid> trackIds, CancellationToken cancellationToken)
    {
        await using var db = databases.CreateDbContext();
        var ids = await (await WithoutLyricsAsync(db, lrcLibFirst: false, cancellationToken)).Where(t => trackIds.Contains(t.Id))
            .Select(t => t.Id).ToListAsync(cancellationToken);
        var found = (await DescribeAsync(db, ids, forced: true, cancellationToken)).ToDictionary(s => s.TrackId);
        foreach (var id in trackIds.Where(id => !found.ContainsKey(id)))
            Forget(id);
        return trackIds.Where(found.ContainsKey).Select(id => found[id]).ToList();
    }

    /// <summary>
    /// Saves what the model recognized as the song's draft, to be reviewed. Nothing is saved when the song got lyrics
    /// meanwhile, or when it already has a draft (a person may be reviewing it); returns whether it was saved.
    /// </summary>
    public async Task<bool> SaveDraftAsync(Guid trackId, IReadOnlyList<TranscribedLine> lines, string? language, string? model,
        CancellationToken cancellationToken)
    {
        try
        {
            if (!lines.Any(l => !string.IsNullOrWhiteSpace(l.Text)))
                return false;
            using var work = await maintenance.WorkAsync(cancellationToken);
            await using var db = databases.CreateDbContext();
            if (await HasWordsAsync(db, trackId, cancellationToken) || await db.LyricsDrafts.AnyAsync(d => d.TrackId == trackId, cancellationToken)
                || !await db.Tracks.AnyAsync(t => t.Id == trackId, cancellationToken))
                return false;
            var now = time.GetUtcNow().UtcDateTime;
            db.LyricsDrafts.Add(new LyricsDraft
            {
                TrackId = trackId,
                Lines = SerializeLines(lines),
                Language = language is { Length: > 0 and <= 16 } ? language : null,
                Model = model is { Length: > 100 } ? model[..100] : model,
                CreatedAt = now,
                UpdatedAt = now,
            });
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        finally
        {
            Forget(trackId);
        }
    }

    /// <summary>How many songs have a draft waiting for review, and how many could be recognized in the background.</summary>
    public async Task<(int Drafts, int WithoutLyrics)> CountAsync(CancellationToken cancellationToken)
    {
        await using var db = databases.CreateDbContext();
        var drafts = await db.LyricsDrafts.CountAsync(cancellationToken);
        var without = await (await WithoutLyricsAsync(db, lrcLibFirst: true, cancellationToken)).CountAsync(cancellationToken);
        return (drafts, without);
    }

    public static string SerializeLines(IEnumerable<TranscribedLine> lines) =>
        JsonSerializer.Serialize(lines.OrderBy(l => l.TimeMs).Select(l => new StoredLine(Math.Max(0, l.TimeMs), l.Text.Trim(),
            l.Confidence is { } c ? Math.Round(Math.Clamp(c, 0, 1), 3) : null)), JsonOptions);

    public static IReadOnlyList<TranscribedLine> ParseLines(string json) =>
        (JsonSerializer.Deserialize<List<StoredLine>>(json, JsonOptions) ?? []).Select(l => new TranscribedLine(l.TimeMs, l.Text, l.Confidence)).ToList();

    private sealed record StoredLine(int TimeMs, string Text, double? Confidence);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>Whether the track has lyrics with words (or is known to be instrumental).</summary>
    internal static Task<bool> HasWordsAsync(ArcanistDbContext db, Guid trackId, CancellationToken cancellationToken) =>
        db.Lyrics.AnyAsync(l => l.TrackId == trackId && (l.Instrumental || l.Plain != null || l.Synced != null), cancellationToken);

    /// <summary>
    /// Tracks with a present copy on this server's disks, no lyrics with words (and not known instrumental) and no
    /// draft. In the background with LRCLib on, LRCLib goes first: only what it did not find.
    /// </summary>
    private async Task<IQueryable<Data.Entities.Track>> WithoutLyricsAsync(ArcanistDbContext db, bool lrcLibFirst, CancellationToken cancellationToken)
    {
        var query = db.Tracks.AsNoTracking()
            .Where(t => t.Files.Any(f => f.MissingSince == null && f.LibrarySource.Kind == LibrarySourceKind.Local))
            .Where(t => !db.Lyrics.Any(l => l.TrackId == t.Id && (l.Instrumental || l.Plain != null || l.Synced != null)))
            .Where(t => !db.LyricsDrafts.Any(d => d.TrackId == t.Id));
        if (!lrcLibFirst || !(await online.GetAsync(cancellationToken)).Lyrics)
            return query;
        var lrcLib = OnlineService.Lyrics;
        return query.Where(t => db.OnlineLookups.Any(o => o.Service == lrcLib && o.Subject == t.Id.ToString() && o.Outcome == LookupOutcome.NotFound));
    }

    private async Task<IReadOnlyList<UntranscribedSong>> DescribeAsync(ArcanistDbContext db, List<Guid> ids, bool forced, CancellationToken cancellationToken)
    {
        if (ids.Count == 0)
            return [];
        var rows = await db.Tracks.AsNoTracking().Where(t => ids.Contains(t.Id))
            .Select(t => new
            {
                t.Id,
                t.Title,
                t.DurationMs,
                Artist = t.Artists.Where(a => a.Role == ArtistRole.Primary).OrderBy(a => a.Position).Select(a => a.Artist.Name).FirstOrDefault(),
            })
            .ToListAsync(cancellationToken);
        var files = (await db.MediaFiles.AsNoTracking()
                .Where(f => ids.Contains(f.TrackId) && f.MissingSince == null && f.LibrarySource.Kind == LibrarySourceKind.Local)
                .Select(f => new { f.TrackId, f.RelativePath, f.LibrarySource, f.StartMs, f.EndMs, f.BitDepth, f.BitRate })
                .ToListAsync(cancellationToken))
            .GroupBy(f => f.TrackId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(f => f.BitDepth ?? 0).ThenByDescending(f => f.BitRate ?? 0).ToList());

        var result = new List<UntranscribedSong>();
        foreach (var row in rows.OrderBy(r => ids.IndexOf(r.Id)))
        {
            foreach (var file in files.GetValueOrDefault(row.Id) ?? [])
            {
                if (storages.For(LibrarySourceKind.Local).TryGetLocalPath(StorageLocation.From(file.LibrarySource), file.RelativePath) is not { } path)
                    continue;
                result.Add(new UntranscribedSong(row.Id, row.Title, row.Artist, row.DurationMs, path, file.StartMs, file.EndMs, forced));
                break;
            }
        }

        return result;
    }
}
