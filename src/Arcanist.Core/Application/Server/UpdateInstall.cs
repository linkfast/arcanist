using System.Text.RegularExpressions;

namespace Arcanist.Core.Application.Server;

/// <summary>A change a newer version brings: a commit's subject. Part of the API contract.</summary>
/// <param name="Area">What it is about (what comes before the colon: <c>Jukebox Jam</c>), when it says.</param>
/// <param name="Text">What changed, in English, as the project wrote it.</param>
/// <param name="Url">The commit.</param>
public sealed record ArcanistChange(string? Area, string Text, string? Url);

/// <summary>A file of the release this server needs. Part of the API contract.</summary>
public sealed record UpdateDownload(string Name, string Url, long Size);

/// <summary>What a set of commands is for. Part of the API contract.</summary>
public enum UpdateCommandKind
{
    /// <summary>Docker Compose, from the folder of its file.</summary>
    DockerCompose,
    /// <summary>Docker without Compose: recreates the container with the mounts, ports and variables it has.</summary>
    DockerRun,
    /// <summary>The .deb package (with OpenMediaVault's plugin on one).</summary>
    DebianPackage,
    /// <summary>A Linux tarball under systemd.</summary>
    Tarball,
    /// <summary>Homebrew's cask on macOS.</summary>
    Homebrew,
}

/// <summary>Commands ready to copy and paste. Part of the API contract.</summary>
public sealed record UpdateCommand(UpdateCommandKind Kind, string Script);

/// <summary>How this server is updated. Part of the API contract.</summary>
/// <param name="Downloads">The files of the release for this installation and its architecture.</param>
/// <param name="Commands">Commands to copy, for installations updated from a terminal.</param>
/// <param name="GuideUrl">The repository's guide for this installation.</param>
/// <param name="UpdatesItself">The tray or menu bar app installs it on its own.</param>
/// <param name="DataInContainer">In Docker, the data directory is not a mount: recreating the container deletes it.</param>
/// <param name="Mounts">In Docker, the folders mounted in the container: what recreating it keeps.</param>
/// <param name="LibrariesOutsideMounts">In Docker, the libraries whose folder is in no mount: recreating the container loses them.</param>
public sealed record UpdateInstall(
    IReadOnlyList<UpdateDownload> Downloads,
    IReadOnlyList<UpdateCommand> Commands,
    string? GuideUrl,
    bool UpdatesItself,
    bool DataInContainer,
    IReadOnlyList<string> Mounts,
    IReadOnlyList<string> LibrariesOutsideMounts);

/// <summary>A file of a release, as it is kept between checks.</summary>
public sealed record ReleaseAsset(string Name, string Url, long Size, string? Sha256);

/// <summary>What to tell an administrator about updating, from how the server was installed.</summary>
internal static partial class UpdateInstructions
{
    private const string Image = "ghcr.io/linkfast/arcanist";

    /// <param name="architecture">As <see cref="ServerHost.Architecture"/> says it (<c>x64</c>, <c>arm64</c>).</param>
    /// <param name="containerId">The container's id, in a container.</param>
    public static UpdateInstall Build(
        InstallKind kind, string architecture, string version, string releaseUrl, IReadOnlyList<ReleaseAsset> assets, string containerId)
    {
        var arm = architecture.Equals("arm64", StringComparison.OrdinalIgnoreCase);
        var downloads = Downloads(kind, arm, assets);
        return new UpdateInstall(
            downloads.Select(a => new UpdateDownload(a.Name, a.Url, a.Size)).ToList(),
            Commands(kind, version, downloads, containerId),
            Guide(kind, version, releaseUrl),
            kind is InstallKind.Windows or InstallKind.WindowsService or InstallKind.MacOS,
            false, [], []);
    }

