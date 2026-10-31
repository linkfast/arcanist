using System.Text.Json.Nodes;

namespace Arcanist.Extensibility;

// Ultimate Cloud for extensions: the accounts the server links (Arcanist links them; an extension never sees their
// tokens), their API, the stores of what the server keeps about syncing and uploading, and the providers an
// extension registers to take on Ultimate Cloud's paid features (Ultimate Cloud Plus).

/// <summary>An account's Ultimate Cloud plan, as last checked.</summary>
public enum CloudPlan
{
    /// <summary>Not linked, its link expired, or not checked for too long.</summary>
    None,

    /// <summary>Any plan: Ultimate Cloud as a library source, Cloud Drop, statistics.</summary>
    Free,

    /// <summary>The Cloud plan, its payment up to date.</summary>
    Paid,
}

/// <summary>An Ultimate Cloud account linked in this server: the server's own (<c>UserId</c> null) or a user's own.</summary>
/// <param name="Subject">The account's id in Ultimate Cloud.</param>
public sealed record CloudAccount(Guid? UserId, string Subject, CloudPlan Plan);

/// <summary>Ultimate Cloud answered with an error (its <c>extensions.code</c>), or could not be reached (<c>cloud_unreachable</c>).</summary>
public sealed class CloudApiException(string code, string? message = null) : Exception(message ?? code)
{
    public string Code { get; } = code;
}

/// <summary>
/// The Ultimate Cloud accounts this server links, and their API. Get it from the services (<c>IUltimateCloud</c>). Calls
/// go as this server's device, signed in with the account chosen (the server's when the user is null).
/// </summary>
public interface IUltimateCloud
{
    /// <summary>Ultimate Cloud is turned on in this server (its built-in extension); everything is off otherwise.</summary>
    bool Enabled { get; }

    /// <summary>The linked accounts: the server's (when linked) and the users' own.</summary>
    Task<IReadOnlyList<CloudAccount>> AccountsAsync(CancellationToken cancellationToken);

    /// <summary>The plan of the server's account (null) or of a user's own.</summary>
    CloudPlan PlanOf(Guid? userId);

    /// <summary>The best plan among the server's account and its administrators' own: what the server's features may use.</summary>
    CloudPlan ServerPlan { get; }

    /// <summary>Runs a query or mutation of Ultimate Cloud's GraphQL API as an account, and returns its <c>data</c>.</summary>
    /// <exception cref="CloudApiException">It answered with errors, or could not be reached.</exception>
    Task<JsonNode> SendAsync(Guid? userId, string query, object? variables, CancellationToken cancellationToken);

    /// <summary>The user an account (by its subject) is linked as: found, and the user (null for the server's account).</summary>
    Task<(bool Found, Guid? UserId)> AccountOfAsync(string subject, CancellationToken cancellationToken);

    /// <summary>The user the server's account belongs to here: the administrator who linked it, or the first administrator.</summary>
    Task<Guid?> ServerAccountOwnerAsync(CancellationToken cancellationToken);

    /// <summary>An HTTP client for Ultimate Cloud's files (B2 uploads and downloads): no timeout, the caller's cancellation ends it.</summary>
    HttpClient FilesClient();

    /// <summary>Accounts were linked or unlinked, a plan was checked, or Ultimate Cloud was turned on or off.</summary>
    event Action? Changed;
}

// —— What the server keeps about syncing libraries (its tables), for the provider that syncs them. ——

public enum CloudSyncDirection
{
    Off,
    Upload,
    Download,
    Both,
}

public enum CloudPairingState
{
    /// <summary>On both sides (the account had it, or has it under another name).</summary>
    Matched,
    Uploaded,
    Downloaded,

    /// <summary>Only in the library: not uploaded (the library does not upload, or the account is full).</summary>
    LocalOnly,

    /// <summary>Tried and failed (<see cref="CloudPairing.Reason"/>); tried again on the next run.</summary>
    Failed,
}

/// <summary>How a library syncs.</summary>
/// <param name="AccountUserId">Whose account: an administrator's own; the server's when null.</param>
/// <param name="DownloadFolderId">The library folder downloads go to; its first when null.</param>
public sealed record CloudLibrarySync(Guid LibraryId, CloudSyncDirection Mode, Guid? AccountUserId, Guid? DownloadFolderId, bool Requested, DateTime? LastRunAt);

