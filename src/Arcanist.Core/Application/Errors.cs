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
