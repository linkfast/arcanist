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
        // Counted while the response lasts, so low-priority background work can wait for listeners.
        if (!HttpMethods.IsHead(context.Request.Method) && context.RequestServices.GetService<StreamActivity>() is { } activity)
            context.Response.RegisterForDispose(activity.Begin());

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
        var storage = storages.For(target.Location.Kind);

        // A bit-rate cap the original already meets needs no conversion.
        var fits = requested is null && offset == 0 && maxBitRate is { } cap && target.BitRate is { } bitRate && bitRate <= cap;
        // A cue track is part of a file: it can only be sent converted, losslessly unless the file is lossy.
        var segment = target.StartMs is not null;
        var convert = !download && (requested is not null || maxBitRate is not null || offset > 0 || segment) && !fits;

        // Where the audio is read from: a file here when there is one (ffmpeg seeks in it), or a stream.
        var localPath = storage.TryGetLocalPath(target.Location, target.RelativePath);
        Func<Task<Stream>> open = () => storage.OpenReadAsync(target.Location, target.RelativePath, cancellationToken);

        // Songs of a library another server shares: from the cache when they are in it, otherwise from that server,
        // which converts them or sends the original for this one to convert.
        if (target.Location.Kind == Data.Entities.LibrarySourceKind.Peer)
        {
            // A share revoked (as last known), or whose owner no longer allows a cache, plays nothing from it.
            var peerStorage = (P2P.PeerStorage)storage;
            if (await peerStorage.RemoteAsync(target.Location, cancellationToken) is not { } remote)
                return Problems.Create(StatusCodes.Status404NotFound, Application.ErrorCodes.NotFound);
            var peerCache = context.RequestServices.GetRequiredService<P2P.PeerCache>();
            if (download || !P2P.PeerCache.Allowed(remote.Remote.Rules) || peerCache.Find(target) is not { } cached)
            {
                if (!download)
                    peerCache.Prefetch(target, remote.Remote.Rules);

                // A server that caps the bit rate never sends the original: it always converts.
                var where = context.RequestServices.GetRequiredService<P2P.RemoteLibrariesSettings>().Current.Transcoding;
                var mayConvertHere = convert && remote.Remote.Rules.MaxBitRate is null;
                Func<Task<IResult>> here = () => ConvertAsync(null, () => peerStorage.OpenStreamAsync(target.Location, target.RelativePath, cancellationToken));
                if (mayConvertHere && where == Application.P2P.RemoteTranscoding.Here)
                    return await here();
                return await P2P.PeerStreaming.ProxyAsync(context, target, download, requested, maxBitRate, offset, cancellationToken,
                    mayConvertHere && where == Application.P2P.RemoteTranscoding.Auto ? here : null);
            }

            localPath = cached;
            open = () => Task.FromResult<Stream>(File.OpenRead(cached));
        }

        if (convert)
            return await ConvertAsync(localPath, open);

        // Originals in Ultimate Cloud come from their signed URLs, Range included.
        if (target.Location.Kind == Data.Entities.LibrarySourceKind.UltimateCloud)
        {
            if (!context.RequestServices.GetRequiredService<Cloud.UltimateCloudAccount>().Enabled)
                return Problems.Create(StatusCodes.Status503ServiceUnavailable, Application.ErrorCodes.CloudDisabled);
            return await Cloud.UltimateCloudStreaming.ProxyAsync(context, target, download, cancellationToken);
        }

        Stream stream;
        try
        {
            stream = await open();
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

        async Task<IResult> ConvertAsync(string? inputPath, Func<Task<Stream>> openInput)
        {
            var profile = requested ?? TranscodeProfile.For(segment && maxBitRate is null && AudioFormats.IsLossless(target.RelativePath)
                ? TranscodeFormat.Flac
                : TranscodeFormat.Mp3);
            // HE-AAC needs an encoder most ffmpeg builds lack (a default chosen with one, say): AAC-LC plays the same.
            if (profile.Format == TranscodeFormat.HeAac
                && context.RequestServices.GetRequiredService<FfmpegLocator>().Current is { } ffmpeg && profile.EncoderIn(ffmpeg.Encoders) is null)
                profile = TranscodeProfile.For(TranscodeFormat.Aac);
            var remaining = Math.Max(0, target.DurationMs / 1000.0 - offset);
            var rate = profile.Clamp(maxBitRate ?? (segment ? 320 : null));

            // A whole song converted before (ahead of time, or the last time it was played) is a file: sent with
            // ranges, so players can seek in it. DLNA's raw PCM is never kept: it is as big as a WAV.
            var kept = context.RequestServices.GetService<TranscodeStore>() is { } store && profile.Format is not null ? store : null;
            if (kept is not null && offset == 0 && kept.Find(target.ContentHash, profile, rate) is { } file)
            {
                context.Response.Headers.CacheControl = "private, max-age=3600";
                context.Response.Headers["X-Content-Duration"] = remaining.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
                return Results.File(file, profile.ContentType, lastModified: target.ModifiedAt,
                    entityTag: new EntityTagHeaderValue($"\"{TranscodeStore.Key(target.ContentHash, profile, rate)}\""), enableRangeProcessing: true);
            }

            if (HttpMethods.IsHead(context.Request.Method))
            {
                context.Response.Headers.CacheControl = "private, no-store";
                context.Response.ContentType = profile.ContentType;
                return Results.Empty;
            }

            var session = await transcoder.StartAsync(
                inputPath,
                openInput,
                profile,
                rate,
                (target.StartMs ?? 0) / 1000.0 + offset,
                cancellationToken,
                segment ? remaining : null);

            // Only a whole song is worth keeping: from an offset it is part of one.
            var cache = kept is not null && offset == 0 && context.RequestServices.GetRequiredService<TranscodingSettings>().Current.CacheEnabled
                ? kept.Cache
                : null;
            return session is null
                ? Problems.Create(StatusCodes.Status503ServiceUnavailable, Application.ErrorCodes.TranscodingUnavailable)
                : new TranscodeResult(session, profile.ContentType, remaining, cache, cache is null ? null : TranscodeStore.Key(target.ContentHash, profile, rate));
        }
    }
}