    private static List<ReleaseAsset> Downloads(InstallKind kind, bool arm, IReadOnlyList<ReleaseAsset> assets)
    {
        var patterns = kind switch
        {
            InstallKind.Windows or InstallKind.WindowsService => [arm ? @"^Arcanist-win-arm64-Setup\.exe$" : @"^Arcanist-win-Setup\.exe$"],
            InstallKind.MacOS => [$@"^Arcanist-.*-macos-{(arm ? "arm64" : "x64")}\.dmg$"],
            InstallKind.Synology => [$@"^arcanist_{(arm ? "aarch64" : "x64")}-.*\.spk$"],
            InstallKind.Qnap => [$@"^Arcanist_.*_{(arm ? "arm_64" : "x86_64")}\.qpkg$"],
            InstallKind.DebianPackage => [$@"^arcanist_.*_{(arm ? "arm64" : "amd64")}\.deb$"],
            InstallKind.OpenMediaVault => [$@"^arcanist_.*_{(arm ? "arm64" : "amd64")}\.deb$", @"^openmediavault-arcanist_.*\.deb$"],
            InstallKind.Systemd => [$@"^arcanist-.*-linux-{(arm ? "arm64" : "x64")}\.tar\.gz$"],
            _ => Array.Empty<string>(),
        };
        return patterns.SelectMany(p => assets.Where(a => Regex.IsMatch(a.Name, p)).Take(1)).ToList();
    }

    private static List<UpdateCommand> Commands(InstallKind kind, string version, List<ReleaseAsset> downloads, string containerId) => kind switch
    {
        InstallKind.Docker =>
        [
            new(UpdateCommandKind.DockerCompose, "docker compose pull\ndocker compose up -d"),
            new(UpdateCommandKind.DockerRun, DockerRun(version, containerId)),
        ],
        InstallKind.DebianPackage or InstallKind.OpenMediaVault when downloads.Count > 0 =>
        [
            new(UpdateCommandKind.DebianPackage, string.Join('\n', downloads.Select(d => $"curl -fLO {d.Url}"))
                + "\nsudo apt install " + string.Join(' ', downloads.Select(d => $"./{d.Name}"))),
        ],
        InstallKind.Systemd when downloads is [var tarball] =>
        [
            new(UpdateCommandKind.Tarball, $"curl -fLO {tarball.Url}\n"
                + "sudo systemctl stop arcanist\n"
                + $"sudo tar -xzf {tarball.Name} -C /opt/arcanist --strip-components=1\n"
                + "sudo systemctl start arcanist"),
        ],
        InstallKind.MacOS => [new(UpdateCommandKind.Homebrew, "brew upgrade --cask linkfast/arcanist/arcanist")],
        _ => [],
    };

    /// <summary>
    /// Recreates the container from what Docker says it has, so that no folder is lost to a command typed by hand: from
    /// inside, the server cannot tell the host's paths. In a subshell, so that a failure does not close the terminal.
    /// </summary>
    private static string DockerRun(string version, string containerId) => $$$"""
        (
          set -e
          CONTAINER={{{containerId}}}
          IMAGE={{{Image}}}:{{{version}}}
          docker pull "$IMAGE"
          NAME=$(docker inspect --format '{{.Name}}' "$CONTAINER" | cut -c2-)
          ARGS=$(docker inspect --format '{{range .Mounts}}-v {{if .Name}}{{if .RW}}{{json (printf "%s:%s" .Name .Destination)}}{{else}}{{json (printf "%s:%s:ro" .Name .Destination)}}{{end}}{{else}}{{if .RW}}{{json (printf "%s:%s" .Source .Destination)}}{{else}}{{json (printf "%s:%s:ro" .Source .Destination)}}{{end}}{{end}} {{end}}{{range $port, $hosts := .HostConfig.PortBindings}}{{range $hosts}}{{$ip := printf "%v" .HostIP}}-p {{if and (ne $ip "") (ne $ip "invalid IP")}}{{$ip}}:{{end}}{{.HostPort}}:{{$port}} {{end}}{{end}}{{range .HostConfig.Devices}}--device {{.PathOnHost}}:{{.PathInContainer}} {{end}}{{range .HostConfig.GroupAdd}}--group-add {{.}} {{end}}{{if .HostConfig.RestartPolicy.Name}}--restart {{.HostConfig.RestartPolicy.Name}} {{end}}{{if .Config.User}}--user {{.Config.User}} {{end}}--network {{.HostConfig.NetworkMode}}' "$CONTAINER")
          docker inspect --format '{{range .Config.Env}}{{println .}}{{end}}' "$CONTAINER" | grep -E '^(Arcanist|ARCANIST_|TZ=|HOST_OS=)' > arcanist.env || true
          docker stop "$CONTAINER"
          docker rm "$CONTAINER"
          eval docker run -d --name "\"$NAME\"" --env-file arcanist.env "$ARGS" "\"$IMAGE\""
          rm arcanist.env
        )
        """;

