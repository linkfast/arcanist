using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Arcanist.Core.Application.Libraries;
using Arcanist.Core.Application.WishList;
using Arcanist.Core.Setup;
using Arcanist.Core.Tests.Scanning;

namespace Arcanist.Core.Tests.Api;

/// <summary>Wished songs that arrive by upload or through a drop folder, and where an upload for a wish goes.</summary>
public sealed class WishArrivalTests : IAsyncLifetime
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private readonly string _music = Directory.CreateTempSubdirectory("arcanist-wish-music").FullName;
    private readonly string _wanted = Directory.CreateTempSubdirectory("arcanist-wish-target").FullName;
    private readonly string _drop = Directory.CreateTempSubdirectory("arcanist-wish-drop").FullName;
    private readonly string _staging = Directory.CreateTempSubdirectory("arcanist-wish-staging").FullName;
    private readonly ArcanistFactory _factory = new(new Dictionary<string, string?>
    {
        ["Arcanist:Uploads:ScanDelay"] = "00:00:00.2",
        ["Arcanist:Organization:StableSeconds"] = "0",
    });
    private HttpClient _admin = null!;

    public async ValueTask InitializeAsync()
    {
        await _factory.CompleteSetupAsync(new SetupLibrary("Music", [_music]), new SetupLibrary("Wanted", [_wanted]));
        _admin = await _factory.CreateAuthenticatedClientAsync();
        await _factory.WaitForScansAsync(_admin);
    }

    private async Task<Wish> AddAsync(WishInput input)
    {
        var response = await _admin.PostAsJsonAsync("/rest/v1/me/wishes", input, ArcanistFactory.Json, Cancel);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<Wish>(ArcanistFactory.Json, Cancel))!;
    }

    private async Task<HttpResponseMessage> UploadAsync(string query, string title)
    {
        var file = AudioFixtures.WriteWav(Path.Combine(_staging, Guid.NewGuid().ToString("N") + ".wav"), title.Length, new FixtureTags(title, "Band", "Arrivals"));
        var content = new ByteArrayContent(await File.ReadAllBytesAsync(file, Cancel));
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        return await _admin.PostAsync("/rest/v1/uploads?fileName=song.wav" + query, content, Cancel);
    }

    private async Task<Wish> WaitObtainedAsync(Guid id)
    {
        for (var i = 0; i < 300; i++)
        {
            var list = await _admin.GetFromJsonAsync<WishListResult>("/rest/v1/me/wishes", ArcanistFactory.Json, Cancel);
            if (list!.Items.Single(w => w.Id == id) is { Status: WishStatus.Obtained } done)
                return done;
            await Task.Delay(100, Cancel);
        }

        throw new TimeoutException("The wish was not fulfilled.");
    }

    [Fact]
    public async Task An_upload_for_a_wish_goes_to_its_library_and_fulfils_it()
    {
        var libraries = (await _admin.GetFromJsonAsync<List<Library>>("/rest/v1/libraries", ArcanistFactory.Json, Cancel))!;
        var wanted = libraries.Single(l => l.Name == "Wanted");
        var wish = await AddAsync(new WishInput("Upload Song", "Band", TargetLibraryId: wanted.Id));
        Assert.Equal(wanted.Id, wish.TargetLibraryId);

        // Without a wish nor a default library, the upload has nowhere to go.
        var nowhere = await UploadAsync("", "Nowhere");
        Assert.Equal(HttpStatusCode.BadRequest, nowhere.StatusCode);
        Assert.Contains("upload_library_required", await nowhere.Content.ReadAsStringAsync(Cancel));

        var uploaded = await UploadAsync($"&wishId={wish.Id}", "Upload Song");
        Assert.Equal(HttpStatusCode.Created, uploaded.StatusCode);
        Assert.Single(Directory.EnumerateFiles(_wanted, "*.wav", SearchOption.AllDirectories));

        var done = await WaitObtainedAsync(wish.Id);
        Assert.Equal("Upload Song", done.Track!.Title);
        Assert.Contains(wanted.Id, done.Track.LibraryIds);

        // The server's default takes uploads that name no library.
        var music = libraries.Single(l => l.Name == "Music");
        var set = await _admin.PutAsJsonAsync("/rest/v1/uploads/settings", new SetUploadSettingsInput(music.Id), Cancel);
        set.EnsureSuccessStatusCode();
        Assert.Equal("Music", (await set.Content.ReadFromJsonAsync<UploadSettings>(ArcanistFactory.Json, Cancel))!.DefaultLibraryName);
        Assert.Equal(HttpStatusCode.Created, (await UploadAsync("", "Default Song")).StatusCode);
        Assert.Single(Directory.EnumerateFiles(_music, "*.wav", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task A_song_imported_from_a_drop_folder_fulfils_a_wish()
    {
        var wish = await AddAsync(new WishInput("Dropped Song", "Band"));
        var source = (await _admin.GetFromJsonAsync<List<Library>>("/rest/v1/libraries", ArcanistFactory.Json, Cancel))!
            .Single(l => l.Name == "Music").Sources![0].Id;
        var created = await _admin.GraphQLAsync(
            "mutation($i: DropFolderInput!) { createDropFolder(input: $i) { id } }",
            new
            {
                i = new
                {
                    path = _drop, librarySourceId = source, enabled = true, duplicatePolicy = "SKIP", convertLossy = false, mode = "MOVE",
                    kind = "FOLDER", uploadToCloud = false,
                },
            });
        Assert.Null(created.ErrorCode());

        // Prepared outside, then moved in: the importer must never read a file being written.
        var file = AudioFixtures.WriteWav(Path.Combine(_staging, "album", "01.wav"), 4, new FixtureTags("Dropped Song", "Band", "Arrivals", TrackNumber: 1));
        Directory.Move(Path.GetDirectoryName(file)!, Path.Combine(_drop, "album"));
        await _factory.WaitForImportsAsync();
        await _factory.WaitForScansAsync(_admin);

        Assert.Equal("Dropped Song", (await WaitObtainedAsync(wish.Id)).Track!.Title);
    }

    public async ValueTask DisposeAsync()
    {
        await _factory.DisposeAsync();
        foreach (var dir in new[] { _music, _wanted, _drop, _staging })
        {
            try
            {
                Directory.Delete(dir, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }
}
