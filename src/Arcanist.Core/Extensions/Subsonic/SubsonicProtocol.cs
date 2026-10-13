using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using Microsoft.Extensions.Primitives;

namespace Arcanist.Core.Extensions.Subsonic;

/// <summary>Subsonic error codes; errors are sent with HTTP 200, as clients expect.</summary>
internal static class SubsonicErrors
{
    public const int Generic = 0;
    public const int MissingParameter = 10;
    public const int WrongCredentials = 40;
    public const int TokenNotSupported = 41;
    public const int ConflictingAuthentication = 43;
    public const int InvalidApiKey = 44;
    public const int NotAuthorized = 50;
    public const int NotFound = 70;
}

/// <summary>A Subsonic error answered to the client.</summary>
internal sealed class SubsonicException(int code, string message) : Exception(message)
{
    public int Code { get; } = code;

    public static SubsonicException Missing(string parameter) => new(SubsonicErrors.MissingParameter, $"Required parameter is missing: {parameter}");

    public static SubsonicException Invalid(string parameter) => new(SubsonicErrors.MissingParameter, $"Invalid value for parameter: {parameter}");

    public static SubsonicException NotFound() => new(SubsonicErrors.NotFound, "The requested data was not found");
}

/// <summary>
/// The parameters of a call: the query string and, for form posts (OpenSubsonic <c>formPost</c>), the body. Names are
/// case-sensitive; repeated parameters (<c>id=1&amp;id=2</c>) keep every value.
/// </summary>
internal sealed class SubsonicParameters
{
    private readonly Dictionary<string, List<string>> _values = new(StringComparer.Ordinal);

    public static async Task<SubsonicParameters> ReadAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        var parameters = new SubsonicParameters();
        foreach (var (name, values) in request.Query)
            parameters.Add(name, values);
        if (HttpMethods.IsPost(request.Method) && request.HasFormContentType)
        {
            var form = await request.ReadFormAsync(cancellationToken);
            foreach (var (name, values) in form)
                parameters.Add(name, values);
        }

        return parameters;
    }

    private void Add(string name, StringValues values)
    {
        if (!_values.TryGetValue(name, out var list))
            _values[name] = list = [];
        list.AddRange(values.OfType<string>());
    }

    public bool Has(string name) => _values.ContainsKey(name);

    public string? this[string name] => _values.TryGetValue(name, out var list) && list.Count > 0 ? list[0] : null;

    public IReadOnlyList<string> All(string name) => _values.TryGetValue(name, out var list) ? list : [];

    public string Required(string name) => this[name] is { Length: > 0 } value ? value : throw SubsonicException.Missing(name);

    public int? Int(string name) => this[name] is { Length: > 0 } value
        ? int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) ? number : throw SubsonicException.Invalid(name)
        : null;

    public long? Long(string name) => this[name] is { Length: > 0 } value
        ? long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) ? number : throw SubsonicException.Invalid(name)
        : null;

    public double? Double(string name) => this[name] is { Length: > 0 } value
        ? double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ? number : throw SubsonicException.Invalid(name)
        : null;

    /// <summary><c>true</c>/<c>false</c> in any case, or <c>1</c>/<c>0</c>.</summary>
    public bool? Bool(string name) => this[name] switch
    {
        null or "" => null,
        var value when value.Equals("true", StringComparison.OrdinalIgnoreCase) || value == "1" => true,
        var value when value.Equals("false", StringComparison.OrdinalIgnoreCase) || value == "0" => false,
        _ => throw SubsonicException.Invalid(name),
    };

    /// <summary>An id parameter that must be there. Ids that are not Arcanist ids are simply not found.</summary>
    public Guid RequiredId(string name) => Id(Required(name));

    public IReadOnlyList<Guid> Ids(string name) => All(name).Where(v => v.Length > 0).Select(Id).ToList();

    /// <summary>Reads an id, without the prefixes some clients add to cover art ids.</summary>
    public static Guid Id(string value)
    {
        var dash = value.IndexOf('-');
        if (dash is 2 && value.Length > 3 && !Guid.TryParse(value, out _))
            value = value[3..];
        return Guid.TryParse(value, out var id) ? id : throw SubsonicException.NotFound();
    }
}

internal enum SubsonicFormat
{
    Xml,
    Json,
    Jsonp,
}

/// <summary>Writes the <c>subsonic-response</c> envelope in the format the client asked for.</summary>
internal sealed partial class SubsonicResponder
{
    public const string ApiVersion = "1.16.1";
    private const string Namespace = "http://subsonic.org/restapi";

    private readonly SubsonicFormat _format;
    private readonly string? _callback;
    private readonly string _serverVersion;

    public SubsonicResponder(SubsonicParameters parameters, string serverVersion)
    {
        _serverVersion = serverVersion;
        _format = parameters["f"]?.ToLowerInvariant() switch
        {
            "json" => SubsonicFormat.Json,
            "jsonp" when parameters["callback"] is { } callback && CallbackName().IsMatch(callback) => SubsonicFormat.Jsonp,
            "jsonp" => SubsonicFormat.Json,
            _ => SubsonicFormat.Xml,
        };
        _callback = parameters["callback"];
    }

    public IResult Ok(SubsonicNode? payload = null) => Write("ok", payload);

    public IResult Error(int code, string message) =>
        Write("failed", new SubsonicNode("error").Attr("code", code).Attr("message", message));

    private IResult Write(string status, SubsonicNode? payload)
    {
        var root = new SubsonicNode("subsonic-response")
            .Attr("status", status)
            .Attr("version", ApiVersion)
            .Attr("type", "arcanist")
            .Attr("serverVersion", _serverVersion)
            .Attr("openSubsonic", true)
            .Child(payload);

        using var buffer = new MemoryStream();
        string contentType;
        if (_format == SubsonicFormat.Xml)
        {
            using (var writer = XmlWriter.Create(buffer, new XmlWriterSettings { Encoding = new UTF8Encoding(false) }))
            {
                writer.WriteStartDocument();
                root.WriteXml(writer, Namespace);
                writer.WriteEndDocument();
            }

            contentType = "text/xml; charset=utf-8";
        }
        else
        {
            if (_format == SubsonicFormat.Jsonp)
                buffer.Write(Encoding.UTF8.GetBytes(_callback + "("));
            using (var writer = new Utf8JsonWriter(buffer))
            {
                writer.WriteStartObject();
                writer.WritePropertyName(root.Name);
                root.WriteJson(writer);
                writer.WriteEndObject();
            }

            if (_format == SubsonicFormat.Jsonp)
                buffer.Write(");"u8);
            contentType = _format == SubsonicFormat.Jsonp ? "text/javascript; charset=utf-8" : "application/json; charset=utf-8";
        }

        return Results.Bytes(buffer.ToArray(), contentType);
    }

    /// <summary>A JavaScript identifier path, so the callback cannot inject script.</summary>
    [GeneratedRegex(@"^[A-Za-z_$][\w$]*(\.[A-Za-z_$][\w$]*)*$")]
    private static partial Regex CallbackName();
}
