using System.ComponentModel;
using Arcanist.Core.Application.Catalog;
using Arcanist.Core.Application.Libraries;
using GraphQL;

namespace Arcanist.Core.Api.GraphQL;

public sealed partial class Query
{
    [Name("trackIds")]
    [Description("The ids of every track the user can play (optionally of one library), at once and sorted: what a client compares with its own to see which tracks left the server.")]
    public static Task<TrackIdList> TrackIds([Id] Guid? libraryId, [FromServices] CatalogService catalog, CancellationToken cancellationToken) =>
        catalog.TrackIdsAsync(libraryId, cancellationToken);

    [Name("myStorage")]
    [Description("How much music each library the user can access holds (files, tracks, bytes) and the free space of its disk when known, with the totals and the free space where the user's uploads go.")]
    public static Task<MyStorage> MyStorage([FromServices] MyStorageService storage, CancellationToken cancellationToken) =>
        storage.UsageAsync(cancellationToken);

    [Name("myUploadSettings")]
    [Description("The library the user chose for their uploads that name none, and where such an upload goes now (their choice, else the server's default).")]
    public static Task<MyUploadSettings> MyUploadSettings([FromServices] UploadDestinations uploads, CancellationToken cancellationToken) =>
        uploads.MyAsync(cancellationToken);
}

public sealed partial class Mutation
{
    [Name("editTrack")]
    [Description("Changes a track's tags: fields left out stay, empty ones (\"\", [], 0, the nil UUID) are cleared. Written to every copy's file (the original goes to the trash) and read back into the catalog. Returns the track as it is now: its id changes only when the new tags make it the same song as another track of its album, which it joins. Requires EDIT_METADATA and libraries the user adds to; read_only where the server cannot write, cue_sheet_track for tracks of a file split by a cue sheet, tags_unsupported for formats without tags, busy while a scan keeps the library.")]
    public static Task<Track> EditTrack([Id] Guid id, TrackEditInput input, [FromServices] TrackEditService edits, CancellationToken cancellationToken) =>
        edits.EditAsync(id, input, cancellationToken);

    [Name("editTracks")]
    [Description("Applies the same tag changes to several tracks (at most 1000), as editTrack does. Returns them as they are now, in the order asked. Requires EDIT_METADATA.")]
    public static Task<IReadOnlyList<Track>> EditTracks(EditTracksInput input, [FromServices] TrackEditService edits, CancellationToken cancellationToken) =>
        edits.EditManyAsync(input, cancellationToken);

    [Name("deleteTrack")]
    [Description("Deletes a track (or only its copies in `libraryId`) as deleteTracks does. Requires DELETE_TRACKS.")]
    public static async Task<bool> DeleteTrack([Id] Guid id, [Id] Guid? libraryId, [FromServices] TrackEditService edits, CancellationToken cancellationToken)
    {
        await edits.DeleteAsync(id, libraryId, cancellationToken);
        return true;
    }

    [Name("deleteTracks")]
    [Description("Deletes tracks (at most 1000), or only their copies in `libraryId`: their files go to the trash of their locations (purged after 30 days) and they leave the catalog with their ratings, plays and playlist entries. One outcome per track. Requires DELETE_TRACKS.")]
    public static Task<IReadOnlyList<TrackDeletion>> DeleteTracks(DeleteTracksInput input, [FromServices] TrackEditService edits, CancellationToken cancellationToken) =>
        edits.DeleteManyAsync(input, cancellationToken);

    [Name("setTrackArtwork")]
    [Description("Makes an image (base64, or a data URL; JPEG or PNG, other formats are converted to JPEG; at most 20 MB) the track's cover: embedded in its files as the front cover, replacing their pictures. An album without a cover takes it too. Requires EDIT_METADATA. REST takes the bytes as they are: PUT /rest/v1/tracks/{id}/artwork.")]
    public static Task<Track> SetTrackArtwork([Id] Guid id, string image, [FromServices] TrackEditService edits, CancellationToken cancellationToken) =>
        edits.SetTrackArtworkAsync(id, TrackEditService.FromBase64(image), cancellationToken);

    [Name("setAlbumArtwork")]
    [Description("Makes an image (as for setTrackArtwork) the album's cover, embedded in every file of its tracks. Requires EDIT_METADATA.")]
    public static Task<Album> SetAlbumArtwork([Id] Guid id, string image, [FromServices] TrackEditService edits, CancellationToken cancellationToken) =>
        edits.SetAlbumArtworkAsync(id, TrackEditService.FromBase64(image), cancellationToken);

    [Name("removeTrackArtwork")]
    [Description("Removes the pictures of the track's files and its own image; it shows its album's cover, if the album has one. Requires EDIT_METADATA.")]
    public static Task<Track> RemoveTrackArtwork([Id] Guid id, [FromServices] TrackEditService edits, CancellationToken cancellationToken) =>
        edits.RemoveTrackArtworkAsync(id, cancellationToken);

    [Name("removeAlbumArtwork")]
    [Description("Removes the album's cover and the pictures of every file of its tracks (a cover image in its folder may bring it back at the next full scan). Requires EDIT_METADATA.")]
    public static Task<Album> RemoveAlbumArtwork([Id] Guid id, [FromServices] TrackEditService edits, CancellationToken cancellationToken) =>
        edits.RemoveAlbumArtworkAsync(id, cancellationToken);

    [Name("renameLibrary")]
    [Description("Renames a library (only its name). Requires MANAGE_LIBRARIES, or to own it (a personal library). duplicate when another library has that name.")]
    public static Task<Library> RenameLibrary([Id] Guid id, RenameLibraryInput input, [FromServices] LibraryService libraries, CancellationToken cancellationToken) =>
        libraries.RenameAsync(id, input, cancellationToken);

    [Name("setMyUploadSettings")]
    [Description("Chooses the library the user's uploads go to when they name none (one they can upload to: libraryId not_found otherwise); null goes back to the server's default. For apps that use the whole server as one library.")]
    public static Task<MyUploadSettings> SetMyUploadSettings(SetMyUploadSettingsInput input, [FromServices] UploadDestinations uploads,
        CancellationToken cancellationToken) =>
        uploads.SetMyAsync(input, cancellationToken);
}

public sealed partial class Subscription
{
    [Name("catalogChanged")]
    [Description("The catalog changed in libraries the user can access: tracks edited or deleted (with their ids), or a scan that added, updated or lost files (`scanned`). Refresh with tracks(changedSince:) and trackIds.")]
    public static IObservable<CatalogChange> CatalogChanged([FromServices] TrackEditService edits) => edits.WatchCatalog();
}
