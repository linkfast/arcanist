namespace Arcanist.Core.Data.Entities;

public enum MstreamImportKind
{
    /// <summary>Read from an mStream installation on this server: its configuration and database.</summary>
    Folder,

    /// <summary>Through the API of an mStream server: users bring their data when they first sign in.</summary>
    Server,
}

public enum MstreamImportStatus
{
    /// <summary>Waiting for the scans of the libraries it uses, to match mStream's files with the catalog.</summary>
    Waiting,

    Running,

    Completed,

    Failed,
}

/// <summary>An import from mStream, kept to finish it in the background and to sign in its users later.</summary>
public sealed class MstreamImport : Entity
{
    public MstreamImportKind Kind { get; init; }

    /// <summary>The mStream folder or server URL.</summary>
    public required string Source { get; init; }

    public MstreamImportStatus Status { get; set; }

    public Guid? RequestedByUserId { get; init; }

    public DateTime CreatedAt { get; init; }

    public DateTime? FinishedAt { get; set; }

    /// <summary>Which library folder each mStream vpath became, as JSON (<c>{"vpath": "sourceId"}</c>).</summary>
    public required string VpathMap { get; set; }

    /// <summary>Which Arcanist user each mStream user became, as JSON (<c>{"name": "userId"}</c>).</summary>
    public required string UserMap { get; set; }

    public int LibrariesCreated { get; set; }

    public int UsersCreated { get; set; }

    public int Playlists { get; set; }

    public int Ratings { get; set; }

    public int Plays { get; set; }

    /// <summary>Playlist entries and track data whose file was not found in the catalog.</summary>
    public int Unmatched { get; set; }

    /// <summary>Why it failed, as an <c>ErrorCodes</c> value.</summary>
    public string? ErrorCode { get; set; }
}
