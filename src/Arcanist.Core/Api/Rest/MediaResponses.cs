using Arcanist.Core.Application.Streaming;
using Arcanist.Core.Media;
using Arcanist.Core.Scanning;
using Arcanist.Core.Storage;
using Microsoft.Net.Http.Headers;

namespace Arcanist.Core.Api.Rest;

/// <summary>
/// Sends a track's audio for the signed-in user: the original with range support, or converted. Shared by the REST
/// API and the compatibility extensions (Subsonic, mStream), so every client streams the same way.
/// </summary>
public static class MediaResponses
{
    /// <param name="requested">Format to convert to; the original when null (unless a cap or offset needs conversion).</param>
    /// <param name="maxBitRate">kbit/s; converts to MP3 only when the original exceeds it.</param>
    /// <param name="timeOffset">Seconds to start at; converts.</param>
    public static Task<IResult> StreamAsync(
        HttpContext context, Guid trackId, Guid? fileId, bool download, TranscodeFormat? requested, int? maxBitRate, double? timeOffset,
        CancellationToken cancellationToken) =>
        StreamWithProfileAsync(context, trackId, fileId, download, requested is { } format ? TranscodeProfile.For(format) : null, maxBitRate,
            timeOffset, cancellationToken);

    /// <param name="requested">Profile to convert with (DLNA also uses <see cref="TranscodeProfile.Lpcm"/>).</param>
    public static async Task<IResult> StreamWithProfileAsync(
        HttpContext context, Guid trackId, Guid? fileId, bool download, TranscodeProfile? requested, int? maxBitRate, double? timeOffset,
        CancellationToken cancellationToken)
    {
        var streams = context.RequestServices.GetRequiredService<StreamService>();
        var storages = context.RequestServices.GetRequiredService<ILibraryStorageResolver>();
        var transcoder = context.RequestServices.GetRequiredService<Transcoder>();

        // Through the P2P network, the peer's rules apply: no downloads unless allowed, and a bit-rate cap on streams.
        if (P2P.P2PRequest.Peer(context) is { } peer)
        {
            if (download && !peer.Rules.AllowDownload)
                return Problems.Create(StatusCodes.Status403Forbidden, Application.ErrorCodes.DownloadNotAllowed);
            if (!download && peer.Rules.MaxBitRate is { } peerCap)
                maxBitRate = Math.Min(maxBitRate ?? peerCap, peerCap);
        }

        var target = await streams.ResolveAsync(trackId, fileId, download, cancellationToken);
        var offset = Math.Max(0, timeOffset ?? 0);
        if (target.Location.Kind == Data.Entities.LibrarySourceKind.Peer)
            return await P2P.PeerStreaming.ProxyAsync(context, target, download, requested, maxBitRate, offset, cancellationToken);
        var storage = storages.For(target.Location.Kind);

        // A bit-rate cap the original already meets needs no conversion.
        var fits = requested is null && offset == 0 && maxBitRate is { } cap && target.BitRate is { } bitRate && bitRate <= cap;
        // A cue track is part of a file: it can only be sent converted, losslessly unless the file is lossy.
        var segment = target.StartMs is not null;
        if (!download && (requested is not null || maxBitRate is not null || offset > 0 || segment) && !fits)
        {
            var profile = requested ?? TranscodeProfile.For(segment && maxBitRate is null && AudioFormats.IsLossless(target.RelativePath)
                ? TranscodeFormat.Flac
                : TranscodeFormat.Mp3);
            var remaining = Math.Max(0, target.DurationMs / 1000.0 - offset);
            if (HttpMethods.IsHead(context.Request.Method))
            {
                context.Response.Headers.CacheControl = "private, no-store";
                context.Response.ContentType = profile.ContentType;
                return Results.Empty;
            }

            var session = await transcoder.StartAsync(
                storage.TryGetLocalPath(target.Location, target.RelativePath),
                () => storage.OpenReadAsync(target.Location, target.RelativePath, cancellationToken),
                profile,
                profile.Clamp(maxBitRate ?? (segment ? 320 : null)),
                (target.StartMs ?? 0) / 1000.0 + offset,
                cancellationToken,
                segment ? remaining : null);

            return session is null
                ? Problems.Create(StatusCodes.Status503ServiceUnavailable, Application.ErrorCodes.TranscodingUnavailable)
                : new TranscodeResult(session, profile.ContentType, remaining);
        }

        Stream stream;
        try
        {
            stream = await storage.OpenReadAsync(target.Location, target.RelativePath, cancellationToken);
        }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
        {
            // Deleted since the last scan, or the share is gone.
            return Problems.Create(StatusCodes.Status404NotFound, Application.ErrorCodes.NotFound);
        }

        // Private: the response depends on who is asking.
        context.Response.Headers.CacheControl = "private, max-age=3600";

        return Results.Stream(
            stream,
            target.ContentType,
            fileDownloadName: download ? target.FileName : null,
            lastModified: target.ModifiedAt,
            entityTag: new EntityTagHeaderValue($"\"{target.ContentHash}\""),
            enableRangeProcessing: true);
    }
}
