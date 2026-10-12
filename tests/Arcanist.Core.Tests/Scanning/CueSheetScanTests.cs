using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Arcanist.Core.Api.Rest;
using Arcanist.Core.Data;
using Arcanist.Core.Setup;
using Arcanist.Core.Tests.Media;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Arcanist.Core.Tests.Scanning;

/// <summary>An album stored as one file with a cue sheet.</summary>
public sealed class CueSheetScanTests : IAsyncLifetime
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private const string Cue = """
        REM DATE 2003
        PERFORMER "The Splitters"
        TITLE "One File"
        FILE "One File.wav" WAVE
          TRACK 01 AUDIO
            TITLE "Slow Start"
            INDEX 01 00:00:00
          TRACK 02 AUDIO
            TITLE "Middle"
            PERFORMER "Guest"
            INDEX 01 00:12:00
          TRACK 03 AUDIO
            TITLE "Fast Finish"
            INDEX 01 00:24:00
        """;

    private readonly string _music = Directory.CreateTempSubdirectory("arcanist-cue-").FullName;
    private ArcanistFactory _factory = null!;
    private HttpClient _admin = null!;

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    private async Task StartAsync(string? ffmpeg = null)
    {
        // Three 12-second parts at different tempos; the file is named as the cue sheet says, but compressed later.
        var album = AudioFixtures.Concatenate(
            AudioFixtures.Music(bpm: 90, seconds: 12, chordSeed: 1),
            AudioFixtures.Music(bpm: 120, seconds: 12, chordSeed: 5),
            AudioFixtures.Music(bpm: 150, seconds: 12, chordSeed: 9));
        File.WriteAllBytes(Path.Combine(_music, "One File.wav"), album);
        File.WriteAllText(Path.Combine(_music, "One File.cue"), Cue);

        var settings = new Dictionary<string, string?>
        {
            ["Arcanist:Analysis:Enabled"] = "true",
            ["Arcanist:Ffmpeg:SearchSystem"] = "false",
            ["Arcanist:Ffmpeg:Path"] = ffmpeg ?? Path.Combine(_music, "no-ffmpeg"),
        };
        _factory = new ArcanistFactory(settings);
        await _factory.CompleteSetupAsync(new SetupLibrary("Music", [_music]));
        _admin = await _factory.CreateAuthenticatedClientAsync();
        await _factory.WaitForScansAsync(_admin);
    }

    private ArcanistDbContext Db() => _factory.Services.GetRequiredService<IArcanistDbContextFactory>().CreateDbContext();

    [Fact]
    public async Task Each_cue_track_is_a_track_with_its_own_analysis()
    {
        await StartAsync();
        await _factory.WaitForAnalysisAsync();

        await using var db = Db();
        var tracks = await db.Tracks.Include(t => t.Album).ThenInclude(a => a.AlbumArtist).Include(t => t.Files).Include(t => t.Artists).ThenInclude(a => a.Artist)
            .OrderBy(t => t.TrackNumber).ToListAsync(Cancel);

        Assert.Equal(["Slow Start", "Middle", "Fast Finish"], tracks.Select(t => t.Title));
        Assert.All(tracks, t => Assert.Equal(("One File", "The Splitters", 2003), (t.Album.Title, t.Album.AlbumArtist.Name, t.Album.Year)));
        Assert.Equal("Guest", tracks[1].Artists.Single().Artist.Name);
        Assert.Equal([12_000, 12_000, 12_000], tracks.Select(t => t.DurationMs));
        Assert.Equal([(0, 12_000), (12_000, 24_000), (24_000, 36_000)], tracks.Select(t => (t.Files[0].StartMs!.Value, t.Files[0].EndMs!.Value)));

        // Each part is analyzed on its own.
        var hashes = tracks.Select(t => t.Files[0].ContentHash).ToList();
        var analyses = await db.AudioAnalyses.Where(a => hashes.Contains(a.ContentHash)).ToDictionaryAsync(a => a.ContentHash, Cancel);
        Assert.Equal(3, analyses.Count);
        Assert.InRange(analyses[hashes[0]].Bpm!.Value, 89, 91);
        Assert.InRange(analyses[hashes[1]].Bpm!.Value, 119, 121);
        Assert.InRange(analyses[hashes[2]].Bpm!.Value, 149, 151);
        Assert.All(analyses.Values, a => Assert.Equal(12_000, a.DurationMs));
    }

    [Fact]
    public async Task Cue_tracks_play_converted_and_need_ffmpeg()
    {
        await StartAsync(TestFfmpeg.Path);
        var page = await _admin.GetFromJsonAsync<JsonElement>("/rest/v1/tracks?search=middle", ArcanistFactory.Json, Cancel);
        var trackId = page.GetProperty("items")[0].GetProperty("id").GetString();

        var response = await _admin.GetAsync($"/rest/v1/stream/{trackId}", Cancel);
        if (TestFfmpeg.Path is null)
        {
            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            return;
        }

        // The original is lossless (WAV), so the part is sent as FLAC.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("audio/flac", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("12", response.Headers.GetValues("X-Content-Duration").Single());
        var body = await response.Content.ReadAsByteArrayAsync(Cancel);
        Assert.Equal("fLaC"u8.ToArray(), body[..4]);
        // About 12 of the 36 seconds, compressed.
        Assert.InRange(body.Length, 100_000, 2_116_800);
    }

    [Fact]
    public async Task Removing_the_cue_sheet_turns_the_file_back_into_one_track()
    {
        await StartAsync();
        File.Delete(Path.Combine(_music, "One File.cue"));

        var library = (await _admin.GetFromJsonAsync<List<JsonElement>>("/rest/v1/libraries", ArcanistFactory.Json, Cancel))![0];
        await _admin.PostAsJsonAsync($"/rest/v1/libraries/{library.GetProperty("id").GetString()}/scans", new StartScanInput(Full: false), Cancel);
        await _factory.WaitForScansAsync(_admin);

        await using var db = Db();
        var file = await db.MediaFiles.Include(f => f.Track).SingleAsync(Cancel);
        Assert.Equal((0, (int?)null, 36_000), (file.CueTrack, file.StartMs, file.DurationMs));
        Assert.Equal(1, await db.Tracks.CountAsync(t => t.Files.Any(), Cancel));
    }

    public async ValueTask DisposeAsync()
    {
        if (_factory is not null)
            await _factory.DisposeAsync();
        Directory.Delete(_music, recursive: true);
    }
}
