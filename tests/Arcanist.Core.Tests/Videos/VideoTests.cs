using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Xml.Linq;
using Arcanist.Core.Analysis;
using Arcanist.Core.Data.Entities;
using Arcanist.Core.Setup;
using Arcanist.Core.Tests.Media;
using Arcanist.Core.Tests.Scanning;

namespace Arcanist.Core.Tests.Videos;

/// <summary>
/// Music videos with a real ffmpeg: scanned next to their songs or named after them, aligned with the song's audio,
/// streamed with the song's audio or their own (the switch keeping the timeline), prepared for HLS and uploaded.
/// </summary>
public sealed class VideoTests : IAsyncLifetime
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private readonly string _music = Directory.CreateTempSubdirectory("arcanist-videos-").FullName;
    private readonly string _work = Directory.CreateTempSubdirectory("arcanist-videos-work-").FullName;
    private ArcanistFactory _factory = null!;
    private HttpClient _admin = null!;
    private string _ffmpeg = null!;

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    /// <summary>
    /// A library with a song (40 s of melody) and, next to it, its video: 5 s of a scene, the song's first 30 s,
    /// 4 s of a scene. Also a video named after the song, a live one of another performance and one of nothing here.
    /// </summary>
    private async Task StartAsync()
    {
        _ffmpeg = TestFfmpeg.Require();
        if (!AudioAligner.IsAvailable)
            Assert.Skip("the native library has no alignment");

        var album = Path.Combine(_music, "Band", "Album");
        Directory.CreateDirectory(album);
        await File.WriteAllBytesAsync(Path.Combine(album, "01 Song.wav"), AudioFixtures.Wav(AudioFixtures.Melody(3, 40)), Cancel);
        AudioFixtures.Tag(Path.Combine(album, "01 Song.wav"), new FixtureTags(Title: "Song", Artist: "Band", Album: "Album", AlbumArtist: "Band", TrackNumber: 1));

        MakeVideo(Path.Combine(album, "01 Song.mp4"), [AudioFixtures.Noise(1, 5), AudioFixtures.Melody(3, 30), AudioFixtures.Noise(2, 4)]);
        MakeVideo(Path.Combine(_music, "Videos", "Band - Song (Official Video).mp4"), [AudioFixtures.Noise(5, 3), AudioFixtures.Melody(3, 20)]);
        MakeVideo(Path.Combine(_music, "Videos", "Band - Song (Live).mp4"), [AudioFixtures.Melody(9, 20)]);
        MakeVideo(Path.Combine(_music, "Videos", "Nobody - Nothing.mp4"), [AudioFixtures.Melody(11, 10)]);

        _factory = new ArcanistFactory(new Dictionary<string, string?>
        {
            ["Arcanist:Analysis:Enabled"] = "true",
            ["Arcanist:Ffmpeg:Path"] = _ffmpeg,
            ["Arcanist:Ffmpeg:SearchSystem"] = "false",
            ["Arcanist:Uploads:ScanDelay"] = "00:00:01",
            ["Arcanist:Extensions:dlna:Enabled"] = "true",
            ["Arcanist:Dlna:Port"] = "8484",
            ["Arcanist:Extensions:subsonic:Enabled"] = "true",
        });
        await _factory.CompleteSetupAsync(new SetupLibrary("Music", [_music]));
        _admin = await _factory.CreateAuthenticatedClientAsync();
        await _factory.WaitForScansAsync(_admin);
    }

    /// <summary>An MP4 (H.264 320×240 and AAC) whose audio is the given pieces one after another.</summary>
    private void MakeVideo(string path, float[][] audio)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var wav = Path.Combine(_work, $"{Guid.NewGuid():N}.wav");
        File.WriteAllBytes(wav, AudioFixtures.Wav(audio.SelectMany(a => a).ToArray()));
        var seconds = audio.Sum(a => a.Length) / 44100.0;
        Run(_ffmpeg, "-hide_banner", "-loglevel", "error", "-y", "-f", "lavfi", "-i", $"testsrc=size=320x240:rate=25:duration={seconds:0.###}",
            "-i", wav, "-c:v", "libx264", "-preset", "ultrafast", "-pix_fmt", "yuv420p", "-c:a", "aac", "-b:a", "160k", "-shortest", path);
    }

    private static void Run(string program, params string[] arguments)
    {
        var start = new ProcessStartInfo(program) { UseShellExecute = false, RedirectStandardError = true };
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var errors = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, errors);
    }

    private async Task<JsonElement> VideosAsync(string query = "") =>
        await _admin.GetFromJsonAsync<JsonElement>($"/rest/v1/videos{query}", ArcanistFactory.Json, Cancel);

    private static JsonElement Named(JsonElement page, string fileEnd) =>
        page.GetProperty("items").EnumerateArray().Single(v => v.GetProperty("path").GetString()!.EndsWith(fileEnd, StringComparison.Ordinal));

    /// <summary>The video once its alignment is done (READY or NO_MATCH).</summary>
    private async Task<JsonElement> AlignedAsync(string id)
    {
        for (var tries = 0; ; tries++)
        {
            var video = await _admin.GetFromJsonAsync<JsonElement>($"/rest/v1/videos/{id}", ArcanistFactory.Json, Cancel);
            if (video.GetProperty("sync").GetString() is "READY" or "NO_MATCH" || tries == 300)
                return video;
            await Task.Delay(200, Cancel);
        }
    }

    [Fact]
    public async Task Videos_are_linked_to_their_songs_and_aligned_with_them()
    {
        await StartAsync();

        var page = await VideosAsync();
        Assert.Equal(4, page.GetProperty("totalCount").GetInt32());
        var sidecar = Named(page, "Album/01 Song.mp4");
        var named = Named(page, "Band - Song (Official Video).mp4");
        var live = Named(page, "Band - Song (Live).mp4");
        var nothing = Named(page, "Nobody - Nothing.mp4");

        Assert.Equal("SIDECAR", sidecar.GetProperty("linkMethod").GetString());
        Assert.Equal("TAGS", named.GetProperty("linkMethod").GetString());
        Assert.Equal("Song", named.GetProperty("title").GetString());
        Assert.Equal("Band", named.GetProperty("artist").GetString());
        // A live video does not go with the studio song; nothing here is "Nothing".
        Assert.True(live.GetProperty("isLive").GetBoolean());
        Assert.Equal(JsonValueKind.Null, live.GetProperty("trackId").ValueKind);
        Assert.Equal(JsonValueKind.Null, nothing.GetProperty("trackId").ValueKind);
        Assert.Equal("UNAVAILABLE", nothing.GetProperty("sync").GetString());
        Assert.Equal(320, sidecar.GetProperty("width").GetInt32());
        Assert.NotEqual(JsonValueKind.Null, sidecar.GetProperty("artworkId").ValueKind);

        // The track lists its videos.
        var trackId = sidecar.GetProperty("trackId").GetString()!;
        var track = await _admin.GetFromJsonAsync<JsonElement>($"/rest/v1/tracks/{trackId}", ArcanistFactory.Json, Cancel);
        Assert.Equal(2, track.GetProperty("videos").GetArrayLength());

        // Aligned: the scene, the song from its start, the scene.
        var aligned = await AlignedAsync(sidecar.GetProperty("id").GetString()!);
        Assert.Equal("READY", aligned.GetProperty("sync").GetString());
        Assert.InRange(aligned.GetProperty("coverage").GetDouble(), 0.7, 0.9);
        var segments = aligned.GetProperty("segments").EnumerateArray().ToList();
        Assert.Equal(["VIDEO", "TRACK", "VIDEO"], segments.Select(s => s.GetProperty("source").GetString()));
        var song = segments[1];
        Assert.InRange(song.GetProperty("videoStartMs").GetInt32(), 4600, 5500);
        Assert.InRange(song.GetProperty("videoEndMs").GetInt32(), 34_400, 35_400);
        Assert.InRange(song.GetProperty("trackStartMs").GetInt32() - (song.GetProperty("videoStartMs").GetInt32() - 5000), -60, 60);

        // Filters.
        Assert.Equal(1, (await VideosAsync("?live=true")).GetProperty("totalCount").GetInt32());
        Assert.Equal(2, (await VideosAsync("?linked=false")).GetProperty("totalCount").GetInt32());
        Assert.Equal(2, (await VideosAsync($"?trackId={trackId}")).GetProperty("totalCount").GetInt32());
        Assert.Equal(1, (await VideosAsync("?search=nothing")).GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task Streams_with_the_songs_audio_or_its_own_and_follows_the_users_choice()
    {
        await StartAsync();
        var sidecar = Named(await VideosAsync(), "Album/01 Song.mp4");
        var video = await AlignedAsync(sidecar.GetProperty("id").GetString()!);
        Assert.Equal("TRACK", video.GetProperty("audioMode").GetString());

        // Its own audio: the MP4 as it is, with ranges.
        var own = await _admin.GetAsync(video.GetProperty("videoAudioUrl").GetString(), Cancel);
        Assert.Equal(HttpStatusCode.OK, own.StatusCode);
        Assert.Equal("video", own.Headers.GetValues("X-Arcanist-Video-Audio").Single());
        Assert.Contains("bytes", own.Headers.AcceptRanges);
        var range = new HttpRequestMessage(HttpMethod.Get, video.GetProperty("videoAudioUrl").GetString()) { Headers = { Range = new RangeHeaderValue(0, 99) } };
        Assert.Equal(HttpStatusCode.PartialContent, (await _admin.SendAsync(range, Cancel)).StatusCode);

        // The song's: mixed while it streams, then kept and served with ranges.
        var mixed = await _admin.GetAsync(video.GetProperty("streamUrl").GetString(), Cancel);
        Assert.Equal(HttpStatusCode.OK, mixed.StatusCode);
        Assert.Equal("track", mixed.Headers.GetValues("X-Arcanist-Video-Audio").Single());
        Assert.Equal("none", mixed.Headers.AcceptRanges.Single());
        var file = Path.Combine(_work, "mixed.mp4");
        await File.WriteAllBytesAsync(file, await mixed.Content.ReadAsByteArrayAsync(Cancel), Cancel);

        // What plays in the middle is the song from the library: its audio aligns all along with the song.
        using (var aligner = AudioAligner.Create()!)
        {
            Assert.Null(Arcanist.Core.Media.FfmpegDecoder.Align(_ffmpeg, aligner, AudioAligner.Video, file, null, Cancel));
            await using (var songFile = File.OpenRead(Path.Combine(_music, "Band", "Album", "01 Song.wav")))
                Assert.Equal(AnalyzerStatus.Ok, aligner.Decode(AudioAligner.Track, songFile, ".wav"));
            var outcome = aligner.Finish();
            using var result = JsonDocument.Parse(outcome.Json!);
            Assert.InRange(result.RootElement.GetProperty("coverage").GetDouble(), 0.7, 0.92);
        }

        HttpResponseMessage kept;
        for (var tries = 0; ; tries++)
        {
            kept = await _admin.GetAsync(video.GetProperty("trackAudioUrl").GetString(), Cancel);
            if (kept.Headers.AcceptRanges.Contains("bytes") || tries == 100)
                break;
            await kept.Content.ReadAsByteArrayAsync(Cancel);
            await Task.Delay(300, Cancel);
        }
        Assert.Contains("bytes", kept.Headers.AcceptRanges);

        // The user prefers the video's own audio.
        var set = await _admin.PutAsJsonAsync("/rest/v1/me/video-settings", new { audio = "VIDEO" }, ArcanistFactory.Json, Cancel);
        var mine = await set.Content.ReadFromJsonAsync<JsonElement>(ArcanistFactory.Json, Cancel);
        Assert.Equal(("VIDEO", "TRACK"), (mine.GetProperty("effective").GetString(), mine.GetProperty("serverDefault").GetString()));
        var again = await _admin.GetFromJsonAsync<JsonElement>($"/rest/v1/videos/{video.GetProperty("id").GetString()}", ArcanistFactory.Json, Cancel);
        Assert.Equal("VIDEO", again.GetProperty("audioMode").GetString());
        Assert.Contains("audio=video", again.GetProperty("streamUrl").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, (await _admin.GetAsync(video.GetProperty("streamUrl").GetString() + "&audio=other", Cancel)).StatusCode);
    }

    [Fact]
    public async Task A_played_video_is_prepared_for_adaptive_streaming_with_both_audios()
    {
        await StartAsync();
        var sidecar = Named(await VideosAsync(), "Album/01 Song.mp4");
        var id = sidecar.GetProperty("id").GetString()!;
        var video = await AlignedAsync(id);
        await (await _admin.GetAsync(video.GetProperty("videoAudioUrl").GetString(), Cancel)).Content.ReadAsByteArrayAsync(Cancel);

        string? hls = null;
        for (var tries = 0; tries < 300 && hls is null; tries++)
        {
            var current = await _admin.GetFromJsonAsync<JsonElement>($"/rest/v1/videos/{id}", ArcanistFactory.Json, Cancel);
            hls = current.GetProperty("hlsUrl").GetString();
            if (hls is null)
                await Task.Delay(300, Cancel);
        }
        Assert.NotNull(hls);

        var master = await _admin.GetStringAsync(hls, Cancel);
        Assert.Contains("NAME=\"Song\",LANGUAGE=\"und\",DEFAULT=YES", master);
        Assert.Contains("NAME=\"Video\",LANGUAGE=\"und\",DEFAULT=NO", master);
        // Every URI carries the token, so players that cannot send headers (Safari) work.
        var basePath = hls[..hls.LastIndexOf('/')];
        var variant = master.Split('\n').First(l => l.StartsWith("v-", StringComparison.Ordinal));
        Assert.Contains("st=", variant);
        var playlist = await _admin.GetStringAsync($"{basePath}/{variant}", Cancel);
        var segment = playlist.Split('\n').First(l => l.StartsWith("seg", StringComparison.Ordinal));
        Assert.Contains("st=", segment);
        // The init segment the playlist maps is there too.
        var init = System.Text.RegularExpressions.Regex.Match(playlist, "EXT-X-MAP:URI=\"([^\"]+)\"").Groups[1].Value;
        Assert.StartsWith("init.mp4?st=", init);
        Assert.NotEmpty(await _admin.GetByteArrayAsync($"{basePath}/{variant[..variant.IndexOf('/')]}/{init}", Cancel));
        var bytes = await _admin.GetByteArrayAsync($"{basePath}/{variant[..variant.IndexOf('/')]}/{segment}", Cancel);
        Assert.NotEmpty(bytes);
        Assert.Equal(HttpStatusCode.NotFound, (await _admin.GetAsync($"{basePath}/../keys/x", Cancel)).StatusCode);
    }

    [Fact]
    public async Task Videos_are_uploaded_next_to_their_song_and_managed()
    {
        await StartAsync();
        var page = await VideosAsync();
        var sidecar = Named(page, "Album/01 Song.mp4");
        var trackId = sidecar.GetProperty("trackId").GetString()!;

        // Another cut of the video, uploaded for the track: it lands next to the song and is linked by the next scan.
        var upload = Path.Combine(_work, "cut.mp4");
        MakeVideo(upload, [AudioFixtures.Noise(7, 2), AudioFixtures.Melody(3, 12)]);
        await using (var body = File.OpenRead(upload))
        {
            var response = await _admin.PostAsync($"/rest/v1/tracks/{trackId}/video?fileName=cut.mp4&client=web", new StreamContent(body), Cancel);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            var result = await response.Content.ReadFromJsonAsync<JsonElement>(ArcanistFactory.Json, Cancel);
            Assert.Equal("Band/Album/01 Song (2).mp4", result.GetProperty("path").GetString());
        }
        Assert.Equal(HttpStatusCode.BadRequest, (await _admin.PostAsync($"/rest/v1/tracks/{trackId}/video?fileName=cut.wav", new ByteArrayContent([1]), Cancel)).StatusCode);

        // Linked by hand to nothing, then given back to the scans.
        var live = Named(page, "Band - Song (Live).mp4").GetProperty("id").GetString()!;
        var linked = await (await _admin.PutAsJsonAsync($"/rest/v1/videos/{live}/link", new { trackId }, ArcanistFactory.Json, Cancel))
            .Content.ReadFromJsonAsync<JsonElement>(ArcanistFactory.Json, Cancel);
        Assert.Equal(("MANUAL", true), (linked.GetProperty("linkMethod").GetString(), linked.GetProperty("linkLocked").GetBoolean()));
        var unlocked = await (await _admin.PostAsync($"/rest/v1/videos/{live}/unlock", null, Cancel)).Content.ReadFromJsonAsync<JsonElement>(ArcanistFactory.Json, Cancel);
        Assert.Equal(JsonValueKind.Null, unlocked.GetProperty("trackId").ValueKind);

        // A user who only listens sees the videos (without their paths) but cannot manage them.
        await _factory.CreateUserAsync("guest");
        var guest = await _factory.CreateAuthenticatedClientAsync("guest");
        var seen = await guest.GetFromJsonAsync<JsonElement>($"/rest/v1/videos/{live}", ArcanistFactory.Json, Cancel);
        Assert.Equal((JsonValueKind.Null, false), (seen.GetProperty("path").ValueKind, seen.GetProperty("canManage").GetBoolean()));
        Assert.Equal(HttpStatusCode.Forbidden, (await guest.PostAsync($"/rest/v1/videos/{live}/realign", null, Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await guest.DeleteAsync($"/rest/v1/videos/{live}", Cancel)).StatusCode);

        // Deleted: to the trash, out of the catalog.
        var deleted = await _admin.DeleteAsync($"/rest/v1/videos/{live}", Cancel);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.False(File.Exists(Path.Combine(_music, "Videos", "Band - Song (Live).mp4")));
        Assert.Equal(HttpStatusCode.NotFound, (await _admin.GetAsync($"/rest/v1/videos/{live}", Cancel)).StatusCode);

        JsonElement after;
        for (var tries = 0; ; tries++)
        {
            await _factory.WaitForScansAsync(_admin);
            after = await VideosAsync($"?trackId={trackId}");
            if (after.GetProperty("totalCount").GetInt32() == 3 || tries == 50)
                break;
            await Task.Delay(300, Cancel);
        }
        var uploaded = Assert.Single(after.GetProperty("items").EnumerateArray(), v => v.GetProperty("path").GetString() == "Band/Album/01 Song (2).mp4");
        Assert.Equal("SIDECAR", uploaded.GetProperty("linkMethod").GetString());
    }

    [Fact]
    public async Task The_server_settings_are_for_settings_managers()
    {
        await StartAsync();
        var settings = await _admin.GetFromJsonAsync<JsonElement>("/rest/v1/videos/settings", ArcanistFactory.Json, Cancel);
        Assert.Equal("TRACK", settings.GetProperty("defaultAudio").GetString());
        Assert.Equal(4, settings.GetProperty("videos").GetInt32());

        var set = await _admin.PutAsJsonAsync("/rest/v1/videos/settings", new
        {
            defaultAudio = "VIDEO", cacheMaxBytes = 5_000_000_000L, cacheMaxAgeDays = 10, prepareLibraryIds = Array.Empty<Guid>(),
            prepareHeights = new[] { 720, 360 }, prepareWhenPlayed = false,
        }, ArcanistFactory.Json, Cancel);
        set.EnsureSuccessStatusCode();
        var mine = await _admin.GetFromJsonAsync<JsonElement>("/rest/v1/me/video-settings", ArcanistFactory.Json, Cancel);
        Assert.Equal("VIDEO", mine.GetProperty("effective").GetString());

        var invalid = await _admin.PutAsJsonAsync("/rest/v1/videos/settings", new
        {
            defaultAudio = "TRACK", cacheMaxBytes = 5_000_000_000L, cacheMaxAgeDays = 10, prepareLibraryIds = Array.Empty<Guid>(),
            prepareHeights = new[] { 900 }, prepareWhenPlayed = false,
        }, ArcanistFactory.Json, Cancel);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);

        await _factory.CreateUserAsync("listener", permissions: Permissions.Upload);
        var listener = await _factory.CreateAuthenticatedClientAsync("listener");
        Assert.Equal(HttpStatusCode.Forbidden, (await listener.GetAsync("/rest/v1/videos/settings", Cancel)).StatusCode);
    }

    [Fact]
    public async Task Dlna_devices_and_Subsonic_apps_get_the_videos()
    {
        await StartAsync();
        var libraryId = (await _admin.GetFromJsonAsync<List<JsonElement>>("/rest/v1/libraries", ArcanistFactory.Json, Cancel))![0].GetProperty("id").GetString();
        (await _admin.PutAsJsonAsync("/rest/v1/dlna/settings", new { friendlyName = "Home", libraryIds = new[] { libraryId }, sections = new[] { "MUSIC_VIDEOS" } },
            ArcanistFactory.Json, Cancel)).EnsureSuccessStatusCode();

        var lan = _factory.CreateRemoteClient("192.168.1.20");
        async Task<XElement> BrowseAsync(string id)
        {
            var body = $"""<?xml version="1.0"?><s:Envelope xmlns:s="http://schemas.xmlsoap.org/soap/envelope/"><s:Body><u:Browse xmlns:u="urn:schemas-upnp-org:service:ContentDirectory:1"><ObjectID>{id}</ObjectID><BrowseFlag>BrowseDirectChildren</BrowseFlag><Filter>*</Filter><StartingIndex>0</StartingIndex><RequestedCount>0</RequestedCount><SortCriteria></SortCriteria></u:Browse></s:Body></s:Envelope>""";
            var response = await lan.PostAsync("/ext/dlna/content-directory/control", new StringContent(body, System.Text.Encoding.UTF8, "text/xml"), Cancel);
            var text = await response.Content.ReadAsStringAsync(Cancel);
            Assert.True(response.IsSuccessStatusCode, text);
            return XElement.Parse(XDocument.Parse(text).Descendants().Single(e => e.Name.LocalName == "Result").Value);
        }

        XNamespace didl = "urn:schemas-upnp-org:metadata-1-0/DIDL-Lite/";
        XNamespace upnp = "urn:schemas-upnp-org:metadata-1-0/upnp/";
        var root = await BrowseAsync("0");
        Assert.Equal("musicvideos", root.Elements().Single().Attribute("id")!.Value);
        var items = (await BrowseAsync("musicvideos")).Elements(didl + "item").ToList();
        Assert.Equal(4, items.Count);
        Assert.All(items, i => Assert.Equal("object.item.videoItem.musicVideoClip", i.Element(upnp + "class")!.Value));
        var resource = items[0].Elements(didl + "res").First();
        Assert.Equal("320x240", resource.Attribute("resolution")!.Value);
        Assert.EndsWith("/track.mp4", resource.Value);
        var video = await lan.GetAsync(new Uri(resource.Value).PathAndQuery.Replace("/track.mp4", "/video.mp4"), HttpCompletionOption.ResponseHeadersRead, Cancel);
        Assert.Equal(HttpStatusCode.OK, video.StatusCode);
        Assert.Equal("video/mp4", video.Content.Headers.ContentType?.MediaType);

        var password = await _admin.PostAsJsonAsync("/rest/v1/me/app-passwords", new { name = "Phone" }, ArcanistFactory.Json, Cancel);
        var secret = Uri.EscapeDataString((await password.Content.ReadFromJsonAsync<JsonElement>(ArcanistFactory.Json, Cancel)).GetProperty("secret").GetString()!);
        var client = _factory.CreateClient();
        var videos = await client.GetFromJsonAsync<JsonElement>($"/rest/getVideos.view?u=admin&p={secret}&v=1.16.1&c=tests&f=json", Cancel);
        var list = videos.GetProperty("subsonic-response").GetProperty("videos").GetProperty("video");
        Assert.Equal(4, list.GetArrayLength());
        Assert.True(list[0].GetProperty("isVideo").GetBoolean());
        var id = list[0].GetProperty("id").GetString();
        var info = await client.GetFromJsonAsync<JsonElement>($"/rest/getVideoInfo.view?u=admin&p={secret}&v=1.16.1&c=tests&f=json&id={id}", Cancel);
        Assert.Equal(2, info.GetProperty("subsonic-response").GetProperty("videoInfo").GetProperty("audioTrack").GetArrayLength());
        var stream = await client.GetAsync($"/rest/stream.view?u=admin&p={secret}&v=1.16.1&c=tests&id={id}&audioTrack=video", HttpCompletionOption.ResponseHeadersRead, Cancel);
        Assert.Equal("video/mp4", stream.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task A_wish_can_want_the_music_video_too()
    {
        await StartAsync();
        var added = await _admin.PostAsJsonAsync("/rest/v1/me/wishes", new { title = "Song", artist = "Band", wantsVideo = true }, ArcanistFactory.Json, Cancel);
        added.EnsureSuccessStatusCode();
        var wish = await added.Content.ReadFromJsonAsync<JsonElement>(ArcanistFactory.Json, Cancel);
        Assert.Equal("OBTAINED", wish.GetProperty("status").GetString());
        Assert.True(wish.GetProperty("wantsVideo").GetBoolean());
        Assert.NotEqual(JsonValueKind.Null, wish.GetProperty("videoObtainedAt").ValueKind);

        // Not wanting it any more clears it.
        var id = wish.GetProperty("id").GetString();
        var updated = await (await _admin.PutAsJsonAsync($"/rest/v1/me/wishes/{id}", new { title = "Song", artist = "Band", wantsVideo = false }, ArcanistFactory.Json, Cancel))
            .Content.ReadFromJsonAsync<JsonElement>(ArcanistFactory.Json, Cancel);
        Assert.Equal(JsonValueKind.Null, updated.GetProperty("videoObtainedAt").ValueKind);
    }

    public async ValueTask DisposeAsync()
    {
        if (_factory is not null)
            await _factory.DisposeAsync();
        foreach (var folder in new[] { _music, _work })
        {
            if (Environment.GetEnvironmentVariable("ARCANIST_KEEP_VIDEOS") is not null)
                continue;
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
