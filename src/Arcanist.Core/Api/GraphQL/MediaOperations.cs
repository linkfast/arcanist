using System.ComponentModel;
using Arcanist.Core.Application.Media;
using GraphQL;

namespace Arcanist.Core.Api.GraphQL;

public sealed partial class Query
{
    [Name("ffmpeg")]
    [Description("Where Arcanist found ffmpeg, and the progress of a download. Requires MANAGE_SETTINGS.")]
    public static Task<FfmpegStatus> Ffmpeg([FromServices] FfmpegService ffmpeg, CancellationToken cancellationToken) =>
        ffmpeg.StatusAsync(cancellationToken);
}

public sealed partial class Mutation
{
    [Name("setFfmpegPath")]
    [Description("Uses the ffmpeg at the given path, or finds one automatically when null. Fails with not_ffmpeg if it does not work. Administrators only, with a recent password.")]
    public static Task<FfmpegStatus> SetFfmpegPath(string? path, [FromServices] FfmpegService ffmpeg, CancellationToken cancellationToken) =>
        ffmpeg.SetPathAsync(path, cancellationToken);

    [Name("installFfmpeg")]
    [Description("Downloads ffmpeg (a static GPL build from BtbN/FFmpeg-Builds) into the data directory in the background. Administrators only, with a recent password.")]
    public static Task<FfmpegStatus> InstallFfmpeg([FromServices] FfmpegService ffmpeg, CancellationToken cancellationToken) =>
        ffmpeg.InstallAsync(cancellationToken);
}
