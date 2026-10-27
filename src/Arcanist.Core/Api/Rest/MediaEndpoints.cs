using System.Security.Claims;
using Arcanist.Core.Application.Artwork;
using Arcanist.Core.Application.Streaming;
using Arcanist.Core.Auth;
using Arcanist.Core.Media;
using Arcanist.Core.Scanning;
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
                         "requesting again with `timeOffset` in seconds. `client` (DESKTOP, WEB or MOBILE: an Ultimate client) " +
                         "applies that client's default quality (streamingDefaults) when the request asks for none. " +
                         "503 transcoding_unavailable without ffmpeg.")
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
        string? client,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        await AuthenticateStreamTokenAsync(context, cancellationToken);

        // An Ultimate client without a quality of its own: the one the server sets for its kind.
        if (!string.IsNullOrEmpty(client))
        {
            if (!Enum.TryParse<StreamingClient>(client, ignoreCase: true, out var kind) || !Enum.IsDefined(kind))
                throw Application.ArcanistException.Field("client", Application.ErrorCodes.InvalidFormat);
            if (string.IsNullOrEmpty(format) && maxBitRate is null && download != true)
            {
                var quality = (await context.RequestServices.GetRequiredService<StreamingDefaultsSettings>().GetAsync(cancellationToken)).For(kind);
                format = quality.Format?.ToString();
                maxBitRate = quality.MaxBitRate;
            }
        }

        // Parsed here rather than bound: query binding of enums is case-sensitive, and the API spells them MP3, OPUS…
        TranscodeFormat? requested = null;
        if (!string.IsNullOrEmpty(format))
        {
            if (!TranscodeProfile.TryParse(format, out var parsed))
                throw Application.ArcanistException.Field("format", Application.ErrorCodes.InvalidFormat);
            requested = parsed;
        }

        return await MediaResponses.StreamAsync(context, trackId, fileId, download == true, requested, maxBitRate, timeOffset, cancellationToken);
    }

    private static async Task<IResult> ArtworkAsync(Guid id, int? size, HttpContext context, ArtworkService artwork, CancellationToken cancellationToken)
    {
        var file = await artwork.GetAsync(id, size, cancellationToken);
        context.Response.Headers.CacheControl = "public, max-age=31536000, immutable";
        return Results.File(file.Path, file.ContentType, entityTag: new EntityTagHeaderValue(file.ETag));
    }

    /// <summary>Signs the request in with <c>?st=</c> when it has no Bearer token.</summary>
    internal static async Task AuthenticateStreamTokenAsync(HttpContext context, CancellationToken cancellationToken)
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
