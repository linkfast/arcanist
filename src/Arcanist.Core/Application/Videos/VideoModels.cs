using Arcanist.Core.Data.Entities;
using Arcanist.Core.Videos;

namespace Arcanist.Core.Application.Videos;

/// <summary>Whether a music video plays with its song's audio. Part of the API contract.</summary>
public enum VideoSync
{
    /// <summary>Aligned: with the track's audio, the song plays from the library wherever the video plays it.</summary>
    Ready,
    /// <summary>Aligned, but the video does not play this recording (another performance): it keeps its own audio.</summary>
    NoMatch,
    /// <summary>Waiting to be aligned; meanwhile it plays with its own audio.</summary>
    Pending,
    /// <summary>It cannot be aligned: no song linked, no copy this server reads, or no ffmpeg.</summary>
    Unavailable,
}

/// <summary>A version of a music video: its size, bit rate and codec, and whether it is ready for adaptive streaming.</summary>
/// <param name="Name">"native" (the video's own picture) or the height ("720p"); what <c>rendition=</c> takes.</param>
/// <param name="Prepared">Made ahead of time: in the HLS playlist, and served at once.</param>
public sealed record VideoVersion(string Name, int Height, int Width, int BitRate, string Codec, bool Prepared);

/// <summary>A stretch of a music video and whose audio plays in it with the track's audio on.</summary>
/// <param name="TrackStartMs">Where the track is when the stretch starts (TRACK stretches).</param>
/// <param name="Rate">Track seconds per video second (TRACK stretches): 1 unless the video runs a little faster or slower.</param>
/// <param name="Similarity">How alike the two audios are there (TRACK stretches), 0–1.</param>
public sealed record VideoSegment(int VideoStartMs, int VideoEndMs, VideoAudioMode Source, int? TrackStartMs, double? Rate, double? Similarity);

/// <summary>A music video of the libraries, with how to play it.</summary>
/// <param name="LibraryId">The library its file is in.</param>
/// <param name="Path">Its path in the library; only for users who manage libraries.</param>
/// <param name="TrackId">The song it shows, when the libraries have it.</param>
/// <param name="LinkLocked">Linked (or unlinked) by a person: scans never change it.</param>
/// <param name="Coverage">Share of the video that plays the track (0–1), once aligned.</param>
/// <param name="AudioMode">Whose audio <paramref name="StreamUrl"/> plays: the user's choice, else the server's default.</param>
/// <param name="StreamUrl">MP4 of the video with the audio of <paramref name="AudioMode"/> (append <c>maxHeight</c>, <c>maxBitRate</c> or <c>rendition</c> to choose a version).</param>
/// <param name="TrackAudioUrl">The same with the song's audio from the library where it matches.</param>
/// <param name="VideoAudioUrl">The same with the video's own audio. Both have the same timeline: switch between them keeping the position.</param>
/// <param name="HlsUrl">Adaptive streaming (HLS) when some version is prepared: its audio has two renditions, the song's (default per the user's choice) and the video's, to switch while playing.</param>
/// <param name="Segments">Where its audio and the track's line up; only in <c>musicVideo</c>.</param>
/// <param name="CanManage">The user can link it, mark it live, align it again or delete it.</param>
public sealed record VideoClip(
    Guid Id,
    string Title,
    string? Artist,
    int DurationMs,
    int? Width,
    int? Height,
    bool IsLive,
    Guid? ArtworkId,
    Guid LibraryId,
    string? Path,
    Guid? TrackId,
    Guid? RecordingId,
    VideoLinkMethod? LinkMethod,
    bool LinkLocked,
    VideoSync Sync,
    double? Coverage,
    VideoAudioMode AudioMode,
    string StreamUrl,
    string TrackAudioUrl,
    string VideoAudioUrl,
    string? HlsUrl,
    IReadOnlyList<VideoVersion> Versions,
    IReadOnlyList<VideoSegment>? Segments,
    DateTime AddedAt,
    bool CanManage);

/// <summary>A page of music videos.</summary>
public sealed record VideoClipPage(IReadOnlyList<VideoClip> Items, string? NextCursor, int TotalCount);

/// <summary>Which music videos to list.</summary>
/// <param name="Linked">True: only those linked to a song; false: only those that are not.</param>
/// <param name="Live">True: only live performances; false: only the others.</param>
/// <param name="TrackId">Only those of this track's song.</param>
public sealed record MusicVideosInput(Guid? LibraryId, string? Search, bool? Linked, bool? Live, Guid? TrackId, string? After, int? First);

/// <summary>The server's music video settings (see <see cref="VideoConfig"/>).</summary>
public sealed record VideoSettingsInput(
    VideoAudioMode DefaultAudio,
    long CacheMaxBytes,
    int CacheMaxAgeDays,
    IReadOnlyList<Guid> PrepareLibraryIds,
    IReadOnlyList<int> PrepareHeights,
    bool PrepareWhenPlayed);

/// <summary>The server's music video settings, with what the cache and the prepared versions hold.</summary>
public sealed record VideoSettingsInfo(
    VideoAudioMode DefaultAudio,
    long CacheMaxBytes,
    int CacheMaxAgeDays,
    IReadOnlyList<Guid> PrepareLibraryIds,
    IReadOnlyList<int> PrepareHeights,
    bool PrepareWhenPlayed,
    long CacheBytes,
    long PreparedBytes,
    int Videos,
    int Linked,
    int Aligned,
    int Pending);

/// <summary>A user's music video choice: null follows the server's default.</summary>
public sealed record MyVideoSettings(VideoAudioMode? Audio, VideoAudioMode ServerDefault, VideoAudioMode Effective);
