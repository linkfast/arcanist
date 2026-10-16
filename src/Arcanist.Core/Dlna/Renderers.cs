using System.Collections.Concurrent;
using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using Arcanist.Core.Application;
using Arcanist.Core.Application.Listening;
using Arcanist.Core.Auth;

namespace Arcanist.Core.Dlna;

/// <summary>A device that can play what Arcanist sends it (a DLNA/UPnP media renderer).</summary>
/// <param name="Id">Its UUID, without the <c>uuid:</c> prefix.</param>
/// <param name="Manual">Added by an administrator from its description URL, not found by SSDP: it never expires.</param>
internal sealed record RendererDevice(
    string Id,
    string Name,
    string? Manufacturer,
    string? Model,
    Uri Location,
    Uri AvTransport,
    Uri? RenderingControl,
    Uri? ConnectionManager,
    bool Manual)
{
    public DateTime LastSeen { get; set; }

    /// <summary>The MIME types it says it plays (ConnectionManager sink protocols); null until asked.</summary>
    public IReadOnlySet<string>? Accepts { get; set; }
}

/// <summary>Renderers found on the network or added by hand.</summary>
internal sealed class RendererRegistry(IHttpClientFactory clients, TimeProvider time, ILogger<RendererRegistry> logger)
{
    public const string HttpClientName = "dlna";

    /// <summary>SSDP announcements last 30 minutes; a renderer not heard of for longer is gone.</summary>
    private static readonly TimeSpan Expiry = TimeSpan.FromMinutes(35);

    private readonly ConcurrentDictionary<string, RendererDevice> _devices = new();
    private readonly ConcurrentDictionary<Uri, DateTime> _fetched = new();

    public HttpClient Client => clients.CreateClient(HttpClientName);

