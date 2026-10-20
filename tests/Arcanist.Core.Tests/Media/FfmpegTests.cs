using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Arcanist.Core.Analysis;
using Arcanist.Core.Api.Rest;
using Arcanist.Core.Data;
using Arcanist.Core.Data.Entities;
using Arcanist.Core.Media;
using Arcanist.Core.Setup;
using Arcanist.Core.Tests.Scanning;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Arcanist.Core.Tests.Media;

/// <summary>The ffmpeg on this machine's PATH, which the tests that need a real one use. They are skipped without it.</summary>
public static class TestFfmpeg
{
    public static string? Path { get; } = (Environment.GetEnvironmentVariable("PATH") ?? "")
        .Split(System.IO.Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
        .Select(directory => System.IO.Path.Combine(directory, OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg"))
        .FirstOrDefault(File.Exists);

    public static string Require()
    {
        if (Path is null)
            Assert.Skip("ffmpeg is not installed");
        return Path;
    }

    /// <summary>Encodes a WAV with ffmpeg, e.g. to Opus, which the built-in decoder does not read.</summary>
    public static void Encode(string input, string output, params string[] arguments)
    {
        var start = new ProcessStartInfo(Require()) { ArgumentList = { "-hide_banner", "-loglevel", "error", "-y", "-i", input }, UseShellExecute = false };
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);
        start.ArgumentList.Add(output);
        using var process = Process.Start(start)!;
        process.WaitForExit();
        Assert.Equal(0, process.ExitCode);
    }
}

public sealed class FfmpegDownloaderTests
{
    private static FfmpegDownloader.Asset Asset(string name) => new(name, 1, "https://example.invalid/" + name);

    [Fact]
    public void Picks_the_newest_static_gpl_release_build_for_the_platform()
    {
        FfmpegDownloader.Asset[] assets =
        [
            Asset("ffmpeg-master-latest-linux64-gpl.tar.xz"),
            Asset("ffmpeg-n7.1-latest-linux64-gpl-7.1.tar.xz"),
            Asset("ffmpeg-n8.1-latest-linux64-gpl-8.1.tar.xz"),
            Asset("ffmpeg-n8.1-latest-linux64-lgpl-8.1.tar.xz"),
            Asset("ffmpeg-n8.1-latest-linux64-gpl-shared-8.1.tar.xz"),
            Asset("ffmpeg-n8.1-latest-linuxarm64-gpl-8.1.tar.xz"),
            Asset("ffmpeg-n8.1-latest-win64-gpl-8.1.zip"),
        ];

        Assert.Equal("ffmpeg-n8.1-latest-linux64-gpl-8.1.tar.xz", FfmpegDownloader.PickAsset(assets, "linux64")!.Name);
        Assert.Equal("ffmpeg-n8.1-latest-win64-gpl-8.1.zip", FfmpegDownloader.PickAsset(assets, "win64")!.Name);
        Assert.Null(FfmpegDownloader.PickAsset(assets, "winarm64"));
    }

    [Fact]
    public void Reads_the_expected_hash_from_the_checksum_list()
    {
        const string checksums = "aaa111  ffmpeg-n8.1-latest-win64-gpl-8.1.zip\nbbb222 *ffmpeg-n8.1-latest-linux64-gpl-8.1.tar.xz\n";

        Assert.Equal("bbb222", FfmpegDownloader.ExpectedHash(checksums, "ffmpeg-n8.1-latest-linux64-gpl-8.1.tar.xz"));
        Assert.Null(FfmpegDownloader.ExpectedHash(checksums, "other.zip"));
    }
}

public sealed class FfmpegIntegrationTests : IAsyncLifetime
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private readonly string _music = Directory.CreateTempSubdirectory("arcanist-ffmpeg-").FullName;
    private ArcanistFactory _factory = null!;

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    private ArcanistFactory Create(string? ffmpegPath, bool analysis = false, bool searchSystem = true)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Arcanist:Analysis:Enabled"] = analysis ? "true" : "false",
            ["Arcanist:Ffmpeg:SearchSystem"] = searchSystem ? "true" : "false",
        };
        if (ffmpegPath is not null)
            settings["Arcanist:Ffmpeg:Path"] = ffmpegPath;
        return _factory = new ArcanistFactory(settings);
    }

    [Fact]
    public async Task Formats_the_built_in_decoder_cannot_read_are_analyzed_through_ffmpeg()
    {
        var ffmpeg = TestFfmpeg.Require();
        var wav = AudioFixtures.WriteMusic(Path.Combine(_music, "source.wav"), new FixtureTags(Title: "Opus song"), bpm: 128);
        TestFfmpeg.Encode(wav, Path.Combine(_music, "song.opus"), "-c:a", "libopus", "-b:a", "128k");
        File.Delete(wav);

        var factory = Create(ffmpeg, analysis: true);
        await factory.CompleteSetupAsync(new SetupLibrary("Music", [_music]));
        await factory.WaitForScansAsync(await factory.CreateAuthenticatedClientAsync());
        await factory.WaitForAnalysisAsync();

        await using var db = factory.Services.GetRequiredService<IArcanistDbContextFactory>().CreateDbContext();
        var analysis = await db.AudioAnalyses.SingleAsync(Cancel);
        Assert.Equal(AnalysisState.Analyzed, analysis.State);
        Assert.InRange(analysis.Bpm!.Value, 127, 129);
        Assert.NotNull(analysis.AcoustIdFingerprint);
    }

    [Fact]
    public async Task Without_ffmpeg_those_formats_wait_for_it()
    {
        var wav = AudioFixtures.WriteMusic(Path.Combine(_music, "source.wav"), new FixtureTags(Title: "Opus song"));
        TestFfmpeg.Encode(wav, Path.Combine(_music, "song.opus"), "-c:a", "libopus");
        File.Delete(wav);

        // A configured path that does not work, and no looking elsewhere.
        var factory = Create(Path.Combine(_music, "missing-ffmpeg.exe"), analysis: true, searchSystem: false);
        await factory.CompleteSetupAsync(new SetupLibrary("Music", [_music]));
        await factory.WaitForScansAsync(await factory.CreateAuthenticatedClientAsync());

        var located = await factory.Services.GetRequiredService<FfmpegLocator>().ResolveAsync(Cancel);
        Assert.Null(located.Installation);
        Assert.False(located.ConfiguredPathWorks);

        var progress = await factory.WaitForAnalysisAsync();
        Assert.Equal(1, progress.NeedsFfmpeg);
    }

    [Fact]
    public async Task Administrators_can_set_the_path_and_others_cannot_see_it()
    {
        var factory = Create(null);
        await factory.CompleteSetupAsync();
        var admin = await factory.CreateAuthenticatedClientAsync();

        var status = await admin.GetFromJsonAsync<JsonElement>("/rest/v1/ffmpeg", ArcanistFactory.Json, Cancel);
        Assert.Equal(status.GetProperty("path").ValueKind == JsonValueKind.String, status.GetProperty("available").GetBoolean());
        Assert.Equal("IDLE", status.GetProperty("download").GetProperty("state").GetString());

        var wrong = await admin.PutAsJsonAsync("/rest/v1/ffmpeg/path", new SetFfmpegPathInput(Path.Combine(_music, "nope.exe")), Cancel);
        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
        Assert.Contains("not_ffmpeg", await wrong.Content.ReadAsStringAsync(Cancel));

        if (TestFfmpeg.Path is { } ffmpeg)
        {
            var set = await admin.GraphQLAsync("mutation($path: String) { setFfmpegPath(path: $path) { available source configuredPath version } }", new { path = ffmpeg });
            var result = set.GetProperty("data").GetProperty("setFfmpegPath");
            Assert.True(result.GetProperty("available").GetBoolean());
            Assert.Equal("CONFIGURED", result.GetProperty("source").GetString());
        }

        await factory.CreateUserAsync("librarian", permissions: Permissions.ManageLibraries);
        var librarian = await factory.CreateAuthenticatedClientAsync("librarian");
        Assert.Equal(HttpStatusCode.Forbidden, (await librarian.GetAsync("/rest/v1/ffmpeg", Cancel)).StatusCode);

        await factory.CreateUserAsync("settings", permissions: Permissions.ManageSettings);
        var settingsManager = await factory.CreateAuthenticatedClientAsync("settings");
        Assert.Equal(HttpStatusCode.OK, (await settingsManager.GetAsync("/rest/v1/ffmpeg", Cancel)).StatusCode);
        // Running a file is for administrators only.
        Assert.Equal(HttpStatusCode.Forbidden, (await settingsManager.PostAsync("/rest/v1/ffmpeg/install", null, Cancel)).StatusCode);
    }

    [Fact]
    public async Task The_setup_wizard_follows_a_download_until_setup_is_done()
    {
        var factory = Create(null);
        var client = factory.CreateClient();

        // Nothing is downloaded here (tests stay offline): only its state, which needs no setup code.
        var download = await client.GetFromJsonAsync<JsonElement>("/rest/v1/setup/ffmpeg/install", ArcanistFactory.Json, Cancel);
        Assert.Equal(FfmpegDownloader.Platform is not null, download.GetProperty("canDownload").GetBoolean());
        Assert.Equal("IDLE", download.GetProperty("progress").GetProperty("state").GetString());

        // Starting it is for the wizard, with the code from other devices.
        var remote = factory.CreateClient();
        remote.DefaultRequestHeaders.Add("X-Forwarded-For", "203.0.113.9");
        Assert.Equal(HttpStatusCode.Forbidden, (await remote.PostAsync("/rest/v1/setup/ffmpeg/install", null, Cancel)).StatusCode);

        (await client.PostAsJsonAsync("/rest/v1/setup/complete", new SetupRequest(
            new SetupDatabase(Core.Data.DatabaseProvider.Sqlite, null), new SetupAdmin("admin", ArcanistFactory.Password, null), [], false, null),
            ArcanistFactory.Json, Cancel)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict, (await client.GetAsync("/rest/v1/setup/ffmpeg/install", Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsync("/rest/v1/setup/ffmpeg/install", null, Cancel)).StatusCode);
    }

    [Fact]
    public async Task The_setup_wizard_checks_and_stores_the_path()
    {
        var factory = Create(null);
        var client = factory.CreateClient();

        var check = await client.GetFromJsonAsync<JsonElement>($"/rest/v1/setup/ffmpeg?path={Uri.EscapeDataString(Path.Combine(_music, "nope"))}", ArcanistFactory.Json, Cancel);
        Assert.False(check.GetProperty("available").GetBoolean());

        var invalid = await client.PostAsJsonAsync("/rest/v1/setup/complete", new SetupRequest(
            new SetupDatabase(Core.Data.DatabaseProvider.Sqlite, null), new SetupAdmin("admin", ArcanistFactory.Password, null), [], false, null,
            FfmpegPath: Path.Combine(_music, "nope")), ArcanistFactory.Json, Cancel);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Contains("ffmpegPath", await invalid.Content.ReadAsStringAsync(Cancel));

        var ffmpeg = TestFfmpeg.Require();
        var valid = await client.PostAsJsonAsync("/rest/v1/setup/complete", new SetupRequest(
            new SetupDatabase(Core.Data.DatabaseProvider.Sqlite, null), new SetupAdmin("admin", ArcanistFactory.Password, null), [], false, null,
            FfmpegPath: ffmpeg), ArcanistFactory.Json, Cancel);
        valid.EnsureSuccessStatusCode();

        var located = await factory.Services.GetRequiredService<FfmpegLocator>().ResolveAsync(Cancel);
        Assert.Equal(FfmpegSource.Configured, located.Installation!.Source);
        Assert.Equal(Path.GetFullPath(ffmpeg), located.Installation.Path);
    }

    public async ValueTask DisposeAsync()
    {
        if (_factory is not null)
            await _factory.DisposeAsync();
        Directory.Delete(_music, recursive: true);
    }
}
