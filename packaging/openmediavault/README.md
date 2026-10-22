# OpenMediaVault plugin

`openmediavault-arcanist` makes Arcanist a service of [OpenMediaVault](https://www.openmediavault.org/) 7 and 8
(amd64 and arm64): **Services → Arcanist** in the workbench. It runs the `arcanist` package (`packaging/linux/deb.sh`)
and only manages what OpenMediaVault knows better than Arcanist; everything else (libraries, users…) is set up in
Arcanist's own web interface.

- **Settings:** enabled, port (8484 by default), server audio (the sound card for the Server audio extension), and the
  buttons to open Arcanist and to restart it.
- **Shared folders:** the folders Arcanist may use, each read-only or writable (uploads, organizing, drop folders,
  imports, backups). Their path is the one to choose in Arcanist.
- **Status:** whether it runs, its version and, until setup is done, the setup code that its wizard asks for.
- **Log:** Diagnostics → System Logs → Arcanist (the service's journal).

## How it works

`src/` is the plugin as it is installed:

- `datamodels/` and `confdb/create.d/`: the settings in OpenMediaVault's database (`conf.service.arcanist`, and
  `conf.service.arcanist.share` for each shared folder).
- `engined/rpc/arcanist.inc`: the RPC service the pages use (`omv-rpc -u admin Arcanist getInfo`);
  `engined/module/arcanist.inc` marks the configuration as changed (the *Apply* banner) and reports the service's
  state; `engined/inc/90arcanist.inc` registers its log.
- `srv/salt/omv/deploy/arcanist/`: what *Apply* runs (`omv-salt deploy run arcanist`). It writes the systemd drop-in
  `/etc/systemd/system/arcanist.service.d/openmediavault.conf` and starts or stops the service. The service keeps its
  hardening (`DynamicUser`, `ProtectSystem=strict`): the drop-in sets the port (`Urls`), adds the group `users`
  (OpenMediaVault's shared folders are `root:users`, mode 2775), `ReadWritePaths=` for the writable folders,
  `RequiresMountsFor=` so it starts after their disks, `UMask=0002` (what it writes stays writable by the group), and
  `PrivateDevices=no` with the group `audio` for server audio. Everything else stays read-only to the service.
- `workbench/`: the pages (navigation, routes, components and the log).

Removing the plugin removes the drop-in (Arcanist goes back to port 8484 and its package's settings); its data in
`/var/lib/arcanist` is never touched.

## Build and install

`packaging/linux/build.ps1` builds it with the `.deb`, and the release attaches it
(`openmediavault-arcanist_<version>_all.deb`). On its own (needs `dpkg-deb`):

```bash
sh packaging/openmediavault/build.sh [arcanist version] [plugin version]
```

The plugin's version is `1.<number of commits>`: OpenMediaVault migrates its settings with the previous version, read
with Python's `packaging`, which rejects Debian's `~` (Arcanist's pre-releases). It depends on the `arcanist` package
of its release. On the NAS, install both:

```bash
sudo apt install ./arcanist_<version>_amd64.deb ./openmediavault-arcanist_<version>_all.deb
```

Updates are the same command with the new files (or System → Plugins → upload). To list it in *System → Plugins*
for everyone, it would go to the community repository of OpenMediaVault's plugins (omv-extras, by its maintainers) or
in an apt repository of Arcanist's.

Tested on OpenMediaVault 7.7 and 8 (Debian 12 and 13, in a container with systemd): install and upgrade, settings,
shared folders (the service writes in the writable one and nowhere else), *Apply*, disabling, the status page, the
log and removal. Not tested on a real NAS yet.
