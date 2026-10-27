using Arcanist.Core.Analysis;
using Arcanist.Core.Application.Catalog;
using Arcanist.Core.Auth;
using Arcanist.Core.Data;
using Arcanist.Core.Data.Entities;
using Arcanist.Core.Data.Search;
using Arcanist.Core.Storage;
using Arcanist.Core.Videos;
using Microsoft.EntityFrameworkCore;

namespace Arcanist.Core.Application.Videos;

/// <summary>
/// The music videos of the libraries: listing them (what the user can access), linking them to their songs by hand,
/// marking them live, aligning them again and deleting them (to the trash of their location), and the settings of
/// whose audio plays with them.
/// </summary>
public sealed class MusicVideoService(
    ArcanistDbContext db,
    AccessGuard guard,
    CatalogService catalog,
    VideoClips clips,
    VideoSettings settings,
    VideoStore store,
    VideoSignals signals,
    ILibraryStorageResolver storages,
    TimeProvider time)
{
    public async Task<VideoClipPage> ListAsync(MusicVideosInput input, CancellationToken cancellationToken)
    {
        var access = guard.Authenticated();
        var videos = catalog.VisibleVideos(access).Include(v => v.LibrarySource).AsQueryable();
        if (Libraries.VirtualLibraries.Filter(input.LibraryId) is { } library)
        {
            if (!access.CanAccessLibrary(library))
                throw new ArcanistException.NotFound();
            videos = videos.Where(v => v.LibrarySource.LibraryId == library);
        }
        if (input.Linked is { } linked)
            videos = linked ? videos.Where(v => v.RecordingId != null) : videos.Where(v => v.RecordingId == null);
        if (input.Live is { } live)
            videos = videos.Where(v => v.IsLive == live);
        if (input.TrackId is { } trackId)
        {
            var recording = await db.Tracks.Where(t => t.Id == trackId).Select(t => (Guid?)t.RecordingId).SingleOrDefaultAsync(cancellationToken);
            videos = videos.Where(v => v.TrackId == trackId || (recording != null && v.RecordingId == recording));
        }
        if (FullTextSearch.Match(db, SearchKind.MusicVideos, input.Search) is { } hits)
            videos = videos.Where(v => hits.Any(h => h.Id == v.Id));

        var (page, next, total) = await Keyset.PageAsync(videos, v => v.NormalizedTitle, input.After, input.First, cancellationToken);
        return new VideoClipPage(await clips.BuildAsync(page, access, guard.SessionId, catalog.VisibleFiles(access), false, cancellationToken), next, total);
    }

    public async Task<VideoClip> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var access = guard.Authenticated();
        var video = await FindAsync(access, id, cancellationToken);
        return (await clips.BuildAsync([video], access, guard.SessionId, catalog.VisibleFiles(access), true, cancellationToken))[0];
    }

    /// <summary>Links the video to a track's song (or, with no track, leaves it unlinked); scans keep it so.</summary>
    public async Task<VideoClip> LinkAsync(Guid id, Guid? trackId, CancellationToken cancellationToken)
    {
        var access = Manager();
        var video = await FindAsync(access, id, cancellationToken, tracked: true);
        if (trackId is { } track)
        {
            var recording = await catalog.Visible(access, null).Where(t => t.Id == track).Select(t => (Guid?)t.RecordingId).SingleOrDefaultAsync(cancellationToken)
                             ?? throw ArcanistException.Field("trackId", ErrorCodes.NotFound);
            video.TrackId = track;
            video.RecordingId = recording;
            video.LinkMethod = VideoLinkMethod.Manual;
        }
        else
        {
            video.TrackId = null;
            video.RecordingId = null;
            video.LinkMethod = null;
        }
        video.LinkLocked = true;
        await db.SaveChangesAsync(cancellationToken);
        await RequestAlignmentAsync(video.Id, cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    /// <summary>Gives the video back to the automatic linking (what scans would do).</summary>
    public async Task<VideoClip> UnlockAsync(Guid id, CancellationToken cancellationToken)
    {
        var access = Manager();
        var video = await FindAsync(access, id, cancellationToken, tracked: true);
        video.LinkLocked = false;
        video.LiveLocked = false;
        video.IsLive = VideoTitles.IsLive(video.Title);
        if (await VideoLinker.FindAsync(db, video, cancellationToken) is { } link)
        {
            video.TrackId = link.TrackId;
            video.RecordingId = link.RecordingId;
            video.LinkMethod = link.Method;
        }
        else
        {
            video.TrackId = null;
            video.RecordingId = null;
            video.LinkMethod = null;
        }
        await db.SaveChangesAsync(cancellationToken);
        await RequestAlignmentAsync(video.Id, cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    public async Task<VideoClip> SetLiveAsync(Guid id, bool live, CancellationToken cancellationToken)
    {
        var access = Manager();
        var video = await FindAsync(access, id, cancellationToken, tracked: true);
        video.IsLive = live;
        video.LiveLocked = true;
        await db.SaveChangesAsync(cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    /// <summary>Aligns the video again first of all (after its song's audio changed, or to retry a failure).</summary>
    public async Task<VideoClip> RealignAsync(Guid id, CancellationToken cancellationToken)
    {
        var access = Manager();
        var video = await FindAsync(access, id, cancellationToken);
        await db.VideoAlignments.Where(a => a.VideoHash == video.ContentHash).ExecuteDeleteAsync(cancellationToken);
        await RequestAlignmentAsync(video.Id, cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    /// <summary>Moves the video's file to the trash of its location and removes it from the catalog.</summary>
    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var access = guard.Permission(Permissions.DeleteTracks);
        var video = await FindAsync(access, id, cancellationToken);
        if (!access.CanChange(video.LibrarySource.LibraryId))
            throw new ArcanistException.Forbidden(ErrorCodes.LibraryReadOnly);
        if (video.LibrarySource.Kind is not (LibrarySourceKind.Local or LibrarySourceKind.S3))
            throw new ArcanistException.Conflict(ErrorCodes.ReadOnly);
        var location = StorageLocation.From(video.LibrarySource);
        try
        {
            await storages.For(location.Kind).TrashAsync(location, video.RelativePath, cancellationToken);
        }
        catch (FileNotFoundException)
        {
        }
        await db.MusicVideos.Where(v => v.Id == id).ExecuteDeleteAsync(cancellationToken);
    }

    public async Task<VideoSettingsInfo> SettingsAsync(CancellationToken cancellationToken)
    {
        guard.Permission(Permissions.ManageSettings);
        var config = await settings.GetAsync(cancellationToken);
        var version = AudioAligner.Version;
        var videos = await db.MusicVideos.CountAsync(v => v.MissingSince == null, cancellationToken);
        var linked = await db.MusicVideos.CountAsync(v => v.MissingSince == null && v.RecordingId != null, cancellationToken);
        var aligned = await db.MusicVideos.CountAsync(v => v.MissingSince == null && v.RecordingId != null
            && db.VideoAlignments.Any(a => a.VideoHash == v.ContentHash && a.AlignerVersion == version), cancellationToken);
        return new VideoSettingsInfo(config.DefaultAudio, config.CacheMaxBytes, config.CacheMaxAgeDays, config.PrepareLibraryIds, config.PrepareHeights,
            config.PrepareWhenPlayed, store.Cache.Usage().Bytes, store.PreparedBytes(), videos, linked, aligned, linked - aligned);
    }

    public async Task<VideoSettingsInfo> SetSettingsAsync(VideoSettingsInput input, CancellationToken cancellationToken)
    {
        guard.Permission(Permissions.ManageSettings);
        if (input.PrepareHeights.Any(h => !VideoLadder.Heights.Contains(h)))
            throw ArcanistException.Field("prepareHeights", ErrorCodes.InvalidFormat);
        await settings.SetAsync(new VideoConfig(input.DefaultAudio, input.CacheMaxBytes, input.CacheMaxAgeDays, input.PrepareLibraryIds,
            input.PrepareHeights, input.PrepareWhenPlayed), cancellationToken);
        return await SettingsAsync(cancellationToken);
    }

    public async Task<MyVideoSettings> MySettingsAsync(CancellationToken cancellationToken)
    {
        var access = guard.Authenticated();
        var mine = await settings.UserAsync(access.UserId, cancellationToken);
        var server = (await settings.GetAsync(cancellationToken)).DefaultAudio;
        return new MyVideoSettings(mine.Audio, server, mine.Audio ?? server);
    }

    public async Task<MyVideoSettings> SetMySettingsAsync(VideoAudioMode? audio, CancellationToken cancellationToken)
    {
        var access = guard.Authenticated();
        await settings.SetUserAsync(access.UserId, new UserVideoConfig(audio), cancellationToken);
        return await MySettingsAsync(cancellationToken);
    }

    private UserAccess Manager()
    {
        var access = guard.Authenticated();
        if (!access.Has(Permissions.ManageLibraries) && !access.Has(Permissions.EditMetadata))
            throw new ArcanistException.Forbidden();
        return access;
    }

    private async Task<MusicVideo> FindAsync(UserAccess access, Guid id, CancellationToken cancellationToken, bool tracked = false)
    {
        var videos = catalog.VisibleVideos(access).Include(v => v.LibrarySource);
        return await (tracked ? videos : videos.AsNoTracking()).SingleOrDefaultAsync(v => v.Id == id, cancellationToken)
               ?? throw new ArcanistException.NotFound();
    }

    private async Task RequestAlignmentAsync(Guid videoId, CancellationToken cancellationToken)
    {
        var top = await db.VideoAlignmentRequests.MaxAsync(r => (long?)r.Priority, cancellationToken) ?? 0;
        var request = await db.VideoAlignmentRequests.SingleOrDefaultAsync(r => r.VideoId == videoId, cancellationToken);
        if (request is null)
            db.VideoAlignmentRequests.Add(new VideoAlignmentRequest { VideoId = videoId, Priority = top + 1, RequestedAt = time.GetUtcNow().UtcDateTime });
        else
            request.Priority = top + 1;
        await db.SaveChangesAsync(cancellationToken);
        signals.RequestAlignment();
    }
}
