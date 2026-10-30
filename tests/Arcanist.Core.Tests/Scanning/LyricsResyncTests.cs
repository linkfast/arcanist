using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Arcanist.Core.Application.Catalog;
using Arcanist.Core.Data;
using Arcanist.Core.Data.Entities;
using Arcanist.Core.Setup;
using Arcanist.Core.Tests.Hosting;
using Arcanist.Core.Tests.Online;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Arcanist.Core.Tests.Scanning;

/// <summary>A user asks for a song's lyrics to be timed again by the Lyrics Sync extension, whatever timing they have.</summary>
public sealed class LyricsResyncTests : IAsyncLifetime
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private const string ResyncMutation = "mutation($id: ID!) { resyncTrackLyrics(trackId: $id) { trackId synced { timeMs } syncedSource canResync resyncPending } }";

    private readonly FakeOnlineServices _online = new();
    private readonly string _music = Directory.CreateTempSubdirectory("arcanist-lyrics-resync-").FullName;
    private ArcanistFactory _factory = null!;
    private HttpClient _admin = null!;

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    /// <summary>A server with a song timed by its file, one with plain lyrics and one without; with a stand-in for the extension, or none.</summary>
    private async Task StartAsync(bool extension)
    {
        AudioFixtures.WriteWav(Path.Combine(_music, "timed.wav"), 1,
            new FixtureTags("Timed", "Band", Lyrics: "[00:01.00]Timed words\n[00:02.00]More words"));
        AudioFixtures.WriteWav(Path.Combine(_music, "plain.wav"), 2, new FixtureTags("Plain", "Band", Lyrics: "First words\nSecond words"));
        AudioFixtures.WriteWav(Path.Combine(_music, "silent.wav"), 3, new FixtureTags("Silent", "Band"));
        _online.Respond = _ => new HttpResponseMessage(HttpStatusCode.NotFound);

        _factory = new ArcanistFactory(new Dictionary<string, string?> { ["Arcanist:Online:RequestInterval"] = "00:00:00.010" },
            services: _online.Register);
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

    private LyricsTiming Timing => _factory.Services.GetRequiredService<LyricsTiming>();

    private async Task<Guid> TrackIdAsync(string title)
    {
        var page = await _admin.GetFromJsonAsync<JsonElement>($"/rest/v1/tracks?search={Uri.EscapeDataString(title)}", ArcanistFactory.Json, Cancel);
        return Guid.Parse(page.GetProperty("items")[0].GetProperty("id").GetString()!);
    }

    private Task<JsonElement> LyricsAsync(Guid id) =>
        _admin.GetFromJsonAsync<JsonElement>($"/rest/v1/tracks/{id}/lyrics", ArcanistFactory.Json, Cancel);

    private static int[] Times(JsonElement lyrics) => lyrics.GetProperty("synced").EnumerateArray().Select(l => l.GetProperty("timeMs").GetInt32()).ToArray();

    private async Task RescanAsync()
    {
        var libraryId = (await _admin.GetFromJsonAsync<JsonElement>("/rest/v1/libraries", ArcanistFactory.Json, Cancel))[0].GetProperty("id").GetString();
        (await _admin.PostAsJsonAsync($"/rest/v1/libraries/{libraryId}/scans", new Core.Api.Rest.StartScanInput(Full: true), Cancel)).EnsureSuccessStatusCode();
        await _factory.WaitForScansAsync(_admin);
    }

    [Fact]
    public async Task A_resync_replaces_the_files_timing_only_once_saved_and_a_rescan_keeps_it_while_the_file_is_the_same()
    {
        await StartAsync(extension: false);
        var id = await TrackIdAsync("Timed");

        // Timed songs are not handed out to time, unless a re-sync asks for them.
        Assert.Empty(await Timing.FindAsync([id], Cancel));
        Timing.Resync(id);
        Assert.True(Timing.IsResyncPending(id));
        Assert.Contains(id, Timing.TakeRequested());
        var song = Assert.Single(await Timing.FindAsync([id], Cancel));
        Assert.True(song.Forced);
        // The words of the timed lines, as there are no plain lyrics apart from them.
        Assert.Equal("Timed words\nMore words", song.Plain);

        // The file's timing stays until the new one is saved; timing for other words is refused and ends the re-sync.
        var before = await LyricsAsync(id);
        Assert.Equal("EMBEDDED", before.GetProperty("syncedSource").GetString());
        Assert.Equal([1000, 2000], Times(before));
        Assert.False(await Timing.SaveAsync(id, LyricsTiming.HashOf("other words"), "[00:05.00]Timed words", Cancel));
        Assert.False(Timing.IsResyncPending(id));
        Assert.Equal("EMBEDDED", (await LyricsAsync(id)).GetProperty("syncedSource").GetString());
        // Without a re-sync, timed lyrics are never replaced.
        Assert.False(await Timing.SaveAsync(id, song.PlainHash, "[00:05.00]Timed words\n[00:07.00]More words", Cancel));

        // The extension says it could not time it: the re-sync ends, the timing stays.
        Timing.Resync(id);
        Timing.EndResync(id);
        Assert.False(Timing.IsResyncPending(id));

        Timing.Resync(id);
        song = Assert.Single(await Timing.FindAsync([id], Cancel));
        Assert.True(await Timing.SaveAsync(id, song.PlainHash, "[00:05.00]Timed words\n[00:07.00]More words", Cancel));
        Assert.False(Timing.IsResyncPending(id));
        var lyrics = await LyricsAsync(id);
        Assert.Equal(("EMBEDDED", "ALIGNED"), (lyrics.GetProperty("source").GetString(), lyrics.GetProperty("syncedSource").GetString()));
        Assert.Equal([5000, 7000], Times(lyrics));
        Assert.Equal("Timed words\nMore words", lyrics.GetProperty("plain").GetString());

        // A full rescan reads the same timing from the file: the one worked out here stays.
        await RescanAsync();
        lyrics = await LyricsAsync(id);
        Assert.Equal([5000, 7000], Times(lyrics));

        // Other timing in the file (edited since) wins.
        AudioFixtures.WriteWav(Path.Combine(_music, "timed.wav"), 1,
            new FixtureTags("Timed", "Band", Lyrics: "[00:01.50]Timed words\n[00:02.50]More words"));
        await RescanAsync();
        lyrics = await LyricsAsync(id);
        Assert.Equal("EMBEDDED", lyrics.GetProperty("syncedSource").GetString());
        Assert.Equal([1500, 2500], Times(lyrics));
    }

    [Fact]
    public async Task A_resync_replaces_lrclibs_timing_and_a_rescan_keeps_it_while_the_words_are_the_same()
    {
        await StartAsync(extension: false);
        var id = await TrackIdAsync("Plain");
        // As LRCLib would have left it: the file's words with its timing.
        await using (var db = _factory.Services.GetRequiredService<IArcanistDbContextFactory>().CreateDbContext())
        {
            await db.Lyrics.Where(l => l.TrackId == id).ExecuteUpdateAsync(s => s
                .SetProperty(l => l.Synced, "[00:03.00]First words\n[00:04.00]Second words")
                .SetProperty(l => l.SyncedSource, LyricsSource.Online), Cancel);
        }

        Assert.Empty(await Timing.PendingAsync(null, 10, Cancel));
        Timing.Resync(id);
        var song = Assert.Single(await Timing.FindAsync([id], Cancel));
        Assert.Equal(("First words\nSecond words", true), (song.Plain, song.Forced));
        Assert.Equal("ONLINE", (await LyricsAsync(id)).GetProperty("syncedSource").GetString());
        Assert.True(await Timing.SaveAsync(id, song.PlainHash, "[00:01.00]First words\n[00:02.20]Second words", Cancel));

        await RescanAsync();
        var lyrics = await LyricsAsync(id);
        Assert.Equal("ALIGNED", lyrics.GetProperty("syncedSource").GetString());
        Assert.Equal([1000, 2200], Times(lyrics));
    }

    [Fact]
    public async Task Without_the_extension_lyrics_cannot_be_resynced()
    {
        await StartAsync(extension: false);
        var id = await TrackIdAsync("Timed");

        Assert.False((await LyricsAsync(id)).GetProperty("canResync").GetBoolean());
        var response = await _admin.PostAsync($"/rest/v1/tracks/{id}/lyrics/resync", null, Cancel);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("lyrics_sync_unavailable", (await response.Content.ReadFromJsonAsync<JsonElement>(Cancel)).GetProperty("code").GetString());
        Assert.Equal("lyrics_sync_unavailable", (await _admin.GraphQLAsync(ResyncMutation, new { id })).ErrorCode());
        Assert.False(Timing.IsResyncPending(id));
    }

    [Fact]
    public async Task With_the_extension_an_editor_asks_for_a_resync()
    {
        await StartAsync(extension: true);
        var timed = await TrackIdAsync("Timed");
        var silent = await TrackIdAsync("Silent");

        var lyrics = await LyricsAsync(timed);
        Assert.Equal((true, false), (lyrics.GetProperty("canResync").GetBoolean(), lyrics.GetProperty("resyncPending").GetBoolean()));

        var result = await _admin.GraphQLAsync(ResyncMutation, new { id = timed });
        Assert.Null(result.ErrorCode());
        var resynced = result.GetProperty("data").GetProperty("resyncTrackLyrics");
        // Pending, with the timing it had until the new one is saved.
        Assert.True(resynced.GetProperty("resyncPending").GetBoolean());
        Assert.Equal("EMBEDDED", resynced.GetProperty("syncedSource").GetString());
        Assert.Equal(2, resynced.GetProperty("synced").GetArrayLength());
        Assert.True(Timing.IsResyncPending(timed));
        Assert.Contains(timed, Timing.TakeRequested());
        Assert.True((await LyricsAsync(timed)).GetProperty("resyncPending").GetBoolean());

        // REST does the same.
        var response = await _admin.PostAsync($"/rest/v1/tracks/{timed}/lyrics/resync", null, Cancel);
        response.EnsureSuccessStatusCode();
        Assert.True((await response.Content.ReadFromJsonAsync<JsonElement>(Cancel)).GetProperty("resyncPending").GetBoolean());

        // Nothing to time: no lyrics, or instrumental.
        Assert.Equal("no_lyrics", (await _admin.GraphQLAsync(ResyncMutation, new { id = silent })).ErrorCode());
        var plain = await TrackIdAsync("Plain");
        await using (var db = _factory.Services.GetRequiredService<IArcanistDbContextFactory>().CreateDbContext())
            await db.Lyrics.Where(l => l.TrackId == plain).ExecuteUpdateAsync(s => s.SetProperty(l => l.Instrumental, true), Cancel);
        Assert.False((await LyricsAsync(plain)).GetProperty("canResync").GetBoolean());
        Assert.Equal("no_lyrics", (await _admin.GraphQLAsync(ResyncMutation, new { id = plain })).ErrorCode());

        Assert.Equal("not_found", (await _admin.GraphQLAsync(ResyncMutation, new { id = Guid.NewGuid() })).ErrorCode());

        // Changing the timing everyone sees takes EDIT_METADATA.
        await _factory.CreateUserAsync("listener");
        var listener = await _factory.CreateAuthenticatedClientAsync("listener");
        Assert.Equal("forbidden", (await listener.GraphQLAsync(ResyncMutation, new { id = timed })).ErrorCode());
    }

    public async ValueTask DisposeAsync()
    {
        if (_factory is not null)
            await _factory.DisposeAsync();
        Directory.Delete(_music, recursive: true);
    }
}
