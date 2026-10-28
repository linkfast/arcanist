using Arcanist.Core.Analysis;
using Arcanist.Core.Auth;
using Arcanist.Core.Data;
using Arcanist.Core.Data.Entities;
using Arcanist.Core.Media;
using Arcanist.Core.Videos;
using Microsoft.EntityFrameworkCore;

namespace Arcanist.Core.Application.Videos;

/// <summary>
/// Turns music video rows into the API's <see cref="VideoClip"/>: their song's copy for the user, whether they are
/// aligned, the versions they stream in and the URLs to play them (signed for the user, like track streams).
/// Shared by the catalog (a track's videos) and the music video operations.
/// </summary>
public sealed class VideoClips(ArcanistDbContext db, VideoSettings settings, VideoStore store, FfmpegLocator ffmpeg, StreamTokens tokens)
{
    public async Task<IReadOnlyList<VideoClip>> BuildAsync(
        IReadOnlyList<MusicVideo> videos, UserAccess access, Guid sessionId, IQueryable<MediaFile> visibleFiles, bool withSegments,
        CancellationToken cancellationToken)
    {
        if (videos.Count == 0)
            return [];
        var mode = await settings.AudioForAsync(access.UserId, cancellationToken);
        var token = tokens.Create(access.UserId, sessionId);
        var hashes = videos.Select(v => v.ContentHash).Distinct().ToList();
        var version = AudioAligner.Version;
        var alignments = await db.VideoAlignments.AsNoTracking()
            .Where(a => hashes.Contains(a.VideoHash) && a.AlignerVersion == version)
            .ToListAsync(cancellationToken);
        var encoders = ffmpeg.Current?.Encoders ?? new HashSet<string>();
        var manage = access.Has(Permissions.ManageLibraries) || access.Has(Permissions.EditMetadata);

        var songs = new Dictionary<Guid, VideoSong?>();
        foreach (var video in videos)
            songs[video.Id] = await VideoAudio.SongAsync(db, video.TrackId, video.RecordingId, visibleFiles, cancellationToken);
        var described = await DescribeAsync(songs.Values.OfType<VideoSong>().Select(s => s.TrackId).Distinct().ToList(), cancellationToken);

        var clips = new List<VideoClip>(videos.Count);
        foreach (var video in videos)
        {
            var song = songs[video.Id];
            var alignment = song is null ? null : alignments.FirstOrDefault(a => a.VideoHash == video.ContentHash && a.AudioHash == song.ContentHash);
            var sync = Sync(video, song, alignment);
            var ladder = VideoLadder.For(video.Width, video.Height, video.VideoCodec, encoders);
            var versions = ladder.Select(r => new VideoVersion(r.Name, r.Height, r.Width, r.Bandwidth, r.Codec,
                VideoStore.IsReady(store.PictureFolder(video.ContentHash, r)))).ToList();
            var audioKey = sync == VideoSync.Ready ? VideoStore.AudioKey(VideoAudioMode.Track, song!.ContentHash, version) : "video";
            var hls = versions.Any(v => v.Prepared) && VideoStore.IsReady(store.AudioFolder(video.ContentHash, "video"))
                                                     && (audioKey == "video" || VideoStore.IsReady(store.AudioFolder(video.ContentHash, audioKey)));
            var stream = $"/rest/v1/videos/{video.Id}/stream?{StreamTokens.QueryParameter}={token}";
            var segments = withSegments ? Segments(video, alignment) : null;
            clips.Add(new VideoClip(
                video.Id,
                video.Title,
                video.Artist,
                video.DurationMs,
                video.Width,
                video.Height,
                video.IsLive,
                video.ArtworkId,
                video.LibrarySource.LibraryId,
                manage ? video.RelativePath : null,
                song?.TrackId ?? video.TrackId,
                video.RecordingId,
                video.LinkMethod,
                video.LinkLocked,
                sync,
                alignment?.State == VideoAlignmentState.Aligned ? alignment.Coverage : null,
                mode,
                $"{stream}&audio={(mode == VideoAudioMode.Track ? "track" : "video")}",
                $"{stream}&audio=track",
                $"{stream}&audio=video",
                hls ? $"/rest/v1/videos/{video.Id}/hls/master.m3u8?{StreamTokens.QueryParameter}={token}" : null,
                versions,
                segments,
                video.AddedAt,
                manage,
                song is not null ? described.GetValueOrDefault(song.TrackId) : null));
        }
        return clips;
    }

    /// <summary>The songs' titles, artists, albums and covers, by track.</summary>
    private async Task<Dictionary<Guid, VideoClipSong>> DescribeAsync(List<Guid> trackIds, CancellationToken cancellationToken)
    {
        if (trackIds.Count == 0)
            return [];
        var rows = await db.Tracks.AsNoTracking().Where(t => trackIds.Contains(t.Id))
            .Select(t => new
            {
                t.Id, t.Title, t.DurationMs, t.AlbumId, Album = t.Album.Title, t.Album.Year,
                ArtworkId = t.ArtworkId ?? t.Album.ArtworkId, AlbumArtist = t.Album.AlbumArtist.Name,
                Artists = t.Artists.Where(a => a.Role == ArtistRole.Primary).OrderBy(a => a.Position).Select(a => a.Artist.Name).ToList(),
            })
            .ToListAsync(cancellationToken);
        return rows.ToDictionary(r => r.Id, r => new VideoClipSong(r.Id, r.Title,
            r.Artists.Count > 0 ? string.Join(", ", r.Artists) : r.AlbumArtist, r.AlbumId, r.Album, r.Year, r.ArtworkId, r.DurationMs));
    }

    internal static VideoSync Sync(MusicVideo video, VideoSong? song, VideoAlignment? alignment) => (song, alignment?.State) switch
    {
        (null, _) => VideoSync.Unavailable,
        (_, VideoAlignmentState.Aligned) => VideoSync.Ready,
        (_, VideoAlignmentState.NoMatch) => VideoSync.NoMatch,
        (_, VideoAlignmentState.NeedsFfmpeg or VideoAlignmentState.Failed) => VideoSync.Unavailable,
        _ when !AudioAligner.IsAvailable || video.LibrarySource.Kind is not (LibrarySourceKind.Local or LibrarySourceKind.S3) => VideoSync.Unavailable,
        _ => VideoSync.Pending,
    };

    private static IReadOnlyList<VideoSegment> Segments(MusicVideo video, VideoAlignment? alignment)
    {
        var segments = alignment?.State == VideoAlignmentState.Aligned ? VideoMix.Parse(alignment.Segments) : VideoMix.Whole(video.DurationMs);
        return segments.Select(s => new VideoSegment(
            s.VideoStartMs, s.VideoEndMs, s.FromTrack ? VideoAudioMode.Track : VideoAudioMode.Video,
            s.FromTrack ? s.TrackStartMs : null, s.FromTrack ? s.Rate : null, s.Similarity)).ToList();
    }
}
