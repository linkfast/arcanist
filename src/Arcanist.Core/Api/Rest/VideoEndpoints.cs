using Arcanist.Core.Application;
using Arcanist.Core.Application.Libraries;
using Arcanist.Core.Application.Videos;
using Arcanist.Core.Videos;
using Microsoft.Extensions.Options;

namespace Arcanist.Core.Api.Rest;

/// <summary>A music video's song (none leaves it with no song).</summary>
public sealed record LinkMusicVideoInput(Guid? TrackId);

public sealed record SetMusicVideoLiveInput(bool Live);

/// <summary>Whose audio plays with music videos for the user; null follows the server's default.</summary>
public sealed record SetMyVideoSettingsInput(VideoAudioMode? Audio);

public static class VideoEndpoints
{
    private static readonly string[] VideoTypes = ["video/webm", "video/x-matroska", "video/quicktime"];

    public static IEndpointRouteBuilder MapVideoEndpoints(this IEndpointRouteBuilder rest)
    {
        var videos = rest.MapGroup("/videos").WithTags("Music videos");

        videos.MapGet("", (Guid? libraryId, string? search, bool? linked, bool? live, Guid? trackId, string? after, int? first, MusicVideoService service,
                CancellationToken ct) => service.ListAsync(new MusicVideosInput(libraryId, search, linked, live, trackId, after, first), ct))
            .WithName("MusicVideos")
            .WithSummary("The music videos in libraries the user can access, by title, with optional filters. Each says whether it plays with its song's audio and how to play it.");

        videos.MapGet("/{id:guid}", (Guid id, MusicVideoService service, CancellationToken ct) => service.GetAsync(id, ct))
            .WithName("MusicVideo")
            .WithSummary("A music video, with the stretches where its audio and its song's line up (segments).");

        videos.MapGet("/{id:guid}/lyrics", (Guid id, VideoLyricsService service, CancellationToken ct) => service.GetAsync(id, ct))
            .WithName("MusicVideoLyrics")
            .WithSummary("A music video's lyrics on the video's timeline: its song's timing carried over to where the video plays the song (nothing over scenes that are not the song; parts it cuts left out, parts it repeats shown again), or timed on the video's own audio when it is not the song's recording. pending: the timing is being worked out; ask again later.");

        videos.MapPut("/{id:guid}/link", (Guid id, LinkMusicVideoInput input, MusicVideoService service, CancellationToken ct) =>
                service.LinkAsync(id, input.TrackId, ct))
            .WithName("LinkMusicVideo")
            .WithSummary("Links a music video to a track's song (or, with no trackId, leaves it with no song); scans keep what a person chose. Requires MANAGE_LIBRARIES or EDIT_METADATA.");

        videos.MapPost("/{id:guid}/unlock", (Guid id, MusicVideoService service, CancellationToken ct) => service.UnlockAsync(id, ct))
            .WithName("UnlockMusicVideo")
            .WithSummary("Gives a music video back to automatic linking and live detection. Requires MANAGE_LIBRARIES or EDIT_METADATA.");

        videos.MapPut("/{id:guid}/live", (Guid id, SetMusicVideoLiveInput input, MusicVideoService service, CancellationToken ct) =>
                service.SetLiveAsync(id, input.Live, ct))
            .WithName("SetMusicVideoLive")
            .WithSummary("Marks a music video as a live performance or not. Requires MANAGE_LIBRARIES or EDIT_METADATA.");

        videos.MapPost("/{id:guid}/realign", (Guid id, MusicVideoService service, CancellationToken ct) => service.RealignAsync(id, ct))
            .WithName("RealignMusicVideo")
            .WithSummary("Forgets a music video's alignment and aligns it again first of all. Requires MANAGE_LIBRARIES or EDIT_METADATA.");

        videos.MapDelete("/{id:guid}", async (Guid id, MusicVideoService service, CancellationToken ct) =>
            {
                await service.DeleteAsync(id, ct);
                return Results.NoContent();
            })
            .WithName("DeleteMusicVideo")
            .WithSummary("Moves a music video's file to the trash of its location and removes it from the catalog. Requires DELETE_TRACKS.");

        videos.MapGet("/settings", (MusicVideoService service, CancellationToken ct) => service.SettingsAsync(ct))
            .WithName("VideoSettings")
            .WithSummary("How the server handles music videos. Requires MANAGE_SETTINGS.");

        videos.MapPut("/settings", (VideoSettingsInput input, MusicVideoService service, CancellationToken ct) => service.SetSettingsAsync(input, ct))
            .WithName("SetVideoSettings")
            .WithSummary("Sets how the server handles music videos. Requires MANAGE_SETTINGS.");

        rest.MapGet("/me/video-settings", (MusicVideoService service, CancellationToken ct) => service.MySettingsAsync(ct))
            .WithTags("Music videos")
            .WithName("MyVideoSettings")
            .WithSummary("The user's choice of whose audio plays with music videos, and the one in effect.");

        rest.MapPut("/me/video-settings", (SetMyVideoSettingsInput input, MusicVideoService service, CancellationToken ct) =>
                service.SetMySettingsAsync(input.Audio, ct))
            .WithTags("Music videos")
            .WithName("SetMyVideoSettings")
            .WithSummary("Chooses whose audio plays with music videos for the user (TRACK, VIDEO, or null for the server's default).");

        videos.MapMethods("/{id:guid}/stream", [HttpMethods.Get, HttpMethods.Head], StreamAsync)
            .WithName("StreamMusicVideo")
            .WithSummary("A music video as MP4. Authenticate with a Bearer token or the `st` parameter of its stream URLs. `audio` is TRACK " +
                         "(the song's audio from the library where the video plays it, the video's own elsewhere) or VIDEO (its own); " +
                         "without it, the user's choice (myVideoSettings). Both have the same timeline: switch keeping the position. " +
                         "`rendition` (native, 2160p…360p) or `maxHeight`/`maxBitRate` choose the version. A version not made yet streams " +
                         "while it is made, without ranges (start later with `t`, in seconds); once made, it is served with ranges. " +
                         "X-Arcanist-Video-Audio says whose audio it has (VIDEO until the alignment is ready).")
            .Produces(StatusCodes.Status200OK, typeof(Stream), "video/mp4", VideoTypes)
            .Produces(StatusCodes.Status206PartialContent, typeof(Stream), "video/mp4", VideoTypes);

        videos.MapGet("/{id:guid}/hls/{**path}", HlsAsync)
            .WithName("MusicVideoHls")
            .WithSummary("Adaptive streaming (HLS) of a prepared music video: `master.m3u8` (its hlsUrl), with the song's audio and the video's " +
                         "own as alternative audio renditions (the default per `audio` or the user's choice). The token travels in every URI.")
            .ExcludeFromDescription();

        rest.MapPost("/tracks/{id:guid}/video", async (Guid id, string fileName, string? client, HttpContext context, UploadService service,
                IOptionsMonitor<UploadOptions> options, CancellationToken ct) =>
            {
                UploadClient? kind = null;
                if (!string.IsNullOrEmpty(client))
                    kind = Enum.TryParse<UploadClient>(client, true, out var parsed) && Enum.IsDefined(parsed) ? parsed : throw ArcanistException.Field("client", ErrorCodes.InvalidFormat);
                if (context.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } limit)
                    limit.MaxRequestBodySize = options.CurrentValue.MaxBytes;
                var result = await service.UploadVideoAsync(id, fileName, context.Request.Body, kind, ct);
                return result.Status == UploadStatus.Imported ? Results.Created((string?)null, result) : Results.Ok(result);
            })
            .WithTags("Uploads")
            .WithName("UploadTrackVideo")
            .WithSummary("Adds a track's music video (MP4, MKV, WebM, MOV) next to its copy in a library the user uploads to, named like it: the next scan links it and the server aligns it. Requires UPLOAD.")
            .Accepts<Stream>("application/octet-stream")
            .Produces<UploadResult>(StatusCodes.Status201Created)
            .Produces<UploadResult>();

        return rest;
    }

    private static async Task<IResult> StreamAsync(
        Guid id, string? audio, string? rendition, int? maxHeight, int? maxBitRate, double? t, HttpContext context, VideoStreamService service,
        CancellationToken cancellationToken)
    {
        await MediaEndpoints.AuthenticateStreamTokenAsync(context, cancellationToken);
        return await service.StreamAsync(context, id, audio, maxHeight, maxBitRate, rendition, t, cancellationToken);
    }

    private static async Task<IResult> HlsAsync(Guid id, string path, string? audio, HttpContext context, VideoStreamService service, CancellationToken cancellationToken)
    {
        await MediaEndpoints.AuthenticateStreamTokenAsync(context, cancellationToken);
        return await service.HlsAsync(context, id, path, audio, cancellationToken);
    }
}
