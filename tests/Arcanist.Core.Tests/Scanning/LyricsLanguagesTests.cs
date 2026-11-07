using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Arcanist.Core.Data;
using Arcanist.Core.Setup;
using Arcanist.Extensibility;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Arcanist.Core.Tests.Scanning;

/// <summary>An extension's reader: Japanese when there is kana, each line "romanized" in capitals, furigana on 星.</summary>
internal sealed class FakeReadings : ILyricsReadings
{
    public int Calls;

    public string Version { get; set; } = "1";

    public string? LanguageOf(IReadOnlyList<string> lines) => lines.Any(l => l.Any(c => c is >= '぀' and <= 'ヿ')) ? "ja" : null;

    public IReadOnlyList<LyricsReading?> Read(IReadOnlyList<string> lines, string language)
    {
        Interlocked.Increment(ref Calls);
        return lines.Select(l => l.Length == 0 ? null
            : new LyricsReading("romaji:" + l, l.Contains('星') ? [new RubySegment("星", "ほし"), new RubySegment(l.Replace("星", ""), null)] : null)).ToList();
    }
}

/// <summary>An extension's translator into Spanish: each line in brackets, after a wait the test controls.</summary>
internal sealed class FakeTranslator : ILyricsTranslator
{
    public readonly SemaphoreSlim Go = new(0);
    public int Calls;
    public bool Short;

    public IReadOnlyList<string> Languages => ["es", "en"];

    public string Engine => "Fake MT";

    public async Task<IReadOnlyList<string>> TranslateAsync(IReadOnlyList<string> lines, string? from, string to, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref Calls);
        await Go.WaitAsync(cancellationToken);
        var translated = lines.Select(l => $"[{to}:{from}] {l}").ToList();
        return Short ? translated.Take(1).ToList() : translated;
    }
}

