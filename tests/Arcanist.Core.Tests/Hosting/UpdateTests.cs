using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Arcanist.Core.Application.Server;
using Microsoft.Extensions.DependencyInjection;

namespace Arcanist.Core.Tests.Hosting;

/// <summary>GitHub's list of releases of Arcanist, in memory.</summary>
public sealed class FakeGitHubReleases : HttpMessageHandler
{
    public const string Url = "https://github.test/repos/linkfast/arcanist/releases";

    public bool Failing { get; set; }

    public object[] Releases { get; set; } = [];

    /// <summary>GitHub's comparison of two tags, by the path after <c>/compare/</c>.</summary>
    public Dictionary<string, object> Comparisons { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var url = request.RequestUri!.ToString();
        object? content = Failing ? null
            : url == Url ? Releases
            : url.StartsWith(Url.Replace("/releases", "/compare/"), StringComparison.Ordinal) ? Comparisons.GetValueOrDefault(url.Split("/compare/")[1])
            : null;
        return Task.FromResult(content is null
            ? new HttpResponseMessage(HttpStatusCode.NotFound)
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(content) });
    }

    public static object Release(string tag, bool prerelease = false, bool draft = false, string[]? assets = null) => new
    {
        tag_name = tag, name = $"Arcanist {tag.TrimStart('v')}", body = $"## What's changed\n* Things in {tag}",
        html_url = $"https://github.com/linkfast/arcanist/releases/tag/{tag}", draft, prerelease, published_at = "2026-10-19T12:00:00Z",
        assets = (assets ?? []).Select(name => new
        {
            name, browser_download_url = $"https://github.com/linkfast/arcanist/releases/download/{tag}/{name}", size = 1000, digest = "sha256:abc",
        }),
    };

    public static object Comparison(int? total, params string[] messages) => new
    {
        total_commits = total ?? messages.Length,
        commits = messages.Select((message, i) => new { html_url = $"https://github.com/linkfast/arcanist/commit/{i}", commit = new { message } }),
    };
}

