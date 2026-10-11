using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Arcanist.Core.Api.Rest;
using Arcanist.Core.Application.Libraries;
using Arcanist.Core.Application.Scanning;
using Arcanist.Core.Setup;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;

namespace Arcanist.Core.Tests.Scanning;

public sealed class ScanLiveUpdatesTests : IAsyncLifetime
{
    private readonly string _root = Directory.CreateTempSubdirectory("arcanist-live").FullName;
    private readonly FakeTimeProvider _time = new(DateTimeOffset.UtcNow);
    private readonly ArcanistFactory _factory;
    private HttpClient _admin = null!;
    private Guid _libraryId;

    public ScanLiveUpdatesTests() => _factory = new ArcanistFactory(time: _time);

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        for (var i = 0; i < 3; i++)
            AudioFixtures.WriteWav(Path.Combine(_root, "Album", $"{i}.wav"), i, new FixtureTags($"Song {i}", "Artist", "Album", TrackNumber: i + 1));

        await _factory.CompleteSetupAsync(new SetupLibrary("Music", [_root]));
        _admin = await _factory.CreateAuthenticatedClientAsync();
        await _factory.WaitForScansAsync(_admin);
        _libraryId = (await _admin.GetFromJsonAsync<List<Library>>("/rest/v1/libraries", ArcanistFactory.Json, Cancel))![0].Id;
    }

    public async ValueTask DisposeAsync()
    {
        await _factory.DisposeAsync();
        Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task Subscribers_receive_scan_updates_until_completion()
    {
        var token = (await _factory.LoginAsync()).AccessToken;
        using var socket = await ConnectAsync(token);
        await SendAsync(socket, new
        {
            id = "1",
            type = "subscribe",
            payload = new { query = "subscription { scanJobUpdated { id status filesSeen progress { phase } } }" },
        });

        // The protocol does not acknowledge subscriptions; wait until it is live so no event is missed.
        var events = _factory.Services.GetRequiredService<ScanEvents>();
        while (events.SubscriberCount == 0)
            await Task.Delay(10, Cancel);

        await _admin.PostAsJsonAsync($"/rest/v1/libraries/{_libraryId}/scans", new StartScanInput(Full: true), Cancel);

        var statuses = new List<string>();
        while (!statuses.Contains("COMPLETED"))
        {
            var message = await ReceiveAsync(socket);
            Assert.True(message.GetProperty("type").GetString() == "next", message.ToString());
            var data = message.GetProperty("payload").GetProperty("data");
            Assert.True(data.ValueKind == JsonValueKind.Object, message.ToString());
            var job = data.GetProperty("scanJobUpdated");
            statuses.Add(job.GetProperty("status").GetString()!);
        }

        Assert.Equal("QUEUED", statuses[0]);
        Assert.Contains("RUNNING", statuses);
        Assert.Equal("COMPLETED", statuses[^1]);
    }

    [Fact]
    public async Task Subscribing_without_a_token_is_refused()
    {
        using var socket = await ConnectAsync(token: null);
        await SendAsync(socket, new { id = "1", type = "subscribe", payload = new { query = "subscription { scanJobUpdated { id } }" } });

        var message = await ReceiveAsync(socket);

        // The operation completes with an error result instead of a stream.
        var payload = message.GetProperty("payload");
        Assert.Equal(JsonValueKind.Null, payload.GetProperty("data").ValueKind);
        Assert.Equal("unauthorized", payload.GetProperty("errors")[0].GetProperty("extensions").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Libraries_with_an_interval_are_scanned_when_it_elapses()
    {
        await _admin.PutAsJsonAsync($"/rest/v1/libraries/{_libraryId}", new UpdateLibraryInput("Music", true, 6), ArcanistFactory.Json, Cancel);
        var scheduler = _factory.Services.GetServices<IHostedService>().OfType<Arcanist.Core.Scanning.ScanScheduler>().Single();

        await scheduler.QueueDueScansAsync(Cancel);
        Assert.Single(await JobsAsync());

        _time.Advance(TimeSpan.FromHours(6));
        await scheduler.QueueDueScansAsync(Cancel);

        var admin = await _factory.CreateAuthenticatedClientAsync();
        var jobs = await admin.GetFromJsonAsync<List<JsonElement>>("/rest/v1/scans", ArcanistFactory.Json, Cancel);
        Assert.Equal(2, jobs!.Count);
        Assert.Equal("INCREMENTAL", jobs[0].GetProperty("kind").GetString());
    }

    private async Task<List<JsonElement>> JobsAsync() =>
        (await _admin.GetFromJsonAsync<List<JsonElement>>("/rest/v1/scans", ArcanistFactory.Json, Cancel))!;

    private async Task<WebSocket> ConnectAsync(string? token)
    {
        var client = _factory.Server.CreateWebSocketClient();
        client.SubProtocols.Add("graphql-transport-ws");
        var socket = await client.ConnectAsync(new Uri(_factory.Server.BaseAddress, "graphql"), Cancel);

        await SendAsync(socket, new
        {
            type = "connection_init",
            payload = token is null ? new Dictionary<string, string>() : new Dictionary<string, string> { ["Authorization"] = $"Bearer {token}" },
        });
        Assert.Equal("connection_ack", (await ReceiveAsync(socket)).GetProperty("type").GetString());
        return socket;
    }

    private static Task SendAsync(WebSocket socket, object message) =>
        socket.SendAsync(JsonSerializer.SerializeToUtf8Bytes(message), WebSocketMessageType.Text, true, Cancel);

    private static async Task<JsonElement> ReceiveAsync(WebSocket socket)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Cancel);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));

        var buffer = new byte[64 * 1024];
        using var message = new MemoryStream();
        WebSocketReceiveResult result;
        do
        {
            result = await socket.ReceiveAsync(buffer, timeout.Token);
            message.Write(buffer, 0, result.Count);
        }
        while (!result.EndOfMessage);

        var json = JsonDocument.Parse(Encoding.UTF8.GetString(message.ToArray())).RootElement;
        // Keep-alive pings are not interesting here.
        return json.GetProperty("type").GetString() is "ping" or "pong" ? await ReceiveAsync(socket) : json;
    }
}
