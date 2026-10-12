using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Arcanist.Core.Api.Rest;
using Arcanist.Core.Scanning;
using Arcanist.Core.Setup;

namespace Arcanist.Core.Tests.Scanning;

public sealed class LrcTests
{
    [Fact]
    public void Parses_timed_lines_and_ignores_the_rest()
    {
        const string lrc = "[ar:Someone]\n[00:12.50]First line\n[00:15.00][01:15.00]Chorus\nno time here\n[1:02.3]Late\n";

        var lines = Lrc.Parse(lrc);

        Assert.Equal(
            [(12_500, "First line"), (15_000, "Chorus"), (62_300, "Late"), (75_000, "Chorus")],
            lines);
    }

    [Fact]
    public void Formats_what_it_parses()
    {
        (int, string)[] lines = [(0, "Intro"), (83_450, "Verse")];

        Assert.Equal("[00:00.00]Intro\n[01:23.45]Verse\n", Lrc.Format(lines));
        Assert.Equal(lines, Lrc.Parse(Lrc.Format(lines)));
    }
}

public sealed class LyricsTests : IAsyncLifetime
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private readonly ArcanistFactory _factory = new();
    private readonly string _music = Directory.CreateTempSubdirectory("arcanist-lyrics-").FullName;
    private HttpClient _admin = null!;

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    private async Task<JsonElement> TrackAsync(string title)
    {
        var page = await _admin.GetFromJsonAsync<JsonElement>($"/rest/v1/tracks?search={Uri.EscapeDataString(title)}", ArcanistFactory.Json, Cancel);
        return page.GetProperty("items")[0];
    }

    [Fact]
    public async Task Lyrics_come_from_tags_and_from_sidecar_files_which_win()
    {
        AudioFixtures.WriteWav(Path.Combine(_music, "tagged.wav"), 1, new FixtureTags(Title: "Tagged", Lyrics: "Words in the tags"));
        AudioFixtures.WriteWav(Path.Combine(_music, "both.wav"), 2, new FixtureTags(Title: "Both", Lyrics: "Tag words"));
        File.WriteAllText(Path.Combine(_music, "both.lrc"), "[00:01.00]Sidecar line one\n[00:02.50]Sidecar line two\n");
        AudioFixtures.WriteWav(Path.Combine(_music, "none.wav"), 3, new FixtureTags(Title: "Nothing"));

        await _factory.CompleteSetupAsync(new SetupLibrary("Music", [_music]));
        _admin = await _factory.CreateAuthenticatedClientAsync();
        await _factory.WaitForScansAsync(_admin);

        var tagged = await TrackAsync("Tagged");
        Assert.True(tagged.GetProperty("hasLyrics").GetBoolean());
        var taggedLyrics = await _admin.GetFromJsonAsync<JsonElement>($"/rest/v1/tracks/{tagged.GetProperty("id")}/lyrics", ArcanistFactory.Json, Cancel);
        Assert.Equal("Words in the tags", taggedLyrics.GetProperty("plain").GetString());
        Assert.Equal("EMBEDDED", taggedLyrics.GetProperty("source").GetString());
        Assert.Equal(JsonValueKind.Null, taggedLyrics.GetProperty("synced").ValueKind);

        var both = await TrackAsync("Both");
        var bothLyrics = await _admin.GraphQLAsync("query($id: ID!) { trackLyrics(trackId: $id) { plain source synced { timeMs text } } }",
            new { id = both.GetProperty("id").GetString() });
        var lyrics = bothLyrics.GetProperty("data").GetProperty("trackLyrics");
        Assert.Equal("SIDECAR", lyrics.GetProperty("source").GetString());
        Assert.Equal(2500, lyrics.GetProperty("synced")[1].GetProperty("timeMs").GetInt32());
        Assert.Equal("Sidecar line one\nSidecar line two", lyrics.GetProperty("plain").GetString());

        var none = await TrackAsync("Nothing");
        Assert.False(none.GetProperty("hasLyrics").GetBoolean());
        Assert.Equal(HttpStatusCode.NotFound, (await _admin.GetAsync($"/rest/v1/tracks/{none.GetProperty("id")}/lyrics", Cancel)).StatusCode);
    }

    [Fact]
    public async Task A_sidecar_added_later_is_picked_up_by_an_incremental_scan()
    {
        AudioFixtures.WriteWav(Path.Combine(_music, "song.wav"), 1, new FixtureTags(Title: "Song"));
        await _factory.CompleteSetupAsync(new SetupLibrary("Music", [_music]));
        _admin = await _factory.CreateAuthenticatedClientAsync();
        await _factory.WaitForScansAsync(_admin);
        Assert.False((await TrackAsync("Song")).GetProperty("hasLyrics").GetBoolean());

        var lrc = Path.Combine(_music, "song.lrc");
        File.WriteAllText(lrc, "[00:00.50]Hello\n");
        File.SetLastWriteTimeUtc(lrc, DateTime.UtcNow.AddMinutes(1));
        var library = (await _admin.GetFromJsonAsync<List<JsonElement>>("/rest/v1/libraries", ArcanistFactory.Json, Cancel))![0];
        await _admin.PostAsJsonAsync($"/rest/v1/libraries/{library.GetProperty("id").GetString()}/scans", new StartScanInput(Full: false), Cancel);
        await _factory.WaitForScansAsync(_admin);

        var track = await TrackAsync("Song");
        Assert.True(track.GetProperty("hasLyrics").GetBoolean());
        var lyrics = await _admin.GetFromJsonAsync<JsonElement>($"/rest/v1/tracks/{track.GetProperty("id")}/lyrics", ArcanistFactory.Json, Cancel);
        Assert.Equal("Hello", lyrics.GetProperty("synced")[0].GetProperty("text").GetString());
    }

    public async ValueTask DisposeAsync()
    {
        await _factory.DisposeAsync();
        Directory.Delete(_music, recursive: true);
    }
}
