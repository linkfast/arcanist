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
/// <param name="PlainHash">Identifies the text: timing saved for other words is not kept (see <see cref="LyricsTiming.SaveAsync"/>).</param>
/// <param name="AudioPath">The audio file; a song inside a file with a CUE sheet goes from <paramref name="StartMs"/> to <paramref name="EndMs"/>.</param>
public sealed record UntimedLyrics(
    Guid TrackId, string Plain, string PlainHash, string Title, string? Artist, int DurationMs, string AudioPath, int? StartMs, int? EndMs);

/// <summary>How many tracks have lyrics and how many of them are timed, and by whom.</summary>
public sealed record LyricsTimingCounts(int WithLyrics, int Timed, int Aligned, int Untimed, int Waiting);

/// <summary>
/// The lyrics a timing extension can work on: plain lyrics without timing, of songs LRCLib has no timing for (or
/// with LRCLib off), with a copy of the audio on this server's disks. What the extension works out is saved as
/// <see cref="LyricsSource.Aligned"/> timing, which a rescan keeps while the text stays the same.
/// </summary>
public sealed class LyricsTiming(
    IArcanistDbContextFactory databases, DatabaseMaintenance maintenance, ILibraryStorageResolver storages, OnlineSettings online, TimeProvider time)
{
    private readonly ConcurrentDictionary<Guid, DateTime> _requested = new();

    /// <summary>Raised when a player asks for the lyrics of a song that has no timing yet.</summary>
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
        return await DescribeAsync(db, ids, cancellationToken);
    }

    /// <summary>These songs, if they still need timing (in the same order).</summary>
    public async Task<IReadOnlyList<UntimedLyrics>> FindAsync(IReadOnlyCollection<Guid> trackIds, CancellationToken cancellationToken)
    {
        await using var db = databases.CreateDbContext();
        var ids = await (await UntimedAsync(db, cancellationToken)).Where(l => trackIds.Contains(l.TrackId)).Select(l => l.TrackId).ToListAsync(cancellationToken);
        var found = (await DescribeAsync(db, ids, cancellationToken)).ToDictionary(l => l.TrackId);
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
    /// since (<paramref name="plainHash"/>) or already have timing; returns whether it was saved.
    /// </summary>
    public async Task<bool> SaveAsync(Guid trackId, string plainHash, string lrc, CancellationToken cancellationToken)
    {
        using var work = await maintenance.WorkAsync(cancellationToken);
        await using var db = databases.CreateDbContext();
        var lyrics = await db.Lyrics.SingleOrDefaultAsync(l => l.TrackId == trackId, cancellationToken);
        if (lyrics is not { Synced: null, Plain: { } plain, Instrumental: false } || HashOf(plain) != plainHash || Lrc.Parse(lrc).Count == 0)
            return false;

        lyrics.Synced = lrc;
        lyrics.SyncedSource = LyricsSource.Aligned;
        lyrics.UpdatedAt = time.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(cancellationToken);
        return true;
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
            .SetProperty(l => l.SyncedSource, (LyricsSource?)null), cancellationToken);
    }

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

    private async Task<IReadOnlyList<UntimedLyrics>> DescribeAsync(ArcanistDbContext db, List<Guid> ids, CancellationToken cancellationToken)
    {
        if (ids.Count == 0)
            return [];

        var rows = await db.Tracks.AsNoTracking().Where(t => ids.Contains(t.Id))
            .Select(t => new
            {
                t.Id,
                t.Title,
                t.DurationMs,
                Plain = db.Lyrics.Where(l => l.TrackId == t.Id).Select(l => l.Plain).FirstOrDefault(),
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
            if (row.Plain is not { } plain || Lrc.LooksTimed(plain))
                continue;
            foreach (var file in files.GetValueOrDefault(row.Id) ?? [])
            {
                if (storages.For(LibrarySourceKind.Local).TryGetLocalPath(StorageLocation.From(file.LibrarySource), file.RelativePath) is not { } path)
                    continue;
                result.Add(new UntimedLyrics(row.Id, plain, HashOf(plain), row.Title, row.Artist, row.DurationMs, path, file.StartMs, file.EndMs));
                break;
            }
        }

        return result;
    }
}
