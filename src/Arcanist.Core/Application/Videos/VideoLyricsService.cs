using Arcanist.Core.Application.Catalog;
using Arcanist.Core.Data;
using Arcanist.Core.Extensions;
using Arcanist.Core.Scanning;
using Arcanist.Core.Videos;
using Microsoft.EntityFrameworkCore;

namespace Arcanist.Core.Application.Videos;

/// <summary>
/// A music video's lyrics on its own timeline. A video that plays its song (aligned) takes the song's timing through
/// the alignment, so lines follow the song wherever the video plays it and nothing shows over its scenes (an intro,
/// a dialogue, an outro), whichever audio is on: both share the video's timeline. A video with audio of its own (no
/// match: live, another edit) takes the timing Lyrics Sync worked out on its audio. Without timing yet, the words
/// come alone and the timing is asked for (the song's, or the video's).
/// </summary>
public sealed class VideoLyricsService(
    ArcanistDbContext db, MusicVideoService videos, CatalogService catalog, VideoLyricsTimings videoTimings, ExtensionHost extensions)
{
    public async Task<MusicVideoLyrics> GetAsync(Guid videoId, CancellationToken cancellationToken)
    {
        // The video as the user sees it (404 when they cannot), with its alignment for their copy of the song.
        var clip = await videos.GetAsync(videoId, cancellationToken);
        if (clip.TrackId is not { } trackId)
            return new MusicVideoLyrics(videoId, null, VideoLyricsSync.None, null, null, false, false);

        TrackLyrics lyrics;
        try
        {
            lyrics = await catalog.LyricsAsync(trackId, cancellationToken);
        }
        catch (ArcanistException.NotFound)
        {
            return new MusicVideoLyrics(videoId, trackId, VideoLyricsSync.None, null, null, false, false);
        }

        if (lyrics.Instrumental)
            return new MusicVideoLyrics(videoId, trackId, VideoLyricsSync.None, null, lyrics.Plain, true, false);

        var timingRuns = LyricsTimingService.StateOf(extensions) == LyricsTimingExtension.Running;
        switch (clip.Sync)
        {
            case VideoSync.Ready when lyrics.Synced is { Count: > 0 } synced && clip.Song is { } song && clip.Segments is { } segments:
                var lines = VideoLyrics.FromSong(synced.Select(l => (l.TimeMs, l.Text)).ToList(), song.DurationMs, segments.Select(Aligned).ToList());
                return Timed(videoId, trackId, VideoLyricsSync.Song, lines, lyrics.Plain);
            case VideoSync.Ready:
                // The song's lyrics are not timed: reading them asked for that (CatalogService.LyricsAsync).
                return new MusicVideoLyrics(videoId, trackId, VideoLyricsSync.None, null, lyrics.Plain, false, timingRuns);
            case VideoSync.Pending:
                return new MusicVideoLyrics(videoId, trackId, VideoLyricsSync.None, null, lyrics.Plain, false, true);
        }

        // Its own audio: the timing worked out on it, if it is for these words.
        var hash = await db.MusicVideos.AsNoTracking().Where(v => v.Id == videoId).Select(v => v.ContentHash).SingleAsync(cancellationToken);
        // The words as the timing extension had them (VideoLyricsTimings), to know whether it is for these.
        var words = await db.Lyrics.AsNoTracking().Where(l => l.TrackId == trackId).Select(l => new { l.Plain, l.Synced }).SingleAsync(cancellationToken);
        var text = LyricsTiming.TextOf(words.Plain, words.Synced);
        var timing = await db.VideoLyricsTimings.AsNoTracking().SingleOrDefaultAsync(t => t.VideoHash == hash, cancellationToken);
        if (timing is not null && text is not null && timing.PlainHash == LyricsTiming.HashOf(text))
            return Timed(videoId, trackId, VideoLyricsSync.Video, VideoLyrics.FromVideo(Lrc.Parse(timing.Lrc), clip.DurationMs), lyrics.Plain);

        if (clip.Sync == VideoSync.NoMatch && text is not null)
            videoTimings.Request(videoId);
        return new MusicVideoLyrics(videoId, trackId, VideoLyricsSync.None, null, lyrics.Plain, false,
            clip.Sync == VideoSync.NoMatch && text is not null && timingRuns);
    }

    private static MusicVideoLyrics Timed(Guid videoId, Guid trackId, VideoLyricsSync sync, IReadOnlyList<VideoLyricLine> lines, string? plain) =>
        new(videoId, trackId, sync, lines.Select(l => new VideoLyricsLine(l.StartMs, l.EndMs, l.Text, l.SongTimeMs)).ToList(), plain, false, false);

    private static AlignedSegment Aligned(VideoSegment segment) => new(
        segment.VideoStartMs, segment.VideoEndMs, segment.Source == VideoAudioMode.Track, segment.TrackStartMs ?? 0, segment.Rate ?? 1, segment.Similarity);
}
