using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Arcanist.Core.Setup;
using Arcanist.Core.Tests.Scanning;

namespace Arcanist.Core.Tests.Media;

public sealed class TranscodingTests : IAsyncLifetime
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private readonly string _music = Directory.CreateTempSubdirectory("arcanist-transcode-").FullName;
    private ArcanistFactory _factory = null!;
    private HttpClient _admin = null!;
    private string _trackId = null!;

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    private async Task StartAsync(string? ffmpeg)
    {
        AudioFixtures.WriteMusic(Path.Combine(_music, "song.wav"), new FixtureTags(Title: "Song"), seconds: 15);
        _factory = new ArcanistFactory(new Dictionary<string, string?>
        {
            ["Arcanist:Ffmpeg:Path"] = ffmpeg ?? Path.Combine(_music, "no-ffmpeg"),
            ["Arcanist:Ffmpeg:SearchSystem"] = "false",
        });
        await _factory.CompleteSetupAsync(new SetupLibrary("Music", [_music]));
        _admin = await _factory.CreateAuthenticatedClientAsync();
        await _factory.WaitForScansAsync(_admin);
        var page = await _admin.GetFromJsonAsync<JsonElement>("/rest/v1/tracks", ArcanistFactory.Json, Cancel);
        _trackId = page.GetProperty("items")[0].GetProperty("id").GetString()!;
    }

    [Fact]
    public async Task Converts_to_each_format_and_streams_it_without_ranges()
    {
        await StartAsync(TestFfmpeg.Require());

        var info = await _admin.GetFromJsonAsync<JsonElement>("/rest/v1/server-info", ArcanistFactory.Json, Cancel);
        var formats = info.GetProperty("transcodingFormats").EnumerateArray().Select(f => f.GetString()).ToList();
        Assert.Contains("MP3", formats);

        foreach (var (format, contentType, magic) in new[] { ("MP3", "audio/mpeg", (byte[]?)null), ("OPUS", "audio/ogg", "OggS"u8.ToArray()), ("AAC", "audio/aac", null) })
        {
            if (!formats.Contains(format))
                continue;

            var response = await _admin.GetAsync($"/rest/v1/stream/{_trackId}?format={format.ToLowerInvariant()}&maxBitRate=96", Cancel);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(contentType, response.Content.Headers.ContentType?.MediaType);
            Assert.Equal("none", response.Headers.AcceptRanges.Single());
            Assert.Equal("15", response.Headers.GetValues("X-Content-Duration").Single());

            var body = await response.Content.ReadAsByteArrayAsync(Cancel);
            // 15 s at 96 kbit/s is about 180 kB; the 2.6 MB WAV was really converted.
            Assert.InRange(body.Length, 50_000, 400_000);
            if (magic is not null)
                Assert.Equal(magic, body[..magic.Length]);
        }
    }

    private Task<JsonElement> TranscodingAsync() => _admin.GetFromJsonAsync<JsonElement>("/rest/v1/transcoding", ArcanistFactory.Json, Cancel);

    private Task<HttpResponseMessage> SetTranscodingAsync(bool cache, params object[] pregenerate) =>
        _admin.PutAsJsonAsync("/rest/v1/transcoding/settings", new
        {
            cacheEnabled = cache, cacheMaxBytes = 1L << 30, cacheMaxAgeDays = 30, pregenerate, pregenerateLibraries = (Guid[]?)null,
            pregenerateOnlyLossless = false,
        }, ArcanistFactory.Json, Cancel);

    /// <summary>The song converted to MP3 at 128 kbit/s, once its kept version (cached or made ahead) is served.</summary>
    private async Task<HttpResponseMessage> KeptAsync()
    {
        for (var tries = 0; ; tries++)
        {
            var response = await _admin.GetAsync($"/rest/v1/stream/{_trackId}?format=mp3&maxBitRate=128", Cancel);
            if (response.Headers.AcceptRanges.Contains("bytes") || tries == 100)
                return response;
            await response.Content.ReadAsByteArrayAsync(Cancel);
            await Task.Delay(300, Cancel);
        }
    }

    [Fact]
    public async Task Whole_conversions_are_kept_and_played_again_with_ranges()
    {
        await StartAsync(TestFfmpeg.Require());

        var live = await _admin.GetAsync($"/rest/v1/stream/{_trackId}?format=mp3&maxBitRate=128", Cancel);
        Assert.Equal("none", live.Headers.AcceptRanges.Single());
        var converted = await live.Content.ReadAsByteArrayAsync(Cancel);

        // Played again: the same audio, now a file that can be sought in.
        var kept = await KeptAsync();
        Assert.Equal(("bytes", (long?)converted.Length), (kept.Headers.AcceptRanges.Single(), kept.Content.Headers.ContentLength));
        Assert.Equal("15", kept.Headers.GetValues("X-Content-Duration").Single());
        var request = new HttpRequestMessage(HttpMethod.Get, $"/rest/v1/stream/{_trackId}?format=mp3&maxBitRate=128");
        request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(100, 199);
        var part = await _admin.SendAsync(request, Cancel);
        Assert.Equal(HttpStatusCode.PartialContent, part.StatusCode);
        Assert.Equal(converted[100..200], await part.Content.ReadAsByteArrayAsync(Cancel));
        Assert.Equal(1, (await TranscodingAsync()).GetProperty("cacheFiles").GetInt32());

        // Part of a song (a seek) is converted as before and not kept.
        var seek = await _admin.GetAsync($"/rest/v1/stream/{_trackId}?format=mp3&maxBitRate=128&timeOffset=10", Cancel);
        Assert.Equal("none", seek.Headers.AcceptRanges.Single());

        // Switched off, the cache is emptied.
        (await SetTranscodingAsync(cache: false)).EnsureSuccessStatusCode();
        Assert.Equal(0, (await TranscodingAsync()).GetProperty("cacheFiles").GetInt32());
        Assert.Equal("none", (await _admin.GetAsync($"/rest/v1/stream/{_trackId}?format=mp3&maxBitRate=128", Cancel)).Headers.AcceptRanges.Single());
    }

    [Fact]
    public async Task Versions_made_ahead_of_time_are_played_first_and_go_when_not_wanted()
    {
        await StartAsync(TestFfmpeg.Require());

        (await SetTranscodingAsync(cache: false, new { format = "MP3", bitRate = 128 })).EnsureSuccessStatusCode();
        JsonElement progress = default;
        for (var tries = 0; tries < 100; tries++)
        {
            progress = (await TranscodingAsync()).GetProperty("pregenerated");
            if (progress.GetArrayLength() == 1 && progress[0].GetProperty("done").GetInt32() == 1)
                break;
            await Task.Delay(300, Cancel);
        }

        Assert.Equal((1, 1), (progress[0].GetProperty("done").GetInt32(), progress[0].GetProperty("total").GetInt32()));
        Assert.True(progress[0].GetProperty("bytes").GetInt64() > 50_000);
        Assert.Equal("bytes", (await KeptAsync()).Headers.AcceptRanges.Single());

        // Not wanted any more: removed.
        (await SetTranscodingAsync(cache: false)).EnsureSuccessStatusCode();
        for (var tries = 0; tries < 100 && Directory.EnumerateFiles(Path.Combine(_factory.DataPath, "transcodes"), "*", SearchOption.AllDirectories).Any(); tries++)
            await Task.Delay(100, Cancel);
        Assert.Empty(Directory.EnumerateFiles(Path.Combine(_factory.DataPath, "transcodes"), "*", SearchOption.AllDirectories));

        await _factory.CreateUserAsync("listener");
        var listener = await _factory.CreateAuthenticatedClientAsync("listener");
        Assert.Equal(HttpStatusCode.Forbidden, (await listener.GetAsync("/rest/v1/transcoding", Cancel)).StatusCode);
    }

    [Fact]
    public async Task Seeks_with_a_time_offset()
    {
        await StartAsync(TestFfmpeg.Require());

        var whole = await _admin.GetByteArrayAsync($"/rest/v1/stream/{_trackId}?format=mp3&maxBitRate=128", Cancel);
        var response = await _admin.GetAsync($"/rest/v1/stream/{_trackId}?format=mp3&maxBitRate=128&timeOffset=10", Cancel);
        var tail = await response.Content.ReadAsByteArrayAsync(Cancel);

        Assert.Equal("5", response.Headers.GetValues("X-Content-Duration").Single());
        Assert.InRange(tail.Length, whole.Length / 5, whole.Length / 2);
    }

    [Fact]
    public async Task A_bit_rate_cap_the_original_meets_sends_the_original()
    {
        await StartAsync(TestFfmpeg.Path);

        // The WAV is 1411 kbit/s.
        var response = await _admin.GetAsync($"/rest/v1/stream/{_trackId}?maxBitRate=2000", Cancel);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("audio/wav", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("bytes", response.Headers.AcceptRanges.Single());
    }

    [Fact]
    public async Task Stream_tokens_work_for_converted_audio()
    {
        await StartAsync(TestFfmpeg.Require());
        var track = await _admin.GetFromJsonAsync<JsonElement>($"/rest/v1/tracks/{_trackId}", ArcanistFactory.Json, Cancel);
        var streamUrl = track.GetProperty("streamUrl").GetString()!;

        var anonymous = _factory.CreateClient();
        var response = await anonymous.GetAsync(streamUrl + "&format=mp3", Cancel);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("audio/mpeg", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Without_ffmpeg_conversion_is_unavailable_and_not_announced()
    {
        await StartAsync(null);

        var info = await _admin.GetFromJsonAsync<JsonElement>("/rest/v1/server-info", ArcanistFactory.Json, Cancel);
        Assert.Equal(0, info.GetProperty("transcodingFormats").GetArrayLength());

        var response = await _admin.GetAsync($"/rest/v1/stream/{_trackId}?format=opus", Cancel);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Contains("transcoding_unavailable", Encoding.UTF8.GetString(await response.Content.ReadAsByteArrayAsync(Cancel)));
    }

    public async ValueTask DisposeAsync()
    {
        if (_factory is not null)
            await _factory.DisposeAsync();
        Directory.Delete(_music, recursive: true);
    }
}
