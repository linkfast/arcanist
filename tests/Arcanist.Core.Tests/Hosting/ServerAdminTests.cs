using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Arcanist.Core.Hosting;
using Arcanist.Core.Setup;
using Arcanist.Core.Tests.Scanning;
using Microsoft.Extensions.DependencyInjection;

namespace Arcanist.Core.Tests.Hosting;

/// <summary>Running the setup wizard again, and resetting settings, users' data or everything.</summary>
public sealed class ServerAdminTests : IAsyncLifetime
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private readonly string _music = Directory.CreateTempSubdirectory("arcanist-admin-").FullName;
    private readonly ArcanistFactory _factory = new(new Dictionary<string, string?>
    {
        ["Arcanist:Hosting:StopAfterFactoryReset"] = "false",
    });
    private HttpClient _admin = null!;

    public async ValueTask InitializeAsync()
    {
        AudioFixtures.WriteWav(Path.Combine(_music, "song.wav"), 1, new FixtureTags("Song", "Band", "Album", "Band", TrackNumber: 1));
        await _factory.CompleteSetupAsync(new SetupLibrary("Music", [_music]));
        _admin = await _factory.CreateAuthenticatedClientAsync();
        await _factory.WaitForScansAsync(_admin);
    }

    private async Task<JsonElement> DataAsync(string query, object? variables = null)
    {
        var response = await _admin.GraphQLAsync(query, variables);
        Assert.False(response.TryGetProperty("errors", out var errors), errors.ToString());
        return response.GetProperty("data");
    }

    [Fact]
    public async Task The_setup_wizard_s_answers_can_be_changed_again()
    {
        var before = (await DataAsync("query { serverConfiguration { ratingMode shareAnonymousStatistics defaultLanguage onlineServices { lyrics } } }"))
            .GetProperty("serverConfiguration");
        Assert.Equal(("STARS", false), (before.GetProperty("ratingMode").GetString(), before.GetProperty("shareAnonymousStatistics").GetBoolean()));

        var after = (await DataAsync("""
            mutation {
              reconfigureServer(input: {
                defaultLanguage: "es", ratingMode: STAR, shareAnonymousStatistics: true, ffmpegPath: null,
                onlineServices: { musicBrainz: false, acoustId: false, coverArtArchive: false, lyrics: true }
              }) { ratingMode shareAnonymousStatistics defaultLanguage onlineServices { lyrics } }
            }
            """)).GetProperty("reconfigureServer");
        Assert.Equal("STAR", after.GetProperty("ratingMode").GetString());
        Assert.True(after.GetProperty("shareAnonymousStatistics").GetBoolean());
        Assert.Equal("es", after.GetProperty("defaultLanguage").GetString());
        Assert.True(after.GetProperty("onlineServices").GetProperty("lyrics").GetBoolean());
        Assert.Equal("STAR", (await _admin.GetFromJsonAsync<JsonElement>("/rest/v1/server-info", ArcanistFactory.Json, Cancel)).GetProperty("ratingMode").GetString());
    }

    [Fact]
    public async Task Privacy_turns_off_what_it_leaves_out_and_keeps_it_off()
    {
        await DataAsync("mutation { setOnlineServices(input: { musicBrainz: true, acoustId: true, coverArtArchive: true, lyrics: true }) { hasAcoustIdKey } }");
        await DataAsync("mutation { setAnonymousStatistics(enabled: true) }");

        // No online services: they go off and cannot be turned on; Ultimate Cloud stays.
        var privacy = (await DataAsync("mutation { setPrivacy(input: { noTelemetry: false, externalServices: ULTIMATE_CLOUD_ONLY }) { noTelemetry externalServices } }"))
            .GetProperty("setPrivacy");
        Assert.Equal("ULTIMATE_CLOUD_ONLY", privacy.GetProperty("externalServices").GetString());
        var online = (await DataAsync("query { serverConfiguration { onlineServices { musicBrainz lyrics } } }")).GetProperty("serverConfiguration").GetProperty("onlineServices");
        Assert.False(online.GetProperty("musicBrainz").GetBoolean());
        Assert.Equal("external_services_disabled",
            (await _admin.GraphQLAsync("mutation { setOnlineServices(input: { musicBrainz: true, acoustId: false, coverArtArchive: false, lyrics: false }) { hasAcoustIdKey } }")).ErrorCode());
        Assert.True(await AnonymousAsync());

        // No telemetry: anonymous statistics go off and stay off; accounts start without sending plays.
        await DataAsync("mutation { setPrivacy(input: { noTelemetry: true, externalServices: ULTIMATE_CLOUD_ONLY }) { noTelemetry } }");
        Assert.False(await AnonymousAsync());
        Assert.Equal("telemetry_disabled", (await _admin.GraphQLAsync("mutation { setAnonymousStatistics(enabled: true) }")).ErrorCode());
        var services = await _factory.Services.GetRequiredService<Arcanist.Core.Cloud.UltimateCloudServicesSettings>().GetAsync(Cancel);
        Assert.Equal((false, false, true), (services.PersonalPlays, services.AlbumPlaythroughs, services.StarTracks));

        // No outside services at all: the Ultimate Cloud extension goes off and stays off.
        await DataAsync("mutation { setPrivacy(input: { noTelemetry: false, externalServices: NONE }) { noTelemetry } }");
        var cloud = (await DataAsync("query { extensions { id enabled } }")).GetProperty("extensions").EnumerateArray()
            .Single(e => e.GetProperty("id").GetString() == "ultimate-cloud");
        Assert.False(cloud.GetProperty("enabled").GetBoolean());
        Assert.Equal("external_services_disabled",
            (await _admin.GraphQLAsync("mutation { setExtensionEnabled(id: \"ultimate-cloud\", enabled: true) { enabled } }")).ErrorCode());

        // Relaxed again: nothing turns back on by itself, but it can be.
        await DataAsync("mutation { setPrivacy(input: { noTelemetry: false, externalServices: ALL }) { noTelemetry } }");
        Assert.False(await AnonymousAsync());
        await DataAsync("mutation { setExtensionEnabled(id: \"ultimate-cloud\", enabled: true) { enabled } }");
        await DataAsync("mutation { setAnonymousStatistics(enabled: true) }");
    }

    private async Task<bool> AnonymousAsync() => (await DataAsync("query { anonymousStatistics }")).GetProperty("anonymousStatistics").GetBoolean();

    [Fact]
    public async Task Resetting_settings_keeps_libraries_users_and_their_data()
    {
        var track = (await _admin.GetFromJsonAsync<JsonElement>("/rest/v1/tracks", ArcanistFactory.Json, Cancel)).GetProperty("items")[0].GetProperty("id").GetString();
        await _admin.PutAsJsonAsync($"/rest/v1/tracks/{track}/rating", new { rating = 3 }, ArcanistFactory.Json, Cancel);
        await DataAsync("mutation { setAnonymousStatistics(enabled: true) }");
        await _admin.PutAsJsonAsync("/rest/v1/extensions/subsonic/enabled", new { enabled = true }, ArcanistFactory.Json, Cancel);

        Assert.False((await DataAsync("mutation { resetSettings { restartRequired } }")).GetProperty("resetSettings").GetProperty("restartRequired").GetBoolean());

        var info = await _admin.GetFromJsonAsync<JsonElement>("/rest/v1/server-info", ArcanistFactory.Json, Cancel);
        Assert.Equal("STAR", info.GetProperty("ratingMode").GetString());
        Assert.False((await DataAsync("query { anonymousStatistics }")).GetProperty("anonymousStatistics").GetBoolean());
        var extensions = await _admin.GetFromJsonAsync<JsonElement>("/rest/v1/extensions", ArcanistFactory.Json, Cancel);
        Assert.False(extensions.EnumerateArray().Single(e => e.GetProperty("id").GetString() == "subsonic").GetProperty("enabled").GetBoolean());
        Assert.True(extensions.EnumerateArray().Single(e => e.GetProperty("id").GetString() == "ultimate-cloud").GetProperty("enabled").GetBoolean());

        // The rating is still there (hidden while one star is the mode, as always).
        await _admin.PutAsJsonAsync("/rest/v1/settings/rating-mode", new { mode = "STARS" }, ArcanistFactory.Json, Cancel);
        Assert.Equal(3, (await _admin.GetFromJsonAsync<JsonElement>($"/rest/v1/tracks/{track}", ArcanistFactory.Json, Cancel)).GetProperty("rating").GetInt32());
    }

    [Fact]
    public async Task Users_data_can_be_deleted()
    {
        var track = (await _admin.GetFromJsonAsync<JsonElement>("/rest/v1/tracks", ArcanistFactory.Json, Cancel)).GetProperty("items")[0].GetProperty("id").GetString();
        await _admin.PutAsJsonAsync($"/rest/v1/tracks/{track}/rating", new { rating = 4 }, ArcanistFactory.Json, Cancel);
        await _admin.PostAsJsonAsync("/rest/v1/plays", new { trackId = track, playedMs = 60_000, playedAt = DateTime.UtcNow, client = "tests" }, ArcanistFactory.Json, Cancel);
        await _admin.PostAsJsonAsync("/rest/v1/playlists", new { name = "Mine", description = (string?)null, isPublic = false, trackIds = new[] { track } }, ArcanistFactory.Json, Cancel);

        Assert.Equal(HttpStatusCode.OK, (await _admin.PostAsync("/rest/v1/server/reset/user-data", null, Cancel)).StatusCode);

        var after = await _admin.GetFromJsonAsync<JsonElement>($"/rest/v1/tracks/{track}", ArcanistFactory.Json, Cancel);
        Assert.Equal((JsonValueKind.Null, 0), (after.GetProperty("rating").ValueKind, after.GetProperty("playCount").GetInt32()));
        Assert.Empty((await _admin.GetFromJsonAsync<JsonElement>("/rest/v1/playlists", ArcanistFactory.Json, Cancel)).EnumerateArray());
        Assert.Single((await _admin.GetFromJsonAsync<JsonElement>("/rest/v1/libraries", ArcanistFactory.Json, Cancel)).EnumerateArray());
    }

    [Fact]
    public async Task A_factory_reset_is_finished_at_the_next_start()
    {
        var paths = _factory.Services.GetRequiredService<ArcanistPaths>();
        var result = await _admin.PostAsJsonAsync("/rest/v1/server/reset/factory", new { removeAddons = true }, ArcanistFactory.Json, Cancel);
        Assert.True((await result.Content.ReadFromJsonAsync<JsonElement>(ArcanistFactory.Json, Cancel)).GetProperty("restartRequired").GetBoolean());

        // The server stops taking work; what it asked for is left for the next start.
        var file = Path.Combine(paths.Root, FactoryReset.MarkerFile);
        for (var i = 0; i < 150 && !File.Exists(file); i++)
            await Task.Delay(200, Cancel);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await _admin.GetAsync("/rest/v1/libraries", Cancel)).StatusCode);
        var marker = JsonSerializer.Deserialize<FactoryResetRequest>(await File.ReadAllTextAsync(Path.Combine(paths.Root, FactoryReset.MarkerFile), Cancel))!;
        Assert.True(marker.RemoveAddons);
        Assert.EndsWith("arcanist.db", marker.SqliteFile);
        Assert.True(File.Exists(Path.Combine(_music, "song.wav")));
    }

    [Fact]
    public void At_the_next_start_the_server_is_as_freshly_installed_and_the_music_stays()
    {
        var root = Directory.CreateTempSubdirectory("arcanist-reset-").FullName;
        var paths = new ArcanistPaths(root);
        paths.EnsureCreated();
        var database = Path.Combine(root, "arcanist.db");
        foreach (var file in new[] { database, database + "-wal", paths.SettingsFile, Path.Combine(paths.Keys, "jwt.key"), Path.Combine(paths.Cache, "a"),
                     Path.Combine(paths.Frontends, "p", "index.html"), Path.Combine(paths.Logs, "arcanist.log"), Path.Combine(_music, "kept.txt") })
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, "x");
        }

        // Without asking for the add-ons, players stay.
        File.WriteAllText(Path.Combine(root, FactoryReset.MarkerFile), JsonSerializer.Serialize(new FactoryResetRequest(database, false, DateTime.UtcNow)));
        FactoryReset.ApplyIfRequested(paths);

        Assert.False(File.Exists(database) || File.Exists(database + "-wal") || File.Exists(paths.SettingsFile));
        Assert.False(Directory.Exists(paths.Keys) || Directory.Exists(paths.Cache));
        Assert.True(File.Exists(Path.Combine(paths.Frontends, "p", "index.html")));
        Assert.True(File.Exists(Path.Combine(paths.Logs, "arcanist.log")));
        Assert.True(File.Exists(Path.Combine(_music, "kept.txt")));
        Assert.False(File.Exists(Path.Combine(root, FactoryReset.MarkerFile)));
        Directory.Delete(root, recursive: true);
    }

    public async ValueTask DisposeAsync()
    {
        await _factory.DisposeAsync();
        try { Directory.Delete(_music, recursive: true); }
        catch (IOException) { }
    }
}
