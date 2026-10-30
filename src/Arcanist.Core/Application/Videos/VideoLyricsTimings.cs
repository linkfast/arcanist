using System.Collections.Concurrent;
using Arcanist.Core.Analysis;
using Arcanist.Core.Application.Catalog;
using Arcanist.Core.Data;
using Arcanist.Core.Data.Entities;
using Arcanist.Core.Scanning;
using Arcanist.Core.Storage;
using Arcanist.Core.Videos;
using Microsoft.EntityFrameworkCore;

namespace Arcanist.Core.Application.Videos;

/// <summary>A music video whose lyrics need timing on its own audio, with its file on this server's disks.</summary>
/// <param name="PlainHash">The song's words (<see cref="LyricsTiming.HashOf"/>), to save the timing with.</param>
public sealed record UntimedVideoLyrics(
    Guid VideoId, string VideoHash, Guid TrackId, string Plain, string PlainHash, string Title, string? Artist, int DurationMs, string VideoPath);

/// <summary>
/// The music videos a timing extension (Lyrics Sync) times on their own audio: those linked to a song with lyrics
/// whose audio is not the song's (the alignment found no match: a live video, another mix or edit), so the song's
/// timing cannot be carried over to them. Videos that play their song are not here: their lyrics follow the song's
/// timing through the alignment (<see cref="VideoLyrics.FromSong"/>), and an untimed song is timed as a song.
/// What is worked out is kept by video content, for the song's words it was timed for.
/// </summary>
public sealed class VideoLyricsTimings(IArcanistDbContextFactory databases, DatabaseMaintenance maintenance, ILibraryStorageResolver storages, TimeProvider time)
{
    private readonly ConcurrentDictionary<Guid, DateTime> _requested = new();

    /// <summary>Raised when a player asks for the lyrics of a video that has no timing yet.</summary>
    public event Action? Requested;

    /// <summary>Asks for the timing of a video someone is watching, before the rest.</summary>
    public void Request(Guid videoId)
    {
        if (_requested.TryAdd(videoId, time.GetUtcNow().UtcDateTime))
            Requested?.Invoke();
    }

    /// <summary>Whether a video's timing was asked for and not taken yet.</summary>
    public bool IsRequested(Guid videoId) => _requested.ContainsKey(videoId);

    /// <summary>The videos asked for, oldest first, and forgets them.</summary>
    public IReadOnlyList<Guid> TakeRequested()
    {
        var ids = _requested.OrderBy(r => r.Value).Select(r => r.Key).ToList();
        foreach (var id in ids)
            _requested.TryRemove(id, out _);
        return ids;
    }

    /// <summary>
    /// The next videos to time, in id order after <paramref name="after"/> (null: from the start), from at most
    /// <paramref name="take"/> candidates (fewer come back when some already have their timing). The caller pages on
    /// with <see cref="UntimedVideoLyrics.VideoId"/> of the last, or stops at an empty page.
    /// </summary>
    public async Task<(IReadOnlyList<UntimedVideoLyrics> Videos, Guid? Last)> PendingAsync(Guid? after, int take, CancellationToken cancellationToken)
    {
        await using var db = databases.CreateDbContext();
        var query = Candidates(db);
        if (after is { } last)
            query = query.Where(v => v.Id.CompareTo(last) > 0);
        var ids = await query.OrderBy(v => v.Id).Select(v => v.Id).Take(take).ToListAsync(cancellationToken);
        return (await DescribeAsync(db, ids, cancellationToken), ids.Count > 0 ? ids[^1] : null);
    }

    /// <summary>These videos, if they still need timing (in the same order).</summary>
    public async Task<IReadOnlyList<UntimedVideoLyrics>> FindAsync(IReadOnlyCollection<Guid> videoIds, CancellationToken cancellationToken)
    {
        await using var db = databases.CreateDbContext();
        var ids = await Candidates(db).Where(v => videoIds.Contains(v.Id)).Select(v => v.Id).ToListAsync(cancellationToken);
        var found = (await DescribeAsync(db, ids, cancellationToken)).ToDictionary(v => v.VideoId);
        return videoIds.Where(found.ContainsKey).Select(id => found[id]).ToList();
    }

