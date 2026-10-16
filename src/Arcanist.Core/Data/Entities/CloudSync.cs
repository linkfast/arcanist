namespace Arcanist.Core.Data.Entities;

public enum CloudSyncMode
{
    Off,

    /// <summary>What the library has and the account does not goes up to Ultimate Cloud.</summary>
    Upload,

    /// <summary>What the account has and the library does not comes down into the library.</summary>
    Download,

    Both,
}

/// <summary>How a library syncs with the linked Ultimate Cloud account (one row per library that ever synced).</summary>
public sealed class LibraryCloudSync
{
    public Guid LibraryId { get; init; }

    public Library Library { get; init; } = null!;

    public CloudSyncMode Mode { get; set; }

    /// <summary>The library folder downloads go to (its first local folder when null).</summary>
    public Guid? DownloadSourceId { get; set; }

    public DateTime? LastRunAt { get; set; }

    /// <summary>Why the last run stopped early, as an <c>ErrorCodes</c> value.</summary>
    public string? ErrorCode { get; set; }

    /// <summary>A run was asked for and has not started yet.</summary>
    public bool Requested { get; set; }

    /// <summary>
    /// Whose library it syncs with: a user's own Ultimate Cloud account (which needs the Cloud plan); the server's
    /// account when null.
    /// </summary>
    public Guid? AccountUserId { get; set; }
}

public enum CloudSyncItemStatus
{
    /// <summary>The same audio is on both sides.</summary>
    Matched,

    Uploaded,

    Downloaded,

    /// <summary>Only in the library, with the audio hash known (so it is not hashed again).</summary>
    LocalOnly,

    Failed,
}

/// <summary>
/// A song of a library paired with its track in Ultimate Cloud: what a sync did with it, so the next one does not
/// hash, upload or download it again. The library side is the audio's <c>ContentHash</c> (stable when files move);
/// the cloud side, its <c>cloudTrackId</c>.
/// </summary>
public sealed class CloudSyncItem : Entity
{
    public Guid LibraryId { get; init; }

    /// <summary>Arcanist's audio hash of the library's copy.</summary>
    public string? ContentHash { get; set; }

    /// <summary>Ultimate Cloud's audio hash (SHA-1, by ultimate-audio).</summary>
    public string? AudioHash { get; set; }

    public string? CloudTrackId { get; set; }

    public CloudSyncItemStatus Status { get; set; }

    /// <summary>Why it failed, as an <c>ErrorCodes</c> value (or <c>tool:…</c>).</summary>
    public string? Reason { get; set; }

    /// <summary>Normalized artist, album and title, to pair a cloud track with a library song before asking.</summary>
    public string? MatchKey { get; set; }

    /// <summary>For people: the song's title and where it is.</summary>
    public required string Title { get; set; }

    public string? Path { get; set; }

    public DateTime UpdatedAt { get; set; }
}
