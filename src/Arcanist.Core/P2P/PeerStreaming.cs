using Arcanist.Core.Api;
using Arcanist.Core.Application;
using Arcanist.Core.Application.Streaming;
using Arcanist.Core.Media;
using Arcanist.Core.Storage;

namespace Arcanist.Core.P2P;

/// <summary>
/// Plays files of a shared library: the request goes to the server that shares them, which applies its own rules
/// (converting, limiting the bit rate, refusing downloads), and its answer is relayed as it arrives, ranges
/// included. This server converts nothing.
/// </summary>
internal static class PeerStreaming
{
    /// <summary>Headers of the other server's answer that are passed on.</summary>
    private static readonly string[] Passed =
    [
        "Content-Type", "Content-Length", "Content-Range", "Accept-Ranges", "ETag", "Last-Modified", "Content-Disposition",
        "X-Content-Duration", "Cache-Control",
    ];

    public static async Task<IResult> ProxyAsync(
        HttpContext context, StreamTarget target, bool download, TranscodeProfile? requested, int? maxBitRate, double offset,
        CancellationToken cancellationToken)
    {
        var storage = (PeerStorage)context.RequestServices.GetRequiredService<ILibraryStorageResolver>().For(target.Location.Kind);
        var client = context.RequestServices.GetRequiredService<PeerClient>();
        if (await storage.RemoteAsync(target.Location, cancellationToken) is not { } remote)
            return Problems.Create(StatusCodes.Status404NotFound, ErrorCodes.NotFound);

        var query = new List<string> { "path=" + Uri.EscapeDataString(target.RelativePath), "cue=" + target.CueTrack };
        if (download)
            query.Add("download=true");
        if (requested is not null)
            query.Add("format=" + (requested.Format is { } format ? format.ToString().ToLowerInvariant() : "lpcm"));
        if (maxBitRate is { } cap)
            query.Add("maxBitRate=" + cap);
        if (offset > 0)
            query.Add("timeOffset=" + offset.ToString(System.Globalization.CultureInfo.InvariantCulture));
        var path = "/rest/v1/peer/stream?" + string.Join('&', query);
        var method = HttpMethods.IsHead(context.Request.Method) ? HttpMethod.Head : HttpMethod.Get;

        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(remote.Ticket, b =>
            {
                var request = new HttpRequestMessage(method, new Uri(b, path));
                foreach (var header in new[] { "Range", "If-Range", "If-None-Match", "If-Modified-Since" })
                {
                    if (context.Request.Headers.TryGetValue(header, out var value))
                        request.Headers.TryAddWithoutValidation(header, value.ToArray());
                }

                return request;
            }, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (ArcanistException e)
        {
            return Problems.Create(StatusCodes.Status503ServiceUnavailable, e.Code);
        }

        return new RelayResult(response);
    }

    private sealed class RelayResult(HttpResponseMessage response) : IResult
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

                await using var body = await response.Content.ReadAsStreamAsync(context.RequestAborted);
                try
                {
                    await body.CopyToAsync(context.Response.Body, context.RequestAborted);
                }
                catch (Exception e) when (e is OperationCanceledException or IOException or HttpRequestException)
                {
                    // The player or the other server went away.
                }
            }
        }
    }
}
