using System.Net.Http.Json;
using System.Text.Json;
using Arcanist.Core.Api.Rest;
using Arcanist.Core.Data;
using Arcanist.Core.Scanning;
using Arcanist.Core.Setup;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Arcanist.Core.Tests.Scanning;

/// <summary>Compilations whose tags do not say so: several artists, one album title, one folder.</summary>
public sealed class CompilationTests : IAsyncLifetime
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private readonly ArcanistFactory _factory = new();
    private readonly string _music = Directory.CreateTempSubdirectory("arcanist-compilations-").FullName;
    private HttpClient _admin = null!;

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    private ArcanistDbContext Db() => _factory.Services.GetRequiredService<IArcanistDbContextFactory>().CreateDbContext();

    [Fact]
    public async Task A_folder_of_several_artists_under_one_title_is_one_various_artists_album()
    {
        var hits = Path.Combine(_music, "Summer Hits");
        AudioFixtures.WriteWav(Path.Combine(hits, "01.wav"), 1, new FixtureTags(Title: "Sun", Artist: "Alpha", Album: "Summer Hits", TrackNumber: 1));
        AudioFixtures.WriteWav(Path.Combine(hits, "02.wav"), 2, new FixtureTags(Title: "Sea", Artist: "Beta", Album: "Summer Hits", TrackNumber: 2));
        AudioFixtures.WriteWav(Path.Combine(hits, "03.wav"), 3, new FixtureTags(Title: "Sand", Artist: "Gamma", Album: "Summer Hits", TrackNumber: 3));
        // A real album by one of them elsewhere stays theirs.
        AudioFixtures.WriteWav(Path.Combine(_music, "Alpha", "Debut", "01.wav"), 4, new FixtureTags(Title: "First", Artist: "Alpha", Album: "Debut"));
        AudioFixtures.WriteWav(Path.Combine(_music, "Alpha", "Debut", "02.wav"), 5, new FixtureTags(Title: "Second", Artist: "Alpha", Album: "Debut"));

        await _factory.CompleteSetupAsync(new SetupLibrary("Music", [_music]));
        _admin = await _factory.CreateAuthenticatedClientAsync();
        await _factory.WaitForScansAsync(_admin);

        await using (var db = Db())
        {
            var albums = await db.Albums.Include(a => a.AlbumArtist).Include(a => a.Tracks).OrderBy(a => a.Title).ToListAsync(Cancel);
            Assert.Equal(["Debut", "Summer Hits"], albums.Select(a => a.Title));
            Assert.Equal(("Alpha", false, 2), (albums[0].AlbumArtist.Name, albums[0].IsCompilation, albums[0].Tracks.Count));
            Assert.Equal((CatalogWriter.VariousArtists, true, 3), (albums[1].AlbumArtist.Name, albums[1].IsCompilation, albums[1].Tracks.Count));
        }

        // A song added later in an incremental scan joins the compilation.
        AudioFixtures.WriteWav(Path.Combine(hits, "04.wav"), 6, new FixtureTags(Title: "Surf", Artist: "Delta", Album: "Summer Hits", TrackNumber: 4));
        var library = (await _admin.GetFromJsonAsync<List<JsonElement>>("/rest/v1/libraries", ArcanistFactory.Json, Cancel))![0];
        await _admin.PostAsJsonAsync($"/rest/v1/libraries/{library.GetProperty("id").GetString()}/scans", new StartScanInput(Full: false), Cancel);
        await _factory.WaitForScansAsync(_admin);

        await using (var db = Db())
        {
            var album = await db.Albums.Include(a => a.Tracks).SingleAsync(a => a.Title == "Summer Hits", Cancel);
            Assert.Equal(4, album.Tracks.Count);
            Assert.Equal(2, await db.Albums.CountAsync(Cancel));
        }
    }

    [Fact]
    public async Task Discs_of_a_compilation_in_subfolders_become_one_album()
    {
        foreach (var (disc, offset) in new[] { ("CD1", 0), ("CD2", 10) })
        {
            var folder = Path.Combine(_music, "Mix", disc);
            AudioFixtures.WriteWav(Path.Combine(folder, "01.wav"), offset + 1, new FixtureTags(Title: $"A{disc}", Artist: "Alpha", Album: "Mix", DiscNumber: offset / 10 + 1));
            AudioFixtures.WriteWav(Path.Combine(folder, "02.wav"), offset + 2, new FixtureTags(Title: $"B{disc}", Artist: "Beta", Album: "Mix", DiscNumber: offset / 10 + 1));
        }

        await _factory.CompleteSetupAsync(new SetupLibrary("Music", [_music]));
        _admin = await _factory.CreateAuthenticatedClientAsync();
        await _factory.WaitForScansAsync(_admin);

        // The cleaner removes the albums emptied by merges at the end of the scan.
        await using var db = Db();
        var album = await db.Albums.Include(a => a.AlbumArtist).Include(a => a.Tracks).Where(a => a.Tracks.Any()).SingleAsync(Cancel);
        Assert.Equal((CatalogWriter.VariousArtists, 4), (album.AlbumArtist.Name, album.Tracks.Count));
    }

    public async ValueTask DisposeAsync()
    {
        await _factory.DisposeAsync();
        Directory.Delete(_music, recursive: true);
    }
}
