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

namespace Arcanist.Core.Tests.Organization;

/// <summary>Moving a library's files to the paths its template gives them, and back.</summary>
public sealed class OrganizeTests : IAsyncLifetime
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private readonly string _music = Directory.CreateTempSubdirectory("arcanist-organize-").FullName;
    private ArcanistFactory _factory = null!;
    private HttpClient _admin = null!;
    private Guid _libraryId;

    public async ValueTask InitializeAsync()
    {
        AudioFixtures.WriteWav(At("Messy/track one.wav"), 1, new FixtureTags("One", "The Band", "First", "The Band", TrackNumber: 1, Year: 2001));
        AudioFixtures.WriteWav(At("Messy/track two.wav"), 2, new FixtureTags("Two", "The Band", "First", "The Band", TrackNumber: 2, Year: 2001));
        File.WriteAllText(At("Messy/track one.lrc"), "[00:01.00]Hello");
        File.WriteAllBytes(At("Messy/cover.jpg"), AudioFixtures.Png(3));
        File.WriteAllBytes(At("Messy/scans/back.jpg"), AudioFixtures.Png(4));
        AudioFixtures.WriteWav(At("loose.wav"), 3, new FixtureTags("Loose: Live", "Solo", "Single", TrackNumber: 1));

        _factory = new ArcanistFactory(new Dictionary<string, string?> { ["Arcanist:Organization:StableSeconds"] = "0" });
        await _factory.CompleteSetupAsync(new SetupLibrary("Music", [_music]));
        _admin = await _factory.CreateAuthenticatedClientAsync();
        await _factory.WaitForScansAsync(_admin);
        var libraries = await _admin.GetFromJsonAsync<List<JsonElement>>("/rest/v1/libraries", ArcanistFactory.Json, Cancel);
        _libraryId = libraries![0].GetProperty("id").GetGuid();
    }

    private string At(string relativePath)
    {
        var path = Path.Combine(_music, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        return path;
    }

    private List<string> Files() =>
        Directory.EnumerateFiles(_music, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(_music, f).Replace(Path.DirectorySeparatorChar, '/'))
            .Where(f => !f.StartsWith('.'))
            .Order(StringComparer.Ordinal)
            .ToList();

    private ArcanistDbContext Db() => _factory.Services.GetRequiredService<IArcanistDbContextFactory>().CreateDbContext();

    [Fact]
    public async Task Preview_lists_the_moves_without_touching_files()
    {
        var before = Files();

        var preview = await _admin.GetFromJsonAsync<JsonElement>($"/rest/v1/libraries/{_libraryId}/organize/preview", ArcanistFactory.Json, Cancel);

        Assert.Equal(3, preview.GetProperty("filesToMove").GetInt32());
        Assert.Equal(0, preview.GetProperty("filesInPlace").GetInt32());
        // The lyrics, the cover and the scans subfolder follow the album.
        Assert.Equal(3, preview.GetProperty("otherFilesToMove").GetInt32());
        var moves = preview.GetProperty("moves").EnumerateArray().ToDictionary(m => m.GetProperty("from").GetString()!, m => m.GetProperty("to").GetString());
        Assert.Equal("The Band/2001 - First/01 One.wav", moves["Messy/track one.wav"]);
        Assert.Equal("Solo/Single/01 Loose - Live.wav", moves["loose.wav"]);
        Assert.Equal(before, Files());

        // Another template can be tried before saving it.
        preview = await _admin.GetFromJsonAsync<JsonElement>(
            $"/rest/v1/libraries/{_libraryId}/organize/preview?template={Uri.EscapeDataString("{artist}/{title}")}", ArcanistFactory.Json, Cancel);
        Assert.Contains(preview.GetProperty("moves").EnumerateArray(), m => m.GetProperty("to").GetString() == "The Band/One.wav");
    }

    [Fact]
    public async Task Organizing_moves_files_with_what_goes_with_them_and_undoing_puts_them_back()
    {
        var before = Files();

        var response = await _admin.PostAsync($"/rest/v1/libraries/{_libraryId}/organize", null, Cancel);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var run = await ArcanistFactory.WaitForOrganizeAsync(_admin, _libraryId);

        Assert.Equal(("COMPLETED", 6, 0), (run.GetProperty("status").GetString(), run.GetProperty("moved").GetInt32(), run.GetProperty("failed").GetInt32()));
        Assert.Equal(
        [
            "Solo/Single/01 Loose - Live.wav",
            "The Band/2001 - First/01 One.lrc",
            "The Band/2001 - First/01 One.wav",
            "The Band/2001 - First/02 Two.wav",
            "The Band/2001 - First/cover.jpg",
            "The Band/2001 - First/scans/back.jpg",
        ], Files());
        Assert.False(Directory.Exists(Path.Combine(_music, "Messy")), "the emptied folder is removed");

        // The catalog follows the files: nothing goes missing and the tracks still play.
        await using (var db = Db())
        {
            Assert.Contains(await db.MediaFiles.Select(f => f.RelativePath).ToListAsync(Cancel), p => p == "The Band/2001 - First/01 One.wav");
            var trackId = await db.Tracks.Where(t => t.Title == "One").Select(t => t.Id).SingleAsync(Cancel);
            Assert.Equal(HttpStatusCode.OK, (await _admin.GetAsync($"/rest/v1/stream/{trackId}", Cancel)).StatusCode);
        }

        await _admin.PostAsJsonAsync($"/rest/v1/libraries/{_libraryId}/scans", new StartScanInput(Full: false), Cancel);
        await _factory.WaitForScansAsync(_admin);
        await using (var db = Db())
            Assert.Equal(0, await db.MediaFiles.CountAsync(f => f.MissingSince != null, Cancel));

        // Organizing again moves nothing.
        var preview = await _admin.GetFromJsonAsync<JsonElement>($"/rest/v1/libraries/{_libraryId}/organize/preview", ArcanistFactory.Json, Cancel);
        Assert.Equal((0, 3), (preview.GetProperty("filesToMove").GetInt32(), preview.GetProperty("filesInPlace").GetInt32()));

        response = await _admin.PostAsync($"/rest/v1/organize-runs/{run.GetProperty("id").GetString()}/undo", null, Cancel);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        run = await ArcanistFactory.WaitForOrganizeAsync(_admin, _libraryId);

        Assert.Equal("UNDONE", run.GetProperty("status").GetString());
        Assert.Equal(before, Files());
        await using (var db = Db())
            Assert.Contains(await db.MediaFiles.Select(f => f.RelativePath).ToListAsync(Cancel), p => p == "Messy/track one.wav");

        // A run is undone only once.
        response = await _admin.PostAsync($"/rest/v1/organize-runs/{run.GetProperty("id").GetString()}/undo", null, Cancel);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Files_added_after_turning_on_automatic_organizing_are_moved_after_the_scan()
    {
        var response = await _admin.PutAsJsonAsync($"/rest/v1/libraries/{_libraryId}/organization",
            new { template = "{artist}/{title}", autoOrganize = true }, ArcanistFactory.Json, Cancel);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var organization = (await response.Content.ReadFromJsonAsync<JsonElement>(ArcanistFactory.Json, Cancel)).GetProperty("organization");
        Assert.Equal(("{artist}/{title}", false, true),
            (organization.GetProperty("template").GetString(), organization.GetProperty("isDefaultTemplate").GetBoolean(), organization.GetProperty("autoOrganize").GetBoolean()));

        AudioFixtures.WriteWav(At("incoming/new file.wav"), 9, new FixtureTags("New", "Fresh", "Album"));
        await _admin.PostAsJsonAsync($"/rest/v1/libraries/{_libraryId}/scans", new StartScanInput(Full: false), Cancel);
        await _factory.WaitForScansAsync(_admin);
        var run = await ArcanistFactory.WaitForOrganizeAsync(_admin, _libraryId);

        Assert.Equal(("AUTOMATIC", 1), (run.GetProperty("kind").GetString(), run.GetProperty("moved").GetInt32()));
        Assert.True(File.Exists(At("Fresh/New.wav")));
        // Files that were there before stay until the library is organized on request.
        Assert.True(File.Exists(At("Messy/track one.wav")));
    }

    [Fact]
    public async Task Templates_are_validated()
    {
        var response = await _admin.PutAsJsonAsync($"/rest/v1/libraries/{_libraryId}/organization",
            new { template = "{albumartist}/{track}", autoOrganize = false }, ArcanistFactory.Json, Cancel);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(ArcanistFactory.Json, Cancel);
        Assert.Equal("template_title_required", problem.GetProperty("errors").GetProperty("template")[0].GetString());

        var graphQL = await _admin.GraphQLAsync("query($id: ID!) { organizePreview(libraryId: $id, template: \"{nope}/{title}\") { filesToMove } }", new { id = _libraryId });
        Assert.Equal("validation_failed", graphQL.ErrorCode());
    }

    [Fact]
    public async Task Only_library_managers_organize()
    {
        await _factory.CreateUserAsync("listener");
        var listener = await _factory.CreateAuthenticatedClientAsync("listener");

        Assert.Equal(HttpStatusCode.Forbidden, (await listener.GetAsync($"/rest/v1/libraries/{_libraryId}/organize/preview", Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await listener.PostAsync($"/rest/v1/libraries/{_libraryId}/organize", null, Cancel)).StatusCode);

        // Managers see how libraries are organized; others do not.
        var library = await listener.GetFromJsonAsync<JsonElement>($"/rest/v1/libraries/{_libraryId}", ArcanistFactory.Json, Cancel);
        Assert.Equal(JsonValueKind.Null, library.GetProperty("organization").ValueKind);
        var own = await _admin.GetFromJsonAsync<JsonElement>($"/rest/v1/libraries/{_libraryId}", ArcanistFactory.Json, Cancel);
        Assert.True(own.GetProperty("organization").GetProperty("isDefaultTemplate").GetBoolean());
    }

    public async ValueTask DisposeAsync()
    {
        if (_factory is not null)
            await _factory.DisposeAsync();
        Directory.Delete(_music, recursive: true);
    }
}
