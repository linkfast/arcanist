using System.Globalization;
using Arcanist.Core.Api.Rest;
using Arcanist.Core.Application;
using Arcanist.Core.Application.Artwork;
using Arcanist.Core.Application.Catalog;
using Arcanist.Core.Application.Listening;
using Arcanist.Core.Application.Server;
using Arcanist.Core.Application.Streaming;
using Arcanist.Core.Data;
using Arcanist.Core.Media;
using Microsoft.EntityFrameworkCore;
using Microsoft.Net.Http.Headers;

namespace Arcanist.Core.Extensions.Subsonic;

internal static partial class SubsonicMethods
{
    /// <summary>
    /// The audio of a song: the original with range support, or converted when the app asks for another format, a
    /// lower bit rate or a start offset (OpenSubsonic <c>transcodeOffset</c>). Streaming never counts as a play.
    /// </summary>
    private static async Task<IResult> StreamAsync(SubsonicCall call, bool download)
    {
        var parameters = call.Parameters;
        var id = parameters.RequiredId("id");
        if (download)
            return await MediaResponses.StreamAsync(call.Http, id, null, true, null, null, null, call.Cancellation);

        var requested = parameters["format"]?.ToLowerInvariant();
        TranscodeFormat? format = requested switch
        {
            null or "" or "raw" => null,
            "flac" => TranscodeFormat.Flac,
            "opus" or "ogg" => TranscodeFormat.Opus,
            "aac" or "m4a" => TranscodeFormat.Aac,
            // MP3, or a format Arcanist does not convert to: MP3 plays everywhere.
            _ => TranscodeFormat.Mp3,
        };
        var maxBitRate = requested == "raw" ? null : parameters.Int("maxBitRate") is > 0 and var cap ? cap : (int?)null;
        var offset = parameters.Double("timeOffset") is > 0 and var seconds ? seconds : (double?)null;

        // Asked for the format the file already is: send it as it is.
        if (format is { } wanted && maxBitRate is null && offset is null)
        {
            var target = await call.Service<StreamService>().ResolveAsync(id, null, false, call.Cancellation);
            if (target.StartMs is null && IsAlready(wanted, target.RelativePath))
                format = null;
        }

        // Without ffmpeg, the original is better than nothing; without the encoder asked for, another one.
        var available = call.Service<ServerInfoService>().Get().TranscodingFormats;
        if (available.Count == 0)
            (format, maxBitRate, offset) = (null, null, null);
        else if (format is { } chosen && !available.Contains(chosen))
            format = available.Contains(TranscodeFormat.Mp3) ? TranscodeFormat.Mp3 : available[0];

        return await MediaResponses.StreamAsync(call.Http, id, null, false, format, maxBitRate, offset, call.Cancellation);

        static bool IsAlready(TranscodeFormat format, string path) => (format, Path.GetExtension(path).ToLowerInvariant()) switch
        {
            (TranscodeFormat.Mp3, ".mp3") or (TranscodeFormat.Flac, ".flac") or (TranscodeFormat.Opus, ".opus") => true,
            _ => false,
        };
    }

    /// <summary>
    /// A cover: by the cover art ids Arcanist hands out, or by album, song or artist id (an artist shows one of its
    /// album covers). <c>size</c> is rounded up to the thumbnail sizes.
    /// </summary>
    private static async Task<IResult> GetCoverArtAsync(SubsonicCall call)
    {
        var id = call.Parameters.RequiredId("id");
        var library = new SubsonicLibrary(call);
        var db = library.Db;
        var scope = await library.ScopeAsync(null);

        var artwork = await db.Artworks.AnyAsync(a => a.Id == id, call.Cancellation) ? id : (Guid?)null;
        artwork ??= await scope.Albums.Where(a => a.Id == id).Select(a => a.ArtworkId).FirstOrDefaultAsync(call.Cancellation);
        artwork ??= await scope.Tracks.Where(t => t.Id == id).Select(t => t.ArtworkId ?? t.Album.ArtworkId).FirstOrDefaultAsync(call.Cancellation);
        artwork ??= await scope.Albums.Where(a => a.AlbumArtistId == id && a.ArtworkId != null)
            .OrderByDescending(a => a.Year).ThenBy(a => a.Id).Select(a => a.ArtworkId).FirstOrDefaultAsync(call.Cancellation);
        if (artwork is null)
            throw SubsonicException.NotFound();

        var file = await call.Service<ArtworkService>().GetAsync(artwork.Value, call.Parameters.Int("size") is > 0 and var size ? size : null, call.Cancellation);
        call.Http.Response.Headers.CacheControl = "public, max-age=604800";
        return Results.File(file.Path, file.ContentType, entityTag: new EntityTagHeaderValue(file.ETag));
    }