    /// <summary>Saves the timing worked out on a video's audio (LRC, on the video's timeline), replacing the one it had.</summary>
    public async Task<bool> SaveAsync(string videoHash, string plainHash, string lrc, CancellationToken cancellationToken)
    {
        if (Lrc.Parse(lrc).Count == 0)
            return false;
        using var work = await maintenance.WorkAsync(cancellationToken);
        await using var db = databases.CreateDbContext();
        var timing = await db.VideoLyricsTimings.SingleOrDefaultAsync(t => t.VideoHash == videoHash, cancellationToken);
        if (timing is null)
        {
            timing = new VideoLyricsTiming { VideoHash = videoHash, PlainHash = plainHash, Lrc = lrc };
            db.VideoLyricsTimings.Add(timing);
        }

        timing.PlainHash = plainHash;
        timing.Lrc = lrc;
        timing.AlignedAt = time.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <summary>Removes the timing worked out on videos' audio, so it is worked out again; returns how many.</summary>
    public async Task<int> ClearAsync(CancellationToken cancellationToken)
    {
        using var work = await maintenance.WorkAsync(cancellationToken);
        await using var db = databases.CreateDbContext();
        return await db.VideoLyricsTimings.ExecuteDeleteAsync(cancellationToken);
    }

    /// <summary>How many videos have timing worked out on their own audio, and how many need it.</summary>
    public async Task<(int Timed, int Untimed)> CountAsync(CancellationToken cancellationToken)
    {
        await using var db = databases.CreateDbContext();
        var timed = await db.VideoLyricsTimings.CountAsync(cancellationToken);
        var ids = await Candidates(db).Select(v => v.Id).ToListAsync(cancellationToken);
        return (timed, (await DescribeAsync(db, ids, cancellationToken)).Count);
    }

    /// <summary>
    /// Present videos on this server's disks, linked to a song with lyrics, whose audio did not match their song's
    /// with the current aligner (and never matched it: one copy of the song lining up is enough to follow it).
    /// </summary>
    private static IQueryable<MusicVideo> Candidates(ArcanistDbContext db)
    {
        var version = AudioAligner.Version;
        return db.MusicVideos.AsNoTracking()
            .Where(v => v.MissingSince == null && v.TrackId != null && v.LibrarySource.Kind == LibrarySourceKind.Local)
            .Where(v => db.VideoAlignments.Any(a => a.VideoHash == v.ContentHash && a.AlignerVersion == version && a.State == VideoAlignmentState.NoMatch))
            .Where(v => !db.VideoAlignments.Any(a => a.VideoHash == v.ContentHash && a.AlignerVersion == version && a.State == VideoAlignmentState.Aligned))
            .Where(v => db.Lyrics.Any(l => l.TrackId == v.TrackId && !l.Instrumental && (l.Plain != null || l.Synced != null)));
    }

    /// <summary>The videos with their song's words, leaving out those already timed for those words.</summary>
    private async Task<IReadOnlyList<UntimedVideoLyrics>> DescribeAsync(ArcanistDbContext db, List<Guid> ids, CancellationToken cancellationToken)
    {
        if (ids.Count == 0)
            return [];
        var rows = await db.MusicVideos.AsNoTracking().Where(v => ids.Contains(v.Id))
            .Select(v => new
            {
                v.Id,
                v.ContentHash,
                v.TrackId,
                v.Title,
                v.Artist,
                v.DurationMs,
                v.RelativePath,
                v.LibrarySource,
                Lyrics = db.Lyrics.Where(l => l.TrackId == v.TrackId).Select(l => new { l.Plain, l.Synced }).FirstOrDefault(),
                Timed = db.VideoLyricsTimings.Where(t => t.VideoHash == v.ContentHash).Select(t => t.PlainHash).FirstOrDefault(),
            })
            .ToListAsync(cancellationToken);

        var result = new List<UntimedVideoLyrics>();
        foreach (var row in rows.OrderBy(r => ids.IndexOf(r.Id)))
        {
            if (LyricsTiming.TextOf(row.Lyrics?.Plain, row.Lyrics?.Synced) is not { } text)
                continue;
            var hash = LyricsTiming.HashOf(text);
            if (row.Timed == hash)
                continue;
            if (storages.For(LibrarySourceKind.Local).TryGetLocalPath(StorageLocation.From(row.LibrarySource), row.RelativePath) is not { } path)
                continue;
            result.Add(new UntimedVideoLyrics(row.Id, row.ContentHash, row.TrackId!.Value, text, hash, row.Title, row.Artist, row.DurationMs, path));
        }

        return result;
    }
}