/// <summary>A song of a library and the account's track it is (or why not yet).</summary>
/// <param name="ContentHash">The library's audio hash; null for a track only downloaded from the account so far.</param>
/// <param name="AudioHash">Ultimate's audio hash of it.</param>
/// <param name="MatchKey">Normalized artist, album and title, to pair it by name.</param>
public sealed class CloudPairing
{
    public Guid Id { get; init; } = Guid.CreateVersion7();
    public Guid LibraryId { get; init; }
    public string? ContentHash { get; set; }
    public string? AudioHash { get; set; }
    public string? CloudTrackId { get; set; }
    public string? Path { get; set; }
    public string? MatchKey { get; set; }
    public string Title { get; set; } = "";
    public CloudPairingState State { get; set; }
    public string? Reason { get; set; }
}

/// <summary>The server's records of the libraries' syncs. Get it from the services (<c>ICloudSyncStore</c>).</summary>
public interface ICloudSyncStore
{
    /// <summary>The library that should sync next (asked for, or not synced for <paramref name="interval"/>), if any.</summary>
    Task<CloudLibrarySync?> NextDueAsync(TimeSpan interval, CancellationToken cancellationToken);

    Task<CloudLibrarySync?> GetAsync(Guid libraryId, CancellationToken cancellationToken);

    /// <summary>A run starts: it is no longer asked for.</summary>
    Task StartRunAsync(Guid libraryId, CancellationToken cancellationToken);

    /// <summary>A run ended, early with an error code or not.</summary>
    Task FinishRunAsync(Guid libraryId, string? errorCode, CancellationToken cancellationToken);

    Task<IReadOnlyList<CloudPairing>> PairingsAsync(Guid libraryId, CancellationToken cancellationToken);

    Task<CloudPairing?> PairingAsync(Guid libraryId, string contentHash, CancellationToken cancellationToken);

    /// <summary>Adds or updates a pairing.</summary>
    Task SaveAsync(CloudPairing pairing, CancellationToken cancellationToken);

    Task DeleteAsync(Guid pairingId, CancellationToken cancellationToken);
}

/// <summary>What a sync is doing now.</summary>
/// <param name="Phase"><c>listing</c>, <c>matching</c>, <c>uploading</c> or <c>downloading</c>.</param>
public sealed record CloudSyncActivity(Guid LibraryId, string Phase, int Done, int Total);

/// <summary>
/// Syncs libraries with Ultimate Cloud: registered by an extension, it makes the server offer syncing (the API's
/// <c>setLibraryCloudSync</c>, the panel's) and is told when a library's sync changes.
/// </summary>
public interface ICloudSyncProvider
{
    /// <summary>The run in progress, if any.</summary>
    CloudSyncActivity? Activity { get; }

    /// <summary>Something changed: a sync was turned on, changed or asked for.</summary>
    void Wake();
}

// —— Users' uploads to their own Ultimate Cloud ——

/// <summary>A song a user asked to send to their own Ultimate Cloud library.</summary>
public sealed record MyCloudUpload(Guid Id, Guid UserId, Guid? FileId, string Name);

public enum MyCloudUploadState
{
    Queued,
    Uploading,
    Uploaded,

    /// <summary>The account's library had it: nothing was uploaded.</summary>
    AlreadyInCloud,
    Failed,
}

/// <summary>The server's queue of users' uploads to their own Ultimate Cloud (their history of uploads). Get it from the services.</summary>
public interface IMyCloudUploadQueue
{
    /// <summary>Those a restart interrupted go back to the queue.</summary>
    Task RequeueInterruptedAsync(CancellationToken cancellationToken);

    /// <summary>The oldest waiting, if any.</summary>
    Task<MyCloudUpload?> NextAsync(CancellationToken cancellationToken);

    Task SetAsync(Guid uploadId, MyCloudUploadState state, string? errorCode, string? cloudTrackId, CancellationToken cancellationToken);

    /// <summary>Fails what a user still has waiting (their account takes nothing more for now).</summary>
    Task FailQueuedAsync(Guid userId, string errorCode, CancellationToken cancellationToken);
}

