namespace Arcanist.Core.Api;

/// <summary>
/// Machine-readable error codes. Clients translate codes; they never display server-provided text.
/// REST responses carry them as RFC 7807 problem details (<c>code</c> extension, and as the values of
/// <c>errors</c> for validation problems); GraphQL carries them as error codes.
/// </summary>
public static class ErrorCodes
{
    public const string SetupRequired = "setup_required";
    public const string SetupCompleted = "setup_completed";
    public const string SetupCodeInvalid = "setup_code_invalid";
    public const string Required = "required";
    public const string InvalidFormat = "invalid_format";
    public const string TooShort = "too_short";
    public const string Duplicate = "duplicate";
    public const string NotFound = "not_found";
    public const string AccessDenied = "access_denied";
    public const string ConnectionFailed = "connection_failed";
    public const string DatabaseNotEmpty = "database_not_empty";
}

public static class Problems
{
    public static IResult Create(int statusCode, string code, string? detail = null) =>
        Results.Problem(statusCode: statusCode, detail: detail, extensions: new Dictionary<string, object?> { ["code"] = code });
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

    public IResult ToResult() => Results.ValidationProblem(ToDictionary());
}
