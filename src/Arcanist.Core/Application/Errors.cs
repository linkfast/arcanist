namespace Arcanist.Core.Application;

/// <summary>
/// Machine-readable error codes. Clients translate codes; they never display server-provided text.
/// REST responses carry them as RFC 7807 problem details (<c>code</c> extension, and as the values of
/// <c>errors</c> for validation problems); GraphQL carries them as the error's <c>code</c> extension.
/// </summary>
public static class ErrorCodes
{
    public const string SetupRequired = "setup_required";
    public const string SetupCompleted = "setup_completed";
    public const string SetupCodeInvalid = "setup_code_invalid";
    public const string ValidationFailed = "validation_failed";
    public const string Required = "required";
    public const string InvalidFormat = "invalid_format";
    public const string TooShort = "too_short";
    public const string Duplicate = "duplicate";
    public const string NotFound = "not_found";
    public const string AccessDenied = "access_denied";
    public const string ConnectionFailed = "connection_failed";
    public const string DatabaseNotEmpty = "database_not_empty";
    public const string Unauthorized = "unauthorized";
    public const string Forbidden = "forbidden";
    public const string ReauthenticationRequired = "reauthentication_required";
    public const string InvalidCredentials = "invalid_credentials";
    public const string AccountDisabled = "account_disabled";
    public const string InvalidRefreshToken = "invalid_refresh_token";
    public const string TooManyRequests = "too_many_requests";
    public const string LastAdministrator = "last_administrator";
    public const string CannotModifySelf = "cannot_modify_self";
    public const string Overlapping = "overlapping";
    public const string ScanFailed = "scan_failed";
    public const string SourceUnavailable = "source_unavailable";
    public const string Interrupted = "interrupted";
    public const string ScanFinished = "scan_finished";
    /// <summary>The given path is not a working ffmpeg executable.</summary>
    public const string NotFfmpeg = "not_ffmpeg";
    /// <summary>No ffmpeg build can be downloaded for this operating system or processor.</summary>
    public const string FfmpegPlatformUnsupported = "ffmpeg_platform_unsupported";
    public const string FfmpegDownloadFailed = "ffmpeg_download_failed";
    /// <summary>The downloaded ffmpeg did not match its published checksum and was discarded.</summary>
    public const string FfmpegChecksumMismatch = "ffmpeg_checksum_mismatch";
    /// <summary>The requested conversion cannot be done now: no ffmpeg (or no encoder for the format), or too many running.</summary>
    public const string TranscodingUnavailable = "transcoding_unavailable";
    /// <summary>The server cannot create or move files in the location (permissions, or a read-only mount).</summary>
    public const string ReadOnly = "read_only";
    /// <summary>A path template names a placeholder that does not exist.</summary>
    public const string TemplateUnknownPlaceholder = "template_unknown_placeholder";
    /// <summary>The file name part of a path template lacks <c>{title}</c>, so files would collide.</summary>
    public const string TemplateTitleRequired = "template_title_required";
    /// <summary>Files could not be written or moved in a location.</summary>
    public const string WriteFailed = "write_failed";
    /// <summary>The library is already being organized, or a run is being undone.</summary>
    public const string OrganizeRunning = "organize_running";
    /// <summary>The uploaded or downloaded file is not a valid package (not a zip, unsafe paths, too big once unpacked).</summary>
    public const string PackageInvalid = "package_invalid";
    public const string PackageTooLarge = "package_too_large";
    /// <summary>The package has no manifest (arcanist-frontend.json or arcanist-extension.json) at its root.</summary>
    public const string ManifestMissing = "manifest_missing";
    /// <summary>The package's manifest is malformed or misses required fields.</summary>
    public const string ManifestInvalid = "manifest_invalid";
    /// <summary>The package needs a newer API than this server has.</summary>
    public const string ApiVersionUnsupported = "api_version_unsupported";
    /// <summary>Downloading from the given URL failed.</summary>
    public const string DownloadFailed = "download_failed";
    /// <summary>The list of Arcanist's releases could not be read.</summary>
    public const string UpdateCheckFailed = "update_check_failed";
    /// <summary>The server that shares the music does not count this server's plays.</summary>
    public const string PlaysNotAccepted = "plays_not_accepted";
    /// <summary>The extension catalog (the marketplace) could not be read.</summary>
    public const string MarketplaceUnavailable = "marketplace_unavailable";
    /// <summary>The extension needs a newer Arcanist than this one.</summary>
    public const string MarketplaceIncompatible = "marketplace_incompatible";
    /// <summary>The downloaded package is not the one the catalog describes (another SHA-256).</summary>
    public const string PackageChecksumMismatch = "package_checksum_mismatch";
    /// <summary>The downloaded package holds another extension than the catalog says.</summary>
    public const string PackageMismatch = "package_mismatch";
    /// <summary>The extension failed to load, register its services or map its endpoints; see the server log.</summary>
    public const string ExtensionFailed = "extension_failed";
    /// <summary>An extension maps or claims routes outside /ext/&lt;id&gt; and the prefixes kept for compatibility layers.</summary>
    public const string RouteNotAllowed = "route_not_allowed";
    /// <summary>Another enabled extension already claims one of the extension's route prefixes.</summary>
    public const string RouteConflict = "route_conflict";
    /// <summary>The setting comes from the environment (variables, appsettings, command line): the panel cannot change it.</summary>
    public const string SetByEnvironment = "set_by_environment";
    public const string ExtensionManaged = "extension_managed";
    /// <summary>The limit of items of that kind was reached (e.g. 50 app passwords per user).</summary>
    public const string TooMany = "too_many";
    /// <summary>A playlist holds at most 10,000 tracks.</summary>
    public const string PlaylistFull = "playlist_full";
    /// <summary>Only finished runs can be undone, and only once.</summary>
    public const string NotUndoable = "not_undoable";
    /// <summary>The mStream server did not answer, or not as mStream does.</summary>
    public const string MstreamUnreachable = "mstream_unreachable";
    /// <summary>The mStream server no longer takes the session kept to copy an import's files.</summary>
    public const string MstreamSessionExpired = "mstream_session_expired";
    /// <summary>Reading an mStream server's users and libraries needs an administrator whose admin API is not locked.</summary>
    public const string MstreamAdminRequired = "mstream_admin_required";
    /// <summary>The DLNA extension is off.</summary>
    public const string DlnaDisabled = "dlna_disabled";

