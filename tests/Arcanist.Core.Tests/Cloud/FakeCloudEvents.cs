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
/// acknowledges the connection, takes the <c>cloudLibraryChanged</c> subscription and sends what a test publishes.
/// </summary>
public sealed class FakeCloudEvents : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly ConcurrentDictionary<WebSocket, string> _subscriptions = new();

    private FakeCloudEvents(WebApplication app) => _app = app;

    /// <summary>The <c>connection_init</c> payloads received.</summary>
    public ConcurrentQueue<JsonNode?> Inits { get; } = new();

    public string Url => _app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First()
        .Replace("http://", "ws://") + "/graphql";

    public bool Subscribed => !_subscriptions.IsEmpty;

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
                        await SendAsync(socket, new { type = "connection_ack" }, cancellationToken);
                        break;
                    case "subscribe":
                        _subscriptions[socket] = message["id"]!.GetValue<string>();
                        break;
                }
            }
        }
        catch (Exception e) when (e is WebSocketException or OperationCanceledException)
        {
        }
        finally
        {
            _subscriptions.TryRemove(socket, out _);
        }
    }

    /// <summary>Sends a <c>cloudLibraryChanged</c> event to every subscriber.</summary>
    public async Task PublishAsync(string library, string[]? added = null, string[]? updated = null, string[]? removed = null)
    {
        foreach (var (socket, id) in _subscriptions)
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
