using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Arcanist.Core.Api.Rest;
using Arcanist.Core.Application.Catalog;
using Arcanist.Core.Data.Entities;
using Arcanist.Core.Setup;
using Arcanist.Core.Tests.Scanning;

namespace Arcanist.Core.Tests.Analysis;

/// <summary>Analysis results as clients see them, on both APIs.</summary>
public sealed class AnalysisApiTests : IAsyncLifetime
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private readonly ArcanistFactory _factory = new(new Dictionary<string, string?> { ["Arcanist:Analysis:Enabled"] = "true" });
    private readonly string _music = Directory.CreateTempSubdirectory("arcanist-analysis-api-").FullName;
    private HttpClient _admin = null!;

    public async ValueTask InitializeAsync()
    {
        var album = new FixtureTags(Album: "Tempo", AlbumArtist: "Click", Artist: "Click");
        AudioFixtures.WriteMusic(Path.Combine(_music, "01.wav"), album with { Title = "One", TrackNumber = 1 }, bpm: 120, chordSeed: 9);
        AudioFixtures.WriteMusic(Path.Combine(_music, "02.wav"), album with { Title = "Two", TrackNumber = 2 }, bpm: 100, chordSeed: 4);

        await _factory.CompleteSetupAsync(new SetupLibrary("Music", [_music]));
        _admin = await _factory.CreateAuthenticatedClientAsync();
        await _factory.WaitForScansAsync(_admin);
        await _factory.WaitForAnalysisAsync();
    }

    [Fact]
    public async Task Tracks_carry_tempo_key_and_loudness_on_both_apis()
    {
        var page = await _admin.GetFromJsonAsync<JsonElement>("/rest/v1/tracks?search=one", ArcanistFactory.Json, Cancel);
        var track = page.GetProperty("items")[0];

        Assert.InRange(track.GetProperty("bpm").GetDouble(), 119, 121);
        var key = track.GetProperty("key");
        Assert.InRange(key.GetProperty("tonic").GetInt32(), 0, 11);
        Assert.Matches("^(1[0-2]|[1-9])[AB]$", key.GetProperty("camelot").GetString());

        var loudness = track.GetProperty("files")[0].GetProperty("loudness");
        var integrated = loudness.GetProperty("integrated").GetDouble();
        Assert.Equal(-18 - integrated, loudness.GetProperty("trackGain").GetDouble(), 1);
        // Both tracks are analyzed, so the album has a gain too.
        Assert.Equal(JsonValueKind.Number, loudness.GetProperty("albumGain").ValueKind);

        var graphql = await _admin.GraphQLAsync(
            "query($id: ID!) { track(id: $id) { bpm key { notation camelot mode } files { loudness { trackGain albumGain } } } }",
            new { id = track.GetProperty("id").GetString() });
        var viaGraphQL = graphql.GetProperty("data").GetProperty("track");
        Assert.Equal(track.GetProperty("bpm").GetDouble(), viaGraphQL.GetProperty("bpm").GetDouble());
        Assert.Equal(key.GetProperty("camelot").GetString(), viaGraphQL.GetProperty("key").GetProperty("camelot").GetString());
        Assert.Equal(loudness.GetProperty("trackGain").GetDouble(),
            viaGraphQL.GetProperty("files")[0].GetProperty("loudness").GetProperty("trackGain").GetDouble());
    }

    [Fact]
    public async Task Waveforms_are_served_separately()
    {
        var page = await _admin.GetFromJsonAsync<JsonElement>("/rest/v1/tracks?search=two", ArcanistFactory.Json, Cancel);
        var trackId = page.GetProperty("items")[0].GetProperty("id").GetString();

        var waveform = await _admin.GetFromJsonAsync<JsonElement>($"/rest/v1/tracks/{trackId}/waveform", ArcanistFactory.Json, Cancel);
        Assert.Equal(1000, waveform.GetProperty("peaks").GetArrayLength());
        Assert.Equal(15_000, waveform.GetProperty("durationMs").GetInt32());
        Assert.Contains(waveform.GetProperty("peaks").EnumerateArray(), p => p.GetInt32() > 0);

        var graphql = await _admin.GraphQLAsync("query($id: ID!) { trackWaveform(trackId: $id) { peaks } }", new { id = trackId });
        Assert.Equal(1000, graphql.GetProperty("data").GetProperty("trackWaveform").GetProperty("peaks").GetArrayLength());

        var missing = await _admin.GetAsync($"/rest/v1/tracks/{Guid.NewGuid()}/waveform", Cancel);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task Progress_is_for_library_managers_and_switching_is_for_settings_managers()
    {
        var progress = await _admin.GetFromJsonAsync<JsonElement>("/rest/v1/analysis", ArcanistFactory.Json, Cancel);
        Assert.Equal("IDLE", progress.GetProperty("activity").GetString());
        Assert.Equal(2, progress.GetProperty("analyzed").GetInt32());

        await _factory.CreateUserAsync("listener");
        var listener = await _factory.CreateAuthenticatedClientAsync("listener");
        Assert.Equal(HttpStatusCode.Forbidden, (await listener.GetAsync("/rest/v1/analysis", Cancel)).StatusCode);

        await _factory.CreateUserAsync("librarian", permissions: Permissions.ManageLibraries);
        var librarian = await _factory.CreateAuthenticatedClientAsync("librarian");
        Assert.Equal(HttpStatusCode.OK, (await librarian.GetAsync("/rest/v1/analysis", Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await librarian.PutAsJsonAsync("/rest/v1/analysis/enabled", new SetAnalysisEnabledInput(false), Cancel)).StatusCode);

        var off = await _admin.PutAsJsonAsync("/rest/v1/analysis/enabled", new SetAnalysisEnabledInput(false), Cancel);
        var state = await off.Content.ReadFromJsonAsync<JsonElement>(Cancel);
        Assert.False(state.GetProperty("enabled").GetBoolean());
        Assert.Equal("DISABLED", state.GetProperty("activity").GetString());

        var on = await _admin.GraphQLAsync("mutation { setAnalysisEnabled(enabled: true) { enabled } }");
        Assert.True(on.GetProperty("data").GetProperty("setAnalysisEnabled").GetProperty("enabled").GetBoolean());
    }

    [Theory]
    [InlineData(0, false, "C", "8B")]
    [InlineData(9, true, "Am", "8A")]
    [InlineData(7, false, "G", "9B")]
    [InlineData(4, true, "Em", "9A")]
    [InlineData(5, false, "F", "7B")]
    [InlineData(6, false, "F#", "2B")]
    [InlineData(11, true, "Bm", "10A")]
    [InlineData(1, true, "C#m", "12A")]
    public void Keys_are_named_and_placed_on_the_camelot_wheel(int tonic, bool minor, string notation, string camelot)
    {
        var key = MusicalKeys.Create(tonic, minor, 0.5);

        Assert.Equal((notation, camelot, minor ? KeyMode.Minor : KeyMode.Major), (key.Notation, key.Camelot, key.Mode));
    }

    public async ValueTask DisposeAsync()
    {
        await _factory.DisposeAsync();
        Directory.Delete(_music, recursive: true);
    }
}
