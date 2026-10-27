using Arcanist.Core.Application.Catalog;
using Arcanist.Core.Application.Videos;
using Arcanist.Core.Data;
using Microsoft.EntityFrameworkCore;

namespace Arcanist.Core.Extensions.Subsonic;

internal static partial class SubsonicMethods
{
    /// <summary>The music videos of the libraries the user can access, by title.</summary>
    private static async Task<IResult> GetVideosAsync(SubsonicCall call)
    {
        var videos = await call.Service<CatalogService>().VisibleVideos(call.Access).AsNoTracking()
            .OrderBy(v => v.NormalizedTitle).ThenBy(v => v.Id)
            .ToListAsync(call.Cancellation);
        return call.Ok(new SubsonicNode("videos").List("video", videos.Select(v => new SubsonicNode("video")
            .Attr("id", v.Id)
            .Attr("isDir", false)
            .Attr("title", v.Title)
            .Attr("artist", v.Artist)
            .Attr("coverArt", v.ArtworkId)
            .Attr("size", v.Size)
            .Attr("contentType", "video/mp4")
            .Attr("suffix", "mp4")
            .Attr("duration", SubsonicLibrary.Seconds(v.DurationMs))
            .Attr("path", v.RelativePath)
            .Attr("isVideo", true)
            .Attr("created", v.AddedAt)
            .Attr("type", "video")
            .Attr("mediaType", "song"))));
    }

    /// <summary>A music video's info: no captions; its audio is the song's or its own, as the stream's <c>audioTrack</c> says.</summary>
    private static async Task<IResult> GetVideoInfoAsync(SubsonicCall call)
    {
        var id = call.Parameters.RequiredId("id");
        if (!await call.Service<CatalogService>().VisibleVideos(call.Access).AnyAsync(v => v.Id == id, call.Cancellation))
            throw new Application.ArcanistException.NotFound();
        return call.Ok(new SubsonicNode("videoInfo").Attr("id", id)
            .List("captions", [])
            .List("audioTrack", [
                new SubsonicNode("audioTrack").Attr("id", "track").Attr("name", "Song"),
                new SubsonicNode("audioTrack").Attr("id", "video").Attr("name", "Video"),
            ])
            .List("conversion", []));
    }

    /// <summary>
    /// A music video as MP4 when <paramref name="id"/> is one (null otherwise): with the user's audio, or the one of
    /// <c>audioTrack</c> (<c>track</c> or <c>video</c>); <c>maxBitRate</c> (kbit/s) and <c>size</c> (<c>WxH</c>) pick
    /// the version, <c>timeOffset</c> starts later. Downloads send it with its own audio.
    /// </summary>
    private static async Task<IResult?> VideoStreamAsync(SubsonicCall call, Guid id, bool download)
    {
        if (!await call.Service<ArcanistDbContext>().MusicVideos.AnyAsync(v => v.Id == id, call.Cancellation))
            return null;
        var parameters = call.Parameters;
        var audio = download ? "video" : parameters["audioTrack"] is "track" or "video" ? parameters["audioTrack"] : null;
        int? maxHeight = parameters["size"]?.Split('x') is [_, var height] && int.TryParse(height, out var h) ? h : null;
        var maxBitRate = parameters.Int("maxBitRate") is > 0 and var rate ? rate * 1000 : (int?)null;
        var offset = parameters.Double("timeOffset") is > 0 and var seconds ? seconds : (double?)null;
        return await call.Service<VideoStreamService>().StreamAsync(call.Http, id, audio, download ? null : maxHeight, download ? null : maxBitRate,
            download ? "native" : null, download ? null : offset, call.Cancellation);
    }
}
