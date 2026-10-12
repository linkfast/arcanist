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

        return rest;
    }
}
