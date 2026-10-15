using Arcanist.Core.Data.Entities;
using Arcanist.Core.Media;

namespace Arcanist.Core.Application.Organization;

// Types in this file are part of the public API contracts (GraphQL schema and OpenAPI document).

/// <summary>How a library's files are organized. Only visible to MANAGE_LIBRARIES.</summary>
/// <param name="Template">Where files go, e.g. <c>{albumartist}/&lt;{year} - &gt;{album}/&lt;{disc}-&gt;&lt;{track} &gt;{title}</c>.</param>
/// <param name="AutoOrganizeSince">Files added since then are organized after each scan.</param>
public sealed record LibraryOrganization(string Template, bool IsDefaultTemplate, bool AutoOrganize, DateTime? AutoOrganizeSince);

/// <param name="Template">Null or empty for the default template.</param>
/// <param name="AutoOrganize">Move files added to the library to their template path after each scan. Existing files
/// are only moved by organizing the library on request.</param>
public sealed record UpdateLibraryOrganizationInput(string? Template, bool AutoOrganize);

/// <summary>What organizing a library would do.</summary>
/// <param name="FilesToMove">Audio files whose path changes.</param>
/// <param name="FilesInPlace">Audio files already where the template puts them.</param>
/// <param name="OtherFilesToMove">Lyrics, cue sheets, artwork and other files that follow their audio.</param>
/// <param name="Moves">The first audio moves, as examples.</param>
/// <param name="UnavailableSourceIds">Locations that could not be read; their files are left out.</param>
public sealed record OrganizePreview(
    string Template,
    int FilesToMove,
    int FilesInPlace,
    int OtherFilesToMove,
    IReadOnlyList<PlannedMove> Moves,
    IReadOnlyList<Guid> UnavailableSourceIds);

/// <summary>A file's path before and after organizing, relative to its library location.</summary>
public sealed record PlannedMove(Guid SourceId, string From, string To);

/// <param name="Total">Files to move, counting those that follow their audio.</param>
/// <param name="Failed">Files that could not be moved (or, after undoing, that could not be moved back).</param>
public sealed record OrganizeRun(
    Guid Id,
    Guid LibraryId,
    OrganizeRunKind Kind,
    OrganizeRunStatus Status,
    string Template,
    DateTime StartedAt,
    DateTime? FinishedAt,
    DateTime? UndoneAt,
    int Total,
    int Moved,
    int Failed,
    string? ErrorCode);

/// <summary>A folder whose files are imported into a library. Administrators only: it reveals server paths.</summary>
/// <param name="LibrarySourceId">The library location imported files go to.</param>
/// <param name="Available">Whether the folder can be read now.</param>
public sealed record DropFolder(
    Guid Id,
    string Path,
    Guid LibraryId,
    Guid LibrarySourceId,
    bool Enabled,
    DuplicatePolicy DuplicatePolicy,
    TranscodeFormat? ConvertTo,
    int? ConvertBitRate,
    bool ConvertLossy,
    DropFolderMode Mode,
    DropFolderKind Kind,
    bool UploadToCloud,
    DateTime CreatedAt,
    bool Available);

/// <param name="Path">Absolute folder path on the server, outside library locations. Not used for Cloud Drop.</param>
/// <param name="Mode">What happens to imported files: they leave the folder (or Cloud Drop), or stay there.</param>
/// <param name="Kind">A folder of the server, or the linked account's Cloud Drop (one at most). Set when the folder is created.</param>
/// <param name="UploadToCloud">Cloud Drop only: upload each song it brings to the account's library once it is in the library and identified.</param>
/// <param name="ConvertTo">Format to convert files to before importing; null keeps them as they are.</param>
/// <param name="ConvertBitRate">kbit/s for lossy formats; null for the format's default.</param>
/// <param name="ConvertLossy">Also convert lossy files in another format, losing some quality. Lossless files are
/// always converted when a format is set; FLAC never converts lossy files.</param>
public sealed record DropFolderInput(
    string? Path,
    Guid LibrarySourceId,
    bool Enabled,
    DuplicatePolicy DuplicatePolicy,
    TranscodeFormat? ConvertTo,
    int? ConvertBitRate,
    bool ConvertLossy,
    DropFolderMode Mode = DropFolderMode.Move,
    DropFolderKind Kind = DropFolderKind.Folder,
    bool UploadToCloud = false);

/// <summary>What was read from a dropped file.</summary>
public sealed record ImportFileDetails(
    string? Title,
    string? Artist,
    string? Album,
    string? AlbumArtist,
    int? Year,
    int? TrackNumber,
    int? DiscNumber,
    string? Genre,
    string? Format,
    int? BitRate,
    int? SampleRate,
    int? BitDepth,
    int DurationMs);

/// <summary>A file found in a drop folder.</summary>
/// <param name="Path">Path inside the drop folder.</param>
/// <param name="DestinationPath">Where it went in the library, once imported.</param>
/// <param name="TrackId">The library's track it duplicates, or whose copies it replaced.</param>
public sealed record ImportItem(
    Guid Id,
    Guid DropFolderId,
    string Path,
    long Size,
    ImportStatus Status,
    ImportReason? Reason,
    ImportFileDetails? Details,
    string? DestinationPath,
    Guid? TrackId,
    DateTime CreatedAt,
    DateTime UpdatedAt);

/// <summary>Tags written to a dropped file before importing it. Null fields are left as they are.</summary>
public sealed record ImportTagsInput(
    string? Title,
    string? Artist,
    string? Album,
    string? AlbumArtist,
    int? Year,
    int? TrackNumber,
    int? DiscNumber,
    string? Genre);

/// <summary>Files of the drop folders by state. <c>Imported</c> counts the history kept (30 days).</summary>
public sealed record ImportSummary(int Pending, int NeedsReview, int Duplicate, int Failed, int Ignored, int Imported);
