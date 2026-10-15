using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Arcanist.Core.Application.P2P;
using Arcanist.Core.P2P;
using Microsoft.Extensions.DependencyInjection;

namespace Arcanist.Core.Tests.Cloud;

/// <summary>
/// The transparent P2P connection of the Cloud plan: the server registers its node in Ultimate Cloud, trusts the
/// account's devices. A real node, on a real port, without anything outside this machine.
/// </summary>
public sealed class UltimateCloudConnectTests : IAsyncDisposable
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private static readonly string Phone = new('a', 64);
    private static readonly string Laptop = new('b', 64);

    private readonly FakeUltimateCloud _cloud = new();
    private ArcanistFactory _factory = null!;
    private HttpClient _admin = null!;

    private async Task StartAsync(string plan = "Cloud")
    {
        _cloud.Plan = plan;
        _cloud.DeviceNodes[Phone] = "Ana's phone";
        _cloud.DeviceNodes[Laptop] = "Ana's laptop";

        var nodePort = FreeUdpPort();
        _factory = new ArcanistFactory(new Dictionary<string, string?>
        {
            ["Arcanist:UltimateCloud:ApiUrl"] = FakeUltimateCloud.ApiUrl,
            ["Arcanist:UltimateCloud:Authority"] = FakeUltimateCloud.Authority,
            ["Arcanist:P2P:Relay"] = "Local",
            ["Arcanist:P2P:BindPort"] = nodePort.ToString(),
        }, services: _cloud.Register);
        _factory.UseKestrel(options => options.Listen(IPAddress.Loopback, 0));
        _factory.StartServer();
        await _factory.CompleteSetupAsync();
        _admin = await _factory.CreateAuthenticatedClientAsync();

        var enabled = await _admin.PutAsJsonAsync("/rest/v1/p2p/settings",
            new P2PSettingsInput(true, P2PRelayMode.N0, null, "Home server", null, null), ArcanistFactory.Json, Cancel);
        Assert.True(enabled.IsSuccessStatusCode, await enabled.Content.ReadAsStringAsync(Cancel));
        await WaitAsync(async () => (await Status()).GetProperty("running").GetBoolean(), "The node did not start");

        await _admin.GraphQLAsync("mutation { startUltimateCloudLink { state } }");
        _cloud.Approve = true;
        await WaitAsync(async () => (await _admin.GraphQLAsync("query { ultimateCloud { plan { name } } }"))
            .GetProperty("data").GetProperty("ultimateCloud").GetProperty("plan").ValueKind != JsonValueKind.Null, "The account was not linked");
    }

    private async Task<JsonElement> Status() =>
        await _admin.GetFromJsonAsync<JsonElement>("/rest/v1/p2p", ArcanistFactory.Json, Cancel);

    private async Task<List<JsonElement>> PeersAsync() =>
        (await _admin.GraphQLAsync("query { networkPeers { nodeId name source kind revokedAt rules { allowDownload } } }"))
        .GetProperty("data").GetProperty("networkPeers").EnumerateArray().ToList();

    private static async Task WaitAsync(Func<Task<bool>> done, string message)
    {
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (!await done())
        {
            Assert.True(DateTime.UtcNow < deadline, message);
            await Task.Delay(100, Cancel);
        }
    }

    [Fact]
    public async Task The_account_s_devices_are_trusted_and_the_server_is_registered()
    {
        await StartAsync();
        await WaitAsync(async () => (await PeersAsync()).Count == 2, "The account's devices were not trusted");

        var peers = await PeersAsync();
        Assert.All(peers, p => Assert.Equal(("ULTIMATE_CLOUD", "DEVICE", true), (p.GetProperty("source").GetString(), p.GetProperty("kind").GetString(),
            p.GetProperty("rules").GetProperty("allowDownload").GetBoolean())));
        Assert.Equal(["Ana's laptop", "Ana's phone"], peers.Select(p => p.GetProperty("name").GetString()).Order());

        // The server's own node, with a ticket the apps can connect with.
        var nodeId = (await Status()).GetProperty("nodeId").GetString()!;
        Assert.Equal("Home server", _cloud.RegisteredNodes[nodeId]);
        var ticket = _cloud.RegisteredTickets.Last()!.Replace('-', '+').Replace('_', '/');
        ticket += new string('=', (4 - ticket.Length % 4) % 4);
        Assert.Equal(nodeId, JsonDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(ticket))).RootElement.GetProperty("id").GetString());

        // A device that leaves the account is no longer trusted.
        _cloud.DeviceNodes.TryRemove(Laptop, out _);
        await _admin.GraphQLAsync("mutation { refreshUltimateCloud { tier } }");
        Assert.Equal([Phone], (await PeersAsync()).Select(p => p.GetProperty("nodeId").GetString()));

        // Without the Cloud plan, none are.
        _cloud.Plan = "Standard";
        await _admin.GraphQLAsync("mutation { refreshUltimateCloud { tier } }");
        Assert.Empty(await PeersAsync());
    }

    private static int FreeUdpPort()
    {
        using var socket = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        return ((IPEndPoint)socket.Client.LocalEndPoint!).Port;
    }

    public async ValueTask DisposeAsync()
    {
        if (_factory is not null)
            await _factory.DisposeAsync();
    }
}
