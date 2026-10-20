using System.Globalization;
using Arcanist.Core.Application;
using Arcanist.Core.Caching;
using Arcanist.Core.Media;

namespace Arcanist.Core.Api.Rest;

/// <summary>
/// Streams a running transcode to the client. There is no length or range support (the size is unknown until
/// ffmpeg finishes): players seek by asking again with <c>timeOffset</c>. ffmpeg stops when the client goes away.
/// With a <paramref name="cache"/>, what is sent is also written to it, and kept only when the whole song was
/// converted and ffmpeg finished cleanly: the next time it is played from there.
/// </summary>
internal sealed class TranscodeResult(
    TranscodeSession session, string contentType, double? durationSeconds, DiskCache? cache = null, string? cacheKey = null) : IResult
{
    public async Task ExecuteAsync(HttpContext context)
    {
        await using (session)
        {
            // Nothing is sent until ffmpeg produces audio, so a conversion that fails at once is an error response
            // rather than an empty 200.
            var buffer = new byte[64 * 1024];
            int read;
            try
            {
                read = await session.Output.ReadAsync(buffer, context.RequestAborted);
            }
            catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
            {
                return;
            }

            if (read == 0)
            {
                await Problems.Create(StatusCodes.Status503ServiceUnavailable, ErrorCodes.TranscodingUnavailable).ExecuteAsync(context);
                return;
            }

            var response = context.Response;
            response.ContentType = contentType;
            response.Headers.CacheControl = "private, no-store";
            response.Headers.AcceptRanges = "none";
            // Lets players show the duration of a stream whose length they cannot know (Firefox reads it).
            if (durationSeconds is { } duration)
                response.Headers["X-Content-Duration"] = duration.ToString("0.###", CultureInfo.InvariantCulture);

            var partial = cache is not null && cacheKey is not null ? cache.NewPartialPath() : null;
            var kept = false;
            try
            {
                await using (var copy = partial is null ? null : new FileStream(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, true))
                {
                    do
                    {
                        await response.Body.WriteAsync(buffer.AsMemory(0, read), context.RequestAborted);
                        if (copy is not null)
                            await copy.WriteAsync(buffer.AsMemory(0, read), context.RequestAborted);
                        read = await session.Output.ReadAsync(buffer, context.RequestAborted);
                    }
                    while (read > 0);
                }

                kept = partial is not null && await session.FinishedCleanlyAsync();
                if (kept)
                    cache!.Commit(partial!, cacheKey!);
            }
            catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
            {
                // The player stopped or seeked; disposing the session ends ffmpeg.
            }
            catch (IOException)
            {
                // The disk of the cache is full or gone: the player still got its audio.
            }
            finally
            {
                if (partial is not null && !kept)
                    DiskCache.Discard(partial);
            }
        }
    }
}
