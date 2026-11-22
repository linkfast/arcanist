using Arcanist.Core.Data;
using Arcanist.Core.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Arcanist.Core.Videos;

/// <summary>
/// Puts a video first in line for <see cref="VideoAlignmentWorker"/>: when a manager links or realigns it, and when someone
/// plays it before it is aligned (they hear the video's own audio until then, so it goes ahead of the backlog and is aligned
/// even while streams run).
/// </summary>
public static class VideoAlignmentQueue
{
    /// <summary>Makes the video's request the first one (creating it), unless it already is. True when it wrote.</summary>
    public static async Task<bool> RequestFirstAsync(ArcanistDbContext db, VideoSignals signals, Guid videoId, DateTime now, CancellationToken cancellationToken)
    {
        var top = await db.VideoAlignmentRequests.MaxAsync(r => (long?)r.Priority, cancellationToken) ?? 0;
        var request = await db.VideoAlignmentRequests.SingleOrDefaultAsync(r => r.VideoId == videoId, cancellationToken);
        if (request is not null && request.Priority == top)
        {
            signals.RequestAlignment();
            return false;
        }
        if (request is null)
            db.VideoAlignmentRequests.Add(new VideoAlignmentRequest { VideoId = videoId, Priority = top + 1, RequestedAt = now });
        else
            request.Priority = top + 1;
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Two plays of the same video at once: the other one's request stands.
            db.ChangeTracker.Clear();
        }
        signals.RequestAlignment();
        return true;
    }
}
