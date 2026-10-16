using Arcanist.Core.Media;

namespace Arcanist.Core.Data.Entities;

/// <summary>What happens to a dropped file that is a song the library already has.</summary>
public enum DuplicatePolicy
{
    /// <summary>The file stays in the drop folder, listed as a duplicate.</summary>
    Skip,

    /// <summary>The file replaces the library's copies when its quality is better (they go to the trash); otherwise it is skipped.</summary>
    KeepBest,

    /// <summary>The file is added as another copy of the same track.</summary>
    AddVersion,
}

/// <summary>
/// A folder whose files are imported into a library: identified by their tags, converted if asked, and moved to the
/// path the library's template gives them. Always a local folder.
/// </summary>
public sealed class DropFolder : Entity
{
    /// <summary>Absolute path of the folder on the server.</summary>
    public required string Path { get; set; }

    /// <summary>Where imported files go: a location of the target library.</summary>
    public Guid LibrarySourceId { get; set; }

    public LibrarySource LibrarySource { get; set; } = null!;

    public bool Enabled { get; set; } = true;

    public DuplicatePolicy DuplicatePolicy { get; set; }

    /// <summary>Format files are converted to before importing; null keeps them as they are.</summary>
    public TranscodeFormat? ConvertTo { get; set; }

    /// <summary>Bit rate for lossy <see cref="ConvertTo"/> formats, kbit/s; null for the format's default.</summary>
    public int? ConvertBitRate { get; set; }

    /// <summary>
    /// Also convert lossy files in another format (losing some quality). Lossless files are always converted to a
    /// <see cref="ConvertTo"/> format; FLAC never converts lossy files, since that cannot bring quality back.
    /// </summary>
    public bool ConvertLossy { get; set; }

    /// <summary>What happens to a file once imported: it leaves the drop folder (the default) or stays there.</summary>
    public DropFolderMode Mode { get; set; }

    /// <summary>
    /// A folder of the server, or the linked account's Cloud Drop: its songs are downloaded to <see cref="Path"/> (a
    /// folder of the server's own) and imported from there; <see cref="Mode"/> then says whether they leave Cloud Drop.
    /// </summary>
    public DropFolderKind Kind { get; set; }

    /// <summary>
    /// Cloud Drop only: once a song it brought is in the library and identified (AcoustID or MusicBrainz), it is
    /// uploaded to the account's library, with the catalog's description.
    /// </summary>
    public bool UploadToCloud { get; set; }

    /// <summary>
    /// The Ultimate Cloud account (its subject) whose library receives the uploads. Nothing is uploaded while another
    /// account, or none, is linked; null (folders from before) means the linked one.
    /// </summary>
    public string? UploadAccount { get; set; }

    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
}

public enum DropFolderKind
{
    /// <summary>A folder of the server.</summary>
    Folder,

    /// <summary>The Cloud Drop of the linked Ultimate Cloud account.</summary>
    UltimateCloudDrop,
}

/// <summary>A song of Cloud Drop brought to a Cloud Drop drop folder, and what became of it.</summary>
public sealed class CloudDropFetch : Entity
{
    public Guid DropFolderId { get; init; }

    public DropFolder DropFolder { get; init; } = null!;

    public required string CloudTrackId { get; init; }

    /// <summary>The file's name in the drop folder's staging folder.</summary>
    public required string FileName { get; init; }

    public string? Title { get; set; }

    public CloudDropFetchState State { get; set; }

    /// <summary>Where the drop folder put it in the library (or the library track it duplicated).</summary>
    public Guid? LibrarySourceId { get; set; }

    public string? LibraryPath { get; set; }

    public Guid? TrackId { get; set; }

    /// <summary>Its upload to the account's library, when the folder uploads.</summary>
    public CloudDropUpload? Upload { get; set; }

    public string? UploadedTrackId { get; set; }

    public string? ErrorCode { get; set; }

    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public enum CloudDropUpload
{
    /// <summary>Waiting until the song is identified (and catalogued).</summary>
    Waiting,

    /// <summary>In the account's library.</summary>
    Uploaded,

    /// <summary>The upload failed; tried again later.</summary>
    Failed,
}

public enum CloudDropFetchState
{
    /// <summary>Downloaded, waiting to be imported (or for a review).</summary>
    Staged,

    /// <summary>In the library (or it had it already) and taken off Cloud Drop.</summary>
    Removed,

    /// <summary>In the library (or it had it already), and left in Cloud Drop (the folder copies).</summary>
    Kept,

    /// <summary>An administrator discarded it; it stays in Cloud Drop.</summary>
    Discarded,

    /// <summary>It could not be downloaded; tried again later.</summary>
    Failed,
}

public enum DropFolderMode
{
    /// <summary>Imported files leave the drop folder (to its trash, for 30 days).</summary>
    Move,

