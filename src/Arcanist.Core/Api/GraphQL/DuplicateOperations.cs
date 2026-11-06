using System.ComponentModel;
using Arcanist.Core.Application.Catalog;
using DuplicateStatus = Arcanist.Core.Data.Entities.DuplicateStatus;
using GraphQL;

namespace Arcanist.Core.Api.GraphQL;

public sealed partial class Query
{
    [Name("duplicateCandidates")]
    [Description("Pairs of tracks that may be the same song and were not merged automatically (also the same song on two albums), most similar first. Open ones by default, or DISMISSED or SHARED. Requires MANAGE_LIBRARIES.")]
    public static Task<IReadOnlyList<DuplicatePair>> DuplicateCandidates(DuplicateStatus? status, int? first, int? skip,
        [FromServices] DuplicateService duplicates, CancellationToken cancellationToken) =>
        duplicates.ListAsync(status, first, skip, cancellationToken);

    [Name("trackCopies")]
    [Description("Songs with several files of their own in one library (a FLAC and an MP3), by track id with a cursor. Conversions Arcanist keeps are not copies. Requires MANAGE_LIBRARIES.")]
    public static Task<TrackCopiesPage> TrackCopies(LibraryDuplicatesInput? input, [FromServices] LibraryDuplicateService duplicates, CancellationToken cancellationToken) =>
        duplicates.CopiesAsync(input ?? new LibraryDuplicatesInput(null, null, null), cancellationToken);

    [Name("libraryDuplicates")]
    [Description("Songs with copies in more than one library (personal libraries aside), by track id with a cursor; with `libraryId`, those that library has among others. Each library says whether the caller can delete its copies (deleteTrack with libraryId) and the users who would stop having the song if it did. Requires MANAGE_LIBRARIES.")]
    public static Task<LibraryDuplicatePage> LibraryDuplicates(LibraryDuplicatesInput? input, [FromServices] LibraryDuplicateService duplicates,
        CancellationToken cancellationToken) =>
        duplicates.ListAsync(input ?? new LibraryDuplicatesInput(null, null, null), cancellationToken);
}

public sealed partial class Mutation
{
    [Name("mergeDuplicate")]
    [Description("Merges an open pair into keepTrackId, one of its two tracks, and returns it. Requires MANAGE_LIBRARIES.")]
    public static Task<Track> MergeDuplicate([Id] Guid id, [Id] Guid keepTrackId, [FromServices] DuplicateService duplicates, CancellationToken cancellationToken) =>
        duplicates.MergeAsync(id, keepTrackId, cancellationToken);

    [Name("dismissDuplicate")]
    [Description("Keeps the two tracks of an open pair apart; it is not proposed again. Requires MANAGE_LIBRARIES.")]
    public static async Task<bool> DismissDuplicate([Id] Guid id, [FromServices] DuplicateService duplicates, CancellationToken cancellationToken)
    {
        await duplicates.DismissAsync(id, cancellationToken);
        return true;
    }

    [Name("shareDuplicateFile")]
    [Description("Both tracks of an open pair stay on their albums and play fileId, one of the pair's shareableFileIds; the other's copy in that folder goes to the trash. Requires MANAGE_LIBRARIES and DELETE_TRACKS.")]
    public static async Task<bool> ShareDuplicateFile([Id] Guid id, [Id] Guid fileId, [FromServices] DuplicateService duplicates, CancellationToken cancellationToken)
    {
        await duplicates.ShareFileAsync(id, fileId, cancellationToken);
        return true;
    }

    [Name("deleteTrackFile")]
    [Description("Moves one copy of a song to the trash; the song stays with the others (last_copy when it has none). Requires DELETE_TRACKS.")]
    public static async Task<bool> DeleteTrackFile([Id] Guid fileId, [FromServices] TrackEditService edits, CancellationToken cancellationToken)
    {
        await edits.DeleteFileAsync(fileId, cancellationToken);
        return true;
    }

    [Name("rescanDuplicates")]
    [Description("Looks through the whole catalog again, in the background, for pairs not proposed yet. Requires MANAGE_LIBRARIES.")]
    public static async Task<bool> RescanDuplicates([FromServices] DuplicateService duplicates, CancellationToken cancellationToken)
    {
        await duplicates.RescanAsync(cancellationToken);
        return true;
    }
}
