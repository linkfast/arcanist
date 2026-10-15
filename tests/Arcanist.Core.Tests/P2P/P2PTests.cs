using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;
using Arcanist.Core.Application.P2P;
using Arcanist.Core.Api.Rest;
using Arcanist.Core.P2P;
using Arcanist.Core.Setup;
using Arcanist.Core.Tests.Scanning;

namespace Arcanist.Core.Tests.P2P;

/// <summary>
/// The P2P network end to end: real servers (Kestrel) whose nodes reach each other on this machine, without relays
/// or address lookup (<c>Arcanist:P2P:Relay=Local</c>).
/// </summary>
public sealed class P2PTests : IAsyncDisposable
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private readonly List<IAsyncDisposable> _disposables = [];
    private readonly List<string> _folders = [];

    /// <summary>A server listening on a real port, with its P2P node on a known UDP port.</summary>
    private sealed record Server(ArcanistFactory Factory, int NodePort, HttpClient Admin);

    private async Task<Server> StartServerAsync(params SetupLibrary[] libraries)
    {
        var nodePort = FreeUdpPort();
        var factory = new ArcanistFactory(new Dictionary<string, string?>
        {
            ["Arcanist:P2P:Relay"] = "Local",
            ["Arcanist:P2P:BindPort"] = nodePort.ToString(),
        });
        _disposables.Add(factory);
        factory.UseKestrel(options => options.Listen(IPAddress.Loopback, 0));
        factory.StartServer();
        await factory.CompleteSetupAsync(libraries);
        var admin = await factory.CreateAuthenticatedClientAsync();

        var enabled = await admin.PutAsJsonAsync("/rest/v1/p2p/settings",
            new P2PSettingsInput(true, P2PRelayMode.N0, null, $"Server {nodePort}", null, null), ArcanistFactory.Json, Cancel);
        Assert.True(enabled.IsSuccessStatusCode, await enabled.Content.ReadAsStringAsync(Cancel));
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (true)
        {
            var status = await admin.GetFromJsonAsync<JsonElement>("/rest/v1/p2p", ArcanistFactory.Json, Cancel);
            if (status.GetProperty("running").GetBoolean())
                break;
            Assert.True(DateTime.UtcNow < deadline, $"The node did not start: {status}");
            await Task.Delay(100, Cancel);
        }

        return new Server(factory, nodePort, admin);
    }

    private static int FreeUdpPort()
    {
        using var socket = new UdpClient(new IPEndPoint(IPAddress.Any, 0));
        return ((IPEndPoint)socket.Client.LocalEndPoint!).Port;
    }

    /// <summary>The invitation with the server's loopback address only, so it connects on any machine.</summary>
    private static (string Link, P2PInvitationData Data) Local(string link, int nodePort)
    {
        var data = P2PLinks.Parse(link)!;
        var ticket = data.Ticket with { Relays = null, Addrs = [$"127.0.0.1:{nodePort}"] };
        return (P2PLinks.Build(data.Kind, ticket, data.Token, data.Name), data with { Ticket = ticket });
    }

    private async Task<string> InviteAsync(HttpClient admin, string path, object input)
    {
        var response = await admin.PostAsJsonAsync(path, input, ArcanistFactory.Json, Cancel);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync(Cancel));
        return (await response.Content.ReadFromJsonAsync<JsonElement>(ArcanistFactory.Json, Cancel)).GetProperty("link").GetString()!;
    }

    /// <summary>An app's node: it only connects.</summary>
    private NativeP2P.Node StartDevice()
    {
        var node = NativeP2P.Node.Start(new NodeConfig(P2PRelay.Local, null, null, null, null), RandomNumberGenerator.GetBytes(32));
        _disposables.Add(new Disposer(node.Dispose));
        return node;
    }

    private string Music(params (string Path, FixtureTags Tags)[] files)
    {
        var folder = Directory.CreateTempSubdirectory("arcanist-p2p-").FullName;
        _folders.Add(folder);
        var seconds = 1;
        foreach (var (path, tags) in files)
            AudioFixtures.WriteWav(Path.Combine(folder, path), seconds++, tags);
        return folder;
    }

    [Fact]
    public async Task A_device_pairs_signs_in_through_the_network_and_can_be_revoked()
    {
        Assert.SkipUnless(NativeP2P.IsAvailable, "The P2P library is not built for this platform.");
        var server = await StartServerAsync(new SetupLibrary("Music", [Music(("a.wav", new FixtureTags("Alone", "Zed", "Solo", "Zed")))]));
        var (_, invitation) = Local(await InviteAsync(server.Admin, "/rest/v1/p2p/invitations/device", new CreateDeviceInvitationInput("Phone", null)),
            server.NodePort);

        var device = StartDevice();
        var port = device.Connect(invitation.Ticket, TimeSpan.FromSeconds(10));
        var tunnel = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}") };

        // Not paired yet: only the handshake answers.
        var unknown = await tunnel.GetAsync("/rest/v1/libraries", Cancel);
        Assert.Equal(HttpStatusCode.Forbidden, unknown.StatusCode);
        Assert.Contains("peer_unknown", await unknown.Content.ReadAsStringAsync(Cancel));
        Assert.Equal(HttpStatusCode.Forbidden, (await tunnel.PostAsJsonAsync("/rest/v1/p2p/pair", new PairDeviceRequest("wrong", null), Cancel)).StatusCode);

        var paired = await tunnel.PostAsJsonAsync("/rest/v1/p2p/pair", new PairDeviceRequest(invitation.Token, "Pixel"), Cancel);
        Assert.True(paired.IsSuccessStatusCode, await paired.Content.ReadAsStringAsync(Cancel));
        // An invitation works once.
        Assert.Equal(HttpStatusCode.Forbidden, (await tunnel.PostAsJsonAsync("/rest/v1/p2p/pair", new PairDeviceRequest(invitation.Token, null), Cancel)).StatusCode);

        // A paired device is a client like any other: it signs in.
        Assert.Equal(HttpStatusCode.Unauthorized, (await tunnel.GetAsync("/rest/v1/libraries", Cancel)).StatusCode);
        var login = await tunnel.PostAsJsonAsync("/rest/v1/auth/login", new LoginRequest(ArcanistFactory.AdminUserName, ArcanistFactory.Password, "phone"),
            ArcanistFactory.Json, Cancel);
        var token = (await login.Content.ReadFromJsonAsync<TokenResponse>(ArcanistFactory.Json, Cancel))!.AccessToken;
        tunnel.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var libraries = await tunnel.GetFromJsonAsync<List<JsonElement>>("/rest/v1/libraries", ArcanistFactory.Json, Cancel);
        Assert.Equal("Music", Assert.Single(libraries!).GetProperty("name").GetString());
        // The server-to-server API is not for devices.
        Assert.Equal(HttpStatusCode.NotFound, (await tunnel.GetAsync("/rest/v1/peer/info", Cancel)).StatusCode);

        var peers = await server.Admin.GetFromJsonAsync<List<JsonElement>>("/rest/v1/p2p/peers", ArcanistFactory.Json, Cancel);
        var peer = Assert.Single(peers!);
        Assert.Equal(("Pixel", "DEVICE", true), (peer.GetProperty("name").GetString(), peer.GetProperty("kind").GetString(), peer.GetProperty("online").GetBoolean()));
        Assert.True(peer.GetProperty("bytesSent").GetInt64() > 0);

        var revoked = await server.Admin.PostAsync($"/rest/v1/p2p/peers/{peer.GetProperty("id").GetString()}/revoke", null, Cancel);
        Assert.True(revoked.IsSuccessStatusCode);
        var refused = await tunnel.GetAsync("/rest/v1/libraries", Cancel);
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        Assert.Contains("peer_revoked", await refused.Content.ReadAsStringAsync(Cancel));

        var activity = await server.Admin.GetFromJsonAsync<List<JsonElement>>("/rest/v1/p2p/activity", ArcanistFactory.Json, Cancel);
        Assert.Contains(activity!, a => a.GetProperty("kind").GetString() == "JOINED");
        Assert.Contains(activity!, a => a.GetProperty("kind").GetString() == "REVOKED");
    }

    [Fact]
    public async Task A_share_becomes_a_library_that_streams_from_its_owner_under_its_rules()
    {
        Assert.SkipUnless(NativeP2P.IsAvailable, "The P2P library is not built for this platform.");
        var music = Music(
            ("Zed/01.wav", new FixtureTags("Alone", "Zed", "Solo", "Zed", TrackNumber: 1)),
            ("Zed/02.wav", new FixtureTags("Again", "Zed", "Solo", "Zed", TrackNumber: 2)));
        var owner = await StartServerAsync(new SetupLibrary("Música", [music]));
        await owner.Factory.WaitForScansAsync(owner.Admin);
        var receiver = await StartServerAsync();
        var ownerLibrary = (await owner.Admin.GetFromJsonAsync<List<JsonElement>>("/rest/v1/libraries", ArcanistFactory.Json, Cancel))![0]
            .GetProperty("id").GetGuid();

        var (link, _) = Local(await InviteAsync(owner.Admin, "/rest/v1/p2p/invitations/share",
            new CreateShareInvitationInput("Ana", [ownerLibrary], new PeerRulesInput(false, null, null, 1), null)), owner.NodePort);
        var accepted = await receiver.Admin.PostAsJsonAsync("/rest/v1/p2p/shares", new AcceptShareInput(link), ArcanistFactory.Json, Cancel);
        Assert.True(accepted.IsSuccessStatusCode, await accepted.Content.ReadAsStringAsync(Cancel));
        var share = await accepted.Content.ReadFromJsonAsync<JsonElement>(ArcanistFactory.Json, Cancel);
        Assert.StartsWith("Server", share.GetProperty("name").GetString());
        await receiver.Factory.WaitForScansAsync(receiver.Admin);

        // The owner's files are in the receiver's catalog, described by the owner.
        var tracks = (await receiver.Admin.GetFromJsonAsync<JsonElement>("/rest/v1/tracks", ArcanistFactory.Json, Cancel)).GetProperty("items")
            .EnumerateArray().ToDictionary(t => t.GetProperty("title").GetString()!);
        Assert.Equal(["Again", "Alone"], tracks.Keys.Order());
        Assert.Equal("Solo", tracks["Alone"].GetProperty("album").GetProperty("title").GetString());

        // Played from the owner, byte for byte, ranges included.
        var alone = tracks["Alone"].GetProperty("id").GetString();
        var original = await File.ReadAllBytesAsync(Path.Combine(music, "Zed", "01.wav"), Cancel);
        var whole = await receiver.Admin.GetAsync($"/rest/v1/stream/{alone}", Cancel);
        Assert.True(whole.IsSuccessStatusCode, $"{(int)whole.StatusCode} {await whole.Content.ReadAsStringAsync(Cancel)}");
        Assert.Equal(original, await whole.Content.ReadAsByteArrayAsync(Cancel));
        using (var range = new HttpRequestMessage(HttpMethod.Get, $"/rest/v1/stream/{alone}"))
        {
            range.Headers.Range = new RangeHeaderValue(10, 19);
            var partial = await receiver.Admin.SendAsync(range, Cancel);
            Assert.Equal(HttpStatusCode.PartialContent, partial.StatusCode);
            Assert.Equal(original[10..20], await partial.Content.ReadAsByteArrayAsync(Cancel));
        }

        // The owner does not allow downloads, whatever the receiver's own permissions say.
        var download = await receiver.Admin.GetAsync($"/rest/v1/stream/{alone}?download=true", Cancel);
        Assert.Equal(HttpStatusCode.Forbidden, download.StatusCode);
        Assert.Contains("download_not_allowed", await download.Content.ReadAsStringAsync(Cancel));

        var shared = Assert.Single((await owner.Admin.GetFromJsonAsync<List<JsonElement>>("/rest/v1/p2p/peers?kind=SERVER", ArcanistFactory.Json, Cancel))!);
        Assert.Equal("Ana", shared.GetProperty("name").GetString());
        Assert.True(shared.GetProperty("bytesSent").GetInt64() >= original.Length);

        var played = await owner.Admin.GetFromJsonAsync<List<JsonElement>>("/rest/v1/p2p/activity", ArcanistFactory.Json, Cancel);
        Assert.Contains(played!, a => a.GetProperty("kind").GetString() == "STREAMED" && a.GetProperty("detail").GetString() == "01.wav");

        // Revoked: nothing plays any more, and the receiver learns it on its next scan.
        await owner.Admin.PostAsync($"/rest/v1/p2p/peers/{shared.GetProperty("id").GetString()}/revoke", null, Cancel);
        Assert.False((await receiver.Admin.GetAsync($"/rest/v1/stream/{alone}", Cancel)).IsSuccessStatusCode);
        var libraryId = share.GetProperty("libraryId").GetString();
        await receiver.Admin.PostAsJsonAsync($"/rest/v1/libraries/{libraryId}/scans", new { full = false }, Cancel);
        await receiver.Factory.WaitForScansAsync(receiver.Admin);
        var received = Assert.Single((await receiver.Admin.GetFromJsonAsync<List<JsonElement>>("/rest/v1/p2p/shares", ArcanistFactory.Json, Cancel))!);
        Assert.False(received.GetProperty("active").GetBoolean());
        // Nothing was marked missing: the catalog stays as it was.
        Assert.Equal(2, (await receiver.Admin.GetFromJsonAsync<JsonElement>("/rest/v1/tracks", ArcanistFactory.Json, Cancel)).GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task Links_round_trip_and_tunnelled_requests_get_an_untrusted_address()
    {
        var ticket = new NodeTicket(new string('a', 64), ["https://relay.example/"], ["192.0.2.1:1234"]);
        var link = P2PLinks.Build(Arcanist.Core.Data.Entities.P2PPeerKind.Server, ticket, "t0k-en_", "Casa de Ana & Luis");
        var parsed = P2PLinks.Parse(link)!;
        Assert.Equal((Arcanist.Core.Data.Entities.P2PPeerKind.Server, "t0k-en_", "Casa de Ana & Luis"), (parsed.Kind, parsed.Token, parsed.Name));
        Assert.Equal(ticket.Id, parsed.Ticket.Id);
        Assert.Equal(ticket.Addrs, parsed.Ticket.Addrs);
        Assert.Null(P2PLinks.Parse("arcanist://p2p?kind=device&node=bad&token=x"));

        var address = P2PGateMiddleware.AddressOf(new string('f', 64));
        Assert.False(IPAddress.IsLoopback(address));
        Assert.Equal("100::ffff:ffff:ffff:ffff", address.ToString());
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var disposable in Enumerable.Reverse(_disposables))
            await disposable.DisposeAsync();
        foreach (var folder in _folders)
        {
            try
            {
                Directory.Delete(folder, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    private sealed class Disposer(Action dispose) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            dispose();
            return ValueTask.CompletedTask;
        }
    }
}
