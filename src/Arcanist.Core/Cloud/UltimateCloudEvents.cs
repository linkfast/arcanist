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
/// One subscription per account: the server's, and each user's own (whose notices scan their personal library).
/// </summary>
public sealed class UltimateCloudEvents(
    UltimateCloudAuth auth,
    UserCloudAccounts accounts,
    IOptionsMonitor<UltimateCloudOptions> options,
    IArcanistDbContextFactory databases,
    ScanCoordinator scans,
    IServiceProvider services,
    IOptionsMonitor<SetupOptions> setup,
    ILogger<UltimateCloudEvents> logger) : BackgroundService
{
    public const string Subscription = "subscription { cloudLibraryChanged { library added updated removed playlistsChanged } }";

    /// <summary>The account's StarTracks changed (on another device, or here).</summary>
    public const string StarsSubscription = "subscription { starTracksChanged { changedAt } }";

    /// <summary>A connection lasts this long, then is made again with a fresh token.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(30);

    /// <summary>Library notices that come together scan once, this long after the last.</summary>
    public static readonly TimeSpan ScanDelay = TimeSpan.FromSeconds(5);

    private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, int> _scanRequests = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, bool> _listening = new();

    /// <summary>Whether the subscription of the server's account is open now.</summary>
    public bool Listening => IsListening(null);

    /// <summary>Whether the subscription of an account (a user's own, or the server's with null) is open now.</summary>
    public bool IsListening(Guid? account) => _listening.GetValueOrDefault(account ?? Guid.Empty);

    /// <summary>Keeps a listener running for each account with a tier, and stops those whose account went.</summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var running = new Dictionary<Guid, (Task Task, CancellationTokenSource Cancel)>();
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var wanted = new HashSet<Guid>();
                if (setup.CurrentValue.Completed && databases.IsConfigured)
                {
                    if (accounts.TierOf(null) != CloudTier.None)
                        wanted.Add(Guid.Empty);
                    foreach (var user in accounts.Linked.Where(u => accounts.TierOf(u) != CloudTier.None))
                        wanted.Add(user);
                }

                foreach (var (key, listener) in running.Where(r => !wanted.Contains(r.Key) || r.Value.Task.IsCompleted).ToList())
                {
                    await listener.Cancel.CancelAsync();
                    listener.Cancel.Dispose();
                    running.Remove(key);
                }

                foreach (var key in wanted.Where(k => !running.ContainsKey(k)))
                {
                    var cancel = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                    running[key] = (RunAsync(key == Guid.Empty ? null : key, cancel.Token), cancel);
                }

                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            foreach (var listener in running.Values)
                await listener.Cancel.CancelAsync();
        }
    }

    /// <summary>One account's subscription, made again after each end while the account has a tier.</summary>
    private async Task RunAsync(Guid? account, CancellationToken stoppingToken)
    {
        await Task.Yield();
        var wait = TimeSpan.FromSeconds(5);
        while (!stoppingToken.IsCancellationRequested)
        {
            if (accounts.TierOf(account) != CloudTier.None && EventsUri() is { } uri)
            {
                try
                {
                    await ListenAsync(account, uri, stoppingToken);
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
                    _listening[account ?? Guid.Empty] = false;
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

    /// <summary>Ends the subscription when the account is gone or the extension is switched off.</summary>
    private async Task CloseWhenUnlinkedAsync(Guid? account, CancellationTokenSource lifetime)
    {
        try
        {
            while (!lifetime.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(5), lifetime.Token);
                if (accounts.TierOf(account) == CloudTier.None)
                    await lifetime.CancelAsync();
            }
        }
        catch (Exception e) when (e is OperationCanceledException or ObjectDisposedException)
        {
            // The connection ended on its own.
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

    private async Task ListenAsync(Guid? account, Uri uri, CancellationToken stoppingToken)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        lifetime.CancelAfter(Lifetime);
        var cancellationToken = lifetime.Token;
        _ = CloseWhenUnlinkedAsync(account, lifetime);

        using var socket = new ClientWebSocket();
        socket.Options.AddSubProtocol("graphql-transport-ws");
        await socket.ConnectAsync(uri, cancellationToken);

        await SendAsync(socket, new
        {
            type = "connection_init",
            payload = new
            {
                authorization = "Bearer " + await auth.AccessTokenAsync(cancellationToken, account),
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
        await SendAsync(socket, new { id = "stars", type = "subscribe", payload = new { query = StarsSubscription } }, cancellationToken);
        _listening[account ?? Guid.Empty] = true;
        logger.LogInformation("Listening to Ultimate Cloud's change notices ({Account})", account?.ToString() ?? "server");

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
                        await HandleAsync(change, cancellationToken, account);
                        break;
                    case "next" when (message["payload"]?["data"]?["starTracksChanged"] is not null):
                        services.GetService<UltimateCloudReporter>()?.StarsChanged();
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

    internal async Task HandleAsync(JsonNode change, CancellationToken cancellationToken, Guid? account = null)
    {
        int Count(string name) => change[name]?.AsArray().Count ?? 0;
        var any = Count("added") + Count("updated") + Count("removed") > 0;
        switch (change["library"]?.GetValue<string>())
        {
            // Cloud Drop comes in through the server's account only.
            case "CLOUD_DROP" when any && account is null:
                services.GetService<CloudDropWorker>()?.Wake();
                break;
            case "STANDARD" when any:
                // Not awaited: the next notices are read meanwhile, and only the last one scans.
                var key = account ?? Guid.Empty;
                _ = ScanSoonAsync(account, _scanRequests.AddOrUpdate(key, 1, (_, n) => n + 1), cancellationToken);
                break;
        }

        await Task.CompletedTask;
    }

    private async Task ScanSoonAsync(Guid? account, int request, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(ScanDelay, cancellationToken);
            if (request == _scanRequests.GetValueOrDefault(account ?? Guid.Empty))
                await ScanWatchingLibrariesAsync(account, cancellationToken);
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
    private async Task ScanWatchingLibrariesAsync(Guid? account, CancellationToken cancellationToken)
    {
        var location = UltimateCloudCollection.Library.ToString();
        List<Guid> libraries;
        await using (var db = databases.CreateDbContext())
        {
            libraries = (await db.LibrarySources.AsNoTracking()
                    .Where(s => s.Kind == LibrarySourceKind.UltimateCloud && s.Location == location && s.Library.WatchForChanges)
                    .Select(s => new { s.LibraryId, s.Settings })
                    .ToListAsync(cancellationToken))
                .Where(s => UserCloudAccounts.UserOf(s.Settings) == account)
                .Select(s => s.LibraryId)
                .Distinct()
                .ToList();
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
