using System.ComponentModel;
using Arcanist.Core.Application.Media;
using Arcanist.Core.Media;
using GraphQL;

namespace Arcanist.Core.Api.GraphQL;

public sealed partial class Query
{
    [Name("ffmpeg")]
    [Description("Where Arcanist found ffmpeg, and the progress of a download. Requires MANAGE_SETTINGS.")]
    public static Task<FfmpegStatus> Ffmpeg([FromServices] FfmpegService ffmpeg, CancellationToken cancellationToken) =>
        ffmpeg.StatusAsync(cancellationToken);

    [Name("transcoding")]
    [Description("The conversion cache and the versions of the music made ahead of time (passive transcoding), with their progress. Requires MANAGE_SETTINGS.")]
    public static Task<TranscodingStatus> Transcoding([FromServices] TranscodingService transcoding, CancellationToken cancellationToken) =>
        transcoding.StatusAsync(cancellationToken);

    [Name("streamingDefaults")]
    [Description("The quality each kind of Ultimate client (desktop, web, mobile) plays by default. The stream applies it to requests with `client` and no quality of their own; clients show it as their default.")]
    public static Task<StreamingDefaults> StreamingDefaults([FromServices] StreamingDefaultsService defaults, CancellationToken cancellationToken) =>
        defaults.GetAsync(cancellationToken);
}

public sealed partial class Mutation
{
    [Name("setFfmpegPath")]
    [Description("Uses the ffmpeg at the given path, or finds one automatically when null. Fails with not_ffmpeg if it does not work. Administrators only, with a recent password.")]
    public static Task<FfmpegStatus> SetFfmpegPath(string? path, [FromServices] FfmpegService ffmpeg, CancellationToken cancellationToken) =>
        ffmpeg.SetPathAsync(path, cancellationToken);

    [Name("setTranscodingSettings")]
    [Description("The conversion cache (on or off, size, age) and the versions to make ahead of time, of which libraries. Versions nobody asks for any more are removed. Requires MANAGE_SETTINGS.")]
    public static Task<TranscodingStatus> SetTranscodingSettings(SetTranscodingSettingsInput input, [FromServices] TranscodingService transcoding, CancellationToken cancellationToken) =>
        transcoding.SetAsync(input, cancellationToken);

    [Name("setStreamingDefaults")]
    [Description("Sets the quality each kind of Ultimate client plays by default: the original (no format; with maxBitRate, MP3 only above it) or a format and bit rate. Requires MANAGE_SETTINGS.")]
    public static Task<StreamingDefaults> SetStreamingDefaults(SetStreamingDefaultsInput input, [FromServices] StreamingDefaultsService defaults, CancellationToken cancellationToken) =>
        defaults.SetAsync(input, cancellationToken);

    [Name("clearTranscodeCache")]
    [Description("Empties the conversion cache; the versions made ahead of time stay. Requires MANAGE_SETTINGS.")]
    public static Task<TranscodingStatus> ClearTranscodeCache([FromServices] TranscodingService transcoding, CancellationToken cancellationToken) =>
        transcoding.ClearCacheAsync(cancellationToken);

    [Name("installFfmpeg")]
    [Description("Downloads ffmpeg (a static GPL build from BtbN/FFmpeg-Builds) into the data directory in the background. Administrators only, with a recent password.")]
    public static Task<FfmpegStatus> InstallFfmpeg([FromServices] FfmpegService ffmpeg, CancellationToken cancellationToken) =>
        ffmpeg.InstallAsync(cancellationToken);
}
