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
    /// <summary>The extension failed to load, register its services or map its endpoints; see the server log.</summary>
    public const string ExtensionFailed = "extension_failed";
    /// <summary>An extension maps or claims routes outside /ext/&lt;id&gt; and the prefixes kept for compatibility layers.</summary>
    public const string RouteNotAllowed = "route_not_allowed";
    /// <summary>Another enabled extension already claims one of the extension's route prefixes.</summary>
    public const string RouteConflict = "route_conflict";
    /// <summary>The setting comes from the environment (variables, appsettings, command line): the panel cannot change it.</summary>
    public const string SetByEnvironment = "set_by_environment";
    /// <summary>The limit of items of that kind was reached (e.g. 50 app passwords per user).</summary>
    public const string TooMany = "too_many";
    /// <summary>A playlist holds at most 10,000 tracks.</summary>
    public const string PlaylistFull = "playlist_full";
    /// <summary>Only finished runs can be undone, and only once.</summary>
    public const string NotUndoable = "not_undoable";
    /// <summary>The mStream server did not answer, or not as mStream does.</summary>
    public const string MstreamUnreachable = "mstream_unreachable";
    /// <summary>Reading an mStream server's users and libraries needs an administrator whose admin API is not locked.</summary>
    public const string MstreamAdminRequired = "mstream_admin_required";
    /// <summary>The DLNA extension is off.</summary>
    public const string DlnaDisabled = "dlna_disabled";
    /// <summary>The renderer did not answer: switched off or gone from the network.</summary>
    public const string RendererUnreachable = "renderer_unreachable";
    /// <summary>The renderer refused the request.</summary>
    public const string RendererFailed = "renderer_failed";
    /// <summary>Arcanist has no queue on that renderer.</summary>
    public const string NothingQueued = "nothing_queued";
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
