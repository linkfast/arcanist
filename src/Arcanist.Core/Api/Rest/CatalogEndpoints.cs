using Arcanist.Core.Application;
using Arcanist.Core.Application.Catalog;

namespace Arcanist.Core.Api.Rest;

public static class CatalogEndpoints
{
    public static IEndpointRouteBuilder MapCatalogEndpoints(this IEndpointRouteBuilder rest)
    {
        var catalog = rest.MapGroup("").WithTags("Catalog");

        catalog.MapGet("/artists", (string? search, string? after, int? first, Guid? libraryId, CatalogService service, CancellationToken ct) =>
                service.ArtistsAsync(search, after, first, ct, libraryId))
            .WithName("Artists")
            .WithSummary("Performers and album artists with music the user can play, by name; optionally only those with music in a library. Pages of `first` items (default 50, at most 500) after the `after` cursor.");

        catalog.MapGet("/artists/{id:guid}", (Guid id, CatalogService service, CancellationToken ct) => service.ArtistAsync(id, ct))
            .WithName("Artist");

        catalog.MapGet("/albums", (string? search, Guid? artistId, string? after, int? first, Guid? libraryId, string? sort, int? seed, CatalogService service,
                CancellationToken ct) =>
                service.AlbumsAsync(search, artistId, after, first, ct, libraryId,
                    sort is null ? AlbumSort.Title
                    : Enum.TryParse<AlbumSort>(sort.Replace("_", ""), ignoreCase: true, out var parsed) ? parsed
                    : throw ArcanistException.Field("sort", ErrorCodes.InvalidFormat), seed))
            .WithName("Albums")
            .WithSummary("Albums with music the user can play, by title (or `sort`: `RECENTLY_ADDED` first, or `RANDOM`, the same order all day for the user and `seed`); optionally only those of an artist, or with music in a library.");

        catalog.MapGet("/albums/{id:guid}", (Guid id, CatalogService service, CancellationToken ct) => service.AlbumAsync(id, ct))
            .WithName("Album");

        catalog.MapGet("/tracks", (string? search, Guid? albumId, Guid? artistId, string? after, int? first, Guid? libraryId, DateTime? changedSince,
                CatalogService service, CancellationToken ct) =>
                service.TracksAsync(search, albumId, artistId, after, first, ct, libraryId, changedSince))
            .WithName("Tracks")
            .WithSummary("Tracks the user can play, by title; optionally only those with a copy in a library. With albumId, all of the album's tracks in disc and track order, without paging. With `changedSince` (an earlier page's `serverTime`), only the tracks the catalog changed after it (tags, copies) or the user rated or played after it; the ones that left are found with trackIds.");

        catalog.MapGet("/tracks/ids", (Guid? libraryId, CatalogService service, CancellationToken ct) => service.TrackIdsAsync(libraryId, ct))
            .WithName("TrackIds")
            .WithSummary("The ids of every track the user can play (optionally of one library), at once and sorted: what a client compares with its own to see which tracks left the server.");

        catalog.MapPost("/tracks/edit", (EditTracksInput input, TrackEditService service, CancellationToken ct) => service.EditManyAsync(input, ct))
            .WithName("EditTracks")
            .WithSummary("Applies the same tag changes to several tracks (at most 1000), as editTrack does. Returns them as they are now, in the order asked. Requires EDIT_METADATA.");

        catalog.MapPost("/tracks/delete", (DeleteTracksInput input, TrackEditService service, CancellationToken ct) => service.DeleteManyAsync(input, ct))
            .WithName("DeleteTracks")
            .WithSummary("Deletes tracks (at most 1000), or only their copies in `libraryId`: their files go to the trash of their locations (purged after 30 days) and they leave the catalog with their ratings, plays and playlist entries. One outcome per track. Requires DELETE_TRACKS.");

        catalog.MapPost("/tracks/match", (MatchTracksInput input, TrackMatchService service, CancellationToken ct) => service.MatchAsync(input, ct))
            .WithName("MatchTracks")
            .WithSummary("Finds the server's tracks for an app's own (at most 1000 per call): by audio hash, MusicBrainz recording, AcoustID, and last by title, artist and album.");

        catalog.MapGet("/tracks/{id:guid}", (Guid id, CatalogService service, CancellationToken ct) => service.TrackAsync(id, ct))
            .WithName("Track")
            .WithSummary("A track with everything a properties dialog shows: its tags, its copies (library, path, size, format, quality, hash, dates), the user's rating and plays, and the analysis (tempo, key, loudness) when done.");

        catalog.MapMethods("/tracks/{id:guid}", [HttpMethods.Patch], (Guid id, TrackEditInput input, TrackEditService service, CancellationToken ct) =>
                service.EditAsync(id, input, ct))
            .WithName("EditTrack")
            .WithSummary("Changes a track's tags: fields left out stay, empty ones (\"\", [], 0, the nil UUID) are cleared. Written to every copy's file (the original goes to the trash) and read back into the catalog. Returns the track as it is now: its id changes only when the new tags make it the same song as another track of its album, which it joins. Requires EDIT_METADATA and libraries the user adds to; read_only where the server cannot write, cue_sheet_track for tracks of a file split by a cue sheet, tags_unsupported for formats without tags, busy while a scan keeps the library.");

        catalog.MapDelete("/tracks/{id:guid}", async (Guid id, Guid? libraryId, TrackEditService service, CancellationToken ct) =>
            {
                await service.DeleteAsync(id, libraryId, ct);
                return Results.NoContent();
            })
            .WithName("DeleteTrack")
            .WithSummary("Deletes a track (or only its copies in `libraryId`) as deleteTracks does. Requires DELETE_TRACKS.");

        catalog.MapPut("/tracks/{id:guid}/artwork", async (Guid id, HttpContext context, TrackEditService service, CancellationToken ct) =>
                await service.SetTrackArtworkAsync(id, await ReadImageAsync(context, ct), ct))
            .WithName("SetTrackArtwork")
            .WithSummary("Makes the image (the request body: JPEG or PNG, other formats are converted to JPEG; at most 20 MB) the track's cover: embedded in its files as the front cover, replacing their pictures. An album without a cover takes it too. Requires EDIT_METADATA.")
            .Accepts<Stream>("image/jpeg", "image/png", "image/webp", "application/octet-stream");

        catalog.MapDelete("/tracks/{id:guid}/artwork", (Guid id, TrackEditService service, CancellationToken ct) => service.RemoveTrackArtworkAsync(id, ct))
            .WithName("RemoveTrackArtwork")
            .WithSummary("Removes the pictures of the track's files and its own image; it shows its album's cover, if the album has one. Requires EDIT_METADATA.");

        catalog.MapPut("/albums/{id:guid}/artwork", async (Guid id, HttpContext context, TrackEditService service, CancellationToken ct) =>
                await service.SetAlbumArtworkAsync(id, await ReadImageAsync(context, ct), ct))
            .WithName("SetAlbumArtwork")
            .WithSummary("Makes the image (the request body, as for setTrackArtwork) the album's cover, embedded in every file of its tracks. Requires EDIT_METADATA.")
            .Accepts<Stream>("image/jpeg", "image/png", "image/webp", "application/octet-stream");

        catalog.MapDelete("/albums/{id:guid}/artwork", (Guid id, TrackEditService service, CancellationToken ct) => service.RemoveAlbumArtworkAsync(id, ct))
            .WithName("RemoveAlbumArtwork")
            .WithSummary("Removes the album's cover and the pictures of every file of its tracks (a cover image in its folder may bring it back at the next full scan). Requires EDIT_METADATA.");

        catalog.MapGet("/tracks/{trackId:guid}/lyrics", (Guid trackId, CatalogService service, CancellationToken ct) =>
                service.LyricsAsync(trackId, ct))
            .WithName("TrackLyrics")
            .WithSummary("A track's lyrics, with synchronized lines when known. not_found when it has none.");

        catalog.MapGet("/tracks/{trackId:guid}/waveform", (Guid trackId, Guid? fileId, CatalogService service, CancellationToken ct) =>
                service.WaveformAsync(trackId, fileId, ct))
            .WithName("TrackWaveform")
            .WithSummary("Overview of a track's audio for seek bars, from its best copy or the given file. not_found until the copy is analyzed.");

        return rest;
    }

    private static async Task<byte[]> ReadImageAsync(HttpContext context, CancellationToken cancellationToken)
    {
        if (context.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } limit)
            limit.MaxRequestBodySize = TrackEditService.MaxArtworkBytes + 1;
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await context.Request.Body.ReadAsync(chunk, cancellationToken)) > 0)
        {
            buffer.Write(chunk, 0, read);
            if (buffer.Length > TrackEditService.MaxArtworkBytes)
                throw ArcanistException.Field("image", ErrorCodes.TooMany);
        }

        return buffer.ToArray();
    }
}