    /// <summary>The Ultimate Cloud extension is switched off.</summary>
    public const string CloudDisabled = "cloud_disabled";

    /// <summary>The privacy settings keep the server from using outside services.</summary>
    public const string ExternalServicesDisabled = "external_services_disabled";

    /// <summary>The privacy settings keep statistics from leaving the server.</summary>
    public const string TelemetryDisabled = "telemetry_disabled";

    /// <summary>A local library keeps its folder: it can be moved, not removed.</summary>
    public const string LastSource = "last_source";

    /// <summary>The library's music is in Ultimate Cloud or S3 and the Ultimate Cloud extension is off: it can only be deleted.</summary>
    public const string LibraryDisabled = "library_disabled";
    /// <summary>The library is a view of others ("All"): nothing can be uploaded to it or changed in it.</summary>
    public const string LibraryVirtual = "library_virtual";

    /// <summary>A different file with the same name is already where a file had to go.</summary>
    public const string StorageConflict = "storage_conflict";

    /// <summary>The move finished, but some files of the old folder could not be deleted.</summary>
    public const string CleanupIncomplete = "cleanup_incomplete";

    /// <summary>That Ultimate Cloud online service is switched off.</summary>
    public const string CloudServiceOff = "cloud_service_off";
    /// <summary>The renderer did not answer: switched off or gone from the network.</summary>
    public const string RendererUnreachable = "renderer_unreachable";
    /// <summary>The renderer refused the request.</summary>
    public const string RendererFailed = "renderer_failed";
    /// <summary>Arcanist has no queue on that renderer.</summary>
    public const string NothingQueued = "nothing_queued";
    /// <summary>Another user's queue is playing on that renderer: only they (or an administrator) control it until it stops.</summary>
    public const string RendererInUse = "renderer_in_use";
    /// <summary>The server audio extension is off.</summary>
    public const string ServerAudioDisabled = "server_audio_disabled";
    /// <summary>The AI extension is off.</summary>
    public const string AiDisabled = "ai_disabled";
    /// <summary>The song cannot be analyzed: its only copies are in sources analyzed on request while that is off, or none can be read.</summary>
    public const string AnalysisUnavailable = "analysis_unavailable";
    /// <summary>The server cannot play audio: its native player could not load (ALSA missing on Linux) or no device opens.</summary>
    public const string ServerAudioUnavailable = "server_audio_unavailable";
    /// <summary>The P2P network is off.</summary>
    public const string P2PDisabled = "p2p_disabled";
    /// <summary>The P2P library is missing for this platform, so the network cannot run.</summary>
    public const string P2PUnavailable = "p2p_unavailable";
    /// <summary>The P2P node could not open its UDP port.</summary>
    public const string P2PBindFailed = "p2p_bind_failed";
    /// <summary>The other node did not answer through the P2P network.</summary>
    public const string PeerUnreachable = "peer_unreachable";
    /// <summary>That node is not paired with this server.</summary>
    public const string PeerUnknown = "peer_unknown";
    /// <summary>The access of that node was revoked.</summary>
    public const string PeerRevoked = "peer_revoked";
    /// <summary>The access of that node ended.</summary>
    public const string PeerExpired = "peer_expired";
    /// <summary>The invitation is not valid, was used already or expired.</summary>
    public const string InvitationInvalid = "invitation_invalid";
    /// <summary>Too many audio streams at once for that peer, or for all peers.</summary>
    public const string TooManyStreams = "too_many_streams";
    /// <summary>The owner of those files does not allow downloading them.</summary>
    public const string DownloadNotAllowed = "download_not_allowed";
    /// <summary>The server is in maintenance (its database is being migrated): try again in a moment.</summary>
    public const string Maintenance = "maintenance";
    /// <summary>Work in progress (scans, organizing, imports, copies) must finish first.</summary>
    public const string Busy = "busy";
    /// <summary>The database is PostgreSQL already.</summary>
    public const string AlreadyPostgres = "already_postgres";
    /// <summary>The copied data does not match the original: nothing was switched.</summary>
    public const string VerificationFailed = "verification_failed";
    /// <summary>Copying the data failed; the server keeps its database.</summary>
    public const string MigrationFailed = "migration_failed";
    /// <summary>The DLNA media server did not answer.</summary>
    public const string DlnaServerUnreachable = "dlna_server_unreachable";
    /// <summary>The address does not describe a DLNA/UPnP media server.</summary>
    public const string NotMediaServer = "not_media_server";
    /// <summary>The file could not be read as audio.</summary>
    public const string Unreadable = "unreadable";
    /// <summary>Only queued, copying or listing imports can be cancelled; only finished ones retried.</summary>
    public const string ImportNotActive = "import_not_active";
    /// <summary>The import stopped because of an unexpected error; see the server log.</summary>
    public const string ImportFailed = "import_failed";
    /// <summary>This server has no Ultimate Cloud address configured.</summary>
    public const string CloudNotConfigured = "cloud_not_configured";
    /// <summary>No Ultimate Cloud account is linked.</summary>
    public const string CloudNotLinked = "cloud_not_linked";
    /// <summary>Ultimate Cloud could not be reached.</summary>
    public const string CloudUnreachable = "cloud_unreachable";
    /// <summary>The code expired before it was approved, or Ultimate Cloud ended the session: link the account again.</summary>
    public const string CloudLinkExpired = "cloud_link_expired";
    /// <summary>The account refused to link this server.</summary>
    public const string CloudLinkDenied = "cloud_link_denied";
    /// <summary>Ultimate Cloud serves no ultimate-audio tool yet.</summary>
    public const string CloudToolUnavailable = "cloud_tool_unavailable";
    /// <summary>The ultimate-audio tool Ultimate Cloud served is not signed by Ultimate, or was altered: it was not installed.</summary>
    public const string CloudToolInvalid = "cloud_tool_invalid";
    /// <summary>There is no ultimate-audio for this platform, or it speaks a protocol this version of Arcanist does not.</summary>
    public const string CloudToolUnsupported = "cloud_tool_unsupported";
    /// <summary>The ultimate-audio tool is not installed.</summary>
    public const string CloudToolMissing = "cloud_tool_missing";
    /// <summary>Syncing with Ultimate Cloud needs the Cloud plan.</summary>
    public const string CloudSyncUnavailable = "cloud_sync_unavailable";
    /// <summary>Another library already downloads from Ultimate Cloud (only one can, or both would get everything).</summary>
    public const string CloudSyncDownloadTaken = "cloud_sync_download_taken";
    /// <summary>The account's storage is full: nothing more is uploaded.</summary>
    public const string CloudQuotaExceeded = "cloud_quota_exceeded";
    /// <summary>The account is read-only (a lapsed subscription): nothing is uploaded.</summary>
    public const string CloudReadOnly = "cloud_read_only";
    /// <summary>Libraries in S3 need the Cloud plan.</summary>
    public const string S3Unavailable = "s3_unavailable";
    /// <summary>The S3 service did not answer at that address.</summary>
    public const string S3Unreachable = "s3_unreachable";
    /// <summary>The S3 service refused the keys (wrong keys, or no permission on the bucket).</summary>
    public const string S3AccessDenied = "s3_access_denied";
    /// <summary>There is no such bucket on that S3 service (or in that region).</summary>
    public const string S3BucketNotFound = "s3_bucket_not_found";
    /// <summary>A sync stopped because of an unexpected error; see the server log.</summary>
    public const string CloudSyncFailed = "cloud_sync_failed";
    /// <summary>Uploading a file to Ultimate Cloud failed.</summary>
    public const string UploadFailed = "upload_failed";
    /// <summary>The song only exists as part of a file split by a CUE sheet, which cannot go to Ultimate Cloud on its own.</summary>
    public const string CloudUploadCue = "cloud_upload_cue";
    /// <summary>Ultimate Cloud's sign-in does not offer linking with a code for this server.</summary>
    public const string CloudLinkUnsupported = "cloud_link_unsupported";
    /// <summary>Changes in this library cannot be noticed as they happen (it is shared by another server, or in S3): scan it on an interval.</summary>
    public const string WatchUnsupported = "watch_unsupported";
    /// <summary>The backup destination cannot be reached or written (a missing folder, a disconnected drive or share, a server that does not answer).</summary>
    public const string BackupDestinationUnavailable = "backup_destination_unavailable";
    /// <summary>A backup folder cannot be inside a library (scans would find the copies) nor inside the server's data folder.</summary>
    public const string BackupDestinationOverlaps = "backup_destination_overlaps";
    /// <summary>Backing up the server (its database, settings and keys) needs a password of at least 8 characters to encrypt them.</summary>
    public const string BackupPasswordRequired = "backup_password_required";
    /// <summary>The password does not open the backup's encrypted server data.</summary>
    public const string BackupPasswordInvalid = "backup_password_invalid";
    /// <summary>There is no such backup (or snapshot) in the destination.</summary>
    public const string BackupNotFound = "backup_not_found";
    /// <summary>The backup was made by a newer version of Arcanist, or is damaged.</summary>
    public const string BackupFormatUnsupported = "backup_format_unsupported";
    /// <summary>The other server's space for this server's backups is full.</summary>
    public const string BackupQuotaExceeded = "backup_quota_exceeded";
    /// <summary>The file is larger than the destination accepts in one piece (5 GB in S3).</summary>
    public const string BackupFileTooLarge = "backup_file_too_large";
    /// <summary>The backup is already running or queued.</summary>
    public const string BackupBusy = "backup_busy";
    /// <summary>The other server does not keep this server's backups (it never agreed, or stopped).</summary>
    public const string BackupsNotAccepted = "backups_not_accepted";
    /// <summary>The backup failed because of an unexpected error; see the server log.</summary>
    public const string BackupFailed = "backup_failed";
    /// <summary>Only queued or running backups can be cancelled.</summary>
    public const string BackupNotActive = "backup_not_active";
    /// <summary>An import that is still going cannot leave the history: cancel it first.</summary>
    public const string ImportRunning = "import_running";
    /// <summary>Users of that mStream server have not signed in yet, and their first sign-in asks that server through this import.</summary>
    public const string ImportInUse = "import_in_use";
}

