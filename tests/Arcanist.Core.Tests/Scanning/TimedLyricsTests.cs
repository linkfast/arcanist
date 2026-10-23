using System.Net.Http.Json;
using System.Text.Json;
using Arcanist.Core.Application.Online;
using Arcanist.Core.Media;
using Arcanist.Core.Scanning;
using Arcanist.Core.Setup;
using Arcanist.Core.Tests.Online;
using Microsoft.Extensions.DependencyInjection;

namespace Arcanist.Core.Tests.Scanning;

public sealed class LrcDetectionTests
{
    [Fact]
    public void Lrc_is_told_from_plain_lyrics()
    {
        Assert.True(Lrc.LooksTimed("[ar:Band]\n[00:01.00]One\n[00:02.50]Two\n\n[00:04.00]Three"));
        Assert.False(Lrc.LooksTimed("One\nTwo\nThree"));
        // A stray time in a long text is not enough.
        Assert.False(Lrc.LooksTimed("[00:01.00]One\nTwo\nThree\nFour"));
    }

    [Theory]
    [InlineData("4.4.2-0ubuntu0.22.04.1", true)]
    [InlineData("n5.1.6", true)]
    [InlineData("6.1.1-3ubuntu5", false)]
    [InlineData("n9.0", false)]
    [InlineData("N-117234-g8f9e7b6c5a", false)]
    [InlineData(null, false)]
    public void Old_ffmpeg_releases_are_told_apart(string? version, bool outdated) =>
        Assert.Equal(outdated, FfmpegLocator.IsOutdated(version));
}

/// <summary>Synchronized lyrics from the file's tags, and timing from LRCLib for plain ones.</summary>
public sealed class TimedLyricsTests : IAsyncLifetime
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private readonly FakeOnlineServices _online = new();
    private readonly string _music = Directory.CreateTempSubdirectory("arcanist-timed-lyrics-").FullName;
    private ArcanistFactory _factory = null!;
    private HttpClient _admin = null!;

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    private async Task<string> TrackIdAsync(string title)
    {
        var page = await _admin.GetFromJsonAsync<JsonElement>($"/rest/v1/tracks?search={Uri.EscapeDataString(title)}", ArcanistFactory.Json, Cancel);
        return page.GetProperty("items")[0].GetProperty("id").GetString()!;
    }

    private Task<JsonElement> LyricsAsync(string id) =>
        _admin.GetFromJsonAsync<JsonElement>($"/rest/v1/tracks/{id}/lyrics", ArcanistFactory.Json, Cancel);

    [Fact]
    public async Task Lrc_in_the_plain_lyrics_tag_is_synchronized_and_plain_lyrics_get_lrclibs_timing()
    {
        AudioFixtures.WriteWav(Path.Combine(_music, "timed.wav"), 1,
            new FixtureTags("Timed", "Band", Lyrics: "[00:01.00]First words\n[00:02.50]Second words"));
        AudioFixtures.WriteWav(Path.Combine(_music, "plain.wav"), 2, new FixtureTags("Plain", "Band", Lyrics: "Words of the file"));
        _online.Respond = request => request.RequestUri!.ToString() is var url && url.StartsWith("https://lrclib.net/api/get") && url.Contains("track_name=Plain")
            ? FakeOnlineServices.Json("""{"plainLyrics":"Words online","syncedLyrics":"[00:03.00]Words online","instrumental":false}""")
            : new HttpResponseMessage(System.Net.HttpStatusCode.NotFound);

        _factory = new ArcanistFactory(new Dictionary<string, string?> { ["Arcanist:Online:RequestInterval"] = "00:00:00.010" },
            services: _online.Register);
        await _factory.CompleteSetupAsync(new SetupLibrary("Music", [_music]));
        _admin = await _factory.CreateAuthenticatedClientAsync();
        await _factory.WaitForScansAsync(_admin);

        var timed = await LyricsAsync(await TrackIdAsync("Timed"));
        Assert.Equal("EMBEDDED", timed.GetProperty("source").GetString());
        Assert.Equal([1000, 2500], timed.GetProperty("synced").EnumerateArray().Select(l => l.GetProperty("timeMs").GetInt32()));
        Assert.Equal("First words\nSecond words", timed.GetProperty("plain").GetString());

        var plainId = await TrackIdAsync("Plain");
        Assert.Equal(JsonValueKind.Null, (await LyricsAsync(plainId)).GetProperty("synced").ValueKind);

        // Only LRCLib on: the file's words stay, the timing comes from it.
        (await _admin.PutAsJsonAsync("/rest/v1/online-services", new SetOnlineServicesInput(false, false, false, true), Cancel)).EnsureSuccessStatusCode();
        JsonElement plain = default;
        for (var i = 0; i < 300; i++)
        {
            plain = await LyricsAsync(plainId);
            if (plain.GetProperty("synced").ValueKind == JsonValueKind.Array)
                break;
            _factory.Services.GetRequiredService<Core.Online.OnlineMonitor>().RequestRun();
            await Task.Delay(100, Cancel);
        }

        Assert.Equal("Words of the file", plain.GetProperty("plain").GetString());
        Assert.Equal(("EMBEDDED", "ONLINE"), (plain.GetProperty("source").GetString(), plain.GetProperty("syncedSource").GetString()));
        Assert.Equal(3000, plain.GetProperty("synced")[0].GetProperty("timeMs").GetInt32());
        // The track with its own timing was never looked up.
        Assert.DoesNotContain(_online.Requests, r => r.RequestUri!.ToString().Contains("track_name=Timed"));
    }

    public async ValueTask DisposeAsync()
    {
        if (_factory is not null)
            await _factory.DisposeAsync();
        Directory.Delete(_music, recursive: true);
    }
}
