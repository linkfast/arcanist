using System.Net;
using Arcanist.Core.Api;
using Arcanist.Core.Application;
using Arcanist.Core.Application.Streaming;
using Arcanist.Core.Storage;

namespace Arcanist.Core.Cloud;

/// <summary>
/// Plays the original of an Ultimate Cloud track: the request (with its Range) goes to the signed URL of the audio,
/// and the answer is relayed. Players never see Ultimate Cloud's URLs nor need its account. Conversions go through
/// the transcoder like any other file.
/// </summary>
internal static class UltimateCloudStreaming
{
    private static readonly string[] Passed = ["Content-Length", "Content-Range", "Accept-Ranges"];

    public static async Task<IResult> ProxyAsync(HttpContext context, StreamTarget target, bool download, CancellationToken cancellationToken)
    {
        var storage = (UltimateCloudStorage)context.RequestServices.GetRequiredService<ILibraryStorageResolver>().For(target.Location.Kind);
        for (var attempt = 0; ; attempt++)
        {
            string url;
            try
            {
                url = await storage.AudioUrlAsync(target.Location, target.RelativePath, cancellationToken);
            }
            catch (FileNotFoundException)
            {
                return Problems.Create(StatusCodes.Status404NotFound, ErrorCodes.NotFound);
            }
            catch (IOException e)
            {
                return Problems.Create(StatusCodes.Status503ServiceUnavailable, e.Message.StartsWith("cloud_", StringComparison.Ordinal) ? e.Message : ErrorCodes.CloudUnreachable);
            }

            using var request = new HttpRequestMessage(HttpMethods.IsHead(context.Request.Method) ? HttpMethod.Head : HttpMethod.Get, url);
            if (context.Request.Headers.Range is { Count: > 0 } range)
                request.Headers.TryAddWithoutValidation("Range", range.ToArray());

            HttpResponseMessage response;
            try
            {
                response = await storage.Files.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            }
            catch (HttpRequestException)
            {
                return Problems.Create(StatusCodes.Status503ServiceUnavailable, ErrorCodes.CloudUnreachable);
            }

            // The signed URL expired early or was revoked: ask for a new one, once.
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden && attempt == 0)
            {
                response.Dispose();
                storage.Forget(target.RelativePath);
                continue;
            }

            if (!response.IsSuccessStatusCode)
            {
                var status = response.StatusCode;
                response.Dispose();
                return status == HttpStatusCode.RequestedRangeNotSatisfiable
                    ? Results.StatusCode(StatusCodes.Status416RangeNotSatisfiable)
                    : Problems.Create(status == HttpStatusCode.NotFound ? StatusCodes.Status404NotFound : StatusCodes.Status503ServiceUnavailable,
                        status == HttpStatusCode.NotFound ? ErrorCodes.NotFound : ErrorCodes.CloudUnreachable);
            }

            return new RelayResult(response, target, download);
        }
    }

    private sealed class RelayResult(HttpResponseMessage response, StreamTarget target, bool download) : IResult
    {
        public async Task ExecuteAsync(HttpContext context)
        {
            using (response)
            {
                context.Response.StatusCode = (int)response.StatusCode;
                foreach (var name in Passed)
                {
                    if (response.Headers.TryGetValues(name, out var values) || response.Content.Headers.TryGetValues(name, out values))
                        context.Response.Headers[name] = values.ToArray();
                }

                // What Arcanist knows of the file, as for any other copy.
                context.Response.ContentType = target.ContentType;
                context.Response.Headers.ETag = $"\"{target.ContentHash}\"";
                context.Response.Headers.CacheControl = "private, max-age=3600";
                context.Response.Headers.AcceptRanges = "bytes";
                if (download)
                    context.Response.Headers.ContentDisposition =
                        new Microsoft.Net.Http.Headers.ContentDispositionHeaderValue("attachment") { FileNameStar = target.FileName }.ToString();

                if (HttpMethods.IsHead(context.Request.Method))
                    return;
                await using var body = await response.Content.ReadAsStreamAsync(context.RequestAborted);
                try
                {
                    await body.CopyToAsync(context.Response.Body, context.RequestAborted);
                }
                catch (Exception e) when (e is OperationCanceledException or IOException or HttpRequestException)
                {
                    // The player or Ultimate Cloud went away.
                }
            }
        }
    }
}
