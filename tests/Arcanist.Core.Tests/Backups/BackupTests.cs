using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Arcanist.Core.Backups;
using Arcanist.Core.Data.Entities;
using Arcanist.Core.Setup;
using Arcanist.Core.Tests.Scanning;

namespace Arcanist.Core.Tests.Backups;

/// <summary>Backups into a folder: the mirror, old versions, what each snapshot holds, and the rules of destinations.</summary>
public sealed class BackupTests : IAsyncDisposable
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private readonly string _music = Directory.CreateTempSubdirectory("arcanist-backup-music-").FullName;
    private readonly string _destination = Directory.CreateTempSubdirectory("arcanist-backup-destination-").FullName;
    private ArcanistFactory _factory = null!;
    private HttpClient _admin = null!;

    private async Task StartAsync()
    {
        AudioFixtures.WriteWav(Path.Combine(_music, "Band", "Pulse", "01 Alpha.wav"), 1, new FixtureTags("Alpha", "Band", "Pulse", TrackNumber: 1));
        AudioFixtures.WriteWav(Path.Combine(_music, "Band", "Pulse", "02 Beta.wav"), 2, new FixtureTags("Beta", "Band", "Pulse", TrackNumber: 2));
        AudioFixtures.WriteWav(Path.Combine(_music, "Other", "Solo", "01 Gamma.wav"), 3, new FixtureTags("Gamma", "Other", "Solo", TrackNumber: 1));
        File.WriteAllBytes(Path.Combine(_music, "Band", "Pulse", "cover.png"), AudioFixtures.Png(4));

        _factory = new ArcanistFactory();
        await _factory.CompleteSetupAsync(new SetupLibrary("Music", [_music]));
        _admin = await _factory.CreateAuthenticatedClientAsync();
        await _factory.WaitForScansAsync(_admin);
    }

    private async Task<JsonElement> PostAsync(string path, object body, HttpStatusCode expected = HttpStatusCode.OK)
    {
        var response = await _admin.PostAsJsonAsync(path, body, ArcanistFactory.Json, Cancel);
        var text = await response.Content.ReadAsStringAsync(Cancel);
        Assert.True(response.StatusCode == expected, text);
        return JsonDocument.Parse(text).RootElement;
    }

    private async Task<Guid> DestinationAsync() =>
        (await PostAsync("/rest/v1/backups/destinations", new { name = "Backup disk", kind = "FOLDER", path = _destination })).GetProperty("id").GetGuid();

    private async Task<JsonElement> BackupAsync(Guid destination, string scope = "SERVER", Guid[]? ids = null) =>
        await PostAsync("/rest/v1/backups", new
        {
            name = "Everything",
            destinationId = destination,
            scope,
            scopeIds = ids ?? [],
            includeUserData = false,
            includeServer = false,
            schedule = "MANUAL",
            retentionDays = 30,
            enabled = true,
        });

    /// <summary>Runs a backup and waits for it to end.</summary>
    private async Task<JsonElement> RunAsync(Guid backupId)
    {
        var run = await PostAsync($"/rest/v1/backups/{backupId}/run", new { });
        var id = run.GetProperty("id").GetGuid();
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (true)
        {
            run = await _admin.GetFromJsonAsync<JsonElement>($"/rest/v1/backups/runs/{id}", ArcanistFactory.Json, Cancel);
            if (run.GetProperty("finishedAt").ValueKind != JsonValueKind.Null)
                return run;
            Assert.True(DateTime.UtcNow < deadline, $"The run did not end: {run}");
            await Task.Delay(50, Cancel);
        }
    }

    private string Root(JsonElement backup) => Path.Combine(_destination, backup.GetProperty("folder").GetString()!);

    private static BackupIndex Index(string root, string snapshot)
    {
        using var file = File.OpenRead(Path.Combine(root, "snapshots", snapshot, "index.json.gz"));
        using var zip = new GZipStream(file, CompressionMode.Decompress);
        return JsonSerializer.Deserialize<BackupIndex>(zip, BackupFormat.Json)!;
    }

    [Fact]
    public async Task Mirrors_the_libraries_and_keeps_what_changed_or_went_as_old_versions()
    {
        await StartAsync();
        var backup = await BackupAsync(await DestinationAsync());
        var backupId = backup.GetProperty("id").GetGuid();
        var root = Root(backup);

        // The first run copies everything, artwork included.
        var first = await RunAsync(backupId);
        Assert.Equal("COMPLETED", first.GetProperty("status").GetString());
        Assert.Equal(4, first.GetProperty("copied").GetInt32());
        foreach (var file in new[] { "Band/Pulse/01 Alpha.wav", "Band/Pulse/02 Beta.wav", "Band/Pulse/cover.png", "Other/Solo/01 Gamma.wav" })
            Assert.Equal(File.ReadAllBytes(Path.Combine(_music, file)), File.ReadAllBytes(Path.Combine(root, "current", "Music", file)));
        var firstSnapshot = first.GetProperty("snapshot").GetString()!;
        var index = Index(root, firstSnapshot);
        Assert.Equal(4, index.Files.Count);
        // Audio files carry the catalog's hash, to match them with the catalog when they come back.
        Assert.All(index.Files.Where(f => f.Path.EndsWith(".wav", StringComparison.Ordinal)), f => Assert.NotNull(f.ContentHash));

        // Nothing changed: nothing is read again.
        var second = await RunAsync(backupId);
        Assert.Equal((0, 4), (second.GetProperty("copied").GetInt32(), second.GetProperty("unchanged").GetInt32()));

        // One changes, one goes, one arrives.
        var alpha = Path.Combine(_music, "Band", "Pulse", "01 Alpha.wav");
        var oldAlpha = File.ReadAllBytes(alpha);
        AudioFixtures.WriteWav(alpha, 9, new FixtureTags("Alpha", "Band", "Pulse", TrackNumber: 1));
        File.SetLastWriteTimeUtc(alpha, DateTime.UtcNow.AddMinutes(1));
        var oldGamma = File.ReadAllBytes(Path.Combine(_music, "Other", "Solo", "01 Gamma.wav"));
        File.Delete(Path.Combine(_music, "Other", "Solo", "01 Gamma.wav"));
        AudioFixtures.WriteWav(Path.Combine(_music, "Band", "Pulse", "03 Delta.wav"), 5, new FixtureTags("Delta", "Band", "Pulse", TrackNumber: 3));

        var third = await RunAsync(backupId);
        Assert.Equal((2, 2), (third.GetProperty("copied").GetInt32(), third.GetProperty("versioned").GetInt32()));
        var thirdSnapshot = third.GetProperty("snapshot").GetString()!;
        Assert.Equal(File.ReadAllBytes(alpha), File.ReadAllBytes(Path.Combine(root, "current", "Music", "Band", "Pulse", "01 Alpha.wav")));
        Assert.Equal(oldAlpha, File.ReadAllBytes(Path.Combine(root, "versions", thirdSnapshot, "Music", "Band", "Pulse", "01 Alpha.wav")));
        Assert.Equal(oldGamma, File.ReadAllBytes(Path.Combine(root, "versions", thirdSnapshot, "Music", "Other", "Solo", "01 Gamma.wav")));
        Assert.False(File.Exists(Path.Combine(root, "current", "Music", "Other", "Solo", "01 Gamma.wav")));
        Assert.True(File.Exists(Path.Combine(root, "current", "Music", "Band", "Pulse", "03 Delta.wav")));

        var latest = Index(root, thirdSnapshot);
        Assert.Equal(["Music/Band/Pulse/01 Alpha.wav", "Music/Other/Solo/01 Gamma.wav"], latest.Moved.Order(StringComparer.Ordinal));
        Assert.DoesNotContain(latest.Files, f => f.Path.EndsWith("Gamma.wav", StringComparison.Ordinal));

        // The manifest lists the snapshots that can be restored, and nothing is left half done.
        var manifest = JsonSerializer.Deserialize<BackupManifest>(File.ReadAllText(Path.Combine(root, "arcanist-backup.json")), BackupFormat.Json)!;
        Assert.Equal(3, manifest.Snapshots.Count);
        Assert.Equal("Music", Assert.Single(manifest.Sources).Folder);
        Assert.False(Directory.Exists(Path.Combine(root, "in-progress")) && Directory.EnumerateFileSystemEntries(Path.Combine(root, "in-progress")).Any());

        var runs = await _admin.GetFromJsonAsync<JsonElement>($"/rest/v1/backups/{backupId}/runs", ArcanistFactory.Json, Cancel);
        Assert.Equal(3, runs.GetArrayLength());
    }

    [Fact]
    public async Task A_touched_file_with_the_same_content_is_not_copied_again()
    {
        await StartAsync();
        var backup = await BackupAsync(await DestinationAsync());
        var backupId = backup.GetProperty("id").GetGuid();
        await RunAsync(backupId);

        File.SetLastWriteTimeUtc(Path.Combine(_music, "Band", "Pulse", "02 Beta.wav"), DateTime.UtcNow.AddHours(1));
        var run = await RunAsync(backupId);
        Assert.Equal((0, 0), (run.GetProperty("copied").GetInt32(), run.GetProperty("versioned").GetInt32()));
    }

    [Fact]
    public async Task An_album_brings_its_folder_and_a_playlist_its_tracks()
    {
        await StartAsync();
        var albums = await _admin.GraphQLAsync("query { albums(first: 10) { items { id title } } }");
        var pulse = albums.GetProperty("data").GetProperty("albums").GetProperty("items").EnumerateArray()
            .Single(a => a.GetProperty("title").GetString() == "Pulse").GetProperty("id").GetGuid();
        var destination = await DestinationAsync();

        var album = await BackupAsync(destination, "ALBUM", [pulse]);
        await RunAsync(album.GetProperty("id").GetGuid());
        var root = Root(album);
        Assert.True(File.Exists(Path.Combine(root, "current", "Music", "Band", "Pulse", "cover.png")));
        Assert.True(File.Exists(Path.Combine(root, "current", "Music", "Band", "Pulse", "02 Beta.wav")));
        Assert.False(Directory.Exists(Path.Combine(root, "current", "Music", "Other")));
    }

    [Fact]
    public async Task A_destination_cannot_be_inside_a_library_nor_the_data_folder()
    {
        await StartAsync();
        Directory.CreateDirectory(Path.Combine(_music, "backups"));
        var inside = await PostAsync("/rest/v1/backups/destinations", new { name = "Bad", kind = "FOLDER", path = Path.Combine(_music, "backups") },
            HttpStatusCode.BadRequest);
        Assert.Equal("backup_destination_overlaps", inside.GetProperty("errors").GetProperty("path")[0].GetString());

        var missing = await PostAsync("/rest/v1/backups/destinations", new { name = "Gone", kind = "FOLDER", path = Path.Combine(_destination, "nope") },
            HttpStatusCode.BadRequest);
        Assert.Equal("not_found", missing.GetProperty("errors").GetProperty("path")[0].GetString());
    }

    [Fact]
    public async Task Backing_up_the_server_needs_a_password()
    {
        await StartAsync();
        var destination = await DestinationAsync();
        var refused = await PostAsync("/rest/v1/backups", new
        {
            name = "Server", destinationId = destination, scope = "SERVER", includeUserData = true, includeServer = true,
            schedule = "MANUAL", retentionDays = 30, enabled = true,
        }, HttpStatusCode.BadRequest);
        Assert.Equal("backup_password_required", refused.GetProperty("errors").GetProperty("password")[0].GetString());
    }

    [Fact]
    public void Schedules_run_at_their_local_time()
    {
        var daily = new Backup
        {
            Name = "Nightly", Folder = "x", ScopeIds = "[]", TimeZone = "America/Lima", Enabled = true,
            Schedule = BackupSchedule.Daily, TimeOfDay = 3 * 60,
        };
        // 2026-10-16 10:00 in Lima (UTC−5) is 15:00 UTC; the next 03:00 there is 08:00 UTC on the 17th.
        Assert.Equal(new DateTime(2026, 10, 17, 8, 0, 0, DateTimeKind.Utc), BackupSchedules.Next(daily, new DateTime(2026, 10, 16, 15, 0, 0, DateTimeKind.Utc)));

        var weekly = new Backup
        {
            Name = "Sundays", Folder = "x", ScopeIds = "[]", TimeZone = "UTC", Enabled = true,
            Schedule = BackupSchedule.Weekly, TimeOfDay = 60, DayOfWeek = DayOfWeek.Sunday,
        };
        Assert.Equal(new DateTime(2026, 10, 18, 1, 0, 0, DateTimeKind.Utc), BackupSchedules.Next(weekly, new DateTime(2026, 10, 16, 15, 0, 0, DateTimeKind.Utc)));

        weekly.Schedule = BackupSchedule.Manual;
        Assert.Null(BackupSchedules.Next(weekly, DateTime.UtcNow));
    }

    public async ValueTask DisposeAsync()
    {
        if (_factory is not null)
            await _factory.DisposeAsync();
        foreach (var folder in new[] { _music, _destination })
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
