using System.Globalization;
using Arcanist.Core.Api;
using Arcanist.Core.Caching;
using Arcanist.Core.Media;
using Arcanist.Core.Videos;

namespace Arcanist.Core.Application.Videos;

/// <summary>
/// Streams a music video version while ffmpeg makes it (fragmented MP4: no length, no ranges; players start later
/// with <c>t</c>). With a cache, what is sent is also written down and, when the whole video was made cleanly,
/// rewritten with its index first and kept: the next time it plays from there, with ranges.
/// </summary>
internal sealed class VideoTranscodeResult(
    TranscodeSession session, double durationSeconds, DiskCache? cache, string cacheKey, VideoTranscoder transcoder) : IResult
{
    public async Task ExecuteAsync(HttpContext context)
    {
        string? partial = null;
        var completed = false;
        await using (session)
        {
            var buffer = new byte[256 * 1024];
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
            response.ContentType = "video/mp4";
            response.Headers.CacheControl = "private, no-store";
            response.Headers.AcceptRanges = "none";
            response.Headers["X-Content-Duration"] = durationSeconds.ToString("0.###", CultureInfo.InvariantCulture);

            partial = cache?.NewPartialPath();
            try
            {
                await using (var copy = partial is null ? null : new FileStream(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None, 256 * 1024, true))
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
                completed = partial is not null && await session.FinishedCleanlyAsync();
            }
            catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
            {
            }
            catch (IOException)
            {
                // The cache's disk is full or gone: the player still got its video.
            }
        }

        if (partial is null)
            return;
        if (!completed)
        {
            DiskCache.Discard(partial);
            return;
        }

        // In the background: the response is over, and the rewrite only copies the streams.
        _ = Task.Run(async () =>
        {
            var seekable = cache!.NewPartialPath() + ".mp4";
            try
            {
                if (await transcoder.MakeSeekableAsync(partial, seekable, CancellationToken.None))
                {
                    var final = cache.NewPartialPath();
                    File.Move(seekable, final);
                    cache.Commit(final, cacheKey);
                }
            }
            catch (IOException)
            {
            }
            finally
            {
                DiskCache.Discard(partial);
                DiskCache.Discard(seekable);
            }
        });
    }
}
