using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Arcanist.Core.Tests.Cloud;

/// <summary>
/// Ultimate Cloud's subscriptions, as far as Arcanist uses them: a WebSocket speaking <c>graphql-transport-ws</c> that
/// acknowledges the connection, takes the <c>cloudLibraryChanged</c> and <c>deviceCommands</c> subscriptions and sends
/// what a test publishes.
/// </summary>
public sealed class FakeCloudEvents : IAsyncDisposable
{
    private readonly WebApplication _app;
    /// <summary>Each connection's subscriptions (id and query) and the device it said it is.</summary>
    private readonly ConcurrentDictionary<WebSocket, (ConcurrentDictionary<string, string> Subscriptions, string? DeviceId)> _connections = new();

    private FakeCloudEvents(WebApplication app) => _app = app;

    /// <summary>The <c>connection_init</c> payloads received.</summary>
    public ConcurrentQueue<JsonNode?> Inits { get; } = new();

    public string Url => _app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First()
        .Replace("http://", "ws://") + "/graphql";

    public bool Subscribed => _connections.Values.Any(c => c.Subscriptions.Values.Any(q => q.Contains("cloudLibraryChanged")));

    /// <summary>Whether a device listens to its commands.</summary>
    public bool Listening(string deviceId) =>
        _connections.Values.Any(c => c.DeviceId == deviceId && c.Subscriptions.Values.Any(q => q.Contains("deviceCommands")));

    public static async Task<FakeCloudEvents> StartAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        var app = builder.Build();
        var fake = new FakeCloudEvents(app);
        app.UseWebSockets();
        app.Map("/graphql", async context =>
        {
            if (!context.WebSockets.IsWebSocketRequest)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }

            using var socket = await context.WebSockets.AcceptWebSocketAsync("graphql-transport-ws");
            await fake.ServeAsync(socket, context.RequestAborted);
        });
        await app.StartAsync();
        return fake;
    }

    private async Task ServeAsync(WebSocket socket, CancellationToken cancellationToken)
    {
        try
        {
            while (await ReceiveAsync(socket, cancellationToken) is { } message)
            {
                switch (message["type"]?.GetValue<string>())
                {
                    case "connection_init":
                        Inits.Enqueue(message["payload"]?.DeepClone());
                        _connections[socket] = (new ConcurrentDictionary<string, string>(), message["payload"]?["deviceId"]?.GetValue<string>());
                        await SendAsync(socket, new { type = "connection_ack" }, cancellationToken);
                        break;
                    case "subscribe" when _connections.TryGetValue(socket, out var connection):
                        connection.Subscriptions[message["id"]!.GetValue<string>()] = message["payload"]!["query"]!.GetValue<string>();
                        break;
                }
            }
        }
        catch (Exception e) when (e is WebSocketException or OperationCanceledException)
        {
        }
        finally
        {
            _connections.TryRemove(socket, out _);
        }
    }

    /// <summary>Sends a <c>cloudLibraryChanged</c> event to every subscriber.</summary>
    public async Task PublishAsync(string library, string[]? added = null, string[]? updated = null, string[]? removed = null)
    {
        foreach (var (socket, id) in Subscriptions("cloudLibraryChanged"))
        {
            await SendAsync(socket, new
            {
                id,
                type = "next",
                payload = new
                {
                    data = new
                    {
                        cloudLibraryChanged = new
                        {
                            library, added = added ?? [], updated = updated ?? [], removed = removed ?? [], playlistsChanged = false,
                        },
                    },
                },
            }, CancellationToken.None);
        }
    }

    /// <summary>Sends a <c>deviceCommands</c> command to a device, on every connection it listens on.</summary>
    public async Task SendCommandAsync(string deviceId, object command)
    {
        foreach (var (socket, id) in Subscriptions("deviceCommands", deviceId))
            await SendAsync(socket, new { id, type = "next", payload = new { data = new { deviceCommands = command } } }, CancellationToken.None);
    }

    private List<(WebSocket Socket, string Id)> Subscriptions(string field, string? deviceId = null) =>
        _connections.Where(c => deviceId is null || c.Value.DeviceId == deviceId)
            .SelectMany(c => c.Value.Subscriptions.Where(s => s.Value.Contains(field)).Select(s => (c.Key, s.Key)))
            .ToList();

    private static Task SendAsync(WebSocket socket, object message, CancellationToken cancellationToken) =>
        socket.SendAsync(JsonSerializer.SerializeToUtf8Bytes(message), WebSocketMessageType.Text, true, cancellationToken);

    private static async Task<JsonNode?> ReceiveAsync(WebSocket socket, CancellationToken cancellationToken)
    {
        var buffer = new byte[8192];
        using var message = new MemoryStream();
        while (true)
        {
            var result = await socket.ReceiveAsync(buffer, cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close)
                return null;
            message.Write(buffer, 0, result.Count);
            if (result.EndOfMessage)
                return JsonNode.Parse(Encoding.UTF8.GetString(message.ToArray()));
        }
    }

    public async ValueTask DisposeAsync() => await _app.DisposeAsync();
}