    public IReadOnlyList<RendererDevice> List()
    {
        var now = time.GetUtcNow().UtcDateTime;
        return _devices.Values.Where(d => d.Manual || now - d.LastSeen < Expiry).OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public RendererDevice? Find(string id) => List().FirstOrDefault(d => d.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    /// <summary>A renderer announced itself or answered a search: reads its description, at most once a minute.</summary>
    public async Task<RendererDevice?> SeenAsync(Uri location, bool manual, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var known = _devices.Values.FirstOrDefault(d => d.Location == location);
        if (!manual && _fetched.TryGetValue(location, out var at) && now - at < TimeSpan.FromMinutes(1))
        {
            if (known is not null)
                known.LastSeen = now;
            return known;
        }

        _fetched[location] = now;
        try
        {
            var xml = await Client.GetStringAsync(location, cancellationToken);
            if (Parse(xml, location, manual || known?.Manual == true) is not { } device)
                return null;
            device.LastSeen = now;
            device.Accepts = known?.Accepts;
            _devices[device.Id] = device;
            return device;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or XmlException && !cancellationToken.IsCancellationRequested)
        {
            logger.LogDebug("Could not read the renderer description at {Location}: {Error}", location, e.Message);
            if (manual)
                throw new ArcanistException.Conflict(ErrorCodes.RendererUnreachable);
            return null;
        }
    }

    /// <summary>A renderer said goodbye.</summary>
    public void Gone(string? usn)
    {
        var id = usn?.Split("::")[0].Replace("uuid:", "", StringComparison.OrdinalIgnoreCase);
        if (id is not null && _devices.TryGetValue(id, out var device) && !device.Manual)
            _devices.TryRemove(id, out _);
    }

    /// <summary>Reads a device description; null when it describes no renderer with AVTransport.</summary>
    internal static RendererDevice? Parse(string xml, Uri location, bool manual)
    {
        var document = XDocument.Parse(xml);
        XNamespace ns = "urn:schemas-upnp-org:device-1-0";
        var root = document.Root;
        var baseUri = root?.Element(ns + "URLBase")?.Value is { Length: > 0 } urlBase && Uri.TryCreate(urlBase, UriKind.Absolute, out var parsed)
            ? parsed
            : location;
        var device = root?.Descendants(ns + "device")
            .FirstOrDefault(d => d.Element(ns + "deviceType")?.Value.Contains("MediaRenderer", StringComparison.OrdinalIgnoreCase) == true);
        if (device is null)
            return null;

        Uri? Control(string type) =>
            device.Element(ns + "serviceList")?.Elements(ns + "service")
                .FirstOrDefault(s => s.Element(ns + "serviceType")?.Value.StartsWith(type, StringComparison.OrdinalIgnoreCase) == true)
                ?.Element(ns + "controlURL")?.Value is { Length: > 0 } url && Uri.TryCreate(baseUri, url, out var control)
                ? control
                : null;

        var avTransport = Control("urn:schemas-upnp-org:service:AVTransport:");
        var udn = device.Element(ns + "UDN")?.Value.Trim();
        if (avTransport is null || string.IsNullOrEmpty(udn))
            return null;

        return new RendererDevice(
            udn.Replace("uuid:", "", StringComparison.OrdinalIgnoreCase),
            device.Element(ns + "friendlyName")?.Value.Trim() is { Length: > 0 } name ? name : location.Host,
            device.Element(ns + "manufacturer")?.Value.Trim(),
            device.Element(ns + "modelName")?.Value.Trim(),
            location,
            avTransport,
            Control("urn:schemas-upnp-org:service:RenderingControl:"),
            Control("urn:schemas-upnp-org:service:ConnectionManager:"),
            manual);
    }
}

/// <summary>What a renderer says it is doing.</summary>
internal sealed record RendererStatus(string State, string? TrackUri, int PositionMs, int DurationMs);

/// <summary>The UPnP actions Arcanist sends to renderers.</summary>
internal static class RendererControl
{
    public static Task SetUriAsync(HttpClient client, RendererDevice device, string uri, string metadata, CancellationToken cancellationToken) =>
        Soap.InvokeAsync(client, device.AvTransport, Ssdp.AvTransport, "SetAVTransportURI",
            [("InstanceID", "0"), ("CurrentURI", uri), ("CurrentURIMetaData", metadata)], cancellationToken);

    public static Task SetNextUriAsync(HttpClient client, RendererDevice device, string uri, string metadata, CancellationToken cancellationToken) =>
        Soap.InvokeAsync(client, device.AvTransport, Ssdp.AvTransport, "SetNextAVTransportURI",
            [("InstanceID", "0"), ("NextURI", uri), ("NextURIMetaData", metadata)], cancellationToken);

    public static Task PlayAsync(HttpClient client, RendererDevice device, CancellationToken cancellationToken) =>
        Soap.InvokeAsync(client, device.AvTransport, Ssdp.AvTransport, "Play", [("InstanceID", "0"), ("Speed", "1")], cancellationToken);

    public static Task PauseAsync(HttpClient client, RendererDevice device, CancellationToken cancellationToken) =>
        Soap.InvokeAsync(client, device.AvTransport, Ssdp.AvTransport, "Pause", [("InstanceID", "0")], cancellationToken);

    public static Task StopAsync(HttpClient client, RendererDevice device, CancellationToken cancellationToken) =>
        Soap.InvokeAsync(client, device.AvTransport, Ssdp.AvTransport, "Stop", [("InstanceID", "0")], cancellationToken);

    public static Task SeekAsync(HttpClient client, RendererDevice device, int positionMs, CancellationToken cancellationToken) =>
        Soap.InvokeAsync(client, device.AvTransport, Ssdp.AvTransport, "Seek",
            [("InstanceID", "0"), ("Unit", "REL_TIME"), ("Target", FormatTime(positionMs))], cancellationToken);

    public static async Task<RendererStatus> StatusAsync(HttpClient client, RendererDevice device, CancellationToken cancellationToken)
    {
        var transport = await Soap.InvokeAsync(client, device.AvTransport, Ssdp.AvTransport, "GetTransportInfo", [("InstanceID", "0")], cancellationToken);
        var position = await Soap.InvokeAsync(client, device.AvTransport, Ssdp.AvTransport, "GetPositionInfo", [("InstanceID", "0")], cancellationToken);
        return new RendererStatus(
            transport.GetValueOrDefault("CurrentTransportState") ?? "STOPPED",
            position.GetValueOrDefault("TrackURI") is { Length: > 0 } uri ? uri : null,
            ParseTime(position.GetValueOrDefault("RelTime")),
            ParseTime(position.GetValueOrDefault("TrackDuration")));
    }

    public static async Task<int?> VolumeAsync(HttpClient client, RendererDevice device, CancellationToken cancellationToken)
    {
        if (device.RenderingControl is null)
            return null;
        var result = await Soap.InvokeAsync(client, device.RenderingControl, Ssdp.RenderingControl, "GetVolume",
            [("InstanceID", "0"), ("Channel", "Master")], cancellationToken);
        return int.TryParse(result.GetValueOrDefault("CurrentVolume"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var volume) ? volume : null;
    }

    public static async Task SetVolumeAsync(HttpClient client, RendererDevice device, int volume, CancellationToken cancellationToken)
    {
        if (device.RenderingControl is null)
            throw new ArcanistException.Conflict(ErrorCodes.RendererFailed);
        await Soap.InvokeAsync(client, device.RenderingControl, Ssdp.RenderingControl, "SetVolume",
            [("InstanceID", "0"), ("Channel", "Master"), ("DesiredVolume", volume.ToString(CultureInfo.InvariantCulture))], cancellationToken);
    }

    /// <summary>The MIME types the renderer plays, asked once; empty when it does not say.</summary>
    public static async Task<IReadOnlySet<string>> AcceptsAsync(HttpClient client, RendererDevice device, CancellationToken cancellationToken)
    {
        if (device.Accepts is { } known)
            return known;
        var accepts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (device.ConnectionManager is not null)
        {
            try
            {
                var result = await Soap.InvokeAsync(client, device.ConnectionManager, Ssdp.ConnectionManager, "GetProtocolInfo", [], cancellationToken);
                foreach (var protocol in (result.GetValueOrDefault("Sink") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    var parts = protocol.Split(':');
                    if (parts.Length >= 3 && parts[0].Equals("http-get", StringComparison.OrdinalIgnoreCase))
                        accepts.Add(DlnaFormats.Normalize(parts[2]));
                }
            }
            catch (ArcanistException)
            {
                // Some renderers do not answer it: everything is offered as if they played anything.
            }
        }

        device.Accepts = accepts;
        return accepts;
    }

    public static string FormatTime(int milliseconds)
    {
        var time = TimeSpan.FromMilliseconds(Math.Max(0, milliseconds));
        return $"{(int)time.TotalHours}:{time.Minutes:00}:{time.Seconds:00}";
    }

    /// <summary>A UPnP time (<c>H:MM:SS[.fff]</c>) in milliseconds; 0 when unknown (<c>NOT_IMPLEMENTED</c>).</summary>
    public static int ParseTime(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return 0;
        var parts = value.Split(':');
        if (parts.Length != 3 || !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var hours)
                              || !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var minutes)
                              || !double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds))
            return 0;
        return (int)((hours * 3600 + minutes * 60 + seconds) * 1000);
    }
}

/// <summary>A track queued on a renderer: the URL it plays and the metadata it shows.</summary>
internal sealed record QueueEntry(Guid TrackId, string Url, string Metadata, int DurationMs);

/// <summary>Arcanist's queue on one renderer.</summary>
internal sealed class RendererSession(RendererDevice device, Guid userId, IReadOnlyList<QueueEntry> queue)
{
    public RendererDevice Device { get; } = device;

