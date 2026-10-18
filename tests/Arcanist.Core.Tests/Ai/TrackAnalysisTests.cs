using System.Buffers.Binary;
using System.Net;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text.Json;
using Arcanist.Core.Ai;
using Arcanist.Core.Analysis;
using Arcanist.Core.Data;
using Arcanist.Core.Setup;
using Arcanist.Core.Tests.Media;
using Arcanist.Core.Tests.Scanning;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Arcanist.Core.Tests.Ai;

/// <summary>The AI extension's analyses for DJ software, through the API.</summary>
public sealed class TrackAnalysisTests : IAsyncDisposable
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private readonly string _music = Directory.CreateTempSubdirectory("arcanist-ai-").FullName;
    private ArcanistFactory _factory = null!;
    private HttpClient _admin = null!;
    private Guid _fast;
    private Guid _slow;

    public async ValueTask DisposeAsync()
    {
        if (_factory is not null)
            await _factory.DisposeAsync();
        try
        {
            Directory.Delete(_music, true);
        }
        catch (IOException)
        {
        }
    }

    private async Task StartAsync(bool analyze = true)
    {
        Assert.SkipUnless(DjAudioAnalyzer.IsAvailable, "The native analyzer is not built for this platform.");
        var fast = AudioFixtures.WriteMusic(Path.Combine(_music, "A", "fast.wav"), new FixtureTags("Fast", "Band", "Tempo", "Band", TrackNumber: 1), bpm: 128, seconds: 24);
        AudioFixtures.WriteMusic(Path.Combine(_music, "A", "slow.wav"), new FixtureTags("Slow", "Band", "Tempo", "Band", TrackNumber: 2), bpm: 100, seconds: 24, chordSeed: 3);
        // The same audio twice: one analysis serves both copies.
        Directory.CreateDirectory(Path.Combine(_music, "B"));
        File.Copy(fast, Path.Combine(_music, "B", "copy.wav"));

        _factory = new ArcanistFactory(new Dictionary<string, string?> { ["Arcanist:Ai:Enabled"] = analyze ? "true" : "false" });
        await _factory.CompleteSetupAsync(new SetupLibrary("Music", [_music]));
        _admin = await _factory.CreateAuthenticatedClientAsync();
        await _factory.WaitForScansAsync(_admin);
        var tracks = (await _admin.GetFromJsonAsync<JsonElement>("/rest/v1/tracks", ArcanistFactory.Json, Cancel)).GetProperty("items").EnumerateArray()
            .ToDictionary(t => t.GetProperty("title").GetString()!, t => t.GetProperty("id").GetGuid());
        (_fast, _slow) = (tracks["Fast"], tracks["Slow"]);
    }

    private Task<HttpResponseMessage> EnableAsync(bool enabled = true) =>
        _admin.PutAsJsonAsync("/rest/v1/extensions/ai/enabled", new { enabled }, ArcanistFactory.Json, Cancel);

    private async Task<(HttpStatusCode Status, JsonElement Body)> AnalysisAsync(Guid track, string? include = null)
    {
        var response = await _admin.GetAsync($"/rest/v1/tracks/{track}/analysis{(include is null ? "" : $"?include={include}")}", Cancel);
        return (response.StatusCode, await response.Content.ReadFromJsonAsync<JsonElement>(ArcanistFactory.Json, Cancel));
    }

    private async Task<JsonElement> ReadyAsync(Guid track, Func<JsonElement, bool>? until = null)
    {
        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (true)
        {
            var (status, body) = await AnalysisAsync(track);
            if (status == HttpStatusCode.OK && body.GetProperty("status").GetString() == "READY" && (until?.Invoke(body) ?? true))
                return body;
            Assert.True(DateTime.UtcNow < deadline, $"Still {status}: {body}");
            _factory.Services.GetRequiredService<AiMonitor>().RequestRun();
            await Task.Delay(200, Cancel);
        }
    }

    [Fact]
    public async Task Is_off_until_the_extension_is_turned_on()
    {
        await StartAsync();
        var (status, body) = await AnalysisAsync(_fast);
        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.Equal("ai_disabled", body.GetProperty("code").GetString());
        var info = await _admin.GetFromJsonAsync<JsonElement>("/rest/v1/server-info", ArcanistFactory.Json, Cancel);
        Assert.DoesNotContain("TRACK_ANALYSIS", info.GetProperty("features").EnumerateArray().Select(f => f.GetString()));
        Assert.Contains("TRACK_STATS", info.GetProperty("features").EnumerateArray().Select(f => f.GetString()));
        Assert.Equal(JsonValueKind.Null, info.GetProperty("trackAnalysis").ValueKind);

        Assert.True((await EnableAsync()).IsSuccessStatusCode);
        info = await _admin.GetFromJsonAsync<JsonElement>("/rest/v1/server-info", ArcanistFactory.Json, Cancel);
        Assert.Contains("TRACK_ANALYSIS", info.GetProperty("features").EnumerateArray().Select(f => f.GetString()));
        Assert.Equal(DjAudioAnalyzer.Version, info.GetProperty("trackAnalysis").GetProperty("analysisVersion").GetInt32());
    }

    [Fact]
    public async Task Songs_wait_in_line_and_one_can_go_first()
    {
        await StartAsync(analyze: false);
        await EnableAsync();

        var (status, fast) = await AnalysisAsync(_fast);
        Assert.Equal(HttpStatusCode.Accepted, status);
        Assert.Equal("PENDING", fast.GetProperty("status").GetString());
        Assert.True(fast.GetProperty("position").GetInt32() >= 0);
        Assert.Equal(JsonValueKind.Null, fast.GetProperty("tempo").ValueKind);

        var prioritized = await (await _admin.PostAsync($"/rest/v1/tracks/{_slow}/analysis/prioritize", null, Cancel))
            .Content.ReadFromJsonAsync<JsonElement>(ArcanistFactory.Json, Cancel);
        Assert.Equal("PENDING", prioritized.GetProperty("status").GetString());
        Assert.Equal(0, prioritized.GetProperty("position").GetInt32());
        Assert.True((await AnalysisAsync(_fast)).Body.GetProperty("position").GetInt32() >= 1);
    }

    [Fact]
    public async Task Analyses_are_served_filtered_listed_and_redone()
    {
        await StartAsync();
        await EnableAsync();

        var fast = await ReadyAsync(_fast);
        await ReadyAsync(_slow);
        Assert.False(fast.GetProperty("stale").GetBoolean());
        Assert.Equal(DjAudioAnalyzer.Version, fast.GetProperty("analysisVersion").GetInt32());
        var tempo = fast.GetProperty("tempo");
        Assert.InRange(tempo.GetProperty("bpm").GetDouble(), 126, 130);
        Assert.True(fast.GetProperty("durationSeconds").GetDouble() > 23);
        Assert.NotEmpty(fast.GetProperty("sections").EnumerateArray());
        Assert.NotEmpty(Convert.FromBase64String(fast.GetProperty("energy").GetProperty("values").GetString()!));
        Assert.Equal(JsonValueKind.Null, fast.GetProperty("stats").ValueKind);

        // Only the blocks asked for.
        var (_, some) = await AnalysisAsync(_fast, "key,loudness,stats");
        Assert.Equal(JsonValueKind.Null, some.GetProperty("energy").ValueKind);
        Assert.Equal(JsonValueKind.Null, some.GetProperty("sections").ValueKind);
        Assert.NotEqual(JsonValueKind.Null, some.GetProperty("loudness").ValueKind);
        Assert.Equal(0, some.GetProperty("stats").GetProperty("plays").GetInt32());
        Assert.Equal(HttpStatusCode.BadRequest, (await AnalysisAsync(_fast, "everything")).Status);

        // One analysis for both copies of the same audio.
        await using (var db = _factory.Services.GetRequiredService<IArcanistDbContextFactory>().CreateDbContext())
            Assert.Equal(2, await db.TrackAnalyses.CountAsync(Cancel));

        // Pages, and keeping up.
        var first = await _admin.GetFromJsonAsync<JsonElement>("/rest/v1/tracks/analysis?first=1&include=key", ArcanistFactory.Json, Cancel);
        Assert.Single(first.GetProperty("items").EnumerateArray());
        var cursor = first.GetProperty("nextCursor").GetString();
        var second = await _admin.GetFromJsonAsync<JsonElement>($"/rest/v1/tracks/analysis?first=1&after={cursor}", ArcanistFactory.Json, Cancel);
        Assert.Single(second.GetProperty("items").EnumerateArray());
        Assert.Equal(JsonValueKind.Null, second.GetProperty("nextCursor").ValueKind);
        var latest = second.GetProperty("items")[0].GetProperty("analyzedAt").GetDateTime();
        var none = await _admin.GetFromJsonAsync<JsonElement>($"/rest/v1/tracks/analysis?updatedSince={Uri.EscapeDataString(latest.ToString("O"))}",
            ArcanistFactory.Json, Cancel);
        Assert.Empty(none.GetProperty("items").EnumerateArray());

        // A newer analyzer: the old analyses are served as stale and redone, and listeners hear it.
        var events = _factory.Services.GetRequiredService<TrackAnalysisEvents>();
        var token = (await _factory.LoginAsync()).AccessToken;
        using var socket = await ConnectAsync(token);
        await SendAsync(socket, new { id = "1", type = "subscribe", payload = new { query = "subscription { trackAnalysisReady { trackId analysisVersion } }" } });
        while (events.SubscriberCount == 0)
            await Task.Delay(10, Cancel);

        await using (var db = _factory.Services.GetRequiredService<IArcanistDbContextFactory>().CreateDbContext())
            await db.TrackAnalyses.ExecuteUpdateAsync(s => s.SetProperty(a => a.AnalysisVersion, 0), Cancel);
        var stale = (await AnalysisAsync(_fast)).Body;
        Assert.Equal("READY", stale.GetProperty("status").GetString());
        Assert.True(stale.GetProperty("stale").GetBoolean());
        _factory.Services.GetRequiredService<AiMonitor>().RequestRun();

        var ready = await ReceiveAsync(socket);
        Assert.Equal("next", ready.GetProperty("type").GetString());
        var notice = ready.GetProperty("payload").GetProperty("data").GetProperty("trackAnalysisReady");
        Assert.Contains(notice.GetProperty("trackId").GetGuid(), new[] { _fast, _slow });
        await ReadyAsync(_fast, b => !b.GetProperty("stale").GetBoolean());
    }

    [Fact]
    public async Task Serves_short_windows_of_audio()
    {
        TestFfmpeg.Require();
        await StartAsync(analyze: false);
        await EnableAsync();

        var response = await _admin.GetAsync($"/rest/v1/tracks/{_fast}/audio-window?start=2&duration=1.5&format=pcm16", Cancel);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync(Cancel));
        Assert.Equal("audio/wav", response.Content.Headers.ContentType?.MediaType);
        var wav = await response.Content.ReadAsByteArrayAsync(Cancel);
        Assert.Equal("RIFF"u8.ToArray(), wav[..4]);
        Assert.Equal(44100, BinaryPrimitives.ReadInt32LittleEndian(wav.AsSpan(24)));
        var data = BinaryPrimitives.ReadInt32LittleEndian(wav.AsSpan(40));
        Assert.Equal(wav.Length - 44, data);
        Assert.InRange(data, (int)(1.45 * 44100 * 4), (int)(1.55 * 44100 * 4));
        // Not silence: the music is there.
        Assert.Contains(wav.Skip(44).Take(4000), b => b != 0);

        Assert.Equal(HttpStatusCode.BadRequest, (await _admin.GetAsync($"/rest/v1/tracks/{_fast}/audio-window?start=0&duration=9", Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _admin.GetAsync($"/rest/v1/tracks/{_fast}/audio-window?start=999&duration=1", Cancel)).StatusCode);
    }

    [Fact]
    public async Task Songs_kept_elsewhere_are_analyzed_when_asked_for()
    {
        Assert.SkipUnless(DjAudioAnalyzer.IsAvailable, "The native analyzer is not built for this platform.");
        using var cloud = new Cloud.FakeUltimateCloud();
        cloud.AddTrack("c1", "Cloud song", "Haze", "Sky", 1, AudioFixtures.Music(128, 20), format: "WAV");
        AudioFixtures.WriteWav(Path.Combine(_music, "local.wav"), 1, new FixtureTags("Local", "Band", "Here", "Band"));
        _factory = new ArcanistFactory(new Dictionary<string, string?>
        {
            ["Arcanist:UltimateCloud:ApiUrl"] = Cloud.FakeUltimateCloud.ApiUrl,
            ["Arcanist:UltimateCloud:Authority"] = Cloud.FakeUltimateCloud.Authority,
        }, services: cloud.Register);
        await _factory.CompleteSetupAsync(new SetupLibrary("Music", [_music]));
        _admin = await _factory.CreateAuthenticatedClientAsync();
        await _admin.GraphQLAsync("mutation { startUltimateCloudLink { state } }");
        cloud.Approve = true;
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while ((await _admin.GraphQLAsync("query { ultimateCloud { state plan { name } } }")).GetProperty("data").GetProperty("ultimateCloud") is var state
               && (state.GetProperty("state").GetString() != "LINKED" || state.GetProperty("plan").ValueKind == JsonValueKind.Null))
        {
            Assert.True(DateTime.UtcNow < deadline, "Not linked");
            await Task.Delay(100, Cancel);
        }

        var added = await _admin.GraphQLAsync("mutation { addUltimateCloudLibrary(input: {collection: LIBRARY}) { libraries { libraryId } } }");
        Assert.True(added.GetProperty("data").ValueKind == JsonValueKind.Object, added.ToString());
        await _factory.WaitForScansAsync(_admin);
        var titles = (await _admin.GetFromJsonAsync<JsonElement>("/rest/v1/tracks", ArcanistFactory.Json, Cancel)).GetProperty("items").EnumerateArray().ToList();
        var song = titles.SingleOrDefault(t => t.GetProperty("title").GetString() == "Cloud song");
        Assert.True(song.ValueKind == JsonValueKind.Object, string.Join(", ", titles.Select(t => t.GetProperty("title").GetString())));
        await EnableAsync();

        // Not walked in the background: asking for it puts it in line, and it is downloaded once to be analyzed.
        var id = song.GetProperty("id").GetGuid();
        var (status, pending) = await AnalysisAsync(id);
        Assert.True(status is HttpStatusCode.Accepted or HttpStatusCode.OK, pending.ToString());
        var ready = await ReadyAsync(id);
        Assert.InRange(ready.GetProperty("tempo").GetProperty("bpm").GetDouble(), 126, 130);
        await using var db = _factory.Services.GetRequiredService<IArcanistDbContextFactory>().CreateDbContext();
        Assert.Empty(await db.TrackAnalysisRequests.ToListAsync(Cancel));
    }

    private async Task<WebSocket> ConnectAsync(string token)
    {
        var client = _factory.Server.CreateWebSocketClient();
        client.SubProtocols.Add("graphql-transport-ws");
        var socket = await client.ConnectAsync(new Uri(_factory.Server.BaseAddress, "graphql"), Cancel);
        await SendAsync(socket, new { type = "connection_init", payload = new Dictionary<string, string> { ["Authorization"] = $"Bearer {token}" } });
        Assert.Equal("connection_ack", (await ReceiveAsync(socket)).GetProperty("type").GetString());
        return socket;
    }

    private static Task SendAsync(WebSocket socket, object message) =>
        socket.SendAsync(JsonSerializer.SerializeToUtf8Bytes(message), WebSocketMessageType.Text, true, Cancel);

    private static async Task<JsonElement> ReceiveAsync(WebSocket socket)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Cancel);
        timeout.CancelAfter(TimeSpan.FromSeconds(60));
        var buffer = new byte[64 * 1024];
        using var message = new MemoryStream();
        WebSocketReceiveResult result;
        do
        {
            result = await socket.ReceiveAsync(buffer, timeout.Token);
            message.Write(buffer, 0, result.Count);
        } while (!result.EndOfMessage);

        return JsonDocument.Parse(message.ToArray()).RootElement.Clone();
    }
}
