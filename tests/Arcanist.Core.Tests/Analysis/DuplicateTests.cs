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

    private async Task<List<JsonElement>> PairsAsync() =>
        (await _admin.GetFromJsonAsync<List<JsonElement>>("/rest/v1/duplicates", ArcanistFactory.Json, Cancel))!;

    private async Task<JsonElement> TrackAsync(Guid id) =>
        await _admin.GetFromJsonAsync<JsonElement>($"/rest/v1/tracks/{id}", ArcanistFactory.Json, Cancel);

    private static string Code(JsonElement problem) => problem.GetProperty("code").GetString()!;

    [Fact]
    public async Task The_same_song_on_two_albums_is_proposed_and_can_be_kept_twice_or_play_one_file()
    {
        var lower = Path.Combine(_music, "best-of", "07.wav");
        AudioFixtures.WriteMusic(Path.Combine(_music, "album", "01.wav"),
            new FixtureTags(Title: "Hit", Album: "Debut", AlbumArtist: "Singer", Artist: "Singer", TrackNumber: 1), chordSeed: 5);
        AudioFixtures.WriteMusic(lower,
            new FixtureTags(Title: "Hit (Remastered)", Album: "Greatest Hits", AlbumArtist: "Singer", Artist: "Singer", TrackNumber: 7), chordSeed: 5, variant: 1);
        await ScanAndAnalyzeAsync();

        var pair = Assert.Single(await PairsAsync());
        Assert.Equal(2, pair.GetProperty("shareableFileIds").GetArrayLength());
        Assert.Equal(JsonValueKind.Null, pair.GetProperty("shareBlocked").ValueKind);

        Guid debut, greatest, file;
        await using (var db = Db())
        {
            debut = await db.Tracks.Where(t => t.Album.Title == "Debut").Select(t => t.Id).SingleAsync(Cancel);
            greatest = await db.Tracks.Where(t => t.Album.Title == "Greatest Hits").Select(t => t.Id).SingleAsync(Cancel);
            file = await db.MediaFiles.Where(f => f.TrackId == debut).Select(f => f.Id).SingleAsync(Cancel);
            db.TrackRatings.Add(new TrackRating { UserId = await db.Users.Select(u => u.Id).FirstAsync(Cancel), TrackId = greatest, Rating = 5, RatedAt = DateTime.UtcNow });
            await db.SaveChangesAsync(Cancel);
        }

        var id = pair.GetProperty("id").GetString();
        // Only one of the pair's own files.
        Assert.Equal(HttpStatusCode.BadRequest, (await _admin.PostAsJsonAsync($"/rest/v1/duplicates/{id}/share", new ShareDuplicateFileInput(Guid.NewGuid()), Cancel)).StatusCode);

        var shared = await _admin.PostAsJsonAsync($"/rest/v1/duplicates/{id}/share", new ShareDuplicateFileInput(file), Cancel);
        Assert.Equal(HttpStatusCode.NoContent, shared.StatusCode);

        // The other album keeps its track and what users did with it, and plays the one file; its copy is in the trash.
        Assert.False(File.Exists(lower));
        Assert.Single(Directory.GetFiles(Path.Combine(_music, ".arcanist-trash"), "07.wav", SearchOption.AllDirectories));
        var greatestHits = await TrackAsync(greatest);
        Assert.Equal("Hit (Remastered)", greatestHits.GetProperty("title").GetString());
        Assert.Equal(5, greatestHits.GetProperty("rating").GetInt32());
        var played = greatestHits.GetProperty("files")[0];
        Assert.Equal("album/01.wav", played.GetProperty("path").GetString());
        Assert.True(played.GetProperty("shared").GetBoolean());
        Assert.Equal(0, played.GetProperty("cueTrack").GetInt32());
        Assert.True(greatestHits.GetProperty("canEdit").GetBoolean());
        Assert.Equal(HttpStatusCode.OK, (await _admin.GetAsync($"/rest/v1/stream/{greatest}", Cancel)).StatusCode);
        Assert.Empty(await PairsAsync());

        // A full rescan reads the file for the track its tags describe; the other stays.
        var library = (await _admin.GetFromJsonAsync<List<JsonElement>>("/rest/v1/libraries", ArcanistFactory.Json, Cancel))![0].GetProperty("id").GetString();
        await _admin.PostAsJsonAsync($"/rest/v1/libraries/{library}/scans", new StartScanInput(Full: true), Cancel);
        await _factory.WaitForScansAsync(_admin);
        Assert.Equal("album/01.wav", (await TrackAsync(greatest)).GetProperty("files")[0].GetProperty("path").GetString());
        Assert.False((await TrackAsync(debut)).GetProperty("files")[0].GetProperty("shared").GetBoolean());

        // Its metadata lives in the catalog: an edit changes the track and not the file, whose tags stay the other's.
        var kept = Path.Combine(_music, "album", "01.wav");
        var bytes = await File.ReadAllBytesAsync(kept, Cancel);
        var editing = await _admin.PatchAsJsonAsync($"/rest/v1/tracks/{greatest}", new { title = "Hit, Best Of", trackNumber = 9 }, Cancel);
        Assert.Equal(HttpStatusCode.OK, editing.StatusCode);
        var edited = await TrackAsync(greatest);
        Assert.Equal("Hit, Best Of", edited.GetProperty("title").GetString());
        Assert.Equal(9, edited.GetProperty("trackNumber").GetInt32());
        Assert.Equal("Greatest Hits", edited.GetProperty("album").GetProperty("title").GetString());
        Assert.Equal(bytes, await File.ReadAllBytesAsync(kept, Cancel));
        Assert.Equal("Hit", (await TrackAsync(debut)).GetProperty("title").GetString());

        // Deleting the track that owns the file leaves it to the other, which now is what the file's tags say: a rescan agrees.
        Assert.Equal(HttpStatusCode.NoContent, (await _admin.DeleteAsync($"/rest/v1/tracks/{debut}", Cancel)).StatusCode);
        Assert.True(File.Exists(kept));
        Assert.Equal(HttpStatusCode.NotFound, (await _admin.GetAsync($"/rest/v1/tracks/{debut}", Cancel)).StatusCode);
        await _admin.PostAsJsonAsync($"/rest/v1/libraries/{library}/scans", new StartScanInput(Full: true), Cancel);
        await _factory.WaitForScansAsync(_admin);
        var heir = await TrackAsync(greatest);
        Assert.Equal("Hit, Best Of", heir.GetProperty("title").GetString());
        Assert.Equal(5, heir.GetProperty("rating").GetInt32());
        Assert.False(heir.GetProperty("files")[0].GetProperty("shared").GetBoolean());
        await using (var db = Db())
            Assert.Equal(1, await db.Tracks.CountAsync(Cancel));

        Assert.Equal(HttpStatusCode.NoContent, (await _admin.DeleteAsync($"/rest/v1/tracks/{greatest}", Cancel)).StatusCode);
        Assert.False(File.Exists(kept));
    }

    [Fact]
    public async Task Audio_the_library_has_arriving_with_another_album_is_not_stored_twice()
    {
        var incoming = Path.Combine(Directory.CreateTempSubdirectory("arcanist-incoming-").FullName, "song.wav");
        AudioFixtures.WriteMusic(Path.Combine(_music, "album", "01.wav"),
            new FixtureTags(Title: "Hit", Album: "Debut", AlbumArtist: "Singer", Artist: "Singer", TrackNumber: 1), chordSeed: 5);
        AudioFixtures.WriteMusic(incoming,
            new FixtureTags(Title: "Hit", Album: "Greatest Hits", AlbumArtist: "Singer", Artist: "Singer", TrackNumber: 7), chordSeed: 5);
        await ScanAndAnalyzeAsync();
        var library = (await _admin.GetFromJsonAsync<List<JsonElement>>("/rest/v1/libraries", ArcanistFactory.Json, Cancel))![0].GetProperty("id").GetString();
        var filesBefore = Directory.GetFiles(_music, "*", SearchOption.AllDirectories).Length;

        using var body = new ByteArrayContent(await File.ReadAllBytesAsync(incoming, Cancel));
        var upload = await _admin.PostAsync($"/rest/v1/libraries/{library}/uploads?fileName=song.wav", body, Cancel);
        var result = await upload.Content.ReadFromJsonAsync<JsonElement>(ArcanistFactory.Json, Cancel);
        Assert.Equal("DUPLICATE", result.GetProperty("status").GetString());

        Assert.Equal(filesBefore, Directory.GetFiles(_music, "*", SearchOption.AllDirectories).Length);
        var track = await TrackAsync(result.GetProperty("trackId").GetGuid());
        Assert.Equal("Greatest Hits", track.GetProperty("album").GetProperty("title").GetString());
        Assert.Equal(7, track.GetProperty("trackNumber").GetInt32());
        Assert.True(track.GetProperty("files")[0].GetProperty("shared").GetBoolean());
        Assert.Equal("album/01.wav", track.GetProperty("files")[0].GetProperty("path").GetString());

        // Again, or on the album that has it: nothing new.
        using var again = new ByteArrayContent(await File.ReadAllBytesAsync(incoming, Cancel));
        await _admin.PostAsync($"/rest/v1/libraries/{library}/uploads?fileName=song.wav", again, Cancel);
        await using var db = Db();
        Assert.Equal(2, await db.Tracks.CountAsync(Cancel));
    }

    [Fact]
    public async Task A_song_with_two_copies_is_listed_and_loses_one_but_never_the_last()
    {
        var album = new FixtureTags(Title: "Hit", Album: "Debut", AlbumArtist: "Singer", Artist: "Singer", TrackNumber: 1);
        AudioFixtures.WriteMusic(Path.Combine(_music, "a", "01.wav"), album, chordSeed: 5);
        AudioFixtures.WriteMusic(Path.Combine(_music, "b", "01.wav"), album, chordSeed: 5, variant: 1);
        await ScanAndAnalyzeAsync();

        var page = await _admin.GetFromJsonAsync<JsonElement>("/rest/v1/duplicates/copies", ArcanistFactory.Json, Cancel);
        Assert.Equal(1, page.GetProperty("totalCount").GetInt32());
        var song = page.GetProperty("items")[0];
        var files = song.GetProperty("deletableFileIds").EnumerateArray().Select(f => f.GetGuid()).ToList();
        Assert.Equal(2, files.Count);

        Assert.Equal(HttpStatusCode.NoContent, (await _admin.DeleteAsync($"/rest/v1/duplicates/copies/{files[0]}", Cancel)).StatusCode);
        Assert.Single(Directory.GetFiles(_music, "01.wav", SearchOption.AllDirectories), f => !f.Contains(".arcanist-trash"));
        Assert.Equal(0, (await _admin.GetFromJsonAsync<JsonElement>("/rest/v1/duplicates/copies", ArcanistFactory.Json, Cancel)).GetProperty("totalCount").GetInt32());

        var last = await _admin.DeleteAsync($"/rest/v1/duplicates/copies/{files[1]}", Cancel);
        Assert.Equal(HttpStatusCode.Conflict, last.StatusCode);
        Assert.Equal("last_copy", Code(await last.Content.ReadFromJsonAsync<JsonElement>(ArcanistFactory.Json, Cancel)));
    }

    [Fact]
    public async Task A_copy_of_audio_already_analyzed_and_added_later_is_found()
    {
        AudioFixtures.WriteMusic(Path.Combine(_music, "album", "01.wav"),
            new FixtureTags(Title: "Hit", Album: "Debut", AlbumArtist: "Singer", Artist: "Singer", TrackNumber: 1), chordSeed: 5);
        await ScanAndAnalyzeAsync();
        Assert.Empty(await PairsAsync());

        AudioFixtures.WriteMusic(Path.Combine(_music, "other", "03.wav"),
            new FixtureTags(Title: "Another Name", Album: "Elsewhere", AlbumArtist: "Someone Else", Artist: "Someone Else", TrackNumber: 3), chordSeed: 5);
        var library = (await _admin.GetFromJsonAsync<List<JsonElement>>("/rest/v1/libraries", ArcanistFactory.Json, Cancel))![0].GetProperty("id").GetString();
        await _admin.PostAsJsonAsync($"/rest/v1/libraries/{library}/scans", new StartScanInput(Full: false), Cancel);
        await _factory.WaitForScansAsync(_admin);
        Assert.Equal(HttpStatusCode.NoContent, (await _admin.PostAsync("/rest/v1/duplicates/rescan", null, Cancel)).StatusCode);

        for (var i = 0; i < 100 && (await PairsAsync()).Count == 0; i++)
            await Task.Delay(200, Cancel);
        Assert.Equal(1, Assert.Single(await PairsAsync()).GetProperty("similarity").GetDouble());
    }

    [Theory]
    [InlineData("hit", "hit")]
    [InlineData("hit (remastered)", "hit")]
    [InlineData("hit (2011 remaster)", "hit")]
    [InlineData("hit - remastered 2011", "hit")]
    [InlineData("hit [deluxe edition] (album version)", "hit")]
    [InlineData("hit (feat. someone)", "hit")]
    [InlineData("hit ft. someone", "hit")]
    [InlineData("hit (live)", "hit (live)")]
    [InlineData("hit (live 1985) (remastered)", "hit (live 1985)")]
    [InlineData("hit - acoustic version", "hit - acoustic version")]
    [InlineData("hit (2011 remix)", "hit (2011 remix)")]
    [InlineData("(remastered)", "(remastered)")]
    public void A_title_loses_only_what_says_which_pressing_it_is(string title, string core) =>
        Assert.Equal(core, Core.Analysis.DuplicateNames.CoreTitle(title));

    [Fact]
    public void One_artist_can_be_inside_the_other()
    {
        Assert.True(Core.Analysis.DuplicateNames.SameArtist(["queen"], ["queen & david bowie"]));
        Assert.False(Core.Analysis.DuplicateNames.SameArtist(["queen"], ["queensryche"]));
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
