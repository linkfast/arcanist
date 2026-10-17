using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Arcanist.Core.Data.Entities;
using Arcanist.Core.ServerAudio;
using Arcanist.Core.Setup;
using Arcanist.Core.Tests.Scanning;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Arcanist.Core.Tests.ServerAudio;

/// <summary>The system's audio, simulated: two devices, and a sink the tests move along.</summary>
internal sealed class FakeServerAudioOutput : IServerAudioOutput
{
    public bool IsAvailable => true;

    public string? UnavailableReason => null;

    public List<AudioDevice> DeviceList { get; } =
        [new("Speakers", "Speakers", true, 48000, 2), new("USB DAC", "USB DAC", false, 96000, 2)];

    public FakeSink? Sink { get; private set; }

    public string? OpenedWith { get; private set; }

    public int Opened { get; private set; }

    public IReadOnlyList<AudioDevice> Devices() => DeviceList;

    public IServerAudioSink Open(string? deviceId)
    {
        OpenedWith = deviceId;
        Opened++;
        var name = DeviceList.FirstOrDefault(d => d.Id == deviceId)?.Name ?? DeviceList.Single(d => d.IsDefault).Name;
        return Sink = new FakeSink(name);
    }
}

/// <summary>A device that plays what it is given until the test says an item ended.</summary>
internal sealed class FakeSink(string device) : IServerAudioSink
{
    private readonly Lock _lock = new();

    public sealed record Item(ulong Token, string? Extension, long Bytes);

    public string Device { get; } = device;

    public Item? Current { get; private set; }

    public Item? Queued { get; private set; }

    public long Position { get; private set; }

    public bool Paused { get; private set; }

    public float Volume { get; private set; } = 1;

    public bool Disposed { get; private set; }

    public List<string> Log { get; } = [];

    private static Item Read(SinkSource source)
    {
        using var stream = source.Stream;
        var bytes = 0L;
        var buffer = new byte[8192];
        int read;
        while ((read = stream.Read(buffer)) > 0)
            bytes += read;
        return new Item(source.Token, source.Extension, bytes);
    }

    public void Play(SinkSource source, long positionMs)
    {
        var item = Read(source);
        lock (_lock)
        {
            (Current, Queued, Position, Paused) = (item, null, positionMs, false);
            Log.Add($"play {item.Token} at {positionMs}");
        }
    }

    public void Queue(SinkSource source)
    {
        var item = Read(source);
        lock (_lock)
        {
            Queued = item;
            Log.Add($"queue {item.Token}");
        }
    }

    public void ClearQueue()
    {
        lock (_lock)
            Queued = null;
    }

    public bool Seek(ulong token, long positionMs)
    {
        lock (_lock)
        {
            if (Current?.Token != token)
                return false;
            Position = positionMs;
            return true;
        }
    }

    public void SetPaused(bool paused)
    {
        lock (_lock)
            Paused = paused;
    }

    public void SetVolume(float volume, bool muted)
    {
        lock (_lock)
            Volume = muted ? 0 : volume;
    }

    public void Stop()
    {
        lock (_lock)
            (Current, Queued) = (null, null);
    }

    /// <summary>The item heard ends: the queued one follows (with no gap), or the device goes idle.</summary>
    public void Finish()
    {
        lock (_lock)
            (Current, Queued, Position) = (Queued, null, 0);
    }

    public SinkStatus Status()
    {
        lock (_lock)
        {
            var state = Current is null ? "idle" : Paused ? "paused" : "playing";
            return new SinkStatus(state, Current?.Token, Position, Current?.Token, Queued?.Token, [], null);
        }
    }

    public void Dispose() => Disposed = true;
}

