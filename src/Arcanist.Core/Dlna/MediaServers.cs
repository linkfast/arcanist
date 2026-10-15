using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Arcanist.Core.Application;
using Arcanist.Core.Scanning;
using Microsoft.Extensions.Options;

namespace Arcanist.Core.Dlna;

/// <summary>A DLNA/UPnP media server on the network, to copy music from.</summary>
/// <param name="Id">Its UUID, without the <c>uuid:</c> prefix.</param>
internal sealed record MediaServerDevice(string Id, string Name, string? Manufacturer, string? Model, Uri Location, Uri ContentDirectory);

/// <summary>A folder of a media server's tree.</summary>
internal sealed record RemoteContainer(string Id, string Title, int? ChildCount);

/// <summary>A piece of music on a media server: what it says about it, and the file to download.</summary>
/// <param name="Url">The original file (or the best resource the server offers).</param>
/// <param name="Extension">The file's extension, from its MIME type or its URL.</param>
internal sealed record RemoteTrack(
    string Id,
    string Title,
    string? Artist,
    string? AlbumArtist,
    string? Album,
    string? Genre,
    int? Year,
    int? Track,
    int? Disc,
    Uri Url,
    string Extension,
    long? Size,
    Uri? ArtUrl);

/// <summary>One page of a container's children.</summary>
internal sealed record RemotePage(IReadOnlyList<RemoteContainer> Containers, IReadOnlyList<RemoteTrack> Tracks, int Returned, int Total);

/// <summary>
/// Talks to DLNA/UPnP media servers as a control point: finds them with an SSDP search, reads their description and
/// browses their ContentDirectory. Independent of Arcanist's own DLNA server (it works with the extension off).
/// </summary>
internal sealed class MediaServerClient(IHttpClientFactory clients, IOptions<DlnaOptions> options, ILogger<MediaServerClient> logger)
{
    public const string HttpClientName = "dlna-import";

    public const string DownloadClientName = "dlna-download";

    private static readonly XNamespace DeviceNs = "urn:schemas-upnp-org:device-1-0";
    private static readonly XNamespace Didl = "urn:schemas-upnp-org:metadata-1-0/DIDL-Lite/";
    private static readonly XNamespace Dc = "http://purl.org/dc/elements/1.1/";
    private static readonly XNamespace Upnp = "urn:schemas-upnp-org:metadata-1-0/upnp/";

    private HttpClient Client => clients.CreateClient(HttpClientName);