/// <summary>Collects field errors as codes, keyed by camelCase field path (e.g. <c>libraries[0].folders[1]</c>).</summary>
public sealed class ValidationErrors
{
    private readonly Dictionary<string, List<string>> _errors = new(StringComparer.Ordinal);

    public bool IsEmpty => _errors.Count == 0;

    public void Add(string field, string code)
    {
        if (!_errors.TryGetValue(field, out var codes))
            _errors[field] = codes = [];

        codes.Add(code);
    }

    public IDictionary<string, string[]> ToDictionary() => _errors.ToDictionary(e => e.Key, e => e.Value.ToArray());

    public void ThrowIfAny()
    {
        if (!IsEmpty)
            throw new ArcanistException.Invalid(this);
    }
}

/// <summary>
/// An expected failure of an application operation. Both APIs translate it: REST into problem details with the
/// matching status, GraphQL into an error with the code in its extensions.
/// </summary>
public abstract class ArcanistException(string code, int statusCode) : Exception(code)
{
    public string Code { get; } = code;

    public int StatusCode { get; } = statusCode;

    public sealed class Unauthorized() : ArcanistException(ErrorCodes.Unauthorized, StatusCodes.Status401Unauthorized);

    public sealed class Forbidden(string code = ErrorCodes.Forbidden) : ArcanistException(code, StatusCodes.Status403Forbidden);

    public sealed class NotFound() : ArcanistException(ErrorCodes.NotFound, StatusCodes.Status404NotFound);

    public sealed class Conflict(string code) : ArcanistException(code, StatusCodes.Status409Conflict);

    /// <summary>The server cannot do it right now (maintenance).</summary>
    public sealed class Unavailable(string code) : ArcanistException(code, StatusCodes.Status503ServiceUnavailable);

    public sealed class Invalid(ValidationErrors errors) : ArcanistException(ErrorCodes.ValidationFailed, StatusCodes.Status400BadRequest)
    {
        public ValidationErrors Errors { get; } = errors;
    }

    public static Invalid Field(string field, string code)
    {
        var errors = new ValidationErrors();
        errors.Add(field, code);
        return new Invalid(errors);
    }
}
