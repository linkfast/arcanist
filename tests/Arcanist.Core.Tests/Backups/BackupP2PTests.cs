using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using Arcanist.Core.Application.P2P;
using Arcanist.Core.Data.Entities;
using Arcanist.Core.P2P;
using Arcanist.Core.Setup;
using Arcanist.Core.Tests.Scanning;

namespace Arcanist.Core.Tests.Backups;

/// <summary>
/// Backups kept by another Arcanist server through the P2P network: real servers (Kestrel) whose nodes reach each
/// other on this machine (<c>Arcanist:P2P:Relay=Local</c>).
/// </summary>
public sealed class BackupP2PTests : IAsyncDisposable
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private readonly List<IAsyncDisposable> _disposables = [];
    private readonly List<string> _folders = [];

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
        while (!(await admin.GetFromJsonAsync<JsonElement>("/rest/v1/p2p", ArcanistFactory.Json, Cancel)).GetProperty("running").GetBoolean())
        {
            Assert.True(DateTime.UtcNow < deadline, "The node did not start.");
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
    private static string Local(string link, int nodePort)
    {
        var data = P2PLinks.Parse(link)!;
        return P2PLinks.Build(data.Kind, data.Ticket with { Relays = null, Addrs = [$"127.0.0.1:{nodePort}"] }, data.Token, data.Name, data.Backup);
    }

    private string Folder()
    {
        var folder = Directory.CreateTempSubdirectory("arcanist-backup-p2p-").FullName;
        _folders.Add(folder);
        return folder;
    }

    private static async Task<JsonElement> PostAsync(HttpClient client, string path, object body, HttpStatusCode expected = HttpStatusCode.OK)
    {
        var response = await client.PostAsJsonAsync(path, body, ArcanistFactory.Json, Cancel);
        var text = await response.Content.ReadAsStringAsync(Cancel);
        Assert.True(response.StatusCode == expected, text);
        return JsonDocument.Parse(text).RootElement;
    }

    private static async Task<JsonElement> RunAsync(HttpClient admin, Guid backupId)
    {
        var id = (await PostAsync(admin, $"/rest/v1/backups/{backupId}/run", new { })).GetProperty("id").GetGuid();
        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (true)
        {
            var run = await admin.GetFromJsonAsync<JsonElement>($"/rest/v1/backups/runs/{id}", ArcanistFactory.Json, Cancel);
            if (run.GetProperty("finishedAt").ValueKind != JsonValueKind.Null)
                return run;
            Assert.True(DateTime.UtcNow < deadline, $"The run did not end: {run}");
            await Task.Delay(100, Cancel);
        }
    }

    [Fact]
    public async Task Another_server_keeps_the_backups_until_it_stops()
    {
        Assert.SkipUnless(NativeP2P.IsAvailable, "The P2P library is not built for this platform.");
        var music = Folder();
        AudioFixtures.WriteWav(Path.Combine(music, "Band", "Pulse", "01 Alpha.wav"), 1, new FixtureTags("Alpha", "Band", "Pulse", TrackNumber: 1));
        AudioFixtures.WriteWav(Path.Combine(music, "Band", "Pulse", "02 Beta.wav"), 2, new FixtureTags("Beta", "Band", "Pulse", TrackNumber: 2));
        var keeper = await StartServerAsync();
        var owner = await StartServerAsync(new SetupLibrary("Music", [music]));
        await owner.Factory.WaitForScansAsync(owner.Admin);

        // The keeper invites the owner to keep its backups in a folder of its own, with a quota.
        var kept = Folder();
        var invitation = await PostAsync(keeper.Admin, "/rest/v1/backups/invitations", new { name = "Owner", folder = kept, quotaGb = 1 });
        var link = Local(invitation.GetProperty("link").GetString()!, keeper.NodePort);
        Assert.StartsWith("arcanist://p2p?kind=backup&", link);
        // A backup invitation is not a share.
        await PostAsync(owner.Admin, "/rest/v1/p2p/shares", new { link }, HttpStatusCode.BadRequest);

        var destination = await PostAsync(owner.Admin, "/rest/v1/backups/destinations", new { name = "", kind = "PEER", invitation = link });
        Assert.Equal("PEER", destination.GetProperty("kind").GetString());
        var backup = await PostAsync(owner.Admin, "/rest/v1/backups", new
        {
            name = "Music", destinationId = destination.GetProperty("id").GetGuid(), scope = "SERVER", includeUserData = true,
            includeServer = true, password = "correct horse battery", schedule = "MANUAL", retentionDays = 30, enabled = true,
        });
        var backupId = backup.GetProperty("id").GetGuid();
        var root = Path.Combine(kept, backup.GetProperty("folder").GetString()!);

        var first = await RunAsync(owner.Admin, backupId);
        Assert.Equal("COMPLETED", first.GetProperty("status").GetString());
        Assert.Equal(File.ReadAllBytes(Path.Combine(music, "Band", "Pulse", "01 Alpha.wav")), File.ReadAllBytes(Path.Combine(root, "current", "Music", "Band", "Pulse", "01 Alpha.wav")));
        Assert.True(File.Exists(Path.Combine(root, "snapshots", first.GetProperty("snapshot").GetString()!, "server.arcb")));

        // Unchanged files are not sent again; a change is, and its previous copy stays as an old version.
        var alpha = Path.Combine(music, "Band", "Pulse", "01 Alpha.wav");
        AudioFixtures.WriteWav(alpha, 9, new FixtureTags("Alpha", "Band", "Pulse", TrackNumber: 1));
        File.SetLastWriteTimeUtc(alpha, DateTime.UtcNow.AddMinutes(1));
        var second = await RunAsync(owner.Admin, backupId);
        Assert.Equal((1, 1, 1), (second.GetProperty("copied").GetInt32(), second.GetProperty("unchanged").GetInt32(), second.GetProperty("versioned").GetInt32()));
        Assert.True(File.Exists(Path.Combine(root, "versions", second.GetProperty("snapshot").GetString()!, "Music", "Band", "Pulse", "01 Alpha.wav")));

        // The keeper sees whose backups it keeps, and how much they take.
        var guests = await keeper.Admin.GetFromJsonAsync<JsonElement>("/rest/v1/backups/guests", ArcanistFactory.Json, Cancel);
        var guest = Assert.Single(guests.EnumerateArray());
        Assert.True(guest.GetProperty("usedBytes").GetInt64() > 0);
        Assert.Equal(1024L * 1024 * 1024, guest.GetProperty("quotaBytes").GetInt64());

        // Revoked, it keeps nothing more.
        var revoked = await keeper.Admin.PostAsync($"/rest/v1/p2p/peers/{guest.GetProperty("peerId").GetGuid()}/revoke", null, Cancel);
        Assert.True(revoked.IsSuccessStatusCode);
        var refused = await RunAsync(owner.Admin, backupId);
        Assert.Equal(("FAILED", "backups_not_accepted"), (refused.GetProperty("status").GetString(), refused.GetProperty("errorCode").GetString()));
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
}
