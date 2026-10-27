using System.Globalization;
using System.Text;
using Arcanist.Core.Application.Catalog;
using Arcanist.Core.Auth;
using Arcanist.Core.Data;
using Arcanist.Core.Data.Entities;
using Arcanist.Core.Media;
using Arcanist.Core.Scanning;
using Arcanist.Core.Storage;
using Arcanist.Core.Videos;
using Microsoft.EntityFrameworkCore;
using Microsoft.Net.Http.Headers;

namespace Arcanist.Core.Application.Videos;

/// <summary>
/// Plays music videos. Progressive MP4 (<c>/videos/{id}/stream</c>): the file as it is when it already plays in
/// browsers with the chosen audio; else a version from the cache, with ranges; else made while it streams (with
/// <c>t</c> to start later, as audio streams do) and kept in the cache when it was made whole. Adaptive streaming
/// (<c>/videos/{id}/hls/…</c>) serves the renditions prepared ahead of time: the picture apart from the audio, and the
/// song's audio and the video's own as alternative audio renditions, so players switch between them while playing.
/// </summary>
public sealed class VideoStreamService(
    ArcanistDbContext db,
    AccessGuard guard,
    CatalogService catalog,
    VideoSettings settings,
    VideoPlans plans,
    VideoStore store,
    VideoTranscoder transcoder,
    VideoPrepareWorker prepare,
    FfmpegLocator ffmpeg,
    ILibraryStorageResolver storages)
{
    public const string AudioHeader = "X-Arcanist-Video-Audio";

    public async Task<IResult> StreamAsync(
        HttpContext context, Guid id, string? audio, int? maxHeight, int? maxBitRate, string? rendition, double? offsetSeconds,
        CancellationToken cancellationToken)
    {
        var access = guard.Authenticated();
        var video = await catalog.VisibleVideos(access).AsNoTracking().Include(v => v.LibrarySource)
            .SingleOrDefaultAsync(v => v.Id == id, cancellationToken) ?? throw new ArcanistException.NotFound();
        if (!HttpMethods.IsHead(context.Request.Method) && context.RequestServices.GetService<StreamActivity>() is { } activity)
            context.Response.RegisterForDispose(activity.Begin());

        var mode = ParseAudio(audio) ?? await settings.AudioForAsync(access.UserId, cancellationToken);
        var ladder = VideoLadder.For(video.Width, video.Height, video.VideoCodec, (await ffmpeg.GetAsync(cancellationToken))?.Encoders ?? new HashSet<string>());
        var chosen = rendition is null ? VideoLadder.Pick(ladder, maxHeight, maxBitRate) : ladder.FirstOrDefault(r => r.Name == rendition);
        if (rendition is not null && chosen is null)
            throw ArcanistException.Field("rendition", ErrorCodes.InvalidFormat);

        var plan = await plans.PlanAsync(db, video, mode, catalog.VisibleFiles(access), cancellationToken);
        context.Response.Headers[AudioHeader] = plan.AudioKey == "video" ? "video" : "track";
        if ((await settings.GetAsync(cancellationToken)).PrepareWhenPlayed)
            prepare.Request(video.Id);

        // Nothing to convert (no ffmpeg, or the file plays as it is): the file itself, with ranges.
        if (chosen is null || (plan.AudioKey == "video" && chosen.Copy && PlaysAsIs(video)))
            return await OriginalAsync(context, video, cancellationToken);

        var key = VideoStore.CacheKey(video.ContentHash, plan.AudioKey, chosen);
        if (store.Cache.TryGet(key) is { } cached)
        {
            context.Response.Headers.CacheControl = "private, max-age=3600";
            return Results.File(cached, "video/mp4", lastModified: video.ModifiedAt,
                entityTag: new EntityTagHeaderValue($"\"{key[..Math.Min(key.Length, 120)]}\""), enableRangeProcessing: true);
        }

        var offsetMs = (int)Math.Clamp((offsetSeconds ?? 0) * 1000, 0, Math.Max(0, video.DurationMs - 1000));
        var session = await transcoder.StartAsync(plan.Input, chosen, offsetMs, cancellationToken);
        if (session is null)
            return await OriginalAsync(context, video, cancellationToken);
        return new VideoTranscodeResult(session, (video.DurationMs - offsetMs) / 1000.0, offsetMs == 0 ? store.Cache : null, key, transcoder);
    }

    /// <summary>The master playlist, a rendition's playlist (with the stream token added to its URIs) or one of its files.</summary>
    public async Task<IResult> HlsAsync(HttpContext context, Guid id, string path, string? audio, CancellationToken cancellationToken)
    {
        var access = guard.Authenticated();
        var video = await catalog.VisibleVideos(access).AsNoTracking().Include(v => v.LibrarySource)
            .SingleOrDefaultAsync(v => v.Id == id, cancellationToken) ?? throw new ArcanistException.NotFound();
        var token = context.Request.Query[StreamTokens.QueryParameter].ToString();
        var suffix = token.Length > 0 ? $"?{StreamTokens.QueryParameter}={Uri.EscapeDataString(token)}" : "";
        var root = store.PreparedRoot(video.ContentHash);

        if (path == "master.m3u8")
        {
            var mode = ParseAudio(audio) ?? await settings.AudioForAsync(access.UserId, cancellationToken);
            var plan = await plans.PlanAsync(db, video, VideoAudioMode.Track, catalog.VisibleFiles(access), cancellationToken);
            var master = Master(video, store, plan.AudioKey, mode, suffix);
            if (master is null)
            {
                prepare.Request(video.Id);
                throw new ArcanistException.NotFound();
            }
            context.Response.Headers.CacheControl = "private, no-cache";
            return Results.Text(master, "application/vnd.apple.mpegurl", Encoding.UTF8);
        }

        // "v-720p/index.m3u8", "a-video/seg0003.m4s": one folder level, plain names only.
        var parts = path.Split('/');
        if (parts.Length != 2 || parts.Any(p => p.Length == 0 || p.StartsWith('.') || p.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.'))))
            throw new ArcanistException.NotFound();
        var file = Path.Combine(root, parts[0], parts[1]);
        if (!File.Exists(file))
            throw new ArcanistException.NotFound();

        if (parts[1].EndsWith(".m3u8", StringComparison.Ordinal))
        {
            var lines = (await File.ReadAllLinesAsync(file, cancellationToken)).Select(line =>
                line.StartsWith("#EXT-X-MAP:URI=\"", StringComparison.Ordinal) ? line.Replace("init.mp4\"", $"init.mp4{suffix}\"", StringComparison.Ordinal)
                : line.Length > 0 && !line.StartsWith('#') ? line + suffix
                : line);
            context.Response.Headers.CacheControl = "private, max-age=3600";
            return Results.Text(string.Join('\n', lines) + "\n", "application/vnd.apple.mpegurl", Encoding.UTF8);
        }

        context.Response.Headers.CacheControl = "private, max-age=86400, immutable";
        return Results.File(file, "video/mp4", enableRangeProcessing: true);
    }

    /// <summary>
    /// The master playlist of what is prepared: the picture renditions, and the audio as alternatives (the song's
    /// when the alignment matched, and the video's own), the default being <paramref name="mode"/>; null when no
    /// picture or audio is ready yet.
    /// </summary>
    internal static string? Master(MusicVideo video, VideoStore store, string mixKey, VideoAudioMode mode, string suffix)
    {
        var pictures = Directory.Exists(store.PreparedRoot(video.ContentHash))
            ? Directory.EnumerateDirectories(store.PreparedRoot(video.ContentHash), "v-*").Where(VideoStore.IsReady).ToList()
            : [];
        var own = VideoStore.IsReady(store.AudioFolder(video.ContentHash, "video"));
        var mix = mixKey != "video" && VideoStore.IsReady(store.AudioFolder(video.ContentHash, mixKey));
        if (pictures.Count == 0 || (!own && !mix))
            return null;

        var defaultTrack = mix && (mode == VideoAudioMode.Track || !own);
        var builder = new StringBuilder("#EXTM3U\n#EXT-X-VERSION:7\n#EXT-X-INDEPENDENT-SEGMENTS\n");
        if (mix)
            builder.Append($"#EXT-X-MEDIA:TYPE=AUDIO,GROUP-ID=\"audio\",NAME=\"Song\",LANGUAGE=\"und\",DEFAULT={(defaultTrack ? "YES" : "NO")},AUTOSELECT=YES,CHANNELS=\"2\",URI=\"a-{mixKey}/index.m3u8{suffix}\"\n");
        if (own)
            builder.Append($"#EXT-X-MEDIA:TYPE=AUDIO,GROUP-ID=\"audio\",NAME=\"Video\",LANGUAGE=\"und\",DEFAULT={(defaultTrack ? "NO" : "YES")},AUTOSELECT=YES,CHANNELS=\"2\",URI=\"a-video/index.m3u8{suffix}\"\n");

        var ladder = new Dictionary<string, (int Height, int Width, int BitRate, string Codec)>();
        foreach (var folder in pictures)
        {
            var name = Path.GetFileName(folder)[2..];
            var height = name == "native" ? video.Height ?? 0 : int.Parse(name.TrimEnd('p'), CultureInfo.InvariantCulture);
            var width = video.Width is > 0 && video.Height is > 0 ? (int)Math.Round(video.Width.Value * (double)height / video.Height.Value / 2) * 2 : 0;
            var codec = name == "native" ? video.VideoCodec ?? "h264" : height > 1080 && File.Exists(Path.Combine(folder, "hevc")) ? "hevc" : "h264";
            var bytes = new DirectoryInfo(folder).EnumerateFiles("seg*.m4s").Sum(f => f.Length);
            var bitRate = video.DurationMs > 0 ? (int)Math.Min(int.MaxValue, bytes * 8 * 1000 / video.DurationMs) : 0;
            ladder[name] = (height, width, bitRate, codec);
        }
        foreach (var (name, r) in ladder.OrderByDescending(r => r.Value.Height))
        {
            var codecs = $"{CodecString(r.Codec)},mp4a.40.2";
            var resolution = r.Width > 0 && r.Height > 0 ? $",RESOLUTION={r.Width}x{r.Height}" : "";
            builder.Append($"#EXT-X-STREAM-INF:BANDWIDTH={Math.Max(1, r.BitRate) + 256_000},AVERAGE-BANDWIDTH={Math.Max(1, r.BitRate) + 192_000}{resolution},CODECS=\"{codecs}\",AUDIO=\"audio\"\n");
            builder.Append($"v-{name}/index.m3u8{suffix}\n");
        }
        return builder.ToString();
    }

    private static string CodecString(string codec) => codec.ToLowerInvariant() switch
    {
        "hevc" => "hvc1.1.6.L150.90",
        "av1" => "av01.0.08M.08",
        "vp9" => "vp09.00.40.08",
        _ => "avc1.640028",
    };

    public static VideoAudioMode? ParseAudio(string? audio) => audio?.ToLowerInvariant() switch
    {
        "track" => VideoAudioMode.Track,
        "video" => VideoAudioMode.Video,
        null or "" => null,
        _ => throw ArcanistException.Field("audio", ErrorCodes.InvalidFormat),
    };

    /// <summary>MP4 files play in browsers as they are; Matroska, WebM and QuickTime are converted to MP4.</summary>
    private static bool PlaysAsIs(MusicVideo video) =>
        VideoFormats.MimeType(video.RelativePath) == "video/mp4" && video.AudioCodec is null or "aac" or "mp3" or "opus";

    private async Task<IResult> OriginalAsync(HttpContext context, MusicVideo video, CancellationToken cancellationToken)
    {
        var location = StorageLocation.From(video.LibrarySource);
        var stream = await storages.For(location.Kind).OpenReadAsync(location, video.RelativePath, cancellationToken);
        context.Response.Headers[AudioHeader] = "video";
        context.Response.Headers.CacheControl = "private, max-age=3600";
        return Results.Stream(stream, VideoFormats.MimeType(video.RelativePath), lastModified: video.ModifiedAt,
            entityTag: new EntityTagHeaderValue($"\"{video.ContentHash}\""), enableRangeProcessing: true);
    }
}
