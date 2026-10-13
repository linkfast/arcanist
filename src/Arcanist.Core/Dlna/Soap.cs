using System.Net.Http.Headers;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Arcanist.Core.Application;

namespace Arcanist.Core.Dlna;

/// <summary>A UPnP action call: its name and arguments.</summary>
internal sealed record SoapAction(string Name, IReadOnlyDictionary<string, string> Arguments)
{
    public string Argument(string name) => Arguments.GetValueOrDefault(name) ?? "";
}

/// <summary>A UPnP error answered to a control point.</summary>
internal sealed class UpnpException(int code, string description) : Exception(description)
{
    public int Code { get; } = code;
}

/// <summary>SOAP as UPnP uses it, both ways: actions devices send to Arcanist, and actions Arcanist sends to renderers.</summary>
internal static class Soap
{
    private static readonly XNamespace Envelope = "http://schemas.xmlsoap.org/soap/envelope/";

    public static async Task<SoapAction> ReadAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var document = await XDocument.LoadAsync(request.Body, LoadOptions.None, cancellationToken);
            var action = document.Root?.Element(Envelope + "Body")?.Elements().FirstOrDefault()
                         ?? throw new UpnpException(401, "Invalid Action");
            return new SoapAction(action.Name.LocalName,
                action.Elements().GroupBy(e => e.Name.LocalName).ToDictionary(g => g.Key, g => g.First().Value));
        }
        catch (XmlException)
        {
            throw new UpnpException(401, "Invalid Action");
        }
    }

    public static IResult Response(string serviceType, string action, IEnumerable<(string Name, string Value)> values)
    {
        var builder = new StringBuilder();
        builder.Append("<?xml version=\"1.0\" encoding=\"utf-8\"?>")
            .Append("<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\" s:encodingStyle=\"http://schemas.xmlsoap.org/soap/encoding/\"><s:Body>")
            .Append($"<u:{action}Response xmlns:u=\"{serviceType}\">");
        foreach (var (name, value) in values)
            builder.Append('<').Append(name).Append('>').Append(Escape(value)).Append("</").Append(name).Append('>');
        builder.Append($"</u:{action}Response></s:Body></s:Envelope>");
        return Results.Text(builder.ToString(), "text/xml; charset=\"utf-8\"", Encoding.UTF8);
    }

    public static IResult Fault(UpnpException error) => Results.Text(
        "<?xml version=\"1.0\" encoding=\"utf-8\"?>" +
        "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\" s:encodingStyle=\"http://schemas.xmlsoap.org/soap/encoding/\"><s:Body>" +
        "<s:Fault><faultcode>s:Client</faultcode><faultstring>UPnPError</faultstring><detail>" +
        $"<UPnPError xmlns=\"urn:schemas-upnp-org:control-1-0\"><errorCode>{error.Code}</errorCode><errorDescription>{Escape(error.Message)}</errorDescription></UPnPError>" +
        "</detail></s:Fault></s:Body></s:Envelope>",
        "text/xml; charset=\"utf-8\"", Encoding.UTF8, StatusCodes.Status500InternalServerError);

    public static string Escape(string value) => new XText(value).ToString();

    /// <summary>Calls an action on a device and returns its output arguments.</summary>
    /// <exception cref="ArcanistException">renderer_unreachable when the device does not answer, renderer_failed when it refuses.</exception>
    public static async Task<Dictionary<string, string>> InvokeAsync(
        HttpClient client, Uri control, string serviceType, string action, IEnumerable<(string Name, string Value)> arguments,
        CancellationToken cancellationToken)
    {
        var body = new StringBuilder()
            .Append("<?xml version=\"1.0\" encoding=\"utf-8\"?>")
            .Append("<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\" s:encodingStyle=\"http://schemas.xmlsoap.org/soap/encoding/\"><s:Body>")
            .Append($"<u:{action} xmlns:u=\"{serviceType}\">");
        foreach (var (name, value) in arguments)
            body.Append('<').Append(name).Append('>').Append(Escape(value)).Append("</").Append(name).Append('>');
        body.Append($"</u:{action}></s:Body></s:Envelope>");

        using var request = new HttpRequestMessage(HttpMethod.Post, control)
        {
            Content = new StringContent(body.ToString(), Encoding.UTF8),
        };
        request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse("text/xml; charset=\"utf-8\"");
        request.Headers.TryAddWithoutValidation("SOAPACTION", $"\"{serviceType}#{action}\"");

        HttpResponseMessage response;
        string text;
        try
        {
            response = await client.SendAsync(request, cancellationToken);
            text = await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            throw new ArcanistException.Conflict(ErrorCodes.RendererUnreachable);
        }

        try
        {
            var document = XDocument.Parse(text);
            if (!response.IsSuccessStatusCode)
                throw new ArcanistException.Conflict(ErrorCodes.RendererFailed);
            var result = document.Root?.Element(Envelope + "Body")?.Elements().FirstOrDefault();
            return result?.Elements().GroupBy(e => e.Name.LocalName).ToDictionary(g => g.Key, g => g.First().Value) ?? [];
        }
        catch (XmlException)
        {
            throw new ArcanistException.Conflict(ErrorCodes.RendererFailed);
        }
    }
}
