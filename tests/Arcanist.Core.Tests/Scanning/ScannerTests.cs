using System.Net;
using System.Net.Http.Json;
using Arcanist.Core.Api.Rest;
using Arcanist.Core.Application.Scanning;
using Arcanist.Core.Data;
using Arcanist.Core.Data.Entities;
using Arcanist.Core.Setup;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ScanJob = Arcanist.Core.Application.Scanning.ScanJob;

namespace Arcanist.Core.Tests.Scanning;

public sealed class ScannerTests : IAsyncLifetime
{
    private readonly string _root = Directory.CreateTempSubdirectory("arcanist-scan").FullName;
    private readonly ArcanistFactory _factory;
    private HttpClient _admin = null!;
    private Guid _libraryId;

    public ScannerTests() => _factory = new ArcanistFactory(new Dictionary<string, string?>
    {
        // Keeps cleanup deterministic: missing files leave the catalog on the next scan.
        ["Arcanist:Scanner:MissingFileRetentionDays"] = "0",
        ["Arcanist:Scanner:BatchSize"] = "2",
    });

    private string Music => Path.Combine(_root, "Music");

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        AudioFixtures.WriteWav(Path.Combine(Music, "Sigur Ros", "Agaetis", "01.wav"), 1,
            new FixtureTags("Intro", "Sigur Rós", "Ágætis byrjun", TrackNumber: 1, Year: 1999, Genre: "Post-rock", WithCover: true));
        AudioFixtures.WriteWav(Path.Combine(Music, "Sigur Ros", "Agaetis", "02.wav"), 2,
            new FixtureTags("Svefn-g-englar", "Sigur Rós", "Ágætis byrjun", TrackNumber: 2, Genre: "Post-rock"));
        AudioFixtures.WriteWav(Path.Combine(Music, "Daft Punk", "Discovery", "01.wav"), 3,
            new FixtureTags("One More Time", "Daft Punk feat. Romanthony", "Discovery", TrackNumber: 1));
        File.WriteAllBytes(Path.Combine(Music, "Daft Punk", "Discovery", "cover.png"), AudioFixtures.Png(seed: 2));
        File.WriteAllText(Path.Combine(Music, "notes.txt"), "not audio");

        await _factory.CompleteSetupAsync(new SetupLibrary("Music", [Music]));
        _admin = await _factory.CreateAuthenticatedClientAsync();