public sealed class UpdateTests
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private readonly FakeGitHubReleases _github = new()
    {
        Releases =
        [
            FakeGitHubReleases.Release("v0.1.0-alpha.1", prerelease: true),
            FakeGitHubReleases.Release("v0.1.0-alpha.2", prerelease: true),
            FakeGitHubReleases.Release("v0.0.9"),
            FakeGitHubReleases.Release("v0.3.0", draft: true),
            FakeGitHubReleases.Release("nightly", prerelease: true),
        ],
    };

    private ArcanistFactory Create(string current) => new(
        new Dictionary<string, string?>
        {
            ["Arcanist:Updates:ReleasesUrl"] = FakeGitHubReleases.Url,
            ["Arcanist:Updates:CurrentVersion"] = current,
        },
        services: services => services.AddHttpClient(UpdateChecker.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => _github));

    [Fact]
    public async Task A_pre_release_server_is_told_of_newer_pre_releases()
    {
        await using var factory = Create("0.1.0-alpha.1");
        await factory.CompleteSetupAsync();
        var admin = await factory.CreateAuthenticatedClientAsync();

        var update = await (await admin.PostAsync("/rest/v1/server/update/check", null, Cancel)).Content.ReadFromJsonAsync<ServerUpdate>(ArcanistFactory.Json, Cancel);
        Assert.True(update!.UpdateAvailable);
        Assert.Equal(("0.1.0-alpha.2", "Arcanist 0.1.0-alpha.2", true), (update.Latest!.Version, update.Latest.Name, update.Latest.Prerelease));
        Assert.Contains("Things in v0.1.0-alpha.2", update.Latest.Notes);
        Assert.Equal("0.1.0-alpha.1", update.CurrentVersion);

        // A failed check keeps what was found before.
        _github.Failing = true;
        update = await (await admin.PostAsync("/rest/v1/server/update/check", null, Cancel)).Content.ReadFromJsonAsync<ServerUpdate>(ArcanistFactory.Json, Cancel);
        Assert.Equal(("update_check_failed", "0.1.0-alpha.2"), (update!.ErrorCode, update.Latest?.Version));

        // Switched off, nothing is checked or shown.
        var off = await admin.PutAsJsonAsync("/rest/v1/server/update/settings", new SetServerUpdateSettingsInput(false), ArcanistFactory.Json, Cancel);
        update = await off.Content.ReadFromJsonAsync<ServerUpdate>(ArcanistFactory.Json, Cancel);
        Assert.Equal((false, false, null), (update!.Enabled, update.UpdateAvailable, update.Latest));

        await factory.CreateUserAsync("listener");
        var listener = await factory.CreateAuthenticatedClientAsync("listener");
        Assert.Equal(HttpStatusCode.Forbidden, (await listener.GetAsync("/rest/v1/server/update", Cancel)).StatusCode);
    }

    [Fact]
    public async Task A_newer_version_says_what_it_changes_since_the_installed_one()
    {
        _github.Releases =
        [
            FakeGitHubReleases.Release("v0.1.0-alpha.1", prerelease: true),
            FakeGitHubReleases.Release("v0.1.0-alpha.2", prerelease: true),
            FakeGitHubReleases.Release("v0.1.0-alpha.3", prerelease: true),
        ];
        _github.Comparisons["v0.1.0-alpha.1...v0.1.0-alpha.3"] = FakeGitHubReleases.Comparison(300,
            "Jukebox Jam: the host's own queue stays\n\nA longer body.",
            "Tests: Ultimate Cloud's fake in a file of its own",
            "Merge branch 'x'",
            "CI: faster builds",
            "Webamp 0.12.0-alpha.1: asks for the lyrics",
            "Webamp for Arcanist 0.15.0-alpha.1",
            "A subject without an area");

        await using var factory = Create("0.1.0-alpha.1");
        await factory.CompleteSetupAsync();
        var admin = await factory.CreateAuthenticatedClientAsync();
        var update = await (await admin.PostAsync("/rest/v1/server/update/check", null, Cancel)).Content.ReadFromJsonAsync<ServerUpdate>(ArcanistFactory.Json, Cancel);

        // Newest first, without what only concerns the project's own work.
        Assert.Equal(
            [(null, "A subject without an area"), ("Webamp", "0.15.0-alpha.1"), ("Webamp", "asks for the lyrics"), ("Jukebox Jam", "the host's own queue stays")],
            update!.Changes.Select(c => (c.Area, c.Text)));
        Assert.Equal("https://github.com/linkfast/arcanist/commit/0", update.Changes[3].Url);
        Assert.Equal((true, 2), (update.MoreChanges, update.VersionsBehind));

        // What cannot be compared (no such tag) leaves the changes empty and the update known.
        _github.Comparisons.Clear();
        update = await (await admin.PostAsync("/rest/v1/server/update/check", null, Cancel)).Content.ReadFromJsonAsync<ServerUpdate>(ArcanistFactory.Json, Cancel);
        Assert.True(update!.UpdateAvailable);
        Assert.Empty(update.Changes);
        Assert.Null(update.ErrorCode);
    }

    [Theory]
    [InlineData(InstallKind.Synology, "x64", "arcanist_x64-7.2_0.2.0-1.spk")]
    [InlineData(InstallKind.Synology, "arm64", "arcanist_aarch64-7.2_0.2.0-1.spk")]
    [InlineData(InstallKind.Qnap, "x64", "Arcanist_0.2.0_x86_64.qpkg")]
    [InlineData(InstallKind.Qnap, "arm64", "Arcanist_0.2.0_arm_64.qpkg")]
    [InlineData(InstallKind.DebianPackage, "arm64", "arcanist_0.2.0-1_arm64.deb")]
    [InlineData(InstallKind.OpenMediaVault, "x64", "arcanist_0.2.0-1_amd64.deb,openmediavault-arcanist_1.2_all.deb")]
    [InlineData(InstallKind.Systemd, "x64", "arcanist-0.2.0-linux-x64.tar.gz")]
    [InlineData(InstallKind.Windows, "x64", "Arcanist-win-Setup.exe")]
    [InlineData(InstallKind.WindowsService, "arm64", "Arcanist-win-arm64-Setup.exe")]
    [InlineData(InstallKind.MacOS, "arm64", "Arcanist-0.2.0-macos-arm64.dmg")]
    [InlineData(InstallKind.Docker, "x64", "")]
    [InlineData(InstallKind.TrueNas, "x64", "")]
    [InlineData(InstallKind.Other, "x64", "")]
    public void Each_installation_gets_the_file_of_its_architecture(InstallKind kind, string architecture, string expected)
    {
        string[] names =
        [
            "arcanist_x64-7.2_0.2.0-1.spk", "arcanist_aarch64-7.2_0.2.0-1.spk", "Arcanist_0.2.0_x86_64.qpkg", "Arcanist_0.2.0_arm_64.qpkg",
            "arcanist_0.2.0-1_amd64.deb", "arcanist_0.2.0-1_arm64.deb", "openmediavault-arcanist_1.2_all.deb",
            "arcanist-0.2.0-linux-x64.tar.gz", "arcanist-0.2.0-linux-x64-dotnet.tar.gz", "arcanist-0.2.0-linux-arm64.tar.gz",
            "Arcanist-win-Setup.exe", "Arcanist-win-arm64-Setup.exe", "Arcanist-0.2.0-macos-arm64.dmg", "Arcanist-0.2.0-macos-x64.dmg",
        ];
        var install = UpdateInstructions.Build(kind, architecture, "0.2.0", "https://github.com/linkfast/arcanist/releases/tag/v0.2.0",
            names.Select(n => new ReleaseAsset(n, $"https://example.test/{n}", 1, null)).ToList(), "abc123");
        Assert.Equal(expected, string.Join(',', install.Downloads.Select(d => d.Name)));
        Assert.Equal(kind is InstallKind.Windows or InstallKind.WindowsService or InstallKind.MacOS, install.UpdatesItself);
    }

    [Fact]
    public void Commands_are_ready_to_paste_and_docker_keeps_what_the_container_has()
    {
        const string release = "https://github.com/linkfast/arcanist/releases/tag/v0.2.0";
        ReleaseAsset[] assets = [new("arcanist_0.2.0-1_amd64.deb", "https://example.test/a.deb", 1, null)];

        var deb = UpdateInstructions.Build(InstallKind.DebianPackage, "x64", "0.2.0", release, assets, "abc123").Commands.Single();
        Assert.Equal("curl -fLO https://example.test/a.deb\nsudo apt install ./arcanist_0.2.0-1_amd64.deb", deb.Script);

        var docker = UpdateInstructions.Build(InstallKind.Docker, "x64", "0.2.0", release, [], "abc123");
        Assert.Equal([UpdateCommandKind.DockerCompose, UpdateCommandKind.DockerRun], docker.Commands.Select(c => c.Kind));
        var run = docker.Commands[1].Script;
        Assert.Contains("CONTAINER=abc123", run);
        Assert.Contains("IMAGE=ghcr.io/linkfast/arcanist:0.2.0", run);
        Assert.Contains("{{range .Mounts}}", run);
        Assert.Equal("https://github.com/linkfast/arcanist/blob/v0.2.0/packaging/docker/README.md", docker.GuideUrl);
    }

    [Fact]
    public void In_a_container_the_mounts_say_what_recreating_it_keeps()
    {
        const string mountInfo = """
            610 500 0:50 / / rw,relatime - overlay overlay rw
            611 610 0:53 / /proc rw - proc proc rw
            612 610 0:54 / /dev rw - tmpfs tmpfs rw
            613 612 0:55 / /dev/shm rw - tmpfs shm rw
            620 610 8:1 /docker/volumes/data/_data /data rw - ext4 /dev/sda1 rw
            621 610 8:1 /home/me/My\040Music /music/My\040Music ro - ext4 /dev/sda1 rw
            622 610 8:1 /docker/containers/x/hosts /etc/hosts rw - ext4 /dev/sda1 rw
            """;
        var mounts = UpdateInstructions.MountsOf(mountInfo);
        Assert.Equal(["/data", "/music/My Music"], mounts);
        Assert.True(UpdateInstructions.IsMounted("/data", mounts));
        Assert.True(UpdateInstructions.IsMounted("/music/My Music/Rock/", mounts));
        Assert.False(UpdateInstructions.IsMounted("/database", mounts));
        Assert.False(UpdateInstructions.IsMounted("/app/music", mounts));
    }

    [Fact]
    public async Task The_panel_only_installs_where_the_installation_lets_it()
    {
        await using var factory = Create("0.1.0-alpha.1");
        await factory.CompleteSetupAsync();
        var admin = await factory.CreateAuthenticatedClientAsync();
        await admin.PostAsync("/rest/v1/server/update/check", null, Cancel);

        // On this machine's kind of installation there is no installer: it says so, and installs nothing.
        var response = await admin.PostAsync("/rest/v1/server/update/install", null, Cancel);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("update_self_unavailable", (await response.Content.ReadFromJsonAsync<JsonElement>(ArcanistFactory.Json, Cancel)).GetProperty("code").GetString());
        var status = await admin.GetFromJsonAsync<JsonElement>("/rest/v1/server/update/install", ArcanistFactory.Json, Cancel);
        Assert.Equal("IDLE", status.GetProperty("state").GetString());

        await factory.CreateUserAsync("listener");
        var listener = await factory.CreateAuthenticatedClientAsync("listener");
        Assert.Equal(HttpStatusCode.Forbidden, (await listener.PostAsync("/rest/v1/server/update/install", null, Cancel)).StatusCode);
    }

    [Theory]
    [InlineData("0.2.0", true)]
    [InlineData("0.2.0-alpha.1", true)]
    [InlineData("../../evil", false)]
    [InlineData("0.2.0/../x", false)]
    [InlineData("0.2.0 --force", false)]
    [InlineData("", false)]
    [InlineData("edge", false)]
    public void The_update_service_only_takes_a_version_from_a_request(string request, bool valid) =>
        Assert.Equal(valid, SelfUpdateTool.IsVersion(request));

    [Fact]
    public async Task A_package_is_only_kept_if_it_matches_its_published_checksum()
    {
        var folder = Directory.CreateTempSubdirectory("arcanist-update-").FullName;
        byte[] content = "the package"u8.ToArray();
        var sha256 = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(content));
        try
        {
            async Task<List<string>> DownloadAsync(string digest)
            {
                var name = $"Arcanist_0.2.0_{(System.Runtime.InteropServices.RuntimeInformation.OSArchitecture == System.Runtime.InteropServices.Architecture.Arm64 ? "arm_64" : "x86_64")}.qpkg";
                var handler = new FakePackages(name, content, digest);
                using var http = new HttpClient(handler);
                return await ReleasePackages.DownloadAsync(http, "https://github.test/repos/linkfast/arcanist", "0.2.0", InstallKind.Qnap, folder, Cancel);
            }

            Assert.Equal(content, await File.ReadAllBytesAsync((await DownloadAsync($"sha256:{sha256}")).Single(), Cancel));
            var wrong = await Assert.ThrowsAsync<SelfUpdateException>(() => DownloadAsync("sha256:0000"));
            Assert.Equal("update_checksum_mismatch", wrong.Code);
            // Without a checksum there is nothing that says it is the one published.
            Assert.Equal("update_checksum_mismatch", (await Assert.ThrowsAsync<SelfUpdateException>(() => DownloadAsync(""))).Code);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    private sealed class FakePackages(string name, byte[] content, string digest) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(request.RequestUri!.ToString().EndsWith("/releases/tags/v0.2.0", StringComparison.Ordinal)
                ? new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new
                    {
                        tag_name = "v0.2.0", html_url = "https://github.test/r", draft = false, prerelease = false,
                        assets = new[] { new { name, browser_download_url = "https://github.test/package", size = content.Length, digest } },
                    }),
                }
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(content) });
    }

    [Fact]
    public async Task A_stable_server_only_follows_stable_releases()
    {
        await using var factory = Create("0.1.0");
        await factory.CompleteSetupAsync();
        var admin = await factory.CreateAuthenticatedClientAsync();

        var update = await (await admin.PostAsync("/rest/v1/server/update/check", null, Cancel)).Content.ReadFromJsonAsync<ServerUpdate>(ArcanistFactory.Json, Cancel);
        Assert.Equal(("0.0.9", false), (update!.Latest?.Version, update.UpdateAvailable));
    }

    [Fact]
    public async Task Without_external_services_there_are_no_checks()
    {
        await using var factory = Create("0.1.0-alpha.1");
        await factory.CompleteSetupAsync();
        var admin = await factory.CreateAuthenticatedClientAsync();
        (await admin.PutAsJsonAsync("/rest/v1/settings/privacy", new { noTelemetry = false, externalServices = "NONE" }, ArcanistFactory.Json, Cancel)).EnsureSuccessStatusCode();

        var update = await (await admin.PostAsync("/rest/v1/server/update/check", null, Cancel)).Content.ReadFromJsonAsync<ServerUpdate>(ArcanistFactory.Json, Cancel);
        Assert.Equal((false, null, null), (update!.Enabled, update.Latest, update.CheckedAt));
    }

    [Fact]
    public async Task Administrators_read_where_the_server_runs()
    {
        await using var factory = Create("0.1.0");
        await factory.CompleteSetupAsync();
        var admin = await factory.CreateAuthenticatedClientAsync();

        var host = await admin.GetFromJsonAsync<JsonElement>("/rest/v1/server/host", ArcanistFactory.Json, Cancel);
        Assert.Equal(factory.DataPath, host.GetProperty("dataDirectory").GetString());
        Assert.Equal(Environment.UserName, host.GetProperty("user").GetString());
        Assert.False(string.IsNullOrEmpty(host.GetProperty("kind").GetString()));
        Assert.Equal(host.GetProperty("kind").GetString(),
            (await admin.GraphQLAsync("{ serverHost { kind } }")).GetProperty("data").GetProperty("serverHost").GetProperty("kind").GetString());

        await factory.CreateUserAsync("listener");
        var listener = await factory.CreateAuthenticatedClientAsync("listener");
        Assert.Equal(HttpStatusCode.Forbidden, (await listener.GetAsync("/rest/v1/server/host", Cancel)).StatusCode);
    }

    [Fact]
    public void A_NAS_app_says_how_it_was_installed()
    {
        // Only this test sets the variable, and only DetectInstallKind reads it.
        try
        {
            foreach (var (value, kind) in new[] { ("ROCKSTOR", InstallKind.Rockstor), ("TRUE_NAS", InstallKind.TrueNas), ("qnap", InstallKind.Qnap) })
            {
                Environment.SetEnvironmentVariable("ARCANIST_INSTALL_KIND", value);
                Assert.Equal(kind, UpdateChecker.DetectInstallKind());
            }
            Environment.SetEnvironmentVariable("ARCANIST_INSTALL_KIND", "SOMETHING_ELSE");
            Assert.NotEqual(InstallKind.Rockstor, UpdateChecker.DetectInstallKind());
        }
        finally
        {
            Environment.SetEnvironmentVariable("ARCANIST_INSTALL_KIND", null);
        }
    }
}