    /// <summary>Plain lyrics of the first song matching an artist and title; empty when there is none.</summary>
    private static async Task<IResult> GetLyricsAsync(SubsonicCall call)
    {
        var title = call.Parameters["title"];
        var artist = call.Parameters["artist"];
        var node = new SubsonicNode("lyrics");
        if (string.IsNullOrWhiteSpace(title))
            return call.Ok(node);

        var library = new SubsonicLibrary(call);
        var scope = await library.ScopeAsync(null);
        var normalizedTitle = TextNormalizer.Normalize(title);
        var normalizedArtist = string.IsNullOrWhiteSpace(artist) ? null : TextNormalizer.Normalize(artist);
        var trackId = await scope.Tracks
            .Where(t => t.NormalizedTitle == normalizedTitle
                        && library.Db.Lyrics.Any(l => l.TrackId == t.Id && !l.Instrumental)
                        && (normalizedArtist == null || t.Artists.Any(a => a.Artist.NormalizedName == normalizedArtist)))
            .OrderBy(t => t.Id)
            .Select(t => (Guid?)t.Id)
            .FirstOrDefaultAsync(call.Cancellation);
        if (trackId is null)
            return call.Ok(node);

        var track = await library.Catalog.TrackAsync(trackId.Value, call.Cancellation);
        var lyrics = await library.Catalog.LyricsAsync(trackId.Value, call.Cancellation);
        return call.Ok(node.Attr("artist", DisplayArtist(track)).Attr("title", track.Title).Text(lyrics.Plain));
    }

    /// <summary>OpenSubsonic <c>songLyrics</c>: the song's lyrics, synchronized when known.</summary>
    private static async Task<IResult> GetLyricsBySongIdAsync(SubsonicCall call)
    {
        var catalog = call.Service<CatalogService>();
        var track = await catalog.TrackAsync(call.Parameters.RequiredId("id"), call.Cancellation);
        var structured = new List<SubsonicNode>();
        if (track.HasLyrics && await catalog.LyricsAsync(track.Id, call.Cancellation) is { Instrumental: false } lyrics)
        {
            var node = new SubsonicNode("structuredLyrics")
                .Attr("displayArtist", DisplayArtist(track))
                .Attr("displayTitle", track.Title)
                // Unknown language.
                .Attr("lang", "xxx");
            if (lyrics.Synced is { } synced)
                node.Attr("synced", true).List("line", synced.Select(l => new SubsonicNode("line").Attr("start", l.TimeMs).Text(l.Text)));
            else
                node.Attr("synced", false).List("line", (lyrics.Plain ?? "").Split('\n').Select(l => new SubsonicNode("line").Text(l.TrimEnd('\r'))));
            structured.Add(node);
        }

        return call.Ok(new SubsonicNode("lyricsList").List("structuredLyrics", structured));
    }

    private static string DisplayArtist(Track track) =>
        string.Join(", ", track.Artists.Where(a => a.Role == Data.Entities.ArtistRole.Primary).Select(a => a.Name));

