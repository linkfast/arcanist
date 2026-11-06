using Arcanist.Core.Data;
using Arcanist.Core.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Arcanist.Core.Analysis;

/// <summary>
/// One file playing on several albums: besides the row of the track its tags describe, each other track has a row with
/// the same path and a negative <see cref="MediaFile.CueTrack"/>. So those tracks stay real tracks of their albums, with
/// what users attached to them, and everything that plays or lists a track works as it does for any other.
/// </summary>
public static class SharedFiles
{
    /// <summary>The row that makes <paramref name="trackId"/> play <paramref name="file"/>.</summary>
    public static MediaFile CopyFor(MediaFile file, Guid trackId, int slot, DateTime now)
    {
        var copy = new MediaFile
        {
            TrackId = trackId,
            LibrarySourceId = file.LibrarySourceId,
            RelativePath = file.RelativePath,
            CueTrack = slot,
            ContentHash = file.ContentHash,
            AddedAt = now,
            TrackLocked = true,
        };
        Follow(copy, file);
        return copy;
    }

    /// <summary>A free negative <see cref="MediaFile.CueTrack"/> of a file, given the ones it has.</summary>
    public static int NextSlot(IEnumerable<int> taken) => Math.Min(0, taken.DefaultIfEmpty(0).Min()) - 1;

    /// <summary>
    /// After a scan or an edit: each shared copy says what its file says now. A file that moved (a new path with the same
    /// audio, the old one gone) takes its copies along; a copy whose file left the catalog goes too, and a track left
    /// without files leaves with it, as any other.
    /// </summary>
    public static async Task SyncAsync(ArcanistDbContext db, Guid sourceId, CancellationToken cancellationToken)
    {
        var copies = await db.MediaFiles.Where(f => f.LibrarySourceId == sourceId && f.CueTrack < 0).ToListAsync(cancellationToken);
        if (copies.Count == 0)
            return;

        var paths = copies.Select(c => c.RelativePath).Distinct().ToList();
        var hashes = copies.Select(c => c.ContentHash).Distinct().ToList();
        var files = await db.MediaFiles.AsNoTracking()
            .Where(f => f.LibrarySourceId == sourceId && f.CueTrack == 0 && (paths.Contains(f.RelativePath) || hashes.Contains(f.ContentHash)))
            .ToListAsync(cancellationToken);

        foreach (var copy in copies)
        {
            var atPath = files.FirstOrDefault(f => f.RelativePath == copy.RelativePath);
            var file = atPath is { MissingSince: null }
                ? atPath
                : files.FirstOrDefault(f => f.MissingSince == null && f.ContentHash == copy.ContentHash) ?? atPath;
            if (file is null)
            {
                db.MediaFiles.Remove(copy);
                continue;
            }

            if (file.RelativePath != copy.RelativePath)
            {
                copy.CueTrack = NextSlot(copies.Where(c => c.RelativePath == file.RelativePath).Select(c => c.CueTrack));
                copy.RelativePath = file.RelativePath;
            }

            Follow(copy, file);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private static void Follow(MediaFile copy, MediaFile file)
    {
        copy.Size = file.Size;
        copy.ModifiedAt = file.ModifiedAt;
        copy.ContentHash = file.ContentHash;
        copy.Format = file.Format;
        copy.BitRate = file.BitRate;
        copy.SampleRate = file.SampleRate;
        copy.BitDepth = file.BitDepth;
        copy.Channels = file.Channels;
        copy.DurationMs = file.DurationMs;
        copy.ReplayGainTrackGain = file.ReplayGainTrackGain;
        copy.ReplayGainTrackPeak = file.ReplayGainTrackPeak;
        copy.ReplayGainAlbumGain = file.ReplayGainAlbumGain;
        copy.ReplayGainAlbumPeak = file.ReplayGainAlbumPeak;
        copy.ScannedAt = file.ScannedAt;
        copy.MissingSince = file.MissingSince;
    }
}