    /// <summary>A copy is imported and the file stays; it is imported again only if it changes.</summary>
    Copy,
}

public enum ImportStatus
{
    /// <summary>Waiting to be processed (new, or an administrator asked to import or retry it).</summary>
    Pending,

    /// <summary>Moved into the library.</summary>
    Imported,

    /// <summary>Its tags are not enough to place it: an administrator completes them or imports it as it is.</summary>
    NeedsReview,

    /// <summary>The library already has it.</summary>
    Duplicate,

    Failed,

    /// <summary>An administrator chose to leave it in the drop folder; it is looked at again only if it changes.</summary>
    Ignored,

    /// <summary>An administrator moved it to the drop folder's trash.</summary>
    Discarded,
}

/// <summary>Why an import item is in its state. Part of the API contract.</summary>
public enum ImportReason
{
    /// <summary>It lacks a title, artist or album.</summary>
    MissingTags,

    /// <summary>The library has a file with exactly the same audio.</summary>
    SameAudio,

    /// <summary>The library has the same song of the same album.</summary>
    SameTrack,

    /// <summary>The library has the same song in equal or better quality.</summary>
    NotBetter,

    /// <summary>Imported, and the library's worse copies went to the trash.</summary>
    ReplacedWorse,

    Unreadable,

    /// <summary>It had to be converted but ffmpeg (or its encoder) is not available.</summary>
    TranscodingUnavailable,

    TranscodingFailed,

    /// <summary>Moving it into the library failed (permissions, disk full, the location is offline…).</summary>
    WriteFailed,
}

/// <summary>A file found in a drop folder and what became of it. Imported items are kept as history for a while.</summary>
public sealed class ImportItem : Entity
{
    public Guid DropFolderId { get; init; }

    public DropFolder DropFolder { get; init; } = null!;

    /// <summary>Path inside the drop folder, '/'-separated, as on disk.</summary>
    public required string RelativePath { get; set; }

    public long Size { get; set; }

    public DateTime ModifiedAt { get; set; }

    public ImportStatus Status { get; set; }

    public ImportReason? Reason { get; set; }

    public string? ContentHash { get; set; }

    /// <summary>The tags read from the file (<c>ImportTags</c> as JSON), shown when reviewing it.</summary>
    public string? Tags { get; set; }

    /// <summary>
    /// What an administrator asked for (<c>ImportRequest</c> as JSON): tags to write before importing, and whether
    /// to import even when the file is incomplete or a duplicate. Consumed when the item is processed.
    /// </summary>
    public string? Request { get; set; }

    /// <summary>The library location and path the file went to.</summary>
    public Guid? DestinationSourceId { get; set; }

    public string? DestinationPath { get; set; }

    /// <summary>The library's track this file duplicates (or replaced files of).</summary>
    public Guid? TrackId { get; set; }

    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public enum OrganizeRunKind
{
    /// <summary>"Organize now" from the admin panel.</summary>
    Manual,

    /// <summary>After a scan, for files added to a library that organizes itself.</summary>
    Automatic,
}

public enum OrganizeRunStatus
{
    Running,
    Completed,
    Failed,
    Undoing,
    Undone,
}

/// <summary>Files of a library moved to the paths its template gives them, with every move recorded to undo it.</summary>
public sealed class OrganizeRun : Entity
{
    public Guid LibraryId { get; init; }

    public Library Library { get; init; } = null!;

    public OrganizeRunKind Kind { get; init; }

    public OrganizeRunStatus Status { get; set; }

    public required string Template { get; init; }

    public Guid? RequestedByUserId { get; init; }

    public DateTime StartedAt { get; init; }

    public DateTime? FinishedAt { get; set; }

    public DateTime? UndoneAt { get; set; }

    /// <summary>Files (audio and their companions) to move.</summary>
    public int Total { get; set; }

    public int Moved { get; set; }

    public int Failed { get; set; }

    /// <summary>Why the run failed, as an <c>ErrorCodes</c> value.</summary>
    public string? ErrorCode { get; set; }

    public List<OrganizeMove> Moves { get; } = [];
}

/// <summary>One file moved by an <see cref="OrganizeRun"/>, recorded as soon as it moves.</summary>
public sealed class OrganizeMove : Entity
{
    public Guid RunId { get; init; }

    public OrganizeRun Run { get; init; } = null!;

    /// <summary>Order of the move within the run; undoing goes backwards.</summary>
    public int Sequence { get; init; }

    public Guid LibrarySourceId { get; init; }

    public required string FromPath { get; init; }

    public required string ToPath { get; init; }

    public bool Undone { get; set; }
}
