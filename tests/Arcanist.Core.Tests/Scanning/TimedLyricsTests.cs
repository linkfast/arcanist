using System.Net.Http.Json;
using System.Text.Json;
using Arcanist.Core.Application.Online;
using Arcanist.Core.Media;
using Arcanist.Core.Scanning;
using Arcanist.Core.Setup;
using Arcanist.Core.Tests.Online;
using Microsoft.EntityFrameworkCore;
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

    [Fact]
    public async Task Timing_worked_out_here_is_kept_while_the_words_stay_the_same()
    {
        var path = Path.Combine(_music, "plain.wav");
        AudioFixtures.WriteWav(path, 1, new FixtureTags("Plain", "Band", Lyrics: "First words\nSecond words"));
        AudioFixtures.WriteWav(Path.Combine(_music, "timed.wav"), 2,
            new FixtureTags("Timed", "Band", Lyrics: "[00:01.00]Timed words\n[00:02.00]More"));
        _online.Respond = _ => new HttpResponseMessage(System.Net.HttpStatusCode.NotFound);
        _factory = new ArcanistFactory(new Dictionary<string, string?> { ["Arcanist:Online:RequestInterval"] = "00:00:00.010" },
            services: _online.Register);
        await _factory.CompleteSetupAsync(new SetupLibrary("Music", [_music]));
        _admin = await _factory.CreateAuthenticatedClientAsync();
        await _factory.WaitForScansAsync(_admin);
        var timing = _factory.Services.GetRequiredService<Core.Application.Catalog.LyricsTiming>();
        var id = Guid.Parse(await TrackIdAsync("Plain"));

        // With LRCLib off, plain lyrics are timed here straight away; already timed ones are not.
        var pending = Assert.Single(await timing.PendingAsync(null, 10, Cancel));
        Assert.Equal((id, "First words\nSecond words", "Band"), (pending.TrackId, pending.Plain, pending.Artist));
        Assert.Equal(Path.GetFullPath(path), Path.GetFullPath(pending.AudioPath));
        Assert.Empty(await timing.PendingAsync(id, 10, Cancel));

        // A listener looking at them asks for them first.
        Assert.Empty(timing.TakeRequested());
        await LyricsAsync(id.ToString());
        Assert.Equal([id], timing.TakeRequested());

        Assert.False(await timing.SaveAsync(id, "other words", "[00:01.00]First words", Cancel));
        Assert.True(await timing.SaveAsync(id, pending.PlainHash, "[00:01.00]First words\n[00:03.20]Second words", Cancel));
        var lyrics = await LyricsAsync(id.ToString());
        Assert.Equal(("EMBEDDED", "ALIGNED"), (lyrics.GetProperty("source").GetString(), lyrics.GetProperty("syncedSource").GetString()));
        Assert.Equal([1000, 3200], lyrics.GetProperty("synced").EnumerateArray().Select(l => l.GetProperty("timeMs").GetInt32()));
        Assert.Empty(await timing.PendingAsync(null, 10, Cancel));
        Assert.Equal(1, (await timing.CountAsync(Cancel)).Aligned);

        // Read again, the same words keep their timing; other words lose it and wait to be timed again.
        var libraryId = (await _admin.GetFromJsonAsync<JsonElement>("/rest/v1/libraries", ArcanistFactory.Json, Cancel))[0].GetProperty("id").GetString();
        (await _admin.PostAsJsonAsync($"/rest/v1/libraries/{libraryId}/scans", new Core.Api.Rest.StartScanInput(Full: true), Cancel)).EnsureSuccessStatusCode();
        await _factory.WaitForScansAsync(_admin);
        Assert.Equal("ALIGNED", (await LyricsAsync(id.ToString())).GetProperty("syncedSource").GetString());

        AudioFixtures.WriteWav(path, 1, new FixtureTags("Plain", "Band", Lyrics: "New words\nSecond words"));
        (await _admin.PostAsJsonAsync($"/rest/v1/libraries/{libraryId}/scans", new Core.Api.Rest.StartScanInput(Full: true), Cancel)).EnsureSuccessStatusCode();
        await _factory.WaitForScansAsync(_admin);
        Assert.Equal(JsonValueKind.Null, (await LyricsAsync(id.ToString())).GetProperty("synced").ValueKind);
        Assert.Equal("New words\nSecond words", Assert.Single(await timing.PendingAsync(null, 10, Cancel)).Plain);

        // With LRCLib on, only what it did not find is timed here (its answers written here, and not due again: the
        // test does not wait for LRCLib's worker). The subject is the track id as the database writes it, as the
        // worker records it (SQLite keeps Guids in upper case).
        Core.Data.Entities.OnlineLookup lookup;
        await using (var db = _factory.Services.GetRequiredService<Core.Data.IArcanistDbContextFactory>().CreateDbContext())
        {
            var subject = await db.Tracks.Where(t => t.Id == id).Select(t => t.Id.ToString()).SingleAsync(Cancel);
            lookup = new Core.Data.Entities.OnlineLookup
            {
                Service = OnlineService.Lyrics, Subject = subject, Outcome = Core.Data.Entities.LookupOutcome.Failed,
                CheckedAt = DateTime.UtcNow, RetryAfter = DateTime.UtcNow.AddDays(1),
            };
            db.OnlineLookups.Add(lookup);
            await db.SaveChangesAsync(Cancel);
        }

        (await _admin.PutAsJsonAsync("/rest/v1/online-services", new SetOnlineServicesInput(false, false, false, true), Cancel)).EnsureSuccessStatusCode();
        Assert.Empty(await timing.PendingAsync(null, 10, Cancel));
        await using (var db = _factory.Services.GetRequiredService<Core.Data.IArcanistDbContextFactory>().CreateDbContext())
        {
            db.OnlineLookups.Attach(lookup);
            lookup.Outcome = Core.Data.Entities.LookupOutcome.NotFound;
            lookup.RetryAfter = DateTime.UtcNow.AddDays(90);
            await db.SaveChangesAsync(Cancel);
        }

        Assert.Equal(id, Assert.Single(await timing.PendingAsync(null, 10, Cancel)).TrackId);
        Assert.Equal(1, (await timing.CountAsync(Cancel)).Untimed);
    }

    public async ValueTask DisposeAsync()
    {
        if (_factory is not null)
            await _factory.DisposeAsync();
        Directory.Delete(_music, recursive: true);
    }
}
