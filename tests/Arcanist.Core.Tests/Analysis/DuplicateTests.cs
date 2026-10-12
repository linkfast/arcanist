using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Arcanist.Core.Api.Rest;
using Arcanist.Core.Data;
using Arcanist.Core.Data.Entities;
using Arcanist.Core.Setup;
using Arcanist.Core.Tests.Scanning;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Arcanist.Core.Tests.Analysis;

/// <summary>Duplicate detection by fingerprint, after analysis.</summary>
public sealed class DuplicateTests : IAsyncLifetime
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private readonly ArcanistFactory _factory = new(new Dictionary<string, string?> { ["Arcanist:Analysis:Enabled"] = "true" });
    private readonly string _music = Directory.CreateTempSubdirectory("arcanist-duplicates-").FullName;
    private HttpClient _admin = null!;

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    private async Task ScanAndAnalyzeAsync()
    {
        await _factory.CompleteSetupAsync(new SetupLibrary("Music", [_music]));
        _admin = await _factory.CreateAuthenticatedClientAsync();
        await _factory.WaitForScansAsync(_admin);
        await _factory.WaitForAnalysisAsync();
    }

    private ArcanistDbContext Db() => _factory.Services.GetRequiredService<IArcanistDbContextFactory>().CreateDbContext();

    [Fact]
    public async Task Copies_tagged_differently_on_one_album_become_one_track_and_stay_merged()
    {
        var album = new FixtureTags(Album: "Rips", AlbumArtist: "Band", Artist: "Band");
        AudioFixtures.WriteMusic(Path.Combine(_music, "cd", "01.wav"), album with { Title = "Song", TrackNumber = 1 }, chordSeed: 3);
        AudioFixtures.WriteMusic(Path.Combine(_music, "web", "song.wav"), album with { Title = "Song (Remastered)" }, chordSeed: 3, variant: 2);
        await ScanAndAnalyzeAsync();

        await using (var db = Db())
        {
            var track = await db.Tracks.Include(t => t.Files).SingleAsync(Cancel);
            Assert.Equal("Song", track.Title);
            Assert.Equal(2, track.Files.Count);
            Assert.Single(track.Files, f => f.TrackLocked);
        }

        // A full rescan reads both files again; the merge holds and the locked copy does not retitle the track.
        var library = (await _admin.GetFromJsonAsync<List<JsonElement>>("/rest/v1/libraries", ArcanistFactory.Json, Cancel))![0];
        await _admin.PostAsJsonAsync($"/rest/v1/libraries/{library.GetProperty("id").GetString()}/scans", new StartScanInput(Full: true), Cancel);
        await _factory.WaitForScansAsync(_admin);

        await using (var db = Db())
        {
            var track = await db.Tracks.Include(t => t.Files).SingleAsync(Cancel);
            Assert.Equal("Song", track.Title);
            Assert.Equal(2, track.Files.Count);
        }
    }

    [Fact]
    public async Task The_same_recording_on_two_albums_shares_a_recording()
    {
        AudioFixtures.WriteMusic(Path.Combine(_music, "album", "01.wav"),
            new FixtureTags(Title: "Hit", Album: "Debut", AlbumArtist: "Singer", Artist: "Singer", TrackNumber: 1), chordSeed: 5);
        AudioFixtures.WriteMusic(Path.Combine(_music, "best-of", "07.wav"),
            new FixtureTags(Title: "Hit", Album: "Greatest Hits", AlbumArtist: "Singer", Artist: "Singer", TrackNumber: 7), chordSeed: 5, variant: 1);
        await ScanAndAnalyzeAsync();

        await using var db = Db();
        var tracks = await db.Tracks.ToListAsync(Cancel);
        Assert.Equal(2, tracks.Count);
        Assert.Single(tracks.Select(t => t.RecordingId).Distinct());
    }

    [Fact]
    public async Task Doubtful_pairs_are_proposed_and_can_be_merged_or_dismissed()
    {
        // The same audio listed twice on one album, with two track numbers: an administrator decides.
        var album = new FixtureTags(Album: "Twice", AlbumArtist: "Band", Artist: "Band");
        AudioFixtures.WriteMusic(Path.Combine(_music, "01.wav"), album with { Title = "Theme", TrackNumber = 1 }, chordSeed: 8);
        AudioFixtures.WriteMusic(Path.Combine(_music, "09.wav"), album with { Title = "Theme (Reprise)", TrackNumber = 9 }, chordSeed: 8, variant: 3);
        AudioFixtures.WriteMusic(Path.Combine(_music, "03.wav"), album with { Title = "Ballad", TrackNumber = 3 }, bpm: 70, chordSeed: 2);
        AudioFixtures.WriteMusic(Path.Combine(_music, "10.wav"), album with { Title = "Ballad (Live)", TrackNumber = 10 }, bpm: 70, chordSeed: 2, variant: 4);
        await ScanAndAnalyzeAsync();

        var pairs = await _admin.GetFromJsonAsync<List<JsonElement>>("/rest/v1/duplicates", ArcanistFactory.Json, Cancel);
        Assert.Equal(2, pairs!.Count);
        Assert.All(pairs, p => Assert.True(p.GetProperty("similarity").GetDouble() >= 0.85));
        var theme = pairs.Single(p => p.GetProperty("trackA").GetProperty("title").GetString()!.StartsWith("Theme"));
        var ballad = pairs.Single(p => p.GetProperty("trackA").GetProperty("title").GetString()!.StartsWith("Ballad"));

        var viaGraphQL = await _admin.GraphQLAsync("{ duplicateCandidates { id trackA { title } trackB { title } similarity status } }");
        Assert.Equal(2, viaGraphQL.GetProperty("data").GetProperty("duplicateCandidates").GetArrayLength());

        var keep = theme.GetProperty("trackA").GetProperty("id").GetGuid();
        var merged = await _admin.PostAsJsonAsync($"/rest/v1/duplicates/{theme.GetProperty("id").GetString()}/merge", new MergeDuplicateInput(keep), Cancel);
        Assert.Equal(HttpStatusCode.OK, merged.StatusCode);
        var track = await merged.Content.ReadFromJsonAsync<JsonElement>(ArcanistFactory.Json, Cancel);
        Assert.Equal(2, track.GetProperty("files").GetArrayLength());

        var dismissed = await _admin.PostAsync($"/rest/v1/duplicates/{ballad.GetProperty("id").GetString()}/dismiss", null, Cancel);
        Assert.Equal(HttpStatusCode.NoContent, dismissed.StatusCode);

        Assert.Empty((await _admin.GetFromJsonAsync<List<JsonElement>>("/rest/v1/duplicates", ArcanistFactory.Json, Cancel))!);
        var kept = Assert.Single((await _admin.GetFromJsonAsync<List<JsonElement>>("/rest/v1/duplicates?status=dismissed", ArcanistFactory.Json, Cancel))!);
        Assert.Equal(ballad.GetProperty("id").GetString(), kept.GetProperty("id").GetString());

        // Resolved pairs cannot be resolved again.
        var again = await _admin.PostAsync($"/rest/v1/duplicates/{ballad.GetProperty("id").GetString()}/dismiss", null, Cancel);
        Assert.Equal(HttpStatusCode.NotFound, again.StatusCode);
    }

    [Fact]
    public async Task Only_library_managers_review_duplicates()
    {
        await _factory.CompleteSetupAsync();
        await _factory.CreateUserAsync("listener");
        var listener = await _factory.CreateAuthenticatedClientAsync("listener");

        Assert.Equal(HttpStatusCode.Forbidden, (await listener.GetAsync("/rest/v1/duplicates", Cancel)).StatusCode);
    }

    public async ValueTask DisposeAsync()
    {
        await _factory.DisposeAsync();
        Directory.Delete(_music, recursive: true);
    }
}