    /// <summary>Asks the local networks for media servers and reads the descriptions of those that answer.</summary>
    public async Task<IReadOnlyList<MediaServerDevice>> SearchAsync(TimeSpan wait, CancellationToken cancellationToken)
    {
        if (!options.Value.Ssdp)
            return [];

        var locations = new HashSet<Uri>();
        var sockets = new List<Socket>();
        try
        {
            foreach (var (address, _, _) in LocalNetwork.Addresses())
            {
                try
                {
                    var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                    if (OperatingSystem.IsWindows())
                    {
                        // SIO_UDP_CONNRESET off: otherwise an ICMP "port unreachable" fails every later receive.
                        socket.IOControl(unchecked((int)0x9800000C), [0, 0, 0, 0], null);
                    }

                    socket.Bind(new IPEndPoint(address, 0));
                    socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastInterface, address.GetAddressBytes());
                    socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastTimeToLive, 4);
                    var search = Encoding.ASCII.GetBytes(Ssdp.Search(Ssdp.MediaServer));
                    // UDP gets lost: ask twice.
                    socket.SendTo(search, new IPEndPoint(Ssdp.Group, Ssdp.Port));
                    socket.SendTo(search, new IPEndPoint(Ssdp.Group, Ssdp.Port));
                    sockets.Add(socket);
                }
                catch (SocketException e)
                {
                    logger.LogDebug("Cannot search for media servers from {Address}: {Error}", address, e.SocketErrorCode);
                }
            }

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(wait);
            await Task.WhenAll(sockets.Select(s => ReceiveAsync(s, locations, timeout.Token)));
        }
        finally
        {
            foreach (var socket in sockets)
                socket.Dispose();
        }

        cancellationToken.ThrowIfCancellationRequested();
        var devices = await Task.WhenAll(locations.Select(async location =>
        {
            // Announced addresses may not be reachable from here (another interface, a firewall): do not wait long.
            using var quick = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            quick.CancelAfter(TimeSpan.FromSeconds(5));
            try
            {
                return await DescribeAsync(location, quick.Token);
            }
            catch (ArcanistException)
            {
                return null;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogDebug("The media server at {Location} did not answer in time", location);
                return null;
            }
        }));
        return devices.OfType<MediaServerDevice>().DistinctBy(d => d.Id).OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static async Task ReceiveAsync(Socket socket, HashSet<Uri> locations, CancellationToken cancellationToken)
    {
        var buffer = new byte[8192];
        while (!cancellationToken.IsCancellationRequested)
        {
            SocketReceiveFromResult received;
            try
            {
                received = await socket.ReceiveFromAsync(buffer, SocketFlags.None, new IPEndPoint(IPAddress.Any, 0), cancellationToken);
            }
            catch (Exception e) when (e is OperationCanceledException or ObjectDisposedException)
            {
                return;
            }
            catch (SocketException)
            {
                // An ICMP error from an earlier send; keep listening until the time is up.
                continue;
            }

            if (Ssdp.Parse(Encoding.ASCII.GetString(buffer, 0, received.ReceivedBytes)) is { IsResponse: true } message
                && message["ST"]?.Contains("MediaServer", StringComparison.OrdinalIgnoreCase) == true
                && message["LOCATION"] is { } location && Uri.TryCreate(location, UriKind.Absolute, out var uri)
                && uri.Scheme is "http" or "https")
            {
                lock (locations)
                    locations.Add(uri);
            }
        }
    }

    /// <summary>Reads the description of the media server at <paramref name="location"/>.</summary>
    /// <exception cref="ArcanistException"><c>dlna_server_unreachable</c>, or <c>not_media_server</c> when it is no media server with a ContentDirectory.</exception>
    public async Task<MediaServerDevice> DescribeAsync(Uri location, CancellationToken cancellationToken)
    {
        if (location.Scheme is not ("http" or "https"))
            throw ArcanistException.Field("location", ErrorCodes.InvalidFormat);

        string xml;
        try
        {
            xml = await Client.GetStringAsync(location, cancellationToken);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            logger.LogDebug("Could not read the media server description at {Location}: {Error}", location, e.Message);
            throw new ArcanistException.Conflict(ErrorCodes.DlnaServerUnreachable);
        }

        try
        {
            return Parse(xml, location) ?? throw new ArcanistException.Conflict(ErrorCodes.NotMediaServer);
        }
        catch (XmlException)
        {
            throw new ArcanistException.Conflict(ErrorCodes.NotMediaServer);
        }
    }

    /// <summary>Reads a device description; null when it describes no media server with a ContentDirectory.</summary>
    internal static MediaServerDevice? Parse(string xml, Uri location)
    {
        var root = XDocument.Parse(xml).Root;
        var baseUri = root?.Element(DeviceNs + "URLBase")?.Value is { Length: > 0 } urlBase && Uri.TryCreate(urlBase, UriKind.Absolute, out var parsed)
            ? parsed
            : location;
        foreach (var device in root?.Descendants(DeviceNs + "device") ?? [])
        {
            var control = device.Element(DeviceNs + "serviceList")?.Elements(DeviceNs + "service")
                .FirstOrDefault(s => s.Element(DeviceNs + "serviceType")?.Value.StartsWith("urn:schemas-upnp-org:service:ContentDirectory:", StringComparison.OrdinalIgnoreCase) == true)
                ?.Element(DeviceNs + "controlURL")?.Value;
            var udn = device.Element(DeviceNs + "UDN")?.Value.Trim();
            if (control is not { Length: > 0 } || string.IsNullOrEmpty(udn) || !Uri.TryCreate(baseUri, control, out var contentDirectory))
                continue;

            return new MediaServerDevice(
                udn.Replace("uuid:", "", StringComparison.OrdinalIgnoreCase),
                device.Element(DeviceNs + "friendlyName")?.Value.Trim() is { Length: > 0 } name ? name : location.Host,
                device.Element(DeviceNs + "manufacturer")?.Value.Trim(),
                device.Element(DeviceNs + "modelName")?.Value.Trim(),
                location,
                contentDirectory);
        }

        return null;
    }

    /// <summary>One page of a container's children (Browse, BrowseDirectChildren).</summary>
    public async Task<RemotePage> BrowseAsync(MediaServerDevice server, string objectId, int start, int count, CancellationToken cancellationToken)
    {
        var result = await Soap.InvokeAsync(Client, server.ContentDirectory, Ssdp.ContentDirectory, "Browse",
            [
                ("ObjectID", objectId), ("BrowseFlag", "BrowseDirectChildren"), ("Filter", "*"),
                ("StartingIndex", start.ToString(CultureInfo.InvariantCulture)), ("RequestedCount", count.ToString(CultureInfo.InvariantCulture)),
                ("SortCriteria", ""),
            ],
            cancellationToken, ErrorCodes.DlnaServerUnreachable, ErrorCodes.DlnaServerUnreachable);

        var (containers, tracks) = ParseDidl(result.GetValueOrDefault("Result") ?? "", server.Location);
        return new RemotePage(containers, tracks, Int(result.GetValueOrDefault("NumberReturned")) ?? containers.Count + tracks.Count,
            Int(result.GetValueOrDefault("TotalMatches")) ?? 0);
    }

    /// <summary>The containers and music of a DIDL-Lite document; other items (video, pictures) are left out.</summary>
    internal static (List<RemoteContainer> Containers, List<RemoteTrack> Tracks) ParseDidl(string didl, Uri baseUri)
    {
        var containers = new List<RemoteContainer>();
        var tracks = new List<RemoteTrack>();
        if (string.IsNullOrWhiteSpace(didl))
            return (containers, tracks);

        XElement root;
        try
        {
            root = XElement.Parse(didl);
        }
        catch (XmlException)
        {
            return (containers, tracks);
        }

        foreach (var element in root.Elements())
        {
            var id = element.Attribute("id")?.Value;
            var title = element.Element(Dc + "title")?.Value.Trim();
            if (string.IsNullOrEmpty(id))
                continue;

            if (element.Name == Didl + "container")
            {
                containers.Add(new RemoteContainer(id, string.IsNullOrEmpty(title) ? id : title, Int(element.Attribute("childCount")?.Value)));
                continue;
            }

            if (element.Name != Didl + "item"
                || element.Element(Upnp + "class")?.Value.StartsWith("object.item.audioItem", StringComparison.OrdinalIgnoreCase) != true
                || Resource(element, baseUri) is not { } resource)
                continue;

            var artists = element.Elements(Upnp + "artist").ToList();
            var creator = element.Element(Dc + "creator")?.Value;
            tracks.Add(new RemoteTrack(
                id,
                string.IsNullOrEmpty(title) ? id : title,
                Text(artists.FirstOrDefault(a => a.Attribute("role") is null || a.Attribute("role")!.Value.Equals("Performer", StringComparison.OrdinalIgnoreCase))?.Value ?? creator),
                Text(artists.FirstOrDefault(a => a.Attribute("role")?.Value.Equals("AlbumArtist", StringComparison.OrdinalIgnoreCase) == true)?.Value),
                Text(element.Element(Upnp + "album")?.Value),
                Text(element.Element(Upnp + "genre")?.Value),
                element.Element(Dc + "date")?.Value is { Length: >= 4 } date ? Int(date[..4]) : null,
                Int(element.Element(Upnp + "originalTrackNumber")?.Value),
                Int(element.Element(Upnp + "originalDiscNumber")?.Value),
                resource.Url,
                resource.Extension,
                resource.Size,
                element.Element(Upnp + "albumArtURI")?.Value is { Length: > 0 } art && Uri.TryCreate(baseUri, art.Trim(), out var artUri)
                && artUri.Scheme is "http" or "https"
                    ? artUri
                    : null));
        }

        return (containers, tracks);
    }

    /// <summary>
    /// The resource to download: an original (not converted by the server, <c>DLNA.ORG_CI=1</c>) in a format the
    /// scanner reads, the lossless or largest first; else the best conversion.
    /// </summary>
    private static (Uri Url, string Extension, long? Size)? Resource(XElement item, Uri baseUri)
    {
        var candidates = new List<(Uri Url, string Extension, long? Size, bool Original, bool Lossless, int Bitrate)>();
        foreach (var res in item.Elements(Didl + "res"))
        {
            var protocol = res.Attribute("protocolInfo")?.Value.Split(':') ?? [];
            if (protocol.Length < 3 || !protocol[0].Equals("http-get", StringComparison.OrdinalIgnoreCase)
                || !Uri.TryCreate(baseUri, res.Value.Trim(), out var url) || url.Scheme is not ("http" or "https"))
                continue;

            var mime = DlnaFormats.Normalize(protocol[2]);
            var extension = ExtensionOf(mime) ?? (AudioFormats.IsAudio(url.AbsolutePath) ? Path.GetExtension(url.AbsolutePath).ToLowerInvariant() : null);
            if (extension is null)
                continue;

            var features = protocol.Length > 3 ? protocol[3] : "";
            candidates.Add((url, extension, Long(res.Attribute("size")?.Value),
                !features.Contains("DLNA.ORG_CI=1", StringComparison.OrdinalIgnoreCase),
                AudioFormats.IsLossless("x" + extension),
                Int(res.Attribute("bitrate")?.Value) ?? 0));
        }

        if (candidates.Count == 0)
            return null;
        var best = candidates
            .OrderByDescending(c => c.Original)
            .ThenByDescending(c => c.Lossless)
            .ThenByDescending(c => c.Bitrate)
            .ThenByDescending(c => c.Size ?? 0)
            .First();
        return (best.Url, best.Extension, best.Size);
    }

    /// <summary>The file extension of an audio MIME type the scanner reads; null for the rest (raw PCM, video…).</summary>
    internal static string? ExtensionOf(string mime) => mime switch
    {
        "audio/flac" => ".flac",
        "audio/mpeg" => ".mp3",
        "audio/mp4" => ".m4a",
        "audio/aac" => ".aac",
        "audio/ogg" => ".ogg",
        "audio/opus" => ".opus",
        "audio/wav" => ".wav",
        "audio/x-aiff" or "audio/aiff" => ".aiff",
        "audio/x-ms-wma" => ".wma",
        "audio/ape" or "audio/x-ape" or "audio/x-monkeys-audio" => ".ape",
        "audio/wavpack" or "audio/x-wavpack" => ".wv",
        "audio/dsf" or "audio/x-dsf" => ".dsf",
        _ => null,
    };

    private static string? Text(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static int? Int(string? value) =>
        int.TryParse(value?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) ? number : null;

    private static long? Long(string? value) =>
        long.TryParse(value?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) ? number : null;
}
