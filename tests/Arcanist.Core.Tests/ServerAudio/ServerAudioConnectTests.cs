using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Arcanist.Core.ServerAudio;
using Arcanist.Core.Setup;
using Arcanist.Core.Tests.Cloud;
using Arcanist.Core.Tests.Scanning;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Arcanist.Core.Tests.ServerAudio;

/// <summary>Server audio as an Ultimate Connect device of the linked account, controlled through Ultimate Cloud.</summary>
public sealed class ServerAudioConnectTests : IAsyncLifetime
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private readonly string _music = Directory.CreateTempSubdirectory("arcanist-server-audio-connect-").FullName;
    private readonly FakeServerAudioOutput _output = new();
    private readonly FakeUltimateCloud _cloud = new();
    private FakeCloudEvents _events = null!;
    private ArcanistFactory _factory = null!;
    private HttpClient _admin = null!;

    public async ValueTask InitializeAsync()
    {
        ServerAudioPlayer.FollowInterval = TimeSpan.FromMilliseconds(20);
        _events = await FakeCloudEvents.StartAsync();
        _factory = new ArcanistFactory(new Dictionary<string, string?>
        {
            ["Arcanist:UltimateCloud:ApiUrl"] = FakeUltimateCloud.ApiUrl,
            ["Arcanist:UltimateCloud:Authority"] = FakeUltimateCloud.Authority,
            ["Arcanist:UltimateCloud:EventsUrl"] = _events.Url,
        }, services: services =>
        {
            _cloud.Register(services);
            services.Replace(ServiceDescriptor.Singleton<IServerAudioOutput>(_output));
        });

        AudioFixtures.WriteWav(Path.Combine(_music, "Band", "First", "01.wav"), 1, new FixtureTags("Opening", "Band", "First Album", "Band", TrackNumber: 1));
        AudioFixtures.WriteWav(Path.Combine(_music, "Band", "First", "02.wav"), 2, new FixtureTags("Closing", "Band", "First Album", "Band", TrackNumber: 2));
        await _factory.CompleteSetupAsync(new SetupLibrary("Music", [_music]));
        _admin = await _factory.CreateAuthenticatedClientAsync();
        await _factory.WaitForScansAsync(_admin);

        await _admin.GraphQLAsync("mutation { startUltimateCloudLink { state } }");
        _cloud.Approve = true;
        await Until(async () => (await _admin.GraphQLAsync("query { ultimateCloud { plan { name } } }"))
            .GetProperty("data").GetProperty("ultimateCloud").GetProperty("plan").ValueKind != JsonValueKind.Null, "the account is linked");
    }

    public async ValueTask DisposeAsync()
    {
        await _factory.DisposeAsync();
        await _events.DisposeAsync();
        Directory.Delete(_music, recursive: true);
    }

    private static async Task Until(Func<Task<bool>> condition, string what)
    {
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (!await condition())
        {
            Assert.True(DateTime.UtcNow < deadline, what);
            await Task.Delay(20, Cancel);
        }
    }

    private static Task Until(Func<bool> condition, string what) => Until(() => Task.FromResult(condition()), what);

    private FakeUltimateCloud.ConnectCall? LastState(string commandId) =>
        _cloud.ConnectCalls.LastOrDefault(c => c.Operation == "publishPlaybackState" && c.Variables?["s"]?["lastCommandId"]?.GetValue<string>() == commandId);

    private async Task<JsonNode> CommandAsync(string deviceId, string commandId, object command)
    {
        var body = JsonSerializer.SerializeToNode(command)!.AsObject();
        body["commandId"] = commandId;
        await _events.SendCommandAsync(deviceId, body);
        await Until(() => LastState(commandId) is not null, $"command {commandId} is acknowledged");
        return LastState(commandId)!.Variables!["s"]!;
    }

    private async Task<Dictionary<string, string>> TracksAsync() =>
        (await _admin.GetFromJsonAsync<JsonElement>("/rest/v1/tracks", ArcanistFactory.Json, Cancel)).GetProperty("items").EnumerateArray()
        .ToDictionary(t => t.GetProperty("title").GetString()!, t => t.GetProperty("id").GetString()!);

    [Fact]
    public async Task The_account_s_apps_control_the_server_s_audio()
    {
        // Off, it is no device of the account.
        await Task.Delay(500, Cancel);
        Assert.DoesNotContain(_cloud.ConnectCalls, c => c.Operation == "touchSyncedDevice");

        // On: registered with its own device id and name, online with what it accepts.
        Assert.True((await _admin.PutAsJsonAsync("/rest/v1/extensions/server-audio/enabled", new { enabled = true }, ArcanistFactory.Json, Cancel)).IsSuccessStatusCode);
        await Until(() => _cloud.ConnectCalls.Any(c => c.Operation == "connectHeartbeat"), "the device goes online");
        var touch = _cloud.ConnectCalls.First(c => c.Operation == "touchSyncedDevice");
        var device = touch.DeviceId;
        Assert.StartsWith("arcanist-audio-", device);
        Assert.Equal("user-1", touch.Account);
        Assert.Equal($"Arcanist ({Environment.MachineName})", touch.Variables!["deviceName"]!.GetValue<string>());
        Assert.Contains("PLAY_ITEMS", _cloud.ConnectCalls.First(c => c.Operation == "connectHeartbeat").Variables!["c"]!.AsArray().Select(c => c!.GetValue<string>()));
        await Until(() => _events.Listening(device), "it listens to its commands");
        await Until(async () => (await _admin.GetFromJsonAsync<JsonElement>("/rest/v1/server-audio", ArcanistFactory.Json, Cancel))
            .GetProperty("connectAccounts").GetInt32() == 1, "the panel says it is a device of the account");
        Assert.Contains(_events.Inits, i => i?["deviceId"]?.GetValue<string>() == device && i["clientApp"]?.GetValue<string>() == "arcanist-server-audio");

        // A song of this server by its id, one it does not have, and one by title and artist: the two it has play.
        var tracks = await TracksAsync();
        var state = await CommandAsync(device, "c1", new
        {
            type = "PLAY_ITEMS",
            startIndex = 0,
            items = new object[]
            {
                new { localTrackId = tracks["Opening"] },
                new { acoustId = Guid.NewGuid().ToString(), title = "Elsewhere", artist = "Nobody" },
                new { title = "closing", artist = "BAND" },
            },
        });
        Assert.Null(state["lastCommandError"]);
        Assert.Equal(2, state["queueLength"]!.GetValue<int>());
        Assert.Equal("Opening", state["track"]!["title"]!.GetValue<string>());
        Assert.Equal("Band", state["track"]!["artist"]!.GetValue<string>());
        Assert.Equal("Closing", state["upNext"]![0]!["title"]!.GetValue<string>());
        var sink = _output.Sink!;
        Assert.NotNull(sink.Current);

        // Pause, the volume and repeat.
        state = await CommandAsync(device, "c2", new { type = "PAUSE" });
        Assert.Equal("PAUSED", state["status"]!.GetValue<string>());
        Assert.True(sink.Paused);
        state = await CommandAsync(device, "c3", new { type = "SET_VOLUME", volume = 0.25 });
        Assert.Equal(0.25, state["volume"]!.GetValue<double>(), 3);
        Assert.Equal(0.25f, sink.Volume, 3);
        state = await CommandAsync(device, "c4", new { type = "SET_REPEAT", repeat = "ALL" });
        Assert.Equal("ALL", state["repeat"]!.GetValue<string>());
        state = await CommandAsync(device, "c5", new { type = "TOGGLE_PLAY" });
        Assert.Equal("PLAYING", state["status"]!.GetValue<string>());

        // A command sent twice runs once: the second "next" would have gone back to the first song.
        state = await CommandAsync(device, "c6", new { type = "NEXT" });
        Assert.Equal("Closing", state["track"]!["title"]!.GetValue<string>());
        await _events.SendCommandAsync(device, new { commandId = "c6", type = "NEXT" });
        state = await CommandAsync(device, "c7", new { type = "SET_SHUFFLE", shuffle = false });
        Assert.Equal("Closing", state["track"]!["title"]!.GetValue<string>());

        // What it cannot do comes back as the command's error.
        state = await CommandAsync(device, "c8", new { type = "PLAY_ITEMS", items = new[] { new { title = "Elsewhere", artist = "Nobody" } } });
        Assert.Equal("This server does not have those songs.", state["lastCommandError"]!.GetValue<string>());
        state = await CommandAsync(device, "c9", new { type = "PLAY_PLAYLIST", playlistId = "cloud-playlist" });
        Assert.Equal("This device does not support that command.", state["lastCommandError"]!.GetValue<string>());

        // The plays are those of the administrator who linked the account.
        sink.Finish();
        await Until(async () => (await _admin.GetFromJsonAsync<JsonElement>($"/rest/v1/tracks/{tracks["Closing"]}", ArcanistFactory.Json, Cancel))
            .GetProperty("playCount").GetInt32() == 1, "the play is recorded");

        // Renamed, it is registered again with the new name.
        Assert.True((await _admin.PutAsJsonAsync("/rest/v1/server-audio/settings", new { deviceId = (string?)null, name = "Living room" }, ArcanistFactory.Json, Cancel))
            .IsSuccessStatusCode);
        await Until(() => _cloud.ConnectCalls.Any(c => c.Operation == "touchSyncedDevice" && c.Variables!["deviceName"]!.GetValue<string>() == "Living room"),
            "the new name is registered");

        // Switched off, it goes offline.
        Assert.True((await _admin.PutAsJsonAsync("/rest/v1/extensions/server-audio/enabled", new { enabled = false }, ArcanistFactory.Json, Cancel)).IsSuccessStatusCode);
        await Until(() => _cloud.ConnectCalls.Any(c => c.Operation == "goOfflineForConnect" && c.DeviceId == device), "the device goes offline");
        await Until(() => !_events.Listening(device), "it stops listening");
    }
}
