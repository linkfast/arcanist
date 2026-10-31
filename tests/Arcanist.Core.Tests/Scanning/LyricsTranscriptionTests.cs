using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Arcanist.Core.Application.Catalog;
using Arcanist.Core.Data.Entities;
using Arcanist.Core.Setup;
using Arcanist.Core.Tests.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Arcanist.Core.Tests.Scanning;

/// <summary>
/// A song without lyrics: a user asks for its words to be recognized, the extension (here, the test itself) saves a
/// draft, a reviewer corrects and accepts it, and lyrics from a file replace it later.
/// </summary>
public sealed class LyricsTranscriptionTests : IAsyncLifetime
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private readonly string _music = Directory.CreateTempSubdirectory("arcanist-lyrics-transcription-").FullName;
    private ArcanistFactory _factory = null!;
    private HttpClient _admin = null!;

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    private async Task StartAsync(bool extension)
    {
        AudioFixtures.WriteWav(Path.Combine(_music, "silent.wav"), 3, new FixtureTags("Silent", "Band"));
        AudioFixtures.WriteWav(Path.Combine(_music, "words.wav"), 4, new FixtureTags("Words", "Band", Lyrics: "Some words"));
        _factory = new ArcanistFactory();
        if (extension)
        {
            // The test extension, installed under Lyrics Sync's id: what the core checks is that it is installed and running.
            Directory.CreateDirectory(_factory.DataPath);
            File.WriteAllText(Path.Combine(_factory.DataPath, "arcanist.json"),
                JsonSerializer.Serialize(new Dictionary<string, object>
                {
                    ["Arcanist"] = new { Extensions = new Dictionary<string, object> { [LyricsTimingService.ExtensionId] = new { Enabled = true } } },
                }));
            var folder = Path.Combine(_factory.DataPath, "extensions", LyricsTimingService.ExtensionId);
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "arcanist-extension.json"), ExtensionTests.Manifest(LyricsTimingService.ExtensionId, apps: []));
            File.Copy(ExtensionTests.Assembly, Path.Combine(folder, "Arcanist.TestExtension.dll"));
        }

        await _factory.CompleteSetupAsync(new SetupLibrary("Music", [_music]));
        _admin = await _factory.CreateAuthenticatedClientAsync();
        await _factory.WaitForScansAsync(_admin);
    }

    private LyricsTranscription Transcription => _factory.Services.GetRequiredService<LyricsTranscription>();

    private async Task<Guid> TrackIdAsync(string title)
    {
        var page = await _admin.GetFromJsonAsync<JsonElement>($"/rest/v1/tracks?search={Uri.EscapeDataString(title)}", ArcanistFactory.Json, Cancel);
        return Guid.Parse(page.GetProperty("items")[0].GetProperty("id").GetString()!);
    }

    private static async Task<JsonElement> StatusAsync(HttpClient client, Guid id) =>
        await client.GetFromJsonAsync<JsonElement>($"/rest/v1/tracks/{id}/lyrics/transcription", ArcanistFactory.Json, Cancel);

    [Fact]
    public async Task Without_the_extension_nothing_can_be_asked_for()
    {
        await StartAsync(extension: false);
        var silent = await TrackIdAsync("Silent");

        var status = await StatusAsync(_admin, silent);
        Assert.Equal("UNAVAILABLE", status.GetProperty("state").GetString());
        Assert.Equal("lyrics_sync_unavailable", status.GetProperty("reason").GetString());
        var response = await _admin.PostAsync($"/rest/v1/tracks/{silent}/lyrics/transcription", null, Cancel);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.DoesNotContain("LYRICS_TRANSCRIPTION", (await _admin.GetStringAsync("/rest/v1/server-info", Cancel)));
    }

    [Fact]
    public async Task A_recognized_draft_is_reviewed_corrected_and_accepted_and_a_files_lyrics_replace_it()
    {
        await StartAsync(extension: true);
        var silent = await TrackIdAsync("Silent");
        var words = await TrackIdAsync("Words");
        Assert.Contains("LYRICS_TRANSCRIPTION", await _admin.GetStringAsync("/rest/v1/server-info", Cancel));

        // A listener (no EDIT_METADATA) asks for it; a song with lyrics has nothing to recognize.
        await _factory.CreateUserAsync("listener");
        var listener = await _factory.CreateAuthenticatedClientAsync("listener");
        Assert.Equal("NOT_REQUESTED", (await StatusAsync(listener, silent)).GetProperty("state").GetString());
        Assert.Equal("HAS_LYRICS", (await StatusAsync(listener, words)).GetProperty("state").GetString());
        var asked = await (await listener.PostAsync($"/rest/v1/tracks/{silent}/lyrics/transcription", null, Cancel))
            .EnsureSuccessStatusCode().Content.ReadFromJsonAsync<JsonElement>(ArcanistFactory.Json, Cancel);
        Assert.Equal("QUEUED", asked.GetProperty("state").GetString());
        Assert.False(asked.GetProperty("canReview").GetBoolean());

        // The extension takes it and saves what it heard.
        Assert.Equal([silent], Transcription.TakeRequested());
        var song = Assert.Single(await Transcription.FindAsync([silent, words], Cancel));
        Assert.True(song.Forced);
        Assert.EndsWith("silent.wav", song.AudioPath);
        Transcription.Start(silent);
        Assert.Equal("WORKING", (await StatusAsync(listener, silent)).GetProperty("state").GetString());
        Assert.True(await Transcription.SaveDraftAsync(silent,
            [new TranscribedLine(1_000, "Hello darkness my old friend", 0.92), new TranscribedLine(4_000, "I've come to toke with you again", 0.41)],
            "en", "test", Cancel));
        Assert.False(await Transcription.SaveDraftAsync(silent, [new TranscribedLine(0, "Again", 1)], "en", "test", Cancel));

        // The listener sees that it waits for review, but not the words; the admin reviews them.
        var waiting = await StatusAsync(listener, silent);
        Assert.Equal("NEEDS_REVIEW", waiting.GetProperty("state").GetString());
        Assert.Equal(JsonValueKind.Null, waiting.GetProperty("draft").ValueKind);
        Assert.Equal(HttpStatusCode.NotFound, (await _admin.GetAsync($"/rest/v1/tracks/{silent}/lyrics", Cancel)).StatusCode);
        var review = await StatusAsync(_admin, silent);
        Assert.True(review.GetProperty("canReview").GetBoolean());
        Assert.Equal(2, review.GetProperty("draft").GetProperty("lines").GetArrayLength());

        var drafts = await _admin.GetFromJsonAsync<JsonElement>("/rest/v1/lyrics/drafts", ArcanistFactory.Json, Cancel);
        var listed = Assert.Single(drafts.GetProperty("items").EnumerateArray());
        Assert.Equal(1, listed.GetProperty("uncertain").GetInt32());
        Assert.Equal(HttpStatusCode.Forbidden, (await listener.GetAsync("/rest/v1/lyrics/drafts", Cancel)).StatusCode);

        // The reviewer fixes the misheard word: that line loses the model's confidence, the other keeps it.
        var saved = await (await _admin.PutAsJsonAsync($"/rest/v1/tracks/{silent}/lyrics/draft", new
            {
                lines = new[] { new { timeMs = 1_000, text = "Hello darkness my old friend" }, new { timeMs = 4_200, text = "I've come to talk with you again" } },
            }, ArcanistFactory.Json, Cancel))
            .EnsureSuccessStatusCode().Content.ReadFromJsonAsync<JsonElement>(ArcanistFactory.Json, Cancel);
        var lines = saved.GetProperty("draft").GetProperty("lines");
        Assert.Equal(0.92, lines[0].GetProperty("confidence").GetDouble());
        Assert.Equal(JsonValueKind.Null, lines[1].GetProperty("confidence").ValueKind);
        Assert.False(string.IsNullOrEmpty(saved.GetProperty("draft").GetProperty("editedBy").GetString()));

        // Accepted, they are the song's lyrics, timed line by line.
        var accepted = await (await _admin.PostAsJsonAsync($"/rest/v1/tracks/{silent}/lyrics/draft/accept", new { }, ArcanistFactory.Json, Cancel))
            .EnsureSuccessStatusCode().Content.ReadFromJsonAsync<JsonElement>(ArcanistFactory.Json, Cancel);
        Assert.Equal("TRANSCRIBED", accepted.GetProperty("source").GetString());
        Assert.Equal([1_000, 4_200], accepted.GetProperty("synced").EnumerateArray().Select(l => l.GetProperty("timeMs").GetInt32()));
        Assert.Contains("talk with you", accepted.GetProperty("plain").GetString());
        Assert.Equal("HAS_LYRICS", (await StatusAsync(listener, silent)).GetProperty("state").GetString());
        Assert.Empty((await _admin.GetFromJsonAsync<JsonElement>("/rest/v1/lyrics/drafts", ArcanistFactory.Json, Cancel)).GetProperty("items").EnumerateArray());

        // Lyrics a file brings later replace the recognized ones.
        await File.WriteAllTextAsync(Path.Combine(_music, "silent.lrc"), "[00:01.50]The real words\n", Cancel);
        var libraryId = (await _admin.GetFromJsonAsync<JsonElement>("/rest/v1/libraries", ArcanistFactory.Json, Cancel))[0].GetProperty("id").GetString();
        (await _admin.PostAsJsonAsync($"/rest/v1/libraries/{libraryId}/scans", new Core.Api.Rest.StartScanInput(Full: true), Cancel)).EnsureSuccessStatusCode();
        await _factory.WaitForScansAsync(_admin);
        var replaced = await _admin.GetFromJsonAsync<JsonElement>($"/rest/v1/tracks/{silent}/lyrics", ArcanistFactory.Json, Cancel);
        Assert.Equal("SIDECAR", replaced.GetProperty("source").GetString());
    }

    [Fact]
    public async Task A_draft_can_be_discarded_and_recognized_again_and_no_voice_can_be_asked_for_again()
    {
        await StartAsync(extension: true);
        var silent = await TrackIdAsync("Silent");
        Transcription.Request(silent);
        Transcription.TakeRequested();
        Transcription.End(silent, TranscriptionOutcome.NoVoice);
        var status = await StatusAsync(_admin, silent);
        Assert.Equal("NO_VOICE", status.GetProperty("state").GetString());
        Assert.True(status.GetProperty("canRequest").GetBoolean());

        Assert.True(await Transcription.SaveDraftAsync(silent, [new TranscribedLine(0, "Words", 0.9)], null, "test", Cancel));
        var discarded = await (await _admin.DeleteAsync($"/rest/v1/tracks/{silent}/lyrics/draft?again=true", Cancel))
            .EnsureSuccessStatusCode().Content.ReadFromJsonAsync<JsonElement>(ArcanistFactory.Json, Cancel);
        Assert.Equal("QUEUED", discarded.GetProperty("state").GetString());
        Assert.Equal([silent], Transcription.TakeRequested());
    }

    public async ValueTask DisposeAsync()
    {
        if (_factory is not null)
            await _factory.DisposeAsync();
        Directory.Delete(_music, recursive: true);
    }
}