    private static string? Guide(InstallKind kind, string version, string releaseUrl)
    {
        var folder = kind switch
        {
            InstallKind.Docker => "docker",
            InstallKind.Synology => "synology",
            InstallKind.Qnap => "qnap",
            InstallKind.OpenMediaVault => "openmediavault",
            InstallKind.TrueNas => "truenas",
            InstallKind.Unraid => "unraid",
            InstallKind.Rockstor => "rockstor",
            InstallKind.MacOS => "macos",
            InstallKind.DebianPackage or InstallKind.Systemd => "linux",
            _ => null,
        };
        var repository = releaseUrl.Split("/releases/")[0];
        return folder is null ? null : $"{repository}/blob/v{version}/packaging/{folder}/README.md";
    }

    /// <summary>
    /// The commits between two versions, newest first, as changes: what only concerns the project's own work is left
    /// out (merges, tests, CI).
    /// </summary>
    public static List<ArcanistChange> Changes(IEnumerable<(string Message, string? Url)> commits)
    {
        var changes = new List<ArcanistChange>();
        foreach (var (message, url) in commits)
        {
            var subject = message.Split('\n')[0].Trim();
            if (subject.Length == 0 || subject.StartsWith("Merge ", StringComparison.Ordinal))
                continue;
            // "Webamp for Arcanist 0.15.0" says a bundled player moved to that version.
            var player = BundledPlayerVersion().Match(subject);
            if (player.Success)
            {
                changes.Add(new ArcanistChange(player.Groups[1].Value, player.Groups[2].Value, url));
                continue;
            }
            var match = AreaOf().Match(subject);
            var area = match.Success ? VersionAtTheEnd().Replace(match.Groups[1].Value, "") : null;
            if (area is not null && InternalAreas.Contains(area))
                continue;
            changes.Add(new ArcanistChange(area, match.Success ? match.Groups[2].Value : subject, url));
        }
        changes.Reverse();
        return changes;
    }

    private static readonly HashSet<string> InternalAreas = new(["Tests", "Test", "CI", "Release"], StringComparer.OrdinalIgnoreCase);

    [GeneratedRegex(@"^([^:]{1,40}):\s+(.+)$")]
    private static partial Regex AreaOf();

    [GeneratedRegex(@"^(\S+) for Arcanist (\d\S*)$")]
    private static partial Regex BundledPlayerVersion();

    [GeneratedRegex(@"\s+v?\d+\.\d+\S*$")]
    private static partial Regex VersionAtTheEnd();

    /// <summary>The mount points of <c>/proc/self/mountinfo</c> a person mounted: not the system's, nor the ones Docker adds.</summary>
    public static List<string> MountsOf(string mountInfo)
    {
        string[] system = ["/proc", "/sys", "/dev"];
        string[] docker = ["/", "/etc/hosts", "/etc/hostname", "/etc/resolv.conf"];
        return mountInfo.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Split(' '))
            .Where(fields => fields.Length > 4)
            // Spaces in a mount point are written as \040.
            .Select(fields => fields[4].Replace(@"\040", " "))
            .Where(path => !docker.Contains(path) && !system.Any(s => path == s || path.StartsWith(s + "/", StringComparison.Ordinal)))
            .Distinct()
            .ToList();
    }

    /// <summary>Whether <paramref name="path"/> is in one of the mounts.</summary>
    public static bool IsMounted(string path, IEnumerable<string> mounts)
    {
        path = path.TrimEnd('/');
        return mounts.Any(mount => path == mount || path.StartsWith(mount.TrimEnd('/') + "/", StringComparison.Ordinal));
    }
}
