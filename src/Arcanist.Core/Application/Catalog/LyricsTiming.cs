using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Arcanist.Core.Application.Online;
using Arcanist.Core.Data;
using Arcanist.Core.Data.Entities;
using Arcanist.Core.Online;
using Arcanist.Core.Scanning;
using Arcanist.Core.Storage;
using Microsoft.EntityFrameworkCore;

namespace Arcanist.Core.Application.Catalog;

/// <summary>
/// A track whose plain lyrics have no timing, and a copy of its audio on this server's disks to time them with.
/// </summary>
/// <param name="Plain">The words to time: the plain lyrics, or (re-syncing lyrics that only exist as timed lines) the
/// text of those lines.</param>
/// <param name="PlainHash">Identifies the text: timing saved for other words is not kept (see <see cref="LyricsTiming.SaveAsync"/>).</param>
/// <param name="AudioPath">The audio file; a song inside a file with a CUE sheet goes from <paramref name="StartMs"/> to <paramref name="EndMs"/>.</param>
public sealed record UntimedLyrics(
    Guid TrackId, string Plain, string PlainHash, string Title, string? Artist, int DurationMs, string AudioPath, int? StartMs, int? EndMs)
{
    /// <summary>
    /// A user asked to time this song again (<see cref="LyricsTiming.Resync"/>), whatever timing it has: it is tried
    /// even when it was tried before, and what is saved replaces its current timing. When it cannot be timed, the
    /// extension says so with <see cref="LyricsTiming.EndResync"/>.
    /// </summary>
    public bool Forced { get; init; }
}

/// <summary>How many tracks have lyrics and how many of them are timed, and by whom.</summary>
public sealed record LyricsTimingCounts(int WithLyrics, int Timed, int Aligned, int Untimed, int Waiting);

