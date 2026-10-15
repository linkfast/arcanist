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

public enum DlnaImportStatus
{
    Queued,

    /// <summary>Walking the chosen folders of the media server to find their music.</summary>
    Listing,

    /// <summary>Downloading the music into the library.</summary>
    Copying,

    Completed,

    Failed,

    Cancelled,
}

public enum DlnaImportItemStatus
{
    Pending,

    Copied,

    /// <summary>The library has the same audio already.</summary>
    Duplicate,

    Failed,
}

/// <summary>A copy of music from a DLNA/UPnP media server into a library folder, done in the background.</summary>
public sealed class DlnaImport : Entity
{
    /// <summary>The media server's description URL.</summary>
    public required string Location { get; set; }

    public required string ServerName { get; set; }

    /// <summary>The chosen containers of the server, as JSON (<c>[{"id": "…", "title": "…"}]</c>).</summary>
    public required string Containers { get; init; }

    public Guid LibrarySourceId { get; init; }

    public LibrarySource LibrarySource { get; init; } = null!;

    public DlnaImportStatus Status { get; set; }

    public Guid? RequestedByUserId { get; init; }

    public DateTime CreatedAt { get; init; }

    public DateTime? FinishedAt { get; set; }

    public long CopiedBytes { get; set; }

    /// <summary>Why it failed, as an <c>ErrorCodes</c> value.</summary>
    public string? ErrorCode { get; set; }

    public List<DlnaImportItem> Items { get; init; } = [];
}

/// <summary>A piece of music found on the media server, and what became of it.</summary>
public sealed class DlnaImportItem : Entity
{
    public Guid ImportId { get; init; }

    /// <summary>Its id on the media server, which stays when the server restarts (its URL may not).</summary>
    public required string ObjectId { get; init; }

    public required string Url { get; set; }

    /// <summary>The file's extension, with its dot.</summary>
    public required string Extension { get; set; }

    public long? Size { get; set; }

    public required string Title { get; set; }

    public string? Artist { get; set; }

    public string? AlbumArtist { get; set; }

    public string? Album { get; set; }

    public string? Genre { get; set; }

    public int? Year { get; set; }

    public int? TrackNumber { get; set; }

    public int? DiscNumber { get; set; }

    public string? ArtUrl { get; set; }

    public DlnaImportItemStatus Status { get; set; }

    /// <summary>Why it failed, as an <c>ErrorCodes</c> value.</summary>
    public string? Reason { get; set; }

    /// <summary>Where it went in the library folder.</summary>
    public string? DestinationPath { get; set; }
}
