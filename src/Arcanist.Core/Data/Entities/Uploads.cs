namespace Arcanist.Core.Data.Entities;

/// <summary>
/// Something a user sent somewhere, for their history of uploads: a file into a library of this server, or a song
/// of its catalog into their own Ultimate Cloud library (queued, and uploaded in the background).
/// </summary>
public sealed class UserUpload : Entity
{
    public Guid UserId { get; init; }

    public User User { get; init; } = null!;

    public UserUploadKind Kind { get; init; }

    public UserUploadStatus Status { get; set; }

    /// <summary>The file's name (into a library) or the song's title (into Ultimate Cloud).</summary>
    public required string Name { get; set; }

    public long? Size { get; set; }

    /// <summary>The library it went into, or the song's library.</summary>
    public Guid? LibraryId { get; set; }

    /// <summary>The song: once catalogued (into a library), or the one uploaded (into Ultimate Cloud).</summary>
    public Guid? TrackId { get; set; }

    /// <summary>The copy uploaded to Ultimate Cloud (a <see cref="MediaFile"/>).</summary>
    public Guid? FileId { get; set; }

    /// <summary>Arcanist's hash of the audio: finds the song once the library is scanned.</summary>
    public string? ContentHash { get; set; }

    /// <summary>Where it went, relative to the library's folder.</summary>
    public string? Path { get; set; }

    public string? CloudTrackId { get; set; }

    /// <summary>The extension that brought the file (its id, such as <c>youtube</c>); null when the user sent it.</summary>
    public string? Source { get; set; }

    /// <summary>The kind of client that sent it through the upload API, when it said (<c>client=</c>).</summary>
    public Application.Libraries.UploadClient? Client { get; set; }

    public string? ErrorCode { get; set; }

    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public enum UserUploadKind
{
    /// <summary>A file into a library of this server.</summary>
    Library,

    /// <summary>A song of the catalog into the user's own Ultimate Cloud library.</summary>
    UltimateCloud,
}

public enum UserUploadStatus
{
    /// <summary>In the library, waiting for its scan.</summary>
    Processing,

    /// <summary>In the library's catalog.</summary>
    Added,

    /// <summary>The library already had this audio.</summary>
    Duplicate,

    /// <summary>The library was scanned but the song is not in its catalog (moved or deleted since, or unreadable).</summary>
    NotAdded,

    /// <summary>Waiting for its turn to go to Ultimate Cloud.</summary>
    Queued,

    /// <summary>Going to Ultimate Cloud.</summary>
    Uploading,

    /// <summary>In the user's Ultimate Cloud library.</summary>
    Uploaded,

    /// <summary>The user's Ultimate Cloud library already had it.</summary>
    AlreadyInCloud,

    /// <summary>It could not be sent (the error code says why).</summary>
    Failed,
}