    /// <summary>Stars songs. Arcanist stars tracks only: albums and artists are accepted but not stored.</summary>
    private static async Task<IResult> StarAsync(SubsonicCall call, bool starred)
    {
        var library = new SubsonicLibrary(call);
        var scope = await library.ScopeAsync(null);
        var listening = call.Service<ListeningService>();
        foreach (var id in call.Parameters.Ids("id"))
        {
            if (await scope.Tracks.AnyAsync(t => t.Id == id, call.Cancellation))
                await listening.StarTrackAsync(id, starred, call.Cancellation);
            else
                await EnsureAlbumOrArtistAsync(scope, id, call.Cancellation);
        }

        foreach (var id in call.Parameters.Ids("albumId").Concat(call.Parameters.Ids("artistId")))
            await EnsureAlbumOrArtistAsync(scope, id, call.Cancellation);
        return call.Ok();
    }

    /// <summary>
    /// Rates a song 1–5, or clears its rating with 0. With single-star ratings any rating stars it. Album and artist
    /// ratings are not stored.
    /// </summary>
    private static async Task<IResult> SetRatingAsync(SubsonicCall call)
    {
        var id = call.Parameters.RequiredId("id");
        var rating = call.Parameters.Int("rating") ?? throw SubsonicException.Missing("rating");
        if (rating is < 0 or > 5)
            throw SubsonicException.Invalid("rating");

        // With single-star ratings, any rating stars the song: apps only know of five stars.
        if (rating > 0 && call.Service<RatingSettings>().Current == Data.Entities.RatingMode.Star)
            rating = 5;

        var scope = await new SubsonicLibrary(call).ScopeAsync(null);
        if (await scope.Tracks.AnyAsync(t => t.Id == id, call.Cancellation))
            await call.Service<ListeningService>().RateTrackAsync(id, rating == 0 ? null : rating, call.Cancellation);
        else
            await EnsureAlbumOrArtistAsync(scope, id, call.Cancellation);
        return call.Ok();
    }

    private static async Task EnsureAlbumOrArtistAsync(SubsonicScope scope, Guid id, CancellationToken cancellationToken)
    {
        if (!await scope.Albums.AnyAsync(a => a.Id == id, cancellationToken) && !await scope.Artists.AnyAsync(a => a.Id == id, cancellationToken))
            throw SubsonicException.NotFound();
    }

    /// <summary>
    /// Records plays (<c>submission=true</c>, the default; a batch of offline plays with their <c>time</c>), or what is
    /// playing now (<c>submission=false</c>). Plays the app played whole; too old ones are dropped.
    /// </summary>
    private static async Task<IResult> ScrobbleAsync(SubsonicCall call)
    {
        var parameters = call.Parameters;
        var ids = parameters.Ids("id");
        if (ids.Count == 0)
            throw SubsonicException.Missing("id");

        var times = parameters.All("time");
        var client = parameters["c"] is { Length: > 0 } c ? c[..Math.Min(c.Length, 128)] : "Subsonic";
        var now = call.Service<TimeProvider>().GetUtcNow().UtcDateTime;
        var nowPlaying = call.Service<SubsonicNowPlaying>();
        var scope = await new SubsonicLibrary(call).ScopeAsync(null);
        var durations = await scope.Tracks.Where(t => ids.Contains(t.Id)).ToDictionaryAsync(t => t.Id, t => t.DurationMs, call.Cancellation);

        if (parameters.Bool("submission") == false)
        {
            var current = ids[^1];
            if (!durations.TryGetValue(current, out var duration))
                throw SubsonicException.NotFound();
            nowPlaying.Set(call.Access.UserId, call.Access.UserName, client, current, duration, now);
            return call.Ok();
        }

        // In a batch, a song that is gone must not make the app resend the others forever.
        if (ids.Count == 1 && !durations.ContainsKey(ids[0]))
            throw SubsonicException.NotFound();

        var listening = call.Service<ListeningService>();
        for (var i = 0; i < ids.Count; i++)
        {
            if (!durations.TryGetValue(ids[i], out var duration))
                continue;
            var playedAt = i < times.Count && long.TryParse(times[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out var ms)
                ? DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime
                : now;
            try
            {
                await listening.RecordPlayAsync(new RecordPlayInput(ids[i], duration, playedAt, client), call.Cancellation);
            }
            catch (ArcanistException.Invalid)
            {
                // Played too long ago, or in the future by a wrong clock.
            }
        }

        nowPlaying.Clear(call.Access.UserId, client);
        return call.Ok();
    }
}
