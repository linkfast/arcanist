using System.Security.Claims;
using Arcanist.Core.Application.Artwork;
using Arcanist.Core.Application.Streaming;
using Arcanist.Core.Auth;
using Arcanist.Core.Media;
using Arcanist.Core.Storage;
using Microsoft.Net.Http.Headers;

namespace Arcanist.Core.Api.Rest;

public static class MediaEndpoints
{
    private static readonly string[] AudioTypes = ["audio/flac", "audio/ogg", "audio/opus", "audio/mp4", "audio/wav", "application/octet-stream"];

    public static IEndpointRouteBuilder MapMediaEndpoints(this IEndpointRouteBuilder rest)
    {
        rest.MapMethods("/stream/{trackId:guid}", [HttpMethods.Get, HttpMethods.Head], StreamAsync)
            .WithTags("Media")
            .WithName("Stream")
            .WithSummary("Audio of a track. Authenticate with a Bearer token or the `st` parameter of a track's streamUrl. " +
                         "`fileId` picks a specific copy; `download=true` sends the original as an attachment and requires DOWNLOAD. " +
                         "Without `format`, `maxBitRate` or `timeOffset` the original is sent with HTTP range support. `format` " +
                         "(see serverInfo.transcodingFormats) converts it, at `maxBitRate` kbit/s if given; `maxBitRate` alone " +
                         "converts to MP3 only when the original exceeds it. Converted audio has no range support: seek by " +
                         "requesting again with `timeOffset` in seconds. 503 transcoding_unavailable without ffmpeg.")
            .Produces(StatusCodes.Status200OK, typeof(Stream), "audio/mpeg", AudioTypes)
            .Produces(StatusCodes.Status206PartialContent, typeof(Stream), "audio/mpeg", AudioTypes);

        rest.MapGet("/artwork/{id:guid}", ArtworkAsync)
            .WithTags("Media")
            .WithName("Artwork")
            .WithSummary("An album or track image by artworkId. `size` scales it so its longest side fits (64, 128, 256, 512 or 1024). " +
                         "Public and cacheable forever, so it can be used directly in image elements.")
            .Produces(StatusCodes.Status200OK, typeof(Stream), "image/jpeg", "image/png", "image/webp");

        return rest;
    }

    private static async Task<IResult> StreamAsync(
        Guid trackId,
        Guid? fileId,
        bool? download,
        string? format,
        int? maxBitRate,
        double? timeOffset,
        HttpContext context,
        StreamService streams,
        ILibraryStorageResolver storages,
        Transcoder transcoder,
        CancellationToken cancellationToken)
    {
        await AuthenticateStreamTokenAsync(context, cancellationToken);

        // Parsed here rather than bound: query binding of enums is case-sensitive, and the API spells them MP3, OPUS…
        TranscodeFormat? requested = null;
        if (!string.IsNullOrEmpty(format))
        {
            if (!Enum.TryParse<TranscodeFormat>(format, ignoreCase: true, out var parsed) || !Enum.IsDefined(parsed))
                throw Application.ArcanistException.Field("format", Application.ErrorCodes.InvalidFormat);
            requested = parsed;
        }

        var target = await streams.ResolveAsync(trackId, fileId, download == true, cancellationToken);
        var storage = storages.For(target.Location.Kind);
        var offset = Math.Max(0, timeOffset ?? 0);

        // A bit-rate cap the original already meets needs no conversion.
        var fits = requested is null && offset == 0 && maxBitRate is { } cap && target.BitRate is { } bitRate && bitRate <= cap;
        if (download != true && (requested is not null || maxBitRate is not null || offset > 0) && !fits)
        {
            var profile = TranscodeProfile.For(requested ?? TranscodeFormat.Mp3);
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
                profile.Clamp(maxBitRate),
                offset,
                cancellationToken);

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
            fileDownloadName: download == true ? target.FileName : null,
            lastModified: target.ModifiedAt,
            entityTag: new EntityTagHeaderValue($"\"{target.ContentHash}\""),
            enableRangeProcessing: true);
    }

    private static async Task<IResult> ArtworkAsync(Guid id, int? size, HttpContext context, ArtworkService artwork, CancellationToken cancellationToken)
    {
        var file = await artwork.GetAsync(id, size, cancellationToken);
        context.Response.Headers.CacheControl = "public, max-age=31536000, immutable";
        return Results.File(file.Path, file.ContentType, entityTag: new EntityTagHeaderValue(file.ETag));
    }

    /// <summary>Signs the request in with <c>?st=</c> when it has no Bearer token.</summary>
    private static async Task AuthenticateStreamTokenAsync(HttpContext context, CancellationToken cancellationToken)
    {
        if (context.Items.ContainsKey(typeof(UserAccess)) || context.Request.Query[StreamTokens.QueryParameter] is not [{ } token])
            return;

        if (context.RequestServices.GetRequiredService<StreamTokens>().Read(token) is not { } claims)
            return;

        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ArcanistClaims.UserId, claims.UserId.ToString()),
            new Claim(ArcanistClaims.SessionId, claims.SessionId.ToString()),
        ], "stream-token"));

        var access = await context.RequestServices.GetRequiredService<UserAccessCache>().GetUserAsync(claims.UserId, cancellationToken);
        if (access is null || access.IsDisabled
            || !await context.RequestServices.GetRequiredService<UserAccessCache>().IsSessionActiveAsync(claims.SessionId, cancellationToken))
        {
            return;
        }

        context.User = principal;
        context.Items[typeof(UserAccess)] = access;
    }
}