/// <summary>Sends users' songs to their own Ultimate Cloud: registered by an extension, it makes the server take them (<c>uploadTracksToMyCloud</c>).</summary>
public interface IMyCloudUploadProvider
{
    /// <summary>Songs were queued.</summary>
    void Wake();
}

// —— Cloud Drop's songs going up to the account's library ——

/// <summary>A song brought from Cloud Drop, now in a library, waiting to go to the account's library.</summary>
/// <param name="UploadAccount">The account it goes to (its subject); the server's when null.</param>
/// <param name="TrackId">The library's track, once the catalog has it.</param>
/// <param name="Identified">The track's recording has an AcoustID or a MusicBrainz id: it can go.</param>
public sealed record CloudDropUploadCandidate(Guid Id, Guid DropFolderId, string? UploadAccount, string Title, Guid? TrackId, bool Identified);

/// <summary>Cloud Drop's songs to send to the account's library (drop folders that ask for it). Get it from the services.</summary>
public interface ICloudDropUploads
{
    /// <summary>The songs waiting (or that failed), oldest first, at most <paramref name="max"/>.</summary>
    Task<IReadOnlyList<CloudDropUploadCandidate>> WaitingAsync(int max, CancellationToken cancellationToken);

    /// <summary>What became of one: uploaded (its track there) or failed (why).</summary>
    Task SetAsync(Guid id, Guid? trackId, string? cloudTrackId, string? errorCode, CancellationToken cancellationToken);
}

// —— The account's devices, trusted through the P2P network ——

/// <summary>A device of an Ultimate Cloud account, by its P2P node.</summary>
/// <param name="OwnerUserId">The user it belongs to here.</param>
public sealed record CloudDevice(string NodeId, string Name, Guid? OwnerUserId);

/// <summary>This server's P2P node, and the devices Ultimate Cloud vouches for. Get it from the services.</summary>
public interface IP2PDevices
{
    /// <summary>The node, while the P2P network is on and running: its id, its ticket (base64url) and its name.</summary>
    Task<(string NodeId, string Ticket, string Name)?> NodeAsync(CancellationToken cancellationToken);

    /// <summary>The devices trusted because Ultimate Cloud vouched for them (of a user, or all).</summary>
    Task<IReadOnlyList<CloudDevice>> CloudDevicesAsync(Guid? ownerUserId, CancellationToken cancellationToken);

    /// <summary>Makes these the devices Ultimate Cloud vouches for: adds the new, drops the gone (those paired by hand stay as they are).</summary>
    Task ReplaceCloudDevicesAsync(IReadOnlyList<CloudDevice> devices, CancellationToken cancellationToken);
}

// —— The Cloud plan's features, and S3 libraries ——

/// <summary>
/// The paid features of Ultimate Cloud (Cloud plan) an extension provides. Registered, the server offers them as
/// capabilities (<c>CLOUD_SYNC</c>, <c>THIRD_PARTY_STORAGE</c>, <c>CLOUD_CONNECT</c>) when they are on.
/// </summary>
public interface ICloudPlusFeatures
{
    bool CloudSync { get; }

    /// <summary>S3 libraries and backups.</summary>
    bool ThirdPartyStorage { get; }

    bool CloudConnect { get; }

    /// <summary>Users' wish lists sync with their Ultimate Cloud account (announced as <c>WISH_LIST_CLOUD_SYNC</c>).</summary>
    bool WishListSync { get; }
}

/// <summary>
/// Whether S3 libraries may be created and used: registered by an extension. Without one the server offers no S3
/// libraries (managed ones, of a managed extension, are not asked about).
/// </summary>
public interface IS3LibraryPolicy
{
    /// <summary>Null when a new S3 library may be created; otherwise why not (an error code).</summary>
    Task<string?> CanCreateAsync(CancellationToken cancellationToken);

    /// <summary>Whether S3 libraries may be read and written now (they are disabled otherwise).</summary>
    bool CanUse { get; }

    /// <summary>Null when the library may be deleted; otherwise why not (an S3 library is never the only one left).</summary>
    Task<string?> CanDeleteAsync(Guid libraryId, CancellationToken cancellationToken);
}
