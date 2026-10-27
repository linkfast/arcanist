using Arcanist.Core.Data;
using Arcanist.Core.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Arcanist.Core.Videos;

/// <summary>The copy of a video's song whose audio goes with it.</summary>
public sealed record VideoSong(Guid TrackId, Guid FileId, string ContentHash, LibrarySource Source, string RelativePath, int? StartMs, int? EndMs, int DurationMs);

public static class VideoAudio
{
    /// <summary>
    /// The best present copy of the video's track (or, when it has none, of another track of the same recording),
    /// among <paramref name="files"/> (all files, or those a user can see): one this server reads itself (local or
    /// S3) first, then the best quality.
    /// </summary>
    public static async Task<VideoSong?> SongAsync(ArcanistDbContext db, Guid? trackId, Guid? recordingId, IQueryable<MediaFile> files, CancellationToken cancellationToken)
    {
        if (trackId is null && recordingId is null)
            return null;
        var candidates = files.Where(f => f.MissingSince == null
                                          && (f.LibrarySource.Kind == LibrarySourceKind.Local || f.LibrarySource.Kind == LibrarySourceKind.S3)
                                          && (f.TrackId == trackId || (recordingId != null && f.Track.RecordingId == recordingId)));
        var best = await candidates
            .OrderBy(f => f.TrackId == trackId ? 0 : 1)
            .ThenByDescending(f => f.BitDepth ?? 0)
            .ThenByDescending(f => f.SampleRate ?? 0)
            .ThenByDescending(f => f.BitRate ?? 0)
            .Select(f => new VideoSong(f.TrackId, f.Id, f.ContentHash, f.LibrarySource, f.RelativePath, f.StartMs, f.EndMs, f.DurationMs))
            .FirstOrDefaultAsync(cancellationToken);
        return best;
    }
}
