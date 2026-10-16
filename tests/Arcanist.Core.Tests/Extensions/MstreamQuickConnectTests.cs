using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Arcanist.Core.Application.Mstream;
using Arcanist.Core.Application.P2P;
using Arcanist.Core.Extensions.Mstream;
using Arcanist.Core.P2P;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;

namespace Arcanist.Core.Tests.Extensions;

/// <summary>
/// mStream's quick connect: the pairing code the apps scan, and what reaches the server through their tunnel. The
/// native node is real; the tunnel's far end (an mStream app) is played by writing what the node writes to the HTTP port.
/// </summary>
public sealed class MstreamQuickConnectTests : IAsyncDisposable
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private readonly ArcanistFactory _factory;
    private HttpClient _admin = null!;

    public MstreamQuickConnectTests()
    {
        using var socket = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        _factory = new ArcanistFactory(new Dictionary<string, string?>
        {
            ["Arcanist:Extensions:mstream:Enabled"] = "true",
            ["Arcanist:P2P:Relay"] = "Local",
            ["Arcanist:P2P:BindPort"] = ((IPEndPoint)socket.Client.LocalEndPoint!).Port.ToString(),
        });
        _factory.UseKestrel(options => options.Listen(IPAddress.Loopback, 0));
    }

    private async Task StartAsync()
    {
        _factory.StartServer();
        await _factory.CompleteSetupAsync();
        _admin = await _factory.CreateAuthenticatedClientAsync();
    }

    private async Task<JsonElement> SettingsAsync(bool quickConnect, HttpStatusCode expected = HttpStatusCode.OK)
    {
        var response = await _admin.PutAsJsonAsync("/rest/v1/mstream/settings",
            new MstreamSettingsInput(1, false, Enum.GetValues<MstreamFeature>(), quickConnect), ArcanistFactory.Json, Cancel);
        var text = await response.Content.ReadAsStringAsync(Cancel);
        Assert.True(response.StatusCode == expected, text);
        return JsonDocument.Parse(text).RootElement;
    }

    /// <summary>
    /// The code once the node runs with the current secret: changes restart the node in the background, and until then
    /// there is no code (or, after a rotation, still the old one).
    /// </summary>
    private async Task<string> PairingCodeAsync(string? previous = null)
    {
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (true)
        {
            var status = await _admin.GetFromJsonAsync<JsonElement>("/rest/v1/mstream", ArcanistFactory.Json, Cancel);
            if (status.GetProperty("pairingCode").GetString() is { } code && code != previous)
                return code;
            Assert.True(DateTime.UtcNow < deadline, $"No new pairing code: {status}");
            await Task.Delay(100, Cancel);
        }
    }

    private async Task WaitForNodeAsync()
    {
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (true)
        {
            var status = await _admin.GetFromJsonAsync<JsonElement>("/rest/v1/p2p", ArcanistFactory.Json, Cancel);
            if (status.GetProperty("running").GetBoolean())
                return;
            Assert.True(DateTime.UtcNow < deadline, $"The node did not start: {status}");
            await Task.Delay(100, Cancel);
        }
    }

    /// <summary>An HTTP request as it arrives through mStream's tunnel: the node's preamble, then the request.</summary>
    private async Task<int> ThroughTunnelAsync(string path)
    {
        var address = _factory.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        using var tcp = new TcpClient();
        await tcp.ConnectAsync(IPAddress.Loopback, new Uri(address).Port, Cancel);
        var stream = tcp.GetStream();
        await stream.WriteAsync(P2PConnections.MstreamPreambleMagic, Cancel);
        await stream.WriteAsync(_factory.Services.GetRequiredService<P2PKeys>().Preamble, Cancel);
        await stream.WriteAsync(RandomNumberGenerator.GetBytes(32), Cancel);
        await stream.WriteAsync(Encoding.ASCII.GetBytes($"GET {path} HTTP/1.1\r\nHost: localhost\r\nConnection: close\r\n\r\n"), Cancel);
        using var reader = new StreamReader(stream, Encoding.ASCII);
        var status = await reader.ReadLineAsync(Cancel);
        return int.Parse(status!.Split(' ')[1]);
    }

    [Fact]
    public async Task The_apps_scan_a_code_and_reach_only_mstream_through_their_tunnel()
    {
        await StartAsync();

        // It needs the P2P network.
        await SettingsAsync(quickConnect: true, HttpStatusCode.BadRequest);
        var enabled = await _admin.PutAsJsonAsync("/rest/v1/p2p/settings",
            new P2PSettingsInput(true, P2PRelayMode.N0, null, "Home", null, null), ArcanistFactory.Json, Cancel);
        Assert.True(enabled.IsSuccessStatusCode, await enabled.Content.ReadAsStringAsync(Cancel));
        await WaitForNodeAsync();

        // Off, nothing answers through it.
        var off = await SettingsAsync(quickConnect: false);
        Assert.Equal(JsonValueKind.Null, off.GetProperty("pairingCode").ValueKind);
        Assert.Equal(404, await ThroughTunnelAsync("/api/v1/ping"));

        await SettingsAsync(quickConnect: true);
        var code = await PairingCodeAsync();
        Assert.StartsWith("mstr1:", code);
        var (ticket, secret) = MstreamQuickConnect.Read(code);
        Assert.StartsWith("endpoint", ticket);
        Assert.Equal(32, secret.Length);

        // mStream's routes answer (with their own sign-in); nothing else does.
        Assert.Equal(401, await ThroughTunnelAsync("/api/v1/ping"));
        Assert.Equal(404, await ThroughTunnelAsync("/rest/v1/me"));
        Assert.Equal(404, await ThroughTunnelAsync("/admin/"));

        // A new code: the old secret no longer opens the tunnel.
        var rotated = await _admin.PostAsync("/rest/v1/mstream/quick-connect/rotate", null, Cancel);
        Assert.True(rotated.IsSuccessStatusCode, await rotated.Content.ReadAsStringAsync(Cancel));
        var next = await PairingCodeAsync(previous: code);
        Assert.NotEqual(Convert.ToBase64String(secret), Convert.ToBase64String(MstreamQuickConnect.Read(next).Secret));
    }

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();
}
