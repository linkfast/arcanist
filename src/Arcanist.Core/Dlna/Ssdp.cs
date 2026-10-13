using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Arcanist.Core.Application.Server;
using Arcanist.Core.Extensions;
using Arcanist.Core.Setup;
using Microsoft.Extensions.Options;

namespace Arcanist.Core.Dlna;

/// <summary>An SSDP message: a start line and headers (names compared without case).</summary>
internal sealed record SsdpMessage(string StartLine, IReadOnlyDictionary<string, string> Headers)
{
    public string? this[string name] => Headers.GetValueOrDefault(name);

    public bool IsSearch => StartLine.StartsWith("M-SEARCH ", StringComparison.OrdinalIgnoreCase);

    public bool IsNotify => StartLine.StartsWith("NOTIFY ", StringComparison.OrdinalIgnoreCase);

    public bool IsResponse => StartLine.StartsWith("HTTP/1.", StringComparison.OrdinalIgnoreCase);
}

/// <summary>SSDP (UPnP discovery) messages: what Arcanist announces, answers and asks.</summary>
internal static class Ssdp
{
    public static readonly IPAddress Group = IPAddress.Parse("239.255.255.250");
    public const int Port = 1900;
    public const int MaxAge = 1800;

    public const string MediaServer = "urn:schemas-upnp-org:device:MediaServer:1";
    public const string MediaRenderer = "urn:schemas-upnp-org:device:MediaRenderer:1";
    public const string ContentDirectory = "urn:schemas-upnp-org:service:ContentDirectory:1";
    public const string ConnectionManager = "urn:schemas-upnp-org:service:ConnectionManager:1";
    public const string AvTransport = "urn:schemas-upnp-org:service:AVTransport:1";
    public const string RenderingControl = "urn:schemas-upnp-org:service:RenderingControl:1";

    public static SsdpMessage? Parse(string text)
    {
        var lines = text.Split("\r\n");
        if (lines.Length == 0 || lines[0].Length == 0)
            return null;
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in lines.Skip(1))
        {
            var colon = line.IndexOf(':');
            if (colon > 0)
                headers[line[..colon].Trim()] = line[(colon + 1)..].Trim();
        }

