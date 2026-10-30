using System.ComponentModel;
using Arcanist.Core.Application.Videos;
using Arcanist.Core.Videos;
using GraphQL;

namespace Arcanist.Core.Api.GraphQL;

public sealed partial class Query
{
    [Name("musicVideos")]
    [Description("The music videos in libraries the user can access, by title, with optional filters (a library, words of the title or artist, linked to a song or not, live or not, those of a track's song). Each says whether it plays with its song's audio and how to play it.")]
    public static Task<VideoClipPage> MusicVideos(MusicVideosInput? input, [FromServices] MusicVideoService videos, CancellationToken cancellationToken) =>
        videos.ListAsync(input ?? new MusicVideosInput(null, null, null, null, null, null, null), cancellationToken);

    [Name("musicVideo")]
    [Description("A music video, with the stretches where its audio and its song's line up (segments).")]
    public static Task<VideoClip> MusicVideo([Id] Guid id, [FromServices] MusicVideoService videos, CancellationToken cancellationToken) =>
        videos.GetAsync(id, cancellationToken);

    [Name("musicVideoLyrics")]
    [Description("A music video's lyrics on the video's timeline: its song's timing carried over to where the video plays the song (nothing over scenes that are not the song; parts it cuts left out, parts it repeats shown again), or timed on the video's own audio when it is not the song's recording. pending: the timing is being worked out; ask again later.")]
    public static Task<MusicVideoLyrics> MusicVideoLyrics([Id] Guid id, [FromServices] VideoLyricsService lyrics, CancellationToken cancellationToken) =>
        lyrics.GetAsync(id, cancellationToken);

    [Name("videoSettings")]
    [Description("How the server handles music videos: whose audio plays by default, the cache of versions made when played, what is prepared ahead of time for adaptive streaming, and how many videos are linked and aligned. Requires MANAGE_SETTINGS.")]
    public static Task<VideoSettingsInfo> VideoSettings([FromServices] MusicVideoService videos, CancellationToken cancellationToken) =>
        videos.SettingsAsync(cancellationToken);

    [Name("myVideoSettings")]
    [Description("The user's choice of whose audio plays with music videos (null follows the server's default), and the one in effect.")]
    public static Task<MyVideoSettings> MyVideoSettings([FromServices] MusicVideoService videos, CancellationToken cancellationToken) =>
        videos.MySettingsAsync(cancellationToken);
}

public sealed partial class Mutation
{
    [Name("linkMusicVideo")]
    [Description("Links a music video to a track's song (or, without trackId, leaves it with no song); scans keep what a person chose. It is aligned again first of all. Requires MANAGE_LIBRARIES or EDIT_METADATA.")]
    public static Task<VideoClip> LinkMusicVideo([Id] Guid id, [Id] Guid? trackId, [FromServices] MusicVideoService videos, CancellationToken cancellationToken) =>
        videos.LinkAsync(id, trackId, cancellationToken);

    [Name("unlockMusicVideo")]
    [Description("Gives a music video back to automatic linking (by its name next to its song, or its artist and title) and to the live detection of its title. Requires MANAGE_LIBRARIES or EDIT_METADATA.")]
    public static Task<VideoClip> UnlockMusicVideo([Id] Guid id, [FromServices] MusicVideoService videos, CancellationToken cancellationToken) =>
        videos.UnlockAsync(id, cancellationToken);

    [Name("setMusicVideoLive")]
    [Description("Marks a music video as a live performance or not (scans keep it): a live video is only linked to a live track. Requires MANAGE_LIBRARIES or EDIT_METADATA.")]
    public static Task<VideoClip> SetMusicVideoLive([Id] Guid id, bool live, [FromServices] MusicVideoService videos, CancellationToken cancellationToken) =>
        videos.SetLiveAsync(id, live, cancellationToken);

    [Name("realignMusicVideo")]
    [Description("Forgets a music video's alignment and aligns it again first of all. Requires MANAGE_LIBRARIES or EDIT_METADATA.")]
    public static Task<VideoClip> RealignMusicVideo([Id] Guid id, [FromServices] MusicVideoService videos, CancellationToken cancellationToken) =>
        videos.RealignAsync(id, cancellationToken);

    [Name("deleteMusicVideo")]
    [Description("Moves a music video's file to the trash of its location (purged after 30 days) and removes it from the catalog; its song stays. Requires DELETE_TRACKS and a library the user adds to.")]
    public static async Task<bool> DeleteMusicVideo([Id] Guid id, [FromServices] MusicVideoService videos, CancellationToken cancellationToken)
    {
        await videos.DeleteAsync(id, cancellationToken);
        return true;
    }

    [Name("setVideoSettings")]
    [Description("Sets how the server handles music videos (heights from 2160, 1440, 1080, 720, 480 and 360). Requires MANAGE_SETTINGS.")]
    public static Task<VideoSettingsInfo> SetVideoSettings(VideoSettingsInput input, [FromServices] MusicVideoService videos, CancellationToken cancellationToken) =>
        videos.SetSettingsAsync(input, cancellationToken);

    [Name("setMyVideoSettings")]
    [Description("Chooses whose audio plays with music videos for the user: TRACK (the song from the library where the video plays it), VIDEO (the video's own), or null for the server's default. Players can still switch while playing.")]
    public static Task<MyVideoSettings> SetMyVideoSettings(VideoAudioMode? audio, [FromServices] MusicVideoService videos, CancellationToken cancellationToken) =>
        videos.SetMySettingsAsync(audio, cancellationToken);
}
