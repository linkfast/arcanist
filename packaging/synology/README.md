# Synology package

`spksrc/` holds the [spksrc](https://github.com/SynoCommunity/spksrc) definitions to submit to SynoCommunity:
`cross/arcanist` downloads the release tarballs for systems with .NET (`arcanist-<version>-linux-<arch>-dotnet`,
x64 and arm64) and `spk/arcanist` packages them for DSM 7.2 and later.

- Depends on two SynoCommunity packages, which Package Center installs with it: **`dotnet10-runtime`** (the
  ASP.NET Core 10 runtime that runs the server; the package is some 70 MB smaller than with a self-contained build)
  and **`ffmpeg8`** (audio conversion, found by Arcanist at `/var/packages/ffmpeg8/target/bin/ffmpeg`).
- Runs as the internal user `sc-arcanist`. Its data (settings, SQLite database, keys, caches, logs) is in
  `/var/packages/arcanist/var`, on the NAS's own volume, never on a share.
- The install wizard asks for the music shared folder (created if missing and made accessible to `sc-arcanist`) and
  the port, and shows the setup code that Arcanist's own setup wizard asks for.
- `bin/arcanist-run` supervises the server: it reads the port from `var/package.conf` on every start, and starts the
  server again when it asks to (exit code 75, after a factory reset or a server restore; more than five times in a
  minute and it stops) or when the package's window asks for it. DSM stops it with TERM, passed on to the server.
- DSM writes the package's output (the supervisor's and the server's console) to `var/arcanist.log` and clears it only
  when the package starts; the supervisor cuts it to its last 2 MB when it passes 10 MB. Arcanist's own log is in
  `var/logs/arcanist-<date>.log` (14 days).
- Server audio: DSM has no ALSA, so the package builds SynoCommunity's `cross/alsa-lib` (`lib/libasound.so.2`, its
  configuration in `share/alsa`) and links it next to the player, which looks there first. The sound cards
  (`/dev/snd`) must also be usable by `sc-arcanist`, which a DSM 7 package cannot arrange: the window says so, and
  how (a task run as root at boot-up with `chmod a+rw /dev/snd/*`).
- Updates come from Package Center; Arcanist has no updater of its own on DSM.

## Arcanist's window in DSM

Arcanist appears in DSM's main menu (administrators only) and opens in a window of DSM's desktop: `app/arcanist.js`
is a DSM application (`SYNOCOMMUNITY.Arcanist.AppInstance`, the package's `dsmappname`) whose window shows
`app/index.html` (on `/webman/3rdparty/arcanist/`) with the session's `SynoToken`. A `url` or `legacy` app would open
in a browser tab instead, without that token. The page talks to `app/api.cgi`, which checks the DSM session
(`authenticate.cgi`, with the cookie and the `SynoToken` the page sends in `X-SYNO-TOKEN`) and the `administrators`
group. It shows:

- whether the server runs, its version and, until it is set up, the setup code;
- the port, which can be changed (the server starts again on it; DSM's firewall has the default port 8484 open);
- the shared folders and whether `sc-arcanist` can read them, with the steps to give it access and a button that
  opens Control Panel > Shared Folder (a package cannot change share permissions itself on DSM 7);
- where its data is and how much it takes, and a link to Arcanist's own panel;
- **logs** (their own tab): Arcanist's, by day, and the package's output, followed live (every 2 s), with a level
  filter that keeps an exception's lines with it, a search box and a download button. `api.cgi` sends a log in
  pieces after an offset (`?action=log&file=…&offset=…`, at most 512 KB, as text with `X-Log-*` headers) and reads
  nothing but those files;
- **test mode**: variables given to Arcanist when it starts, to try it against services not published yet (for
  example `Arcanist:UltimateCloud:ApiUrl` with a test Ultimate Cloud). Settings may be written with `:` and are saved
  as variables (`Arcanist__UltimateCloud__ApiUrl`) in `var/environment`; `TEST_MODE=1` in `var/package.conf` turns
  them on. They take precedence over `arcanist.json`. Names the package relies on (`PATH`, `HOME`, `LD_*`,
  `DOTNET_ROOT`, the data and server folders, `URLS`, `Arcanist__Hosting__*`) are refused, and `arcanist-run` checks
  them again and logs only their names. Turning it off keeps them without applying them.

`api.cgi` is plain `sh` (DSM has no Python or jq by default). POST requests need the header `X-Arcanist-Window: 1`,
which a form on another site cannot send.

## Building a package to try

One command builds everything from this checkout (native libraries, admin panel, server and package) with **Docker
as the only requirement** (Docker Desktop with Linux containers, on Windows, macOS or Linux), in PowerShell:

```powershell
pwsh packaging/synology/build.ps1                 # x64 (Intel or AMD NAS)
pwsh packaging/synology/build.ps1 -Arch arm64     # ARMv8; -Arch all for both
```

The package is `artifacts/synology/arcanist_<x64|aarch64>-7.2_<version>-<revision>.spk`: install it in Package Center
with **Manual Install**. Each build takes the next revision, so DSM installs it over the previous one (`-Revision`
sets it). The first build takes a while (it builds a Rust image and spksrc downloads the DSM toolchain, several GB);
later ones reuse their caches (Docker volumes `arcanist-*` and `artifacts/spksrc`). `-SkipNative`, `-SkipPanel` and
`-SkipServer` reuse what the last build left. On Windows PowerShell 5, use `powershell -File` instead of `pwsh`.

The package bundles Webamp, like every Arcanist package: by default its published release, pinned by version and
SHA-256 in `src/Arcanist.Core/Arcanist.Core.csproj` (`BundledPlayer`). To try a local Webamp instead, build it into
the package from its checkout (by default `../ArcanistWebPlayers/webamp`, or `-WebampPath`):

```powershell
pwsh packaging/synology/build.ps1 -LocalWebamp
```

It runs Webamp's own `packages/arcanist-webamp/scripts/pack.ps1` (also in Docker), which alone builds its zip.

With the tools installed instead (Rust and cargo-zigbuild, Node 24, .NET 10, Docker), from a shell:

```sh
packaging/linux/pack.sh                 # ARCHES="x64" FLAVORS="dotnet" for just what the package needs
packaging/synology/build-local.sh       # artifacts/synology/arcanist_*.spk (ARCHES too)
```

## Building for SynoCommunity

In a checkout of spksrc (normally its Docker build environment):

```sh
cp -R <arcanist>/packaging/synology/spksrc/cross/arcanist cross/
cp -R <arcanist>/packaging/synology/spksrc/spk/arcanist spk/
make -C cross/arcanist digests       # once per version, after the GitHub release exists
make -C spk/arcanist arch-x64-7.2 arch-aarch64-7.2
```

For a new version, set `PKG_VERS` in `cross/arcanist/Makefile` and `SPK_VERS` in `spk/arcanist/Makefile` (reset
`SPK_REV` to 1), regenerate the digests and update `CHANGELOG`.
