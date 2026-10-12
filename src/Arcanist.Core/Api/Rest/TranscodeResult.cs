using System.Globalization;
using Arcanist.Core.Media;

namespace Arcanist.Core.Api.Rest;

/// <summary>
/// Streams a running transcode to the client. There is no length or range support (the size is unknown until
/// ffmpeg finishes): players seek by asking again with <c>timeOffset</c>. ffmpeg stops when the client goes away.
/// </summary>
internal sealed class TranscodeResult(TranscodeSession session, string contentType, double? durationSeconds) : IResult
{
    public async Task ExecuteAsync(HttpContext context)
    {
        await using (session)
        {
            var response = context.Response;
            response.ContentType = contentType;
            response.Headers.CacheControl = "private, no-store";
            response.Headers.AcceptRanges = "none";
            // Lets players show the duration of a stream whose length they cannot know (Firefox reads it).
            if (durationSeconds is { } duration)
                response.Headers["X-Content-Duration"] = duration.ToString("0.###", CultureInfo.InvariantCulture);

            try
            {
                await session.Output.CopyToAsync(response.Body, context.RequestAborted);
            }
            catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
            {
                // The player stopped or seeked; disposing the session ends ffmpeg.
            }
        }
    }
}
