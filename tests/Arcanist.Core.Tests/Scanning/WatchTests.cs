using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Arcanist.Core.Application.Libraries;
using Arcanist.Core.Setup;

namespace Arcanist.Core.Tests.Scanning;

/// <summary>Libraries that watch their folder, and scheduled scans in minutes.</summary>
public sealed class WatchTests : IAsyncLifetime
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private readonly string _music = Directory.CreateTempSubdirectory("arcanist-watch-").FullName;
    private readonly string _outside = Directory.CreateTempSubdirectory("arcanist-watch-outside-").FullName;
    private ArcanistFactory _factory = null!;
    private HttpClient _admin = null!;
    private Guid _libraryId;

    public async ValueTask InitializeAsync()
    {
        AudioFixtures.WriteWav(Path.Combine(_music, "one.wav"), 1, new FixtureTags("One", "The Band", "First", "The Band", TrackNumber: 1));

        _factory = new ArcanistFactory(new Dictionary<string, string?> { ["Arcanist:Organization:StableSeconds"] = "0" });
        await _factory.CompleteSetupAsync(new SetupLibrary("Music", [_music]));
        _admin = await _factory.CreateAuthenticatedClientAsync();
        await _factory.WaitForScansAsync(_admin);
        var libraries = await _admin.GetFromJsonAsync<List<JsonElement>>("/rest/v1/libraries", ArcanistFactory.Json, Cancel);
        _libraryId = libraries![0].GetProperty("id").GetGuid();
    }

    private async Task<int> TrackCountAsync() =>
        (await _admin.GraphQLAsync("query { tracks(first: 10) { totalCount } }"))
        .GetProperty("data").GetProperty("tracks").GetProperty("totalCount").GetInt32();

    [Fact]
    public async Task Files_added_to_a_watched_library_are_scanned_without_asking()
    {
        var response = await _admin.PutAsJsonAsync($"/rest/v1/libraries/{_libraryId}",
            new UpdateLibraryInput("Music", true, 30, WatchForChanges: true), ArcanistFactory.Json, Cancel);
        var library = (await response.Content.ReadFromJsonAsync<Library>(ArcanistFactory.Json, Cancel))!;
        Assert.Equal((30, true), (library.ScanIntervalMinutes, library.WatchForChanges));

        // Written outside and moved in, so the watcher never sees it half-written.
        var file = Path.Combine(_outside, "two.wav");
        AudioFixtures.WriteWav(file, 2, new FixtureTags("Two", "The Band", "First", "The Band", TrackNumber: 2));
        // The watcher picks the library up within a second of the change.
        await Task.Delay(TimeSpan.FromSeconds(2), Cancel);
        File.Move(file, Path.Combine(_music, "two.wav"));

        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (await TrackCountAsync() < 2)
        {
            Assert.True(DateTime.UtcNow < deadline, "The new file was not scanned");
            await Task.Delay(200, Cancel);
        }
    }

    [Fact]
    public async Task Intervals_are_in_minutes_from_five_to_thirty_days()
    {
        foreach (var minutes in new[] { 4, 60 * 24 * 30 + 1 })
        {
            var refused = await _admin.PutAsJsonAsync($"/rest/v1/libraries/{_libraryId}",
                new UpdateLibraryInput("Music", true, minutes, false), ArcanistFactory.Json, Cancel);
            Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
            Assert.Contains("scanIntervalMinutes", await refused.Content.ReadAsStringAsync(Cancel));
        }

        var accepted = await _admin.PutAsJsonAsync($"/rest/v1/libraries/{_libraryId}",
            new UpdateLibraryInput("Music", true, 5, false), ArcanistFactory.Json, Cancel);
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
    }

    public async ValueTask DisposeAsync()
    {
        await _factory.DisposeAsync();
        foreach (var folder in new[] { _music, _outside })
        {
            try
            {
                Directory.Delete(folder, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }
}
