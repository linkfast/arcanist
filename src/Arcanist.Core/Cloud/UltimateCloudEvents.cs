using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Arcanist.Core.Capabilities;
using Arcanist.Core.Data;
using Arcanist.Core.Data.Entities;
using Arcanist.Core.Scanning;
using Arcanist.Core.Setup;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Arcanist.Core.Cloud;

/// <summary>
/// Listens to Ultimate Cloud's <c>cloudLibraryChanged</c> subscription while an account is linked, so changes are
/// picked up at once: Cloud Drop changes wake the Cloud Drop drop folder, and library changes scan the Ultimate Cloud
/// libraries that watch for changes (a few seconds after the last notice). GraphQL over WebSocket
/// (<c>graphql-transport-ws</c>), signed in like every request; reconnects with a fresh token every half hour, and
/// after a failure, waiting longer each time. Polling and scheduled scans still catch what a lost connection misses.
/// </summary>
public sealed class UltimateCloudEvents(
    UltimateCloudAuth auth,
    ICloudLinkState link,
    IOptionsMonitor<UltimateCloudOptions> options,
    IArcanistDbContextFactory databases,
    ScanCoordinator scans,
    IServiceProvider services,
    IOptionsMonitor<SetupOptions> setup,
    ILogger<UltimateCloudEvents> logger) : BackgroundService
{
    public const string Subscription = "subscription { cloudLibraryChanged { library added updated removed playlistsChanged } }";

    /// <summary>A connection lasts this long, then is made again with a fresh token.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(30);

    /// <summary>Library notices that come together scan once, this long after the last.</summary>
    public static readonly TimeSpan ScanDelay = TimeSpan.FromSeconds(5);

    private int _scanRequests;

    /// <summary>Whether the subscription is open now.</summary>
    public bool Listening { get; private set; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var wait = TimeSpan.FromSeconds(5);
        while (!stoppingToken.IsCancellationRequested)
        {
            if (setup.CurrentValue.Completed && databases.IsConfigured && link.Tier != CloudTier.None && EventsUri() is { } uri)
            {
                try
                {
                    await ListenAsync(uri, stoppingToken);
                    wait = TimeSpan.FromSeconds(5);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception e)
                {
                    logger.LogDebug(e, "Ultimate Cloud's change notices dropped");
                    wait = TimeSpan.FromTicks(Math.Min(wait.Ticks * 2, TimeSpan.FromMinutes(5).Ticks));
                }
                finally
                {
                    Listening = false;
                }
            }

            try
            {
                await Task.Delay(wait, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <summary><c>EventsUrl</c>, or the API's URL as a WebSocket one.</summary>
    private Uri? EventsUri()
    {
        var configured = options.CurrentValue.EventsUrl ?? options.CurrentValue.ApiUrl;
        if (string.IsNullOrWhiteSpace(configured) || !Uri.TryCreate(configured, UriKind.Absolute, out var uri))
            return null;
        var builder = new UriBuilder(uri) { Scheme = uri.Scheme switch { "https" => "wss", "http" => "ws", _ => uri.Scheme } };
        return builder.Uri;
    }

    private async Task ListenAsync(Uri uri, CancellationToken stoppingToken)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        lifetime.CancelAfter(Lifetime);
        var cancellationToken = lifetime.Token;

        using var socket = new ClientWebSocket();
        socket.Options.AddSubProtocol("graphql-transport-ws");
        await socket.ConnectAsync(uri, cancellationToken);

        await SendAsync(socket, new
        {
            type = "connection_init",
            payload = new
            {
                authorization = "Bearer " + await auth.AccessTokenAsync(cancellationToken),
                deviceId = await auth.DeviceIdAsync(cancellationToken),
                clientApp = UltimateCloudClient.ClientApp,
            },
        }, cancellationToken);

        using (var handshake = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            handshake.CancelAfter(TimeSpan.FromSeconds(15));
            while ((await ReceiveAsync(socket, handshake.Token))?["type"]?.GetValue<string>() is { } type && type != "connection_ack")
            {
                if (type == "ping")
                    await SendAsync(socket, new { type = "pong" }, cancellationToken);
            }
        }

        await SendAsync(socket, new { id = "library", type = "subscribe", payload = new { query = Subscription } }, cancellationToken);
        Listening = true;
        logger.LogInformation("Listening to Ultimate Cloud's change notices");

        try
        {
            while (await ReceiveAsync(socket, cancellationToken) is { } message)
            {
                switch (message["type"]?.GetValue<string>())
                {
                    case "ping":
                        await SendAsync(socket, new { type = "pong" }, cancellationToken);
                        break;
                    case "next" when (message["payload"]?["data"]?["cloudLibraryChanged"]) is { } change:
                        await HandleAsync(change, cancellationToken);
                        break;
                    case "error":
                    case "complete":
                        logger.LogInformation("Ultimate Cloud ended the change notices: {Message}", message["payload"]?.ToJsonString());
                        return;
                }
            }
        }
        catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
        {
            // The half hour is over: a new connection follows at once.
            await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
        }
    }

    internal async Task HandleAsync(JsonNode change, CancellationToken cancellationToken)
    {
        int Count(string name) => change[name]?.AsArray().Count ?? 0;
        var any = Count("added") + Count("updated") + Count("removed") > 0;
        switch (change["library"]?.GetValue<string>())
        {
            case "CLOUD_DROP" when any:
                services.GetService<CloudDropWorker>()?.Wake();
                break;
            case "STANDARD" when any:
                // Not awaited: the next notices are read meanwhile, and only the last one scans.
                _ = ScanSoonAsync(Interlocked.Increment(ref _scanRequests), cancellationToken);
                break;
        }

        await Task.CompletedTask;
    }

    private async Task ScanSoonAsync(int request, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(ScanDelay, cancellationToken);
            if (request == Volatile.Read(ref _scanRequests))
                await ScanWatchingLibrariesAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Scanning after Ultimate Cloud's notice failed");
        }
    }

    /// <summary>Scans the libraries of the account's music that watch for changes.</summary>
    private async Task ScanWatchingLibrariesAsync(CancellationToken cancellationToken)
    {
        var location = UltimateCloudCollection.Library.ToString();
        List<Guid> libraries;
        await using (var db = databases.CreateDbContext())
        {
            libraries = await db.LibrarySources.AsNoTracking()
                .Where(s => s.Kind == LibrarySourceKind.UltimateCloud && s.Location == location && s.Library.WatchForChanges)
                .Select(s => s.LibraryId)
                .Distinct()
                .ToListAsync(cancellationToken);
        }

        foreach (var library in libraries)
        {
            logger.LogInformation("Ultimate Cloud changed; scanning library {Library}", library);
            await scans.EnqueueAsync(library, ScanKind.Incremental, requestedBy: null, cancellationToken);
        }
    }

    private static async Task SendAsync(ClientWebSocket socket, object message, CancellationToken cancellationToken) =>
        await socket.SendAsync(JsonSerializer.SerializeToUtf8Bytes(message), WebSocketMessageType.Text, endOfMessage: true, cancellationToken);

    /// <summary>The next message; null once the socket closes.</summary>
    private static async Task<JsonNode?> ReceiveAsync(ClientWebSocket socket, CancellationToken cancellationToken)
    {
        var buffer = new byte[16 * 1024];
        using var message = new MemoryStream();
        while (true)
        {
            var result = await socket.ReceiveAsync(buffer, cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close)
                return null;
            message.Write(buffer, 0, result.Count);
            if (message.Length > 4 * 1024 * 1024)
                throw new InvalidDataException("A notice too large");
            if (result.EndOfMessage)
                return JsonNode.Parse(Encoding.UTF8.GetString(message.ToArray()));
        }
    }
}