/// <summary>
/// The lyrics a timing extension can work on: plain lyrics without timing, of songs LRCLib has no timing for (or
/// with LRCLib off), with a copy of the audio on this server's disks. What the extension works out is saved as
/// <see cref="LyricsSource.Aligned"/> timing, which a rescan keeps while the text stays the same. A user may also ask
/// for a song to be timed again (<see cref="Resync"/>), whatever timing it has.
/// </summary>
public sealed class LyricsTiming(
    IArcanistDbContextFactory databases, DatabaseMaintenance maintenance, ILibraryStorageResolver storages, OnlineSettings online, TimeProvider time)
{
    /// <summary>How long a re-sync waits for the extension to take it (it waits while the server scans or streams).</summary>
    public static readonly TimeSpan ResyncWaitLimit = TimeSpan.FromHours(24);

    /// <summary>How long a re-sync the extension took counts as in progress when it never says how it ended.</summary>
    public static readonly TimeSpan ResyncWorkLimit = TimeSpan.FromHours(1);

    private readonly ConcurrentDictionary<Guid, DateTime> _requested = new();
    private readonly ConcurrentDictionary<Guid, ResyncMark> _resyncs = new();

    private sealed record ResyncMark(DateTime At, DateTime? TakenAt);

    /// <summary>Raised when a player asks for the lyrics of a song that has no timing yet, or a re-sync.</summary>
    public event Action? Requested;

    /// <summary>
    /// Asks for the timing of a song a listener is looking at, before the rest. Called when its lyrics are read;
    /// without a timing extension nothing happens.
    /// </summary>
    public void Request(Guid trackId)
    {
        if (_requested.TryAdd(trackId, time.GetUtcNow().UtcDateTime))
            Requested?.Invoke();
    }

    /// <summary>
    /// Asks for the song to be timed again, even when it has timing (from its file, LRCLib or worked out here): its
    /// timing stays until the new one is saved. In memory: a restart forgets it.
    /// </summary>
    public void Resync(Guid trackId)
    {
        var now = time.GetUtcNow().UtcDateTime;
        _resyncs[trackId] = new ResyncMark(now, null);
        _requested.TryAdd(trackId, now);
        Requested?.Invoke();
    }

    /// <summary>Whether a re-sync of the song is waiting for the extension or being worked on.</summary>
    public bool IsResyncPending(Guid trackId)
    {
        if (!_resyncs.TryGetValue(trackId, out var mark))
            return false;
        var now = time.GetUtcNow().UtcDateTime;
        if (mark.TakenAt is { } taken ? now - taken < ResyncWorkLimit : now - mark.At < ResyncWaitLimit)
            return true;
        _resyncs.TryRemove(new KeyValuePair<Guid, ResyncMark>(trackId, mark));
        return false;
    }

    /// <summary>The extension could not time a re-synced song (too little heard, or it failed): its timing stays.</summary>
    public void EndResync(Guid trackId) => _resyncs.TryRemove(trackId, out _);

    /// <summary>The extension cannot time anything now (not set up): no re-sync stays pending.</summary>
    public void EndAllResyncs() => _resyncs.Clear();

    /// <summary>The songs asked for, oldest first, and forgets them.</summary>
    public IReadOnlyList<Guid> TakeRequested()
    {
        var ids = _requested.OrderBy(r => r.Value).Select(r => r.Key).ToList();
        foreach (var id in ids)
            _requested.TryRemove(id, out _);
        return ids;
    }

    /// <summary>A hash of the plain text, to know whether the timing still matches it.</summary>
    public static string HashOf(string plain) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(plain.ReplaceLineEndings("\n").Trim())));

    /// <summary>A hash of LRC timing, the same however its lines and stamps are written.</summary>
    public static string TimingHashOf(string lrc) => HashOf(Lrc.Format(Lrc.Parse(lrc)));

    /// <summary>
    /// The words a song's timing is worked out for: its plain lyrics or, when it only has timed lines (or LRC kept as
    /// plain lyrics), their text. Null when there are none.
    /// </summary>
    public static string? TextOf(string? plain, string? synced)
    {
        if (plain is { } words && !string.IsNullOrWhiteSpace(words) && !Lrc.LooksTimed(words))
            return words;
        if ((synced ?? plain) is not { } lrc)
            return null;
        var text = string.Join('\n', Lrc.Parse(lrc).Select(l => l.Text).Where(t => t.Length > 0));
        return text.Length == 0 ? null : text;
    }

    /// <summary>
    /// The next songs to time, in track id order after <paramref name="after"/> (null: from the start), at most
    /// <paramref name="take"/>. The caller skips what it already tried and pages on with the last id.
    /// </summary>
    public async Task<IReadOnlyList<UntimedLyrics>> PendingAsync(Guid? after, int take, CancellationToken cancellationToken)
    {
        await using var db = databases.CreateDbContext();
        var query = await UntimedAsync(db, cancellationToken);
        if (after is { } last)
            query = query.Where(l => l.TrackId.CompareTo(last) > 0);
        var ids = await query.OrderBy(l => l.TrackId).Select(l => l.TrackId).Take(take).ToListAsync(cancellationToken);
        return await DescribeAsync(db, ids, forced: false, cancellationToken);
    }

    /// <summary>
    /// These songs, if they still need timing (in the same order). Songs with a pending re-sync are there whatever
    /// timing they have, marked <see cref="UntimedLyrics.Forced"/>.
    /// </summary>
    public async Task<IReadOnlyList<UntimedLyrics>> FindAsync(IReadOnlyCollection<Guid> trackIds, CancellationToken cancellationToken)
    {
        await using var db = databases.CreateDbContext();
        var forcedIds = trackIds.Where(IsResyncPending).ToList();
        var otherIds = trackIds.Except(forcedIds).ToList();

        var ids = otherIds.Count == 0
            ? []
            : await (await UntimedAsync(db, cancellationToken)).Where(l => otherIds.Contains(l.TrackId)).Select(l => l.TrackId).ToListAsync(cancellationToken);
        var found = (await DescribeAsync(db, ids, forced: false, cancellationToken)).ToDictionary(l => l.TrackId);

        if (forcedIds.Count > 0)
        {
            var timeable = await Timeable(db.Lyrics.AsNoTracking()).Where(l => forcedIds.Contains(l.TrackId)).Select(l => l.TrackId)
                .ToListAsync(cancellationToken);
            var now = time.GetUtcNow().UtcDateTime;
            foreach (var song in await DescribeAsync(db, timeable, forced: true, cancellationToken))
            {
                found[song.TrackId] = song;
                if (_resyncs.TryGetValue(song.TrackId, out var mark))
                    _resyncs.TryUpdate(song.TrackId, mark with { TakenAt = now }, mark);
            }

            // Nothing left to time (the lyrics or the copies went away).
            foreach (var id in forcedIds.Where(id => !found.ContainsKey(id)))
                EndResync(id);
        }

        return trackIds.Where(found.ContainsKey).Select(id => found[id]).ToList();
    }

    /// <summary>Lyrics, timed lyrics, those timed here, those left to time and those waiting for LRCLib first.</summary>
    public async Task<LyricsTimingCounts> CountAsync(CancellationToken cancellationToken)
    {
        await using var db = databases.CreateDbContext();
        var lyrics = db.Lyrics.AsNoTracking().Where(l => !l.Instrumental && (l.Plain != null || l.Synced != null));
        var withLyrics = await lyrics.CountAsync(cancellationToken);
        var timed = await lyrics.CountAsync(l => l.Synced != null, cancellationToken);
        var aligned = await lyrics.CountAsync(l => l.Synced != null && l.SyncedSource == LyricsSource.Aligned, cancellationToken);
        var untimed = await (await UntimedAsync(db, cancellationToken)).CountAsync(cancellationToken);
        return new LyricsTimingCounts(withLyrics, timed, aligned, untimed, Math.Max(0, withLyrics - timed - untimed));
    }

    /// <summary>
    /// Saves the timing worked out for a song's plain lyrics, in LRC. Nothing is saved when the lyrics changed
    /// since (<paramref name="plainHash"/>) or already have timing; returns whether it was saved. For a song with a
    /// pending re-sync, the timing replaces the one it has (see <see cref="Resync"/>).
    /// </summary>
    public async Task<bool> SaveAsync(Guid trackId, string plainHash, string lrc, CancellationToken cancellationToken)
    {
        var forced = IsResyncPending(trackId);
        try
        {
            using var work = await maintenance.WorkAsync(cancellationToken);
            await using var db = databases.CreateDbContext();
            var lyrics = await db.Lyrics.SingleOrDefaultAsync(l => l.TrackId == trackId, cancellationToken);
            if (Lrc.Parse(lrc).Count == 0)
                return false;

            if (forced)
            {
                if (lyrics is not { Instrumental: false } || TextOf(lyrics.Plain, lyrics.Synced) is not { } text || HashOf(text) != plainHash)
                    return false;
                // The file's own timing, to keep this one while the file still brings that (CatalogWriter).
                if (lyrics.SyncedSource is not LyricsSource.Aligned)
                    lyrics.ReplacedTimingHash = FileTiming(lyrics) is { } replaced ? TimingHashOf(replaced) : null;
                if (lyrics.Plain is null || Lrc.LooksTimed(lyrics.Plain))
                    lyrics.Plain = text;
            }
            else if (lyrics is not { Synced: null, Plain: { } plain, Instrumental: false } || HashOf(plain) != plainHash)
            {
                return false;
            }

            lyrics.Synced = lrc;
            lyrics.SyncedSource = LyricsSource.Aligned;
            lyrics.UpdatedAt = time.GetUtcNow().UtcDateTime;
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        finally
        {
            if (forced)
                EndResync(trackId);
        }
    }

    /// <summary>Removes the timing worked out here (all of it, or these songs'), so it is worked out again.</summary>
    public async Task<int> ClearAsync(IReadOnlyCollection<Guid>? trackIds, CancellationToken cancellationToken)
    {
        using var work = await maintenance.WorkAsync(cancellationToken);
        await using var db = databases.CreateDbContext();
        var query = db.Lyrics.Where(l => l.SyncedSource == LyricsSource.Aligned);
        if (trackIds is not null)
            query = query.Where(l => trackIds.Contains(l.TrackId));
        return await query.ExecuteUpdateAsync(s => s
            .SetProperty(l => l.Synced, (string?)null)
            .SetProperty(l => l.SyncedSource, (LyricsSource?)null)
            .SetProperty(l => l.ReplacedTimingHash, (string?)null), cancellationToken);
    }

    /// <summary>
    /// Lyrics with words that are not instrumental, of songs with a copy on this server's disks: what a re-sync can
    /// work on (<see cref="TextOf"/> has the last word on the words).
    /// </summary>
    internal static IQueryable<Lyrics> Timeable(IQueryable<Lyrics> lyrics) =>
        lyrics.Where(l => (l.Plain != null || l.Synced != null) && !l.Instrumental)
            .Where(l => l.Track.Files.Any(f => f.MissingSince == null && f.LibrarySource.Kind == LibrarySourceKind.Local));

    /// <summary>The timing a track's file brings, if its timing came from the file.</summary>
    private static string? FileTiming(Lyrics lyrics) =>
        lyrics.SyncedSource is null && lyrics.Source is LyricsSource.Sidecar or LyricsSource.Embedded
            ? lyrics.Synced ?? (lyrics.Plain is { } plain && Lrc.LooksTimed(plain) ? plain : null)
            : null;

    private async Task<IQueryable<Lyrics>> UntimedAsync(ArcanistDbContext db, CancellationToken cancellationToken)
    {
        var withLrcLib = (await online.GetAsync(cancellationToken)).Lyrics;
        var lrcLib = OnlineService.Lyrics;
        var query = db.Lyrics.AsNoTracking()
            .Where(l => l.Plain != null && l.Synced == null && !l.Instrumental)
            .Where(l => l.Track.Files.Any(f => f.MissingSince == null && f.LibrarySource.Kind == LibrarySourceKind.Local));
        // LRCLib's timing is made by people: it goes first, and only what it does not have is timed here.
        return withLrcLib
            ? query.Where(l => db.OnlineLookups.Any(o => o.Service == lrcLib && o.Subject == l.TrackId.ToString() && o.Outcome == LookupOutcome.NotFound))
            : query;
    }

    private async Task<IReadOnlyList<UntimedLyrics>> DescribeAsync(ArcanistDbContext db, List<Guid> ids, bool forced, CancellationToken cancellationToken)
    {
        if (ids.Count == 0)
            return [];

        var rows = await db.Tracks.AsNoTracking().Where(t => ids.Contains(t.Id))
            .Select(t => new
            {
                t.Id,
                t.Title,
                t.DurationMs,
                Lyrics = db.Lyrics.Where(l => l.TrackId == t.Id).Select(l => new { l.Plain, l.Synced }).FirstOrDefault(),
                Artist = t.Artists.Where(a => a.Role == ArtistRole.Primary).OrderBy(a => a.Position).Select(a => a.Artist.Name).FirstOrDefault(),
            })
            .ToListAsync(cancellationToken);
        // The best present copy on this server's disks.
        var files = (await db.MediaFiles.AsNoTracking()
                .Where(f => ids.Contains(f.TrackId) && f.MissingSince == null && f.LibrarySource.Kind == LibrarySourceKind.Local)
                .Select(f => new { f.TrackId, f.RelativePath, f.LibrarySource, f.StartMs, f.EndMs, f.BitDepth, f.BitRate })
                .ToListAsync(cancellationToken))
            .GroupBy(f => f.TrackId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(f => f.BitDepth ?? 0).ThenByDescending(f => f.BitRate ?? 0).ToList());

        var result = new List<UntimedLyrics>();
        foreach (var row in rows.OrderBy(r => ids.IndexOf(r.Id)))
        {
            var text = forced
                ? TextOf(row.Lyrics?.Plain, row.Lyrics?.Synced)
                : row.Lyrics?.Plain is { } plain && !Lrc.LooksTimed(plain) ? plain : null;
            if (text is null)
                continue;
            foreach (var file in files.GetValueOrDefault(row.Id) ?? [])
            {
                if (storages.For(LibrarySourceKind.Local).TryGetLocalPath(StorageLocation.From(file.LibrarySource), file.RelativePath) is not { } path)
                    continue;
                result.Add(new UntimedLyrics(row.Id, text, HashOf(text), row.Title, row.Artist, row.DurationMs, path, file.StartMs, file.EndMs)
                {
                    Forced = forced,
                });
                break;
            }
        }

        return result;
    }
}