    /// <summary>Who started it: their plays are recorded as the renderer plays.</summary>
    public Guid UserId { get; } = userId;

    /// <summary>The queue; changed only under <see cref="Lock"/> (<see cref="RendererSessions.EditAsync"/>).</summary>
    public List<QueueEntry> Queue { get; } = [.. queue];

    public int Index { get; set; }

    /// <summary>The renderer took the next track with SetNextAVTransportURI, so it moves on by itself (gapless).</summary>
    public bool NextQueued { get; set; }

    /// <summary>Stopped on purpose: do not move on.</summary>
    public bool Stopped { get; set; }

    public string LastState { get; set; } = "TRANSITIONING";

    public int LastPositionMs { get; set; }

    public int Failures { get; set; }

    public QueueEntry? Current => Index >= 0 && Index < Queue.Count ? Queue[Index] : null;

    /// <summary>Something of this queue is on the renderer now (not stopped by the user nor run out, and not replaced there by
    /// another app): who started it decides what plays.</summary>
    public bool Active => !Stopped && LastState is "PLAYING" or "PAUSED_PLAYBACK" or "TRANSITIONING";

    public SemaphoreSlim Lock { get; } = new(1, 1);
}

/// <summary>
/// "Play to": queues on renderers. Each renderer gets a track at a time, and the next one ahead when it supports
/// that; a loop follows every active renderer each second, moves on when a track ends and records the play for the
/// user who started the queue.
/// </summary>
internal sealed class RendererSessions(
    RendererRegistry registry,
    IServiceScopeFactory scopes,
    TimeProvider time,
    Data.DatabaseMaintenance maintenance,
    ILogger<RendererSessions> logger) : BackgroundService
{
    private readonly ConcurrentDictionary<string, RendererSession> _sessions = new();

    public RendererSession? Find(string rendererId) => _sessions.GetValueOrDefault(rendererId);

    public async Task<RendererSession> StartAsync(RendererDevice device, Guid userId, IReadOnlyList<QueueEntry> queue, int index, CancellationToken cancellationToken)
    {
        var session = new RendererSession(device, userId, queue) { Index = Math.Clamp(index, 0, queue.Count - 1) };
        _sessions[device.Id] = session;
        await session.Lock.WaitAsync(cancellationToken);
        try
        {
            await PlayCurrentAsync(session, cancellationToken);
        }
        finally
        {
            session.Lock.Release();
        }

        return session;
    }

    /// <summary>Moves to a track of the queue and plays it.</summary>
    public async Task GoToAsync(RendererSession session, int index, CancellationToken cancellationToken)
    {
        await session.Lock.WaitAsync(cancellationToken);
        try
        {
            if (index < 0 || index >= session.Queue.Count)
                return;
            session.Index = index;
            await PlayCurrentAsync(session, cancellationToken);
        }
        finally
        {
            session.Lock.Release();
        }
    }

    /// <summary>
    /// Changes a queue that is playing (<paramref name="edit"/> runs under the session's lock and returns false to change nothing). The
    /// track playing goes on; when what follows it changed, the renderer is handed the new next track.
    /// </summary>
    public async Task<bool> EditAsync(RendererSession session, Func<RendererSession, bool> edit, CancellationToken cancellationToken)
    {
        await session.Lock.WaitAsync(cancellationToken);
        try
        {
            var nextBefore = session.Index + 1 < session.Queue.Count ? session.Queue[session.Index + 1] : null;
            if (!edit(session))
                return false;
            var nextAfter = session.Index + 1 < session.Queue.Count ? session.Queue[session.Index + 1] : null;
            if (!ReferenceEquals(nextBefore, nextAfter))
            {
                if (nextAfter is not null)
                    await QueueNextAsync(session, cancellationToken);
                else if (session.NextQueued)
                {
                    try
                    {
                        await RendererControl.SetNextUriAsync(registry.Client, session.Device, "", "", cancellationToken);
                    }
                    catch (ArcanistException)
                    {
                        // A renderer that won't clear it plays the removed track: Arcanist's queue then follows the renderer.
                    }
                    session.NextQueued = false;
                }
            }
            return true;
        }
        finally
        {
            session.Lock.Release();
        }
    }

    private async Task PlayCurrentAsync(RendererSession session, CancellationToken cancellationToken)
    {
        var client = registry.Client;
        var entry = session.Current!;
        session.Stopped = false;
        session.NextQueued = false;
        session.LastState = "TRANSITIONING";
        session.LastPositionMs = 0;
        await RendererControl.SetUriAsync(client, session.Device, entry.Url, entry.Metadata, cancellationToken);
        await RendererControl.PlayAsync(client, session.Device, cancellationToken);
        await QueueNextAsync(session, cancellationToken);
    }

    private async Task QueueNextAsync(RendererSession session, CancellationToken cancellationToken)
    {
        if (session.Index + 1 >= session.Queue.Count)
            return;
        var next = session.Queue[session.Index + 1];
        try
        {
            await RendererControl.SetNextUriAsync(registry.Client, session.Device, next.Url, next.Metadata, cancellationToken);
            session.NextQueued = true;
        }
        catch (ArcanistException)
        {
            // Not every renderer takes a next track; Arcanist then starts it when the current one ends.
            session.NextQueued = false;
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                foreach (var session in _sessions.Values.Where(s => !s.Stopped && s.Current is not null))
                {
                    try
                    {
                        // Plays are recorded as tracks end: not while the database is migrated.
                        using (await maintenance.WorkAsync(stoppingToken))
                            await FollowAsync(session, stoppingToken);
                        session.Failures = 0;
                    }
                    catch (ArcanistException)
                    {
                        // A renderer switched off: give it a minute before forgetting its queue.
                        if (++session.Failures > 60)
                            _sessions.TryRemove(session.Device.Id, out _);
                    }
                }

                await Task.Delay(TimeSpan.FromSeconds(1), time, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    /// <summary>Checks one renderer: notices a track that ended, records it and moves on.</summary>
    internal async Task FollowAsync(RendererSession session, CancellationToken cancellationToken)
    {
        if (!await session.Lock.WaitAsync(0, cancellationToken))
            return;
        try
        {
            var status = await RendererControl.StatusAsync(registry.Client, session.Device, cancellationToken);
            var current = session.Current!;
            var next = session.Index + 1 < session.Queue.Count ? session.Queue[session.Index + 1] : null;

            if (session.NextQueued && next is not null && status.TrackUri == next.Url)
            {
                // The renderer moved on by itself.
                await RecordAsync(session, current, current.DurationMs);
                session.Index++;
                session.LastPositionMs = status.PositionMs;
                session.LastState = status.State;
                session.NextQueued = false;
                await QueueNextAsync(session, cancellationToken);
                return;
            }

            var ended = status.State is "STOPPED" or "NO_MEDIA_PRESENT" && session.LastState == "PLAYING"
                        && (current.DurationMs == 0 || session.LastPositionMs >= current.DurationMs - 5000);
            if (ended)
            {
                await RecordAsync(session, current, current.DurationMs);
                if (next is null)
                {
                    session.Stopped = true;
                    session.LastState = status.State;
                    return;
                }

                session.Index++;
                await PlayCurrentAsync(session, cancellationToken);
                return;
            }

            session.LastState = status.State;
            if (status.State == "PLAYING")
                session.LastPositionMs = status.PositionMs;
        }
        finally
        {
            session.Lock.Release();
        }
    }

    private async Task RecordAsync(RendererSession session, QueueEntry entry, int playedMs)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            if (!(await scope.ServiceProvider.GetRequiredService<DlnaSettings>().GetAsync(CancellationToken.None)).RecordPlays)
                return;
            if (await scope.ServiceProvider.GetRequiredService<UserAccessCache>().GetUserAsync(session.UserId, CancellationToken.None) is not { } access)
                return;
            await scope.ServiceProvider.GetRequiredService<ListeningService>()
                .RecordPlayAsync(access, new RecordPlayInput(entry.TrackId, playedMs, null, $"DLNA: {session.Device.Name}"), CancellationToken.None);
        }
        catch (Exception e) when (e is ArcanistException or InvalidOperationException)
        {
            logger.LogDebug("Could not record a play on {Renderer}: {Error}", session.Device.Name, e.Message);
        }
    }
}