        return new SsdpMessage(lines[0].Trim(), headers);
    }

    /// <summary>What the server announces: the root device, its UUID, its type and its services.</summary>
    public static IReadOnlyList<(string Type, string Usn)> Targets(Guid deviceId)
    {
        var uuid = $"uuid:{deviceId}";
        return
        [
            ("upnp:rootdevice", $"{uuid}::upnp:rootdevice"),
            (uuid, uuid),
            (MediaServer, $"{uuid}::{MediaServer}"),
            (ContentDirectory, $"{uuid}::{ContentDirectory}"),
            (ConnectionManager, $"{uuid}::{ConnectionManager}"),
        ];
    }

    /// <summary>The targets a search asks for (<c>ssdp:all</c> is every one).</summary>
    public static IEnumerable<(string Type, string Usn)> Matching(string? searchTarget, Guid deviceId) =>
        searchTarget is null
            ? []
            : Targets(deviceId).Where(t => searchTarget.Equals("ssdp:all", StringComparison.OrdinalIgnoreCase)
                                           || t.Type.Equals(searchTarget, StringComparison.OrdinalIgnoreCase));

    public static string Notify(string location, string type, string usn, bool alive, string server) =>
        alive
            ? $"NOTIFY * HTTP/1.1\r\nHOST: 239.255.255.250:1900\r\nCACHE-CONTROL: max-age={MaxAge}\r\nLOCATION: {location}\r\n" +
              $"NT: {type}\r\nNTS: ssdp:alive\r\nSERVER: {server}\r\nUSN: {usn}\r\n\r\n"
            : $"NOTIFY * HTTP/1.1\r\nHOST: 239.255.255.250:1900\r\nNT: {type}\r\nNTS: ssdp:byebye\r\nUSN: {usn}\r\n\r\n";

    public static string SearchResponse(string location, string type, string usn, string server, DateTime now) =>
        $"HTTP/1.1 200 OK\r\nCACHE-CONTROL: max-age={MaxAge}\r\nDATE: {now.ToString("r", CultureInfo.InvariantCulture)}\r\nEXT:\r\n" +
        $"LOCATION: {location}\r\nSERVER: {server}\r\nST: {type}\r\nUSN: {usn}\r\n\r\n";

    public static string Search(string type) =>
        $"M-SEARCH * HTTP/1.1\r\nHOST: 239.255.255.250:1900\r\nMAN: \"ssdp:discover\"\r\nMX: 2\r\nST: {type}\r\n\r\n";

    /// <summary>Whether a message is about a renderer Arcanist can play to.</summary>
    public static bool IsRenderer(SsdpMessage message) =>
        (message["NT"] ?? message["ST"]) is { } type
        && (type.Contains("MediaRenderer", StringComparison.OrdinalIgnoreCase) || type.Contains("AVTransport", StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// Announces the media server on the local networks and answers searches for it; also finds renderers for "play
/// to". Runs while the DLNA extension is on (and <see cref="DlnaOptions.Ssdp"/>), on every local IPv4 interface.
/// </summary>
internal sealed class SsdpService(
    ExtensionHost extensions,
    DlnaSettings settings,
    RendererRegistry renderers,
    DlnaEndpoint endpoint,
    ServerInfoService serverInfo,
    IOptions<DlnaOptions> options,
    IOptionsMonitor<SetupOptions> setup,
    TimeProvider time,
    ILogger<SsdpService> logger) : BackgroundService
{
    private static readonly TimeSpan AnnounceEvery = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan SearchEvery = TimeSpan.FromMinutes(2);

    private readonly ILogger<SsdpService> _logger = logger;
    private readonly RendererRegistry _renderers = renderers;
    private Session? _session;

    /// <summary>Asks the network for renderers now (the panel's refresh).</summary>
    public void SearchNow() => _session?.Search();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Ssdp)
            return;

        var lastAnnounce = DateTime.MinValue;
        var lastSearch = DateTime.MinValue;
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var enabled = setup.CurrentValue.Completed && extensions.IsEnabled(Extensions.Dlna.DlnaExtension.Id);
                var addresses = enabled ? LocalNetwork.Addresses().Select(a => a.Address).ToList() : [];
                if (_session is not null && (!enabled || !_session.Addresses.SequenceEqual(addresses)))
                {
                    await _session.DisposeAsync();
                    _session = null;
                }

                if (enabled && _session is null && addresses.Count > 0 && endpoint.Port is { } port)
                {
                    await settings.GetAsync(stoppingToken);
                    _session = new Session(this, await settings.DeviceIdAsync(stoppingToken), addresses, port, stoppingToken);
                    lastAnnounce = lastSearch = DateTime.MinValue;
                }

                var now = time.GetUtcNow().UtcDateTime;
                if (_session is not null && now - lastAnnounce > AnnounceEvery)
                {
                    _session.Announce(alive: true);
                    lastAnnounce = now;
                }

                if (_session is not null && now - lastSearch > SearchEvery)
                {
                    _session.Search();
                    lastSearch = now;
                }

                await Task.Delay(TimeSpan.FromSeconds(5), time, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        finally
        {
            if (_session is not null)
                await _session.DisposeAsync();
        }
    }

    private string ServerHeader => $"{Environment.OSVersion.Platform}/{Environment.OSVersion.Version.Major} UPnP/1.0 Arcanist/{serverInfo.Get().Version}";

    /// <summary>The sockets of one set of interfaces: a listener on port 1900 and a sender per interface.</summary>
    private sealed class Session : IAsyncDisposable
    {
        private readonly SsdpService _owner;
        private readonly Guid _deviceId;
        private readonly int _port;
        private readonly CancellationTokenSource _stop;
        private readonly Socket? _listener;
        private readonly List<(IPAddress Address, Socket Socket)> _senders = [];
        private readonly List<Task> _loops = [];

        public Session(SsdpService owner, Guid deviceId, IReadOnlyList<IPAddress> addresses, int port, CancellationToken stopping)
        {
            _owner = owner;
            _deviceId = deviceId;
            _port = port;
            Addresses = addresses;
            _stop = CancellationTokenSource.CreateLinkedTokenSource(stopping);

            try
            {
                _listener = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                _listener.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                _listener.Bind(new IPEndPoint(IPAddress.Any, Ssdp.Port));
                foreach (var address in addresses)
                    _listener.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.AddMembership, new MulticastOption(Ssdp.Group, address));
                _loops.Add(ReceiveAsync(_listener));
            }
            catch (SocketException e)
            {
                // Another program holds the port: the server still announces itself and finds renderers by searching.
                owner._logger.LogWarning("DLNA cannot listen on UDP port 1900 ({Error}); devices may take longer to find it", e.SocketErrorCode);
                _listener?.Dispose();
                _listener = null;
            }

            foreach (var address in addresses)
            {
                try
                {
                    var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                    socket.Bind(new IPEndPoint(address, 0));
                    socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastInterface, address.GetAddressBytes());
                    socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastTimeToLive, 4);
                    _senders.Add((address, socket));
                    _loops.Add(ReceiveAsync(socket));
                }
                catch (SocketException e)
                {
                    owner._logger.LogWarning("DLNA cannot use {Address} ({Error})", address, e.SocketErrorCode);
                }
            }

            owner._logger.LogInformation("DLNA announced on {Addresses}", string.Join(", ", addresses));
        }

        public IReadOnlyList<IPAddress> Addresses { get; }

        private string Location(IPAddress address) => $"http://{address}:{_port}{Extensions.Dlna.DlnaExtension.DescriptionPath}";

        public void Announce(bool alive)
        {
            foreach (var (address, socket) in _senders)
            {
                foreach (var (type, usn) in Ssdp.Targets(_deviceId))
                    Send(socket, Ssdp.Notify(Location(address), type, usn, alive, _owner.ServerHeader), new IPEndPoint(Ssdp.Group, Ssdp.Port));
            }
        }

        public void Search()
        {
            foreach (var (_, socket) in _senders)
                Send(socket, Ssdp.Search(Ssdp.MediaRenderer), new IPEndPoint(Ssdp.Group, Ssdp.Port));
        }

        private void Send(Socket socket, string message, EndPoint to)
        {
            try
            {
                socket.SendTo(Encoding.ASCII.GetBytes(message), to);
            }
            catch (SocketException e)
            {
                _owner._logger.LogDebug("SSDP send failed: {Error}", e.SocketErrorCode);
            }
            catch (ObjectDisposedException)
            {
            }
        }

        private async Task ReceiveAsync(Socket socket)
        {
            var buffer = new byte[8192];
            while (!_stop.IsCancellationRequested)
            {
                SocketReceiveFromResult received;
                try
                {
                    received = await socket.ReceiveFromAsync(buffer, SocketFlags.None, new IPEndPoint(IPAddress.Any, 0), _stop.Token);
                }
                catch (Exception e) when (e is OperationCanceledException or ObjectDisposedException)
                {
                    return;
                }
                catch (SocketException)
                {
                    // An ICMP "port unreachable" from an earlier send surfaces here on Windows; keep listening.
                    continue;
                }

                if (Ssdp.Parse(Encoding.ASCII.GetString(buffer, 0, received.ReceivedBytes)) is not { } message
                    || received.RemoteEndPoint is not IPEndPoint from)
                    continue;

                if (message.IsSearch)
                    _ = AnswerAsync(message, from);
                else if ((message.IsNotify || message.IsResponse) && Ssdp.IsRenderer(message) && message["LOCATION"] is { } location
                         && Uri.TryCreate(location, UriKind.Absolute, out var uri))
                {
                    if (message["NTS"]?.Equals("ssdp:byebye", StringComparison.OrdinalIgnoreCase) == true)
                        _owner._renderers.Gone(message["USN"]);
                    else
                        _ = _owner._renderers.SeenAsync(uri, manual: false, CancellationToken.None);
                }
            }
        }

        private async Task AnswerAsync(SsdpMessage search, IPEndPoint from)
        {
            var matches = Ssdp.Matching(search["ST"], _deviceId).ToList();
            if (matches.Count == 0 || !LocalNetwork.Contains(from.Address))
                return;

            var sender = _senders.FirstOrDefault(s => LocalNetwork.Addresses().Any(a => a.Address.Equals(s.Address)
                                                                                        && LocalNetwork.SameSubnet(a.Address, from.Address, a.Mask)));
            if (sender.Socket is null)
                sender = _senders.FirstOrDefault();
            if (sender.Socket is null)
                return;

            // Searchers allow up to MX seconds; answering at once from many servers floods them.
            var mx = int.TryParse(search["MX"], out var seconds) ? Math.Clamp(seconds, 0, 3) : 1;
            await Task.Delay(Random.Shared.Next(0, mx * 300 + 1));
            foreach (var (type, usn) in matches)
                Send(sender.Socket, Ssdp.SearchResponse(Location(sender.Address), type, usn, _owner.ServerHeader, DateTime.UtcNow), from);
        }

        public async ValueTask DisposeAsync()
        {
            Announce(alive: false);
            await _stop.CancelAsync();
            _listener?.Dispose();
            foreach (var (_, socket) in _senders)
                socket.Dispose();
            await Task.WhenAll(_loops).ContinueWith(_ => { }, TaskScheduler.Default);
            _stop.Dispose();
        }
    }
}
