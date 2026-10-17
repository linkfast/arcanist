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

Before a release exists, from the local tarballs (needs Docker):

```sh
packaging/linux/pack.sh
packaging/synology/build-local.sh       # artifacts/synology/arcanist_*.spk
```

Install the `.spk` in Package Center with "Manual Install".

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
