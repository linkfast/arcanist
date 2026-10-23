using Arcanist.Core.Application.Media;

namespace Arcanist.Core.Api.Rest;

public sealed record SetFfmpegPathInput(string? Path);

public static class FfmpegEndpoints
{
    public static IEndpointRouteBuilder MapFfmpegEndpoints(this IEndpointRouteBuilder rest)
    {
        var ffmpeg = rest.MapGroup("/ffmpeg").WithTags("Media tools");

        ffmpeg.MapGet("", (FfmpegService service, CancellationToken ct) => service.StatusAsync(ct))
            .WithName("Ffmpeg")
            .WithSummary("Where Arcanist found ffmpeg, and the progress of a download. Requires MANAGE_SETTINGS.");

        ffmpeg.MapPut("/path", (SetFfmpegPathInput input, FfmpegService service, CancellationToken ct) => service.SetPathAsync(input.Path, ct))
            .WithName("SetFfmpegPath")
            .WithSummary("Uses the ffmpeg at the given path, or finds one automatically when null. Fails with not_ffmpeg if it does not work. Administrators only, with a recent password.");

        ffmpeg.MapPost("/install", (FfmpegService service, CancellationToken ct) => service.InstallAsync(ct))
            .WithName("InstallFfmpeg")
            .WithSummary("Downloads ffmpeg (a static GPL build from BtbN/FFmpeg-Builds) into the data directory in the background. Poll the status to follow it. Administrators only, with a recent password.");

        ffmpeg.MapDelete("/download", (FfmpegService service, CancellationToken ct) => service.RemoveDownloadedAsync(ct))
            .WithName("RemoveDownloadedFfmpeg")
            .WithSummary("Deletes the ffmpeg Arcanist downloaded; the next one found (the configured one, the system's) takes its place. Administrators only, with a recent password.");

        var transcoding = rest.MapGroup("/transcoding").WithTags("Media tools");

        transcoding.MapGet("", (TranscodingService service, CancellationToken ct) => service.StatusAsync(ct))
            .WithName("Transcoding")
            .WithSummary("The conversion cache and the versions of the music made ahead of time (passive transcoding), with their progress. Requires MANAGE_SETTINGS.");

        transcoding.MapPut("/settings", (SetTranscodingSettingsInput input, TranscodingService service, CancellationToken ct) => service.SetAsync(input, ct))
            .WithName("SetTranscodingSettings")
            .WithSummary("The conversion cache (on or off, size, age) and the versions to make ahead of time, of which libraries. Versions nobody asks for any more are removed. Requires MANAGE_SETTINGS.");

        transcoding.MapDelete("/cache", (TranscodingService service, CancellationToken ct) => service.ClearCacheAsync(ct))
            .WithName("ClearTranscodeCache")
            .WithSummary("Empties the conversion cache; the versions made ahead of time stay. Requires MANAGE_SETTINGS.");

        var defaults = rest.MapGroup("/streaming-defaults").WithTags("Media");

        defaults.MapGet("", (StreamingDefaultsService service, CancellationToken ct) => service.GetAsync(ct))
            .WithName("StreamingDefaults")
            .WithSummary("The quality each kind of Ultimate client (desktop, web, mobile) plays by default. The stream applies it to requests with `client` and no quality of their own; clients show it as their default.");

        defaults.MapPut("", (SetStreamingDefaultsInput input, StreamingDefaultsService service, CancellationToken ct) => service.SetAsync(input, ct))
            .WithName("SetStreamingDefaults")
            .WithSummary("Sets the quality each kind of Ultimate client plays by default: the original (no format; with maxBitRate, MP3 only above it) or a format and bit rate. Requires MANAGE_SETTINGS.");

        return rest;
    }
}
