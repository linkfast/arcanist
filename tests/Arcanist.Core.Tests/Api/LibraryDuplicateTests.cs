using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Arcanist.Core.Data;
using Arcanist.Core.Data.Entities;
using Arcanist.Core.Setup;
using Arcanist.Core.Tests.Scanning;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Arcanist.Core.Tests.Api;

/// <summary>Songs that several libraries have: found across libraries, with who would lose a song if one library's copy went.</summary>
public sealed class LibraryDuplicateTests : IAsyncLifetime
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private readonly string _home = Directory.CreateTempSubdirectory("arcanist-dup-home-").FullName;
    private readonly string _kids = Directory.CreateTempSubdirectory("arcanist-dup-kids-").FullName;
    private readonly ArcanistFactory _factory = new();
    private HttpClient _admin = null!;
    private Guid _homeId;
    private Guid _kidsId;

    public async ValueTask InitializeAsync()
    {
        // The same song in both libraries (one track with two copies), and one only the first has.
        var tags = new FixtureTags("Shared", "The Band", "Album", "The Band", TrackNumber: 1);
        AudioFixtures.WriteWav(Path.Combine(_home, "Shared.wav"), 1, tags);
        AudioFixtures.WriteWav(Path.Combine(_kids, "Shared.wav"), 1, tags);
        AudioFixtures.WriteWav(Path.Combine(_home, "Alone.wav"), 2, new FixtureTags("Alone", "The Band", "Album", "The Band", TrackNumber: 2));
        await _factory.CompleteSetupAsync(new SetupLibrary("Home", [_home]), new SetupLibrary("Kids", [_kids]));
        _admin = await _factory.CreateAuthenticatedClientAsync();
        await _factory.WaitForScansAsync(_admin);

        var libraries = await _admin.GetFromJsonAsync<JsonElement>("/rest/v1/libraries", ArcanistFactory.Json, Cancel);
        var ids = libraries.EnumerateArray().ToDictionary(l => l.GetProperty("name").GetString()!, l => l.GetProperty("id").GetGuid());
        (_homeId, _kidsId) = (ids["Home"], ids["Kids"]);

        // Ana listens to Home only; Bruno to both.
        var ana = await _factory.CreateUserAsync("ana");
        var bruno = await _factory.CreateUserAsync("bruno");
        await using var db = _factory.Services.GetRequiredService<IArcanistDbContextFactory>().CreateDbContext();
        await db.Libraries.ExecuteUpdateAsync(l => l.SetProperty(x => x.VisibleToAll, false), Cancel);
        db.LibraryAccess.AddRange(
            new LibraryAccess { UserId = ana.Id, LibraryId = _homeId },
            new LibraryAccess { UserId = bruno.Id, LibraryId = _homeId },
            new LibraryAccess { UserId = bruno.Id, LibraryId = _kidsId, ReadOnly = true });
        await db.SaveChangesAsync(Cancel);
    }

    public async ValueTask DisposeAsync()
    {
        await _factory.DisposeAsync();
        foreach (var folder in new[] { _home, _kids })
            Directory.Delete(folder, recursive: true);
    }

    private async Task<JsonElement> PageAsync(string query = "") =>
        await _admin.GetFromJsonAsync<JsonElement>("/rest/v1/duplicates/libraries" + query, ArcanistFactory.Json, Cancel);

    [Fact]
    public async Task Songs_in_several_libraries_say_who_would_lose_them()
    {
        var page = await PageAsync();
        Assert.Equal(1, page.GetProperty("totalCount").GetInt32());
        var song = Assert.Single(page.GetProperty("items").EnumerateArray());
        Assert.Equal("Shared", song.GetProperty("track").GetProperty("title").GetString());
        Assert.Equal(2, song.GetProperty("track").GetProperty("files").GetArrayLength());

        var libraries = song.GetProperty("libraries").EnumerateArray().ToDictionary(l => l.GetProperty("name").GetString()!);
        Assert.True(libraries["Home"].GetProperty("canDelete").GetBoolean());
        // Deleting Home's copy leaves Ana without it (she has no Kids); Bruno still has Kids's. Kids's copy hurts nobody.
        Assert.Equal(["ana"], libraries["Home"].GetProperty("losingUsers").EnumerateArray().Select(u => u.GetProperty("name").GetString()));
        Assert.Empty(libraries["Kids"].GetProperty("losingUsers").EnumerateArray());

        // One library's view: the songs it has among others.
        Assert.Equal(1, (await PageAsync($"?libraryId={_kidsId}")).GetProperty("totalCount").GetInt32());

        // Deleting Kids's copy leaves the song in Home, and no longer a duplicate.
        var trackId = song.GetProperty("track").GetProperty("id").GetString();
        var deleted = await _admin.DeleteAsync($"/rest/v1/tracks/{trackId}?libraryId={_kidsId}", Cancel);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal(0, (await PageAsync()).GetProperty("totalCount").GetInt32());
        Assert.True(File.Exists(Path.Combine(_home, "Shared.wav")));
        Assert.False(File.Exists(Path.Combine(_kids, "Shared.wav")));
    }

    [Fact]
    public async Task Only_library_managers_see_them()
    {
        await _factory.CreateUserAsync("guest");
        var guest = await _factory.CreateAuthenticatedClientAsync("guest");
        Assert.Equal(HttpStatusCode.Forbidden, (await guest.GetAsync("/rest/v1/duplicates/libraries", Cancel)).StatusCode);
    }
}