public sealed class ServerAudioTests : IAsyncLifetime
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private readonly string _music = Directory.CreateTempSubdirectory("arcanist-server-audio-").FullName;
    private readonly FakeServerAudioOutput _output = new();
    private readonly ArcanistFactory _factory;
    private HttpClient _admin = null!;

    public ServerAudioTests()
    {
        ServerAudioPlayer.FollowInterval = TimeSpan.FromMilliseconds(20);
        _factory = new ArcanistFactory(services: services => services.Replace(ServiceDescriptor.Singleton<IServerAudioOutput>(_output)));
    }

    public async ValueTask InitializeAsync()
    {
        AudioFixtures.WriteWav(Path.Combine(_music, "Band", "First", "01.wav"), 1, new FixtureTags("Opening", "Band", "First Album", "Band", TrackNumber: 1));
        AudioFixtures.WriteWav(Path.Combine(_music, "Band", "First", "02.wav"), 2, new FixtureTags("Closing", "Band", "First Album", "Band", TrackNumber: 2));
        AudioFixtures.WriteWav(Path.Combine(_music, "Band", "First", "03.wav"), 3, new FixtureTags("Encore", "Band", "First Album", "Band", TrackNumber: 3));
        await _factory.CompleteSetupAsync(new SetupLibrary("Music", [_music]));
        _admin = await _factory.CreateAuthenticatedClientAsync();
        await _factory.WaitForScansAsync(_admin);
        await OkAsync(_admin.PutAsJsonAsync("/rest/v1/extensions/server-audio/enabled", new { enabled = true }, ArcanistFactory.Json, Cancel));
    }

    public async ValueTask DisposeAsync()
    {
        await _factory.DisposeAsync();
        Directory.Delete(_music, recursive: true);
    }

    private async Task<Dictionary<string, string>> TracksAsync() =>
        (await _admin.GetFromJsonAsync<JsonElement>("/rest/v1/tracks", ArcanistFactory.Json, Cancel)).GetProperty("items").EnumerateArray()
        .ToDictionary(t => t.GetProperty("title").GetString()!, t => t.GetProperty("id").GetString()!);

    private static async Task Until(Func<bool> condition, string what)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, what);
            await Task.Delay(10, Cancel);
        }
    }

    private static async Task<JsonElement> StateAsync(HttpClient client) =>
        await client.GetFromJsonAsync<JsonElement>("/rest/v1/dlna/renderers/server", ArcanistFactory.Json, Cancel);

    private static async Task<JsonElement> OkAsync(Task<HttpResponseMessage> request)
    {
        var response = await request;
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode}: {await response.Content.ReadAsStringAsync(Cancel)}");
        return await response.Content.ReadFromJsonAsync<JsonElement>(ArcanistFactory.Json, Cancel);
    }

    [Fact]
    public async Task The_server_plays_on_its_device_through_play_to()
    {
        // The administrator chooses the device and the name.
        await Until(() => _output.Sink is not null, "the default device opens");
        var status = await _admin.GetFromJsonAsync<JsonElement>("/rest/v1/server-audio", ArcanistFactory.Json, Cancel);
        Assert.Equal((true, true, 2), (status.GetProperty("enabled").GetBoolean(), status.GetProperty("available").GetBoolean(), status.GetProperty("devices").GetArrayLength()));
        Assert.Equal("Speakers", status.GetProperty("device").GetString());
        var saved = await OkAsync(_admin.PutAsJsonAsync("/rest/v1/server-audio/settings", new { deviceId = "USB DAC", name = "Living room" }, ArcanistFactory.Json, Cancel));
        Assert.Equal(("USB DAC", "Living room"), (saved.GetProperty("deviceId").GetString(), saved.GetProperty("name").GetString()));
        await Until(() => _output.OpenedWith == "USB DAC" && _output.Sink is { Device: "USB DAC" }, "the chosen device opens");

        // It is a renderer of "play to" (DLNA itself is off) for those allowed to play on the server.
        var renderers = await _admin.GetFromJsonAsync<List<JsonElement>>("/rest/v1/dlna/renderers", ArcanistFactory.Json, Cancel);
        Assert.Equal(("server", "Living room"), (renderers![0].GetProperty("id").GetString(), renderers[0].GetProperty("name").GetString()));
        await _factory.CreateUserAsync("guest");
        var guest = await _factory.CreateAuthenticatedClientAsync("guest");
        Assert.Empty((await guest.GetFromJsonAsync<List<JsonElement>>("/rest/v1/dlna/renderers", ArcanistFactory.Json, Cancel))!);
        var tracks = await TracksAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await guest.PostAsJsonAsync("/rest/v1/dlna/play",
            new { rendererId = "server", trackIds = new[] { tracks["Opening"] } }, ArcanistFactory.Json, Cancel)).StatusCode);

        // A user allowed to plays an album: the first track plays and the next one is handed over ahead.
        await _factory.CreateUserAsync("ana", permissions: Permissions.PlayOnServer);
        var ana = await _factory.CreateAuthenticatedClientAsync("ana");
        var playing = await OkAsync(ana.PostAsJsonAsync("/rest/v1/dlna/play",
            new { rendererId = "server", trackIds = new[] { tracks["Opening"], tracks["Closing"], tracks["Encore"] } }, ArcanistFactory.Json, Cancel));
        Assert.Equal("Opening", playing.GetProperty("track").GetProperty("title").GetString());
        Assert.True(playing.GetProperty("startedByYou").GetBoolean());
        var sink = _output.Sink!;
        Assert.Equal("wav", sink.Current!.Extension);
        Assert.True(sink.Current.Bytes > 1000);
        await Until(() => sink.Queued is not null, "the next track is queued ahead");

        // Her queue is hers: another user (but an administrator) cannot take it over.
        await _factory.CreateUserAsync("luis", permissions: Permissions.PlayOnServer);
        var luis = await _factory.CreateAuthenticatedClientAsync("luis");
        var refused = await luis.PostAsJsonAsync("/rest/v1/dlna/control", new { rendererId = "server", action = "PAUSE" }, ArcanistFactory.Json, Cancel);
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Contains("renderer_in_use", await refused.Content.ReadAsStringAsync(Cancel));

        // The device moves on by itself: the first track counts as played, for her.
        sink.Finish();
        await Until(() => StateAsync(ana).Result.GetProperty("track").GetProperty("title").GetString() == "Closing", "the second track is heard");
        await Until(() => _admin.GetFromJsonAsync<JsonElement>($"/rest/v1/tracks/{tracks["Opening"]}", ArcanistFactory.Json, Cancel).Result
            .GetProperty("playCount").GetInt32() == 0, "the play is hers, not the administrator's");
        await Until(() => ana.GetFromJsonAsync<JsonElement>($"/rest/v1/tracks/{tracks["Opening"]}", ArcanistFactory.Json, Cancel).Result
            .GetProperty("playCount").GetInt32() == 1, "the play is recorded");

        // Pause, seek, next and the volume.
        await OkAsync(ana.PostAsJsonAsync("/rest/v1/dlna/control", new { rendererId = "server", action = "PAUSE" }, ArcanistFactory.Json, Cancel));
        Assert.True(sink.Paused);
        await OkAsync(ana.PostAsJsonAsync("/rest/v1/dlna/control", new { rendererId = "server", action = "SEEK", positionMs = 100 }, ArcanistFactory.Json, Cancel));
        Assert.Equal(100, sink.Position);
        await OkAsync(ana.PostAsJsonAsync("/rest/v1/dlna/control", new { rendererId = "server", action = "NEXT" }, ArcanistFactory.Json, Cancel));
        Assert.Equal("Encore", (await StateAsync(ana)).GetProperty("track").GetProperty("title").GetString());
        var louder = await OkAsync(ana.PutAsJsonAsync("/rest/v1/dlna/renderers/server/volume", new { volume = 40 }, ArcanistFactory.Json, Cancel));
        Assert.Equal(40, louder.GetProperty("volume").GetInt32());
        Assert.Equal(0.4f, sink.Volume, 3);

        // The queue can be changed while it plays.
        var queued = await OkAsync(ana.PostAsJsonAsync("/rest/v1/dlna/queue",
            new { rendererId = "server", action = "APPEND", trackIds = new[] { tracks["Opening"] } }, ArcanistFactory.Json, Cancel));
        Assert.Equal(4, queued.GetProperty("queue").GetArrayLength());

        // An administrator stops it; then the device can be tested.
        await OkAsync(_admin.PostAsJsonAsync("/rest/v1/dlna/control", new { rendererId = "server", action = "STOP" }, ArcanistFactory.Json, Cancel));
        Assert.Null(sink.Current);
        Assert.Equal("STOPPED", (await StateAsync(ana)).GetProperty("state").GetString());
        Assert.True((await _admin.PostAsync("/rest/v1/server-audio/test", null, Cancel)).IsSuccessStatusCode);
        Assert.Equal("wav", sink.Current?.Extension);

        // Switched off, the device is released and the server is no renderer any more.
        await OkAsync(_admin.PutAsJsonAsync("/rest/v1/extensions/server-audio/enabled", new { enabled = false }, ArcanistFactory.Json, Cancel));
        await Until(() => sink.Disposed, "the device is released");
        Assert.Empty((await ana.GetFromJsonAsync<List<JsonElement>>("/rest/v1/dlna/renderers", ArcanistFactory.Json, Cancel))!);
        var off = await ana.PostAsJsonAsync("/rest/v1/dlna/play", new { rendererId = "server", trackIds = new[] { tracks["Opening"] } }, ArcanistFactory.Json, Cancel);
        Assert.Contains("server_audio_disabled", await off.Content.ReadAsStringAsync(Cancel));
    }

    [Fact]
    public async Task A_queue_that_runs_out_stops_and_repeats_when_asked()
    {
        await Until(() => _output.Sink is not null, "the default device opens");
        var tracks = await TracksAsync();
        await OkAsync(_admin.PostAsJsonAsync("/rest/v1/dlna/play",
            new { rendererId = "server", trackIds = new[] { tracks["Closing"], tracks["Encore"] }, startIndex = 1 }, ArcanistFactory.Json, Cancel));
        var sink = _output.Sink!;
        Assert.Equal("Encore", (await StateAsync(_admin)).GetProperty("track").GetProperty("title").GetString());

        // The last track ends: nothing follows.
        sink.Finish();
        await Until(() => StateAsync(_admin).Result.GetProperty("state").GetString() == "STOPPED", "the queue stops at its end");
        Assert.Null(sink.Current);

        // Play again starts where the queue was; with repeat, the end goes back to the start.
        await OkAsync(_admin.PostAsJsonAsync("/rest/v1/dlna/control", new { rendererId = "server", action = "PLAY" }, ArcanistFactory.Json, Cancel));
        Assert.Equal("Encore", (await StateAsync(_admin)).GetProperty("track").GetProperty("title").GetString());
        await _factory.Services.GetRequiredService<ServerAudioPlayer>().SetRepeatAsync(ServerAudioRepeat.All, Cancel);
        await Until(() => sink.Queued is not null, "the first track is queued after the last");
        sink.Finish();
        await Until(() => StateAsync(_admin).Result.GetProperty("track").GetProperty("title").GetString() == "Closing", "it starts over");
    }
}