public sealed class LyricsLanguagesTests : IAsyncLifetime
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private readonly string _music = Directory.CreateTempSubdirectory("arcanist-lyrics-languages-").FullName;
    private readonly FakeReadings _readings = new();
    private readonly FakeTranslator _translator = new();
    private ArcanistFactory _factory = null!;
    private HttpClient _admin = null!;

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        _translator.Go.Release(100);
        if (_factory is not null)
            await _factory.DisposeAsync();
        Directory.Delete(_music, recursive: true);
    }

    private async Task StartAsync(bool extension = true)
    {
        AudioFixtures.WriteWav(Path.Combine(_music, "hoshi.wav"), 1, new FixtureTags(Title: "Hoshi", Artist: "Band"));
        File.WriteAllText(Path.Combine(_music, "hoshi.lrc"), "[00:01.00]夜空に光る星\n[00:03.00]\n[00:05.00]きみのなまえ\n");
        AudioFixtures.WriteWav(Path.Combine(_music, "twin.wav"), 2, new FixtureTags(Title: "Twin", Artist: "Band"));
        File.WriteAllText(Path.Combine(_music, "twin.lrc"), "[00:01.00]夜空に光る星\n[00:03.00]\n[00:05.00]きみのなまえ\n");
        AudioFixtures.WriteWav(Path.Combine(_music, "english.wav"), 3, new FixtureTags(Title: "English", Artist: "Band", Lyrics: "Hello darkness\nmy old friend"));
        _factory = new ArcanistFactory(services: services =>
        {
            if (extension)
            {
                services.AddSingleton<ILyricsReadings>(_readings);
                services.AddSingleton<ILyricsTranslator>(_translator);
            }
        });
        await _factory.CompleteSetupAsync(new SetupLibrary("Music", [_music]));
        _admin = await _factory.CreateAuthenticatedClientAsync();
        await _factory.WaitForScansAsync(_admin);
    }

    private async Task<string> TrackAsync(string title) =>
        (await _admin.GetFromJsonAsync<JsonElement>($"/rest/v1/tracks?search={title}", ArcanistFactory.Json, Cancel)).GetProperty("items")[0].GetProperty("id").GetString()!;

    private async Task<JsonElement> LyricsAsync(string id) =>
        await _admin.GetFromJsonAsync<JsonElement>($"/rest/v1/tracks/{id}/lyrics", ArcanistFactory.Json, Cancel);

    private async Task<JsonElement> TranslationAsync(string id, string language, bool request = false)
    {
        var response = request
            ? await _admin.PostAsync($"/rest/v1/tracks/{id}/lyrics/translations/{language}", null, Cancel)
            : await _admin.GetAsync($"/rest/v1/tracks/{id}/lyrics/translations/{language}", Cancel);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync(Cancel));
        return await response.Content.ReadFromJsonAsync<JsonElement>(ArcanistFactory.Json, Cancel);
    }

    private async Task<JsonElement> WaitForAsync(string id, string language, string status)
    {
        for (var tries = 0; tries < 200; tries++)
        {
            var result = await TranslationAsync(id, language);
            if (result.GetProperty("status").GetString() == status)
                return result;
            await Task.Delay(50, Cancel);
        }

        throw new TimeoutException((await TranslationAsync(id, language)).ToString());
    }

    [Fact]
    public async Task Lyrics_come_with_how_each_line_is_read_kept_by_their_text()
    {
        await StartAsync();
        var info = await _admin.GetFromJsonAsync<JsonElement>("/rest/v1/server-info", ArcanistFactory.Json, Cancel);
        Assert.Contains("LYRICS_READINGS", info.GetProperty("features").EnumerateArray().Select(f => f.GetString()));

        var lyrics = await LyricsAsync(await TrackAsync("Hoshi"));
        Assert.Equal("ja", lyrics.GetProperty("language").GetString());
        var readings = lyrics.GetProperty("readings");
        // One per line shown, the empty one without.
        Assert.Equal(3, readings.GetArrayLength());
        Assert.Equal("romaji:夜空に光る星", readings[0].GetProperty("romanized").GetString());
        Assert.Equal(("星", "ほし"), (readings[0].GetProperty("ruby")[0].GetProperty("text").GetString(), readings[0].GetProperty("ruby")[0].GetProperty("reading").GetString()));
        Assert.Equal(JsonValueKind.Null, readings[1].ValueKind);

        // The same lyrics on another song, and the same song again: worked out once.
        await LyricsAsync(await TrackAsync("Twin"));
        await LyricsAsync(await TrackAsync("Hoshi"));
        Assert.Equal(1, _readings.Calls);
        // A new version of the reader works them out again.
        _readings.Version = "2";
        await LyricsAsync(await TrackAsync("Hoshi"));
        Assert.Equal(2, _readings.Calls);

        // Lyrics in the Latin alphabet need none.
        var english = await LyricsAsync(await TrackAsync("English"));
        Assert.Equal((JsonValueKind.Null, JsonValueKind.Null), (english.GetProperty("language").ValueKind, english.GetProperty("readings").ValueKind));
    }

    [Fact]
    public async Task A_translation_is_asked_for_made_once_in_the_background_and_kept_for_everyone()
    {
        await StartAsync();
        var hoshi = await TrackAsync("Hoshi");
        var twin = await TrackAsync("Twin");
        Assert.Equal(["es", "en"], (await _admin.GetFromJsonAsync<List<string>>("/rest/v1/lyrics/translation-languages", ArcanistFactory.Json, Cancel))!);

        Assert.Equal("NOT_REQUESTED", (await TranslationAsync(hoshi, "es")).GetProperty("status").GetString());
        Assert.Equal("SAME_LANGUAGE", (await TranslationAsync(hoshi, "ja", request: true)).GetProperty("status").GetString());
        Assert.Equal("UNAVAILABLE", (await TranslationAsync(hoshi, "fr", request: true)).GetProperty("status").GetString());

        // Asked for twice (the same lyrics on another song): one translation.
        var asked = await TranslationAsync(hoshi, "es", request: true);
        Assert.Contains(asked.GetProperty("status").GetString(), new[] { "QUEUED", "WORKING" });
        await TranslationAsync(twin, "es", request: true);
        await WaitForAsync(hoshi, "es", "WORKING");
        _translator.Go.Release();

        var ready = await WaitForAsync(hoshi, "es", "READY");
        Assert.Equal(["[es:ja] 夜空に光る星", "", "[es:ja] きみのなまえ"], ready.GetProperty("lines").EnumerateArray().Select(l => l.GetString()));
        Assert.Equal(("Fake MT", "ja"), (ready.GetProperty("engine").GetString(), ready.GetProperty("from").GetString()));
        Assert.Equal("READY", (await TranslationAsync(twin, "es")).GetProperty("status").GetString());
        Assert.Equal(1, _translator.Calls);

        // GraphQL says the same, to any user who sees the song.
        await _factory.CreateUserAsync("listener");
        var listener = await _factory.CreateAuthenticatedClientAsync("listener");
        var graph = await listener.GraphQLAsync("query($id: ID!) { lyricsTranslation(trackId: $id, language: \"es\") { status lines } }", new { id = hoshi });
        Assert.Equal("READY", graph.GetProperty("data").GetProperty("lyricsTranslation").GetProperty("status").GetString());

        // A translator that loses lines is not kept: it failed.
        _translator.Short = true;
        await TranslationAsync(hoshi, "en", request: true);
        _translator.Go.Release();
        await WaitForAsync(hoshi, "en", "FAILED");
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ArcanistDbContext>();
        Assert.Equal(1, await db.LyricsTranslations.CountAsync(Cancel));
    }

    [Fact]
    public async Task Without_the_extension_lyrics_are_as_before_and_nothing_translates()
    {
        await StartAsync(extension: false);
        var info = await _admin.GetFromJsonAsync<JsonElement>("/rest/v1/server-info", ArcanistFactory.Json, Cancel);
        Assert.DoesNotContain("LYRICS_TRANSLATION", info.GetProperty("features").EnumerateArray().Select(f => f.GetString()));

        var hoshi = await TrackAsync("Hoshi");
        var lyrics = await LyricsAsync(hoshi);
        Assert.Equal(JsonValueKind.Null, lyrics.GetProperty("readings").ValueKind);
        Assert.Equal("UNAVAILABLE", (await TranslationAsync(hoshi, "es", request: true)).GetProperty("status").GetString());
        Assert.Empty((await _admin.GetFromJsonAsync<List<string>>("/rest/v1/lyrics/translation-languages", ArcanistFactory.Json, Cancel))!);
        Assert.Equal(HttpStatusCode.BadRequest, (await _admin.GetAsync($"/rest/v1/tracks/{hoshi}/lyrics/translations/not a language", Cancel)).StatusCode);
    }
}
