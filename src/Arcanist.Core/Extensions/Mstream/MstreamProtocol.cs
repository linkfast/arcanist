using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Arcanist.Core.Auth;
using Microsoft.AspNetCore.DataProtection;

namespace Arcanist.Core.Extensions.Mstream;

/// <summary>An error answered the way mStream does: a status code and <c>{"error": "…"}</c>.</summary>
internal sealed class MstreamException(int status, string message) : Exception(message)
{
    public int Status { get; } = status;

    public static MstreamException NotFound(string message = "Not Found") => new(StatusCodes.Status404NotFound, message);

    public static MstreamException Invalid(string field) => new(StatusCodes.Status400BadRequest, $"\"{field}\" is required");
}

/// <summary>
/// A request body as mStream clients send it: JSON (an object, or a bare array for batches) or an urlencoded form,
/// which the mStream app uses to sign in.
/// </summary>
internal sealed class MstreamBody
{
    private JsonElement? _json;
    private IFormCollection? _form;

    public static async Task<MstreamBody> ReadAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        var body = new MstreamBody();
        if (HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method))
            return body;

        if (request.HasFormContentType)
        {
            body._form = await request.ReadFormAsync(cancellationToken);
            return body;
        }

        try
        {
            using var document = await JsonDocument.ParseAsync(request.Body, cancellationToken: cancellationToken);
            body._json = document.RootElement.Clone();
        }
        catch (JsonException)
        {
            // An empty or malformed body reads as no fields; required ones are reported as missing.
        }

        return body;
    }

    /// <summary>The whole body, for endpoints that take a bare JSON array.</summary>
    public JsonElement? Root => _json;

    private JsonElement? Field(string name) =>
        _json is { ValueKind: JsonValueKind.Object } json && json.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null ? value : null;

    public bool Has(string name) => Field(name) is not null || _form?.ContainsKey(name) == true;

    /// <summary>Present with a null value (e.g. <c>"rating": null</c>), which some endpoints use to clear.</summary>
    public bool IsNull(string name) =>
        _json is { ValueKind: JsonValueKind.Object } json && json.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Null;

    public string? String(string name) => Field(name) switch
    {
        { ValueKind: JsonValueKind.String } value => value.GetString(),
        { ValueKind: JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False } value => value.GetRawText(),
        _ => _form?[name].FirstOrDefault(),
    };

    public string Required(string name) => String(name) is { Length: > 0 } value ? value : throw MstreamException.Invalid(name);

    public int? Int(string name)
    {
        if (Field(name) is { ValueKind: JsonValueKind.Number } number)
            return number.TryGetInt32(out var exact) ? exact : (int)Math.Round(number.GetDouble());
        return int.TryParse(String(name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : null;
    }

    public bool Bool(string name) => Field(name) is { ValueKind: JsonValueKind.True } || String(name) is "true" or "1";

    /// <summary>A list of strings: a JSON array, repeated form fields, or a JSON array encoded in a string (as forms send it).</summary>
    public IReadOnlyList<string> Strings(string name)
    {
        if (Field(name) is { ValueKind: JsonValueKind.Array } array)
            return array.EnumerateArray().Where(v => v.ValueKind == JsonValueKind.String).Select(v => v.GetString()!).ToList();

        List<string> values = Field(name) is { ValueKind: JsonValueKind.String } text ? [text.GetString()!] : _form?[name].OfType<string>().ToList() ?? [];
        if (values is [var single] && single.TrimStart().StartsWith('['))
        {
            try
            {
                return JsonSerializer.Deserialize<List<string>>(single) ?? [];
            }
            catch (JsonException)
            {
                return [];
            }
        }

        return values;
    }
}

/// <summary>
/// mStream tokens. Each names the app password it was signed in with, so it lasts until that password is deleted,
/// as mStream's own tokens last until changed. Protected with the server's Data Protection keys.
/// </summary>
internal sealed class MstreamTokens(IDataProtectionProvider protection)
{
    private IDataProtector Protector => protection.CreateProtector("Arcanist.Mstream.Tokens");

    public string Create(Guid passwordId) => Protector.Protect(passwordId.ToString("N"));

    public Guid? Read(string token)
    {
        try
        {
            return Guid.TryParseExact(Protector.Unprotect(token), "N", out var id) ? id : null;
        }
        catch (CryptographicException)
        {
            return null;
        }
    }
}

/// <summary>One mStream call: its body, route values and the signed-in user.</summary>
internal sealed class MstreamCall(HttpContext http, MstreamBody body, CancellationToken cancellation)
{
    public HttpContext Http { get; } = http;

    public MstreamBody Body { get; } = body;

    public CancellationToken Cancellation { get; } = cancellation;

    public UserAccess Access => Http.Items[typeof(UserAccess)] as UserAccess ?? throw new Application.ArcanistException.Unauthorized();

    public T Service<T>() where T : notnull => Http.RequestServices.GetRequiredService<T>();

    public string? Route(string name) => Http.Request.RouteValues[name] as string;

    public string? Query(string name) => Http.Request.Query[name].FirstOrDefault();

    /// <summary>A JSON body or query value, whichever the client used.</summary>
    public string? Value(string name) => Body.String(name) ?? Query(name);

    public static IResult Json(object? value) => Results.Json(value, Options);

    public static IResult Error(int status, string message) => Results.Json(new Dictionary<string, string> { ["error"] = message }, Options, statusCode: status);

    /// <summary>Property names are written as given: mStream's are kebab-case and snake_case.</summary>
    private static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy = null };
}