        // Setup queues the first scan.
        var first = Assert.Single(await JobsAsync());
        _libraryId = first.LibraryId;
        Assert.Equal(ScanKind.Full, first.Kind);
        await WaitAsync(first.Id);
    }

    public async ValueTask DisposeAsync()
    {
        await _factory.DisposeAsync();
        Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task First_scan_builds_the_catalog_from_tags()
    {
        var job = (await JobsAsync())[0];
        Assert.Equal(ScanStatus.Completed, job.Status);
        Assert.Equal(3, job.FilesSeen);
        Assert.Equal(3, job.FilesAdded);
        Assert.Null(job.ErrorCode);

        await using var db = Db();
        var albums = await db.Albums.Include(a => a.AlbumArtist).OrderBy(a => a.Title).ToListAsync(Cancel);
        Assert.Equal(["Discovery", "Ágætis byrjun"], albums.Select(a => a.Title));
        Assert.Equal("Sigur Rós", albums[1].AlbumArtist.Name);
        Assert.Equal(1999, albums[1].Year);

        var intro = await db.Tracks.Include(t => t.Genres).ThenInclude(g => g.Genre).SingleAsync(t => t.Title == "Intro", Cancel);
        Assert.Equal(1, intro.TrackNumber);
        Assert.True(intro.DurationMs > 0);
        Assert.Equal("Post-rock", Assert.Single(intro.Genres).Genre.Name);

        var oneMoreTime = await db.Tracks.Include(t => t.Artists).ThenInclude(a => a.Artist).SingleAsync(t => t.Title == "One More Time", Cancel);
        Assert.Equal(["Daft Punk", "Romanthony"], oneMoreTime.Artists.OrderBy(a => a.Position).Select(a => a.Artist.Name));
        Assert.Equal(ArtistRole.Featured, oneMoreTime.Artists.Single(a => a.Artist.Name == "Romanthony").Role);

        // Embedded cover for one album, folder image for the other; both cached once.
        Assert.All(albums, a => Assert.NotNull(a.ArtworkId));
        Assert.Equal(2, await db.Artworks.CountAsync(Cancel));
        var artwork = await db.Artworks.FirstAsync(Cancel);
        Assert.True(File.Exists(Path.Combine(_factory.DataPath, "cache", "artwork", artwork.Hash[..2], artwork.Hash + ".png")));
    }

    [Fact]
    public async Task Untagged_files_take_artist_and_album_from_their_folders()
    {
        AudioFixtures.WriteWav(Path.Combine(Music, "Boards of Canada", "Geogaddi", "03 Gyroscope.wav"), 30, new FixtureTags());
        AudioFixtures.WriteWav(Path.Combine(Music, "Loose", "untitled.wav"), 31, new FixtureTags());

        await ScanAsync();

        await using var db = Db();
        var gyroscope = await db.Tracks.Include(t => t.Album).ThenInclude(a => a.AlbumArtist)
            .Include(t => t.Artists).ThenInclude(a => a.Artist)
            .SingleAsync(t => t.Title == "03 Gyroscope", Cancel);
        Assert.Equal("Geogaddi", gyroscope.Album.Title);
        Assert.Equal("Boards of Canada", gyroscope.Album.AlbumArtist.Name);
        Assert.Equal("Boards of Canada", Assert.Single(gyroscope.Artists).Artist.Name);

        // A single folder level is an album, not an artist.
        var untitled = await db.Tracks.Include(t => t.Album).ThenInclude(a => a.AlbumArtist).SingleAsync(t => t.Title == "untitled", Cancel);
        Assert.Equal("Loose", untitled.Album.Title);
        Assert.Equal("Unknown Artist", untitled.Album.AlbumArtist.Name);
    }

    [Fact]
    public async Task Unchanged_files_are_skipped_by_incremental_scans()
    {
        var job = await ScanAsync();

        Assert.Equal(ScanStatus.Completed, job.Status);
        Assert.Equal(3, job.FilesSeen);
        Assert.Equal(0, job.FilesAdded);
        Assert.Equal(0, job.FilesUpdated);
    }

    [Fact]
    public async Task Retagging_a_file_keeps_its_track_and_content_hash()
    {
        var path = Path.Combine(Music, "Sigur Ros", "Agaetis", "02.wav");
        await using var before = Db();
        var original = await before.MediaFiles.Include(f => f.Track).SingleAsync(f => f.RelativePath.EndsWith("Agaetis/02.wav"), Cancel);

        AudioFixtures.Tag(path, new FixtureTags("Svefn-g-englar (Remastered)", "Sigur Rós", "Ágætis byrjun", TrackNumber: 2));
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(1));

        var job = await ScanAsync();
        Assert.Equal(1, job.FilesUpdated);

        await using var after = Db();
        var updated = await after.MediaFiles.Include(f => f.Track).SingleAsync(f => f.Id == original.Id, Cancel);
        Assert.Equal(original.TrackId, updated.TrackId);
        Assert.Equal("Svefn-g-englar (Remastered)", updated.Track.Title);
        Assert.Equal(original.ContentHash, updated.ContentHash);
    }

    [Fact]
    public async Task Copies_of_the_same_song_are_one_track_with_several_files()
    {
        var copy = Path.Combine(Music, "Backup", "Intro.wav");
        Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
        File.Copy(Path.Combine(Music, "Sigur Ros", "Agaetis", "01.wav"), copy);

        var job = await ScanAsync();
        Assert.Equal(1, job.FilesAdded);

        await using var db = Db();
        var intro = await db.Tracks.Include(t => t.Files).SingleAsync(t => t.Title == "Intro", Cancel);
        Assert.Equal(2, intro.Files.Count);
        Assert.Equal(3, await db.Tracks.CountAsync(Cancel));
    }

    [Fact]
    public async Task Tracks_with_the_same_recording_id_merge_even_with_different_titles()
    {
        var mbid = Guid.NewGuid().ToString();
        AudioFixtures.WriteWav(Path.Combine(Music, "Live", "a.wav"), 10, new FixtureTags("Song", "Band", "Live", TrackNumber: 1, RecordingMbid: mbid));
        AudioFixtures.WriteWav(Path.Combine(Music, "Live", "b.wav"), 11, new FixtureTags("Song (Live)", "Band", "Live", TrackNumber: 1, RecordingMbid: mbid));

        await ScanAsync();

        await using var db = Db();
        var track = await db.Tracks.Include(t => t.Files).Include(t => t.Recording).SingleAsync(t => t.Album.Title == "Live", Cancel);
        Assert.Equal(2, track.Files.Count);
        Assert.Equal(Guid.Parse(mbid), track.Recording.MusicBrainzId);
    }

    [Fact]
    public async Task Deleted_files_are_marked_missing_then_removed_with_what_depended_on_them()
    {
        File.Delete(Path.Combine(Music, "Daft Punk", "Discovery", "01.wav"));

        var job = await ScanAsync();
        Assert.Equal(1, job.FilesMissing);

        // Retention is 0 days in these tests, so the next scan purges it and the now-empty album and artists.
        await ScanAsync();
        await using var db = Db();
        Assert.False(await db.Albums.AnyAsync(a => a.Title == "Discovery", Cancel));
        Assert.False(await db.Artists.AnyAsync(a => a.Name == "Romanthony", Cancel));
        Assert.Equal(1, await db.Artworks.CountAsync(Cancel));
        Assert.Equal(2, await db.Tracks.CountAsync(Cancel));
    }

    [Fact]
    public async Task An_unreachable_source_is_skipped_without_losing_the_catalog()
    {
        Directory.Move(Music, Music + "-unplugged");
        try
        {
            var job = await ScanAsync();

            Assert.Equal(ScanStatus.Completed, job.Status);
            Assert.Equal("source_unavailable", job.ErrorCode);
            Assert.Equal(0, job.FilesMissing);
            await using var db = Db();
            Assert.Equal(3, await db.MediaFiles.CountAsync(f => f.MissingSince == null, Cancel));
        }
        finally
        {
            Directory.Move(Music + "-unplugged", Music);
        }
    }

    [Fact]
    public async Task A_source_that_suddenly_looks_empty_is_treated_as_unmounted()
    {
        Directory.Move(Music, Music + "-real");
        Directory.CreateDirectory(Music); // an empty mount point
        try
        {
            var job = await ScanAsync();

            Assert.Equal("source_unavailable", job.ErrorCode);
            Assert.Equal(0, job.FilesMissing);
        }
        finally
        {
            Directory.Delete(Music);
            Directory.Move(Music + "-real", Music);
        }
    }

    [Fact]
    public async Task Starting_a_scan_requires_the_permission()
    {
        await _factory.CreateUserAsync("listener");
        var listener = await _factory.CreateAuthenticatedClientAsync("listener");

        var response = await listener.PostAsJsonAsync($"/rest/v1/libraries/{_libraryId}/scans", new StartScanInput(), Cancel);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        var graphql = await listener.GraphQLAsync("{ scanJobs { id } }");
        Assert.Equal("forbidden", graphql.ErrorCode());
    }

    [Fact]
    public async Task Scans_can_be_started_and_followed_over_graphql()
    {
        var started = await _admin.GraphQLAsync("mutation($id: ID!) { startScan(libraryId: $id, full: true) { id kind } }", new { id = _libraryId });
        var job = started.GetProperty("data").GetProperty("startScan");
        Assert.Equal("FULL", job.GetProperty("kind").GetString());

        var finished = await WaitAsync(Guid.Parse(job.GetProperty("id").GetString()!));
        Assert.Equal(3, finished.FilesUpdated);
    }

    private async Task<ScanJob> ScanAsync()
    {
        var response = await _admin.PostAsJsonAsync($"/rest/v1/libraries/{_libraryId}/scans", new StartScanInput(), Cancel);
        response.EnsureSuccessStatusCode();
        var job = (await response.Content.ReadFromJsonAsync<ScanJob>(ArcanistFactory.Json, Cancel))!;
        return await WaitAsync(job.Id);
    }

    private async Task<ScanJob> WaitAsync(Guid jobId)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (true)
        {
            var job = (await _admin.GetFromJsonAsync<ScanJob>($"/rest/v1/scans/{jobId}", ArcanistFactory.Json, Cancel))!;
            if (job.Status is not (ScanStatus.Queued or ScanStatus.Running))
                return job;

            Assert.True(DateTime.UtcNow < deadline, $"Scan {jobId} did not finish in time.");
            await Task.Delay(50, Cancel);
        }
    }

    private async Task<List<ScanJob>> JobsAsync() =>
        (await _admin.GetFromJsonAsync<List<ScanJob>>("/rest/v1/scans", ArcanistFactory.Json, Cancel))!;

    private ArcanistDbContext Db() => _factory.Services.GetRequiredService<IArcanistDbContextFactory>().CreateDbContext();
}
