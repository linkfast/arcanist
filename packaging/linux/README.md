# Arcanist on Linux

## Ubuntu and Debian: the package

For **Ubuntu 24.04 LTS and later** and **Debian 12 and later**, on amd64 or arm64. Download the `.deb` of a
[release](https://github.com/linkfast/arcanist/releases) and install it with `apt`, which also brings what it needs
(and ffmpeg, recommended, to convert audio):

```sh
sudo apt install ./arcanist_<version>_amd64.deb
```

That is all: Arcanist runs as the systemd service `arcanist`, starts with the system, and listens on port 8484.
Open `http://<server>:8484/` and follow the setup wizard; from another machine it asks for the setup code:

```sh
sudo cat /var/lib/arcanist/setup-code.txt
```

- **Update:** install the new `.deb` the same way; the service restarts with the new version and the database is
  migrated. **Remove:** `sudo apt remove arcanist` (`purge` too) never deletes the data in `/var/lib/arcanist`.
- **Status and logs:** `systemctl status arcanist`, `journalctl -u arcanist -f`.
- The server is in `/opt/arcanist` and runs as a user systemd creates for it; everything below about the data,
  music folders, server audio and settings applies (change the service with `sudo systemctl edit arcanist`).
- **Build it yourself** from a checkout, with Docker as the only requirement (PowerShell, also on Windows):
  `pwsh packaging/linux/build.ps1` (`-Arch arm64` or `all`) leaves `artifacts/linux/arcanist_<version>-1_<arch>.deb`
  and the tarballs. With the tools installed instead: `packaging/linux/pack.sh`, then `packaging/linux/deb.sh`.

## Tarballs

Release tarballs (`arcanist-<version>-linux-x64.tar.gz`, `…-linux-arm64.tar.gz`) are self-contained: no .NET install
is needed, only the ICU library most distributions already have (`libicu`).

The `…-dotnet.tar.gz` ones are smaller, for systems with the ASP.NET Core 10 runtime installed (the
`aspnetcore-runtime-10.0` package of your distribution, or Microsoft's): run them with `dotnet arcanist.dll`, and in
the systemd unit set `ExecStart=/usr/bin/dotnet /opt/arcanist/arcanist.dll`. The Synology package uses them.

### Install as a systemd service

```sh
sudo mkdir -p /opt/arcanist
sudo tar -xzf arcanist-<version>-linux-x64.tar.gz -C /opt/arcanist --strip-components=1
sudo cp /opt/arcanist/arcanist.service /etc/systemd/system/
sudo systemctl enable --now arcanist
```

Open `http://<host>:8484/` and follow the setup wizard. From another machine it asks for the setup code, which is in
`journalctl -u arcanist` and in `/var/lib/arcanist/setup-code.txt`.

- **Data** (settings, SQLite database, keys, caches, logs) is in `/var/lib/arcanist`, owned by a user systemd creates
  for the service (`DynamicUser=yes`). Back up that folder. Never put it on a network share when using SQLite.
- **Music folders** must be readable by that user: readable by everyone, or by a group you add with
  `sudo systemctl edit arcanist`:
  ```ini
  [Service]
  SupplementaryGroups=music
  ```
- **Server audio** (the extension that plays through a sound card of the server) needs the sound devices, which the
  unit hides. To use it, in `sudo systemctl edit arcanist`:
  ```ini
  [Service]
  PrivateDevices=no
  SupplementaryGroups=audio
  ```
  It plays through ALSA directly: on a desktop machine, a sound server (PipeWire, PulseAudio) may hold the card.
- **Port and settings:** `/var/lib/arcanist/arcanist.json` (written by the setup wizard), or environment variables
  in `systemctl edit arcanist`, e.g. `Environment=ASPNETCORE_URLS=http://*:9000`.

### Update

Stop the service, replace `/opt/arcanist` with the new tarball's contents and start it again. The data directory is
untouched and the database is migrated on start.

```sh
sudo systemctl stop arcanist
sudo rm -rf /opt/arcanist && sudo mkdir /opt/arcanist
sudo tar -xzf arcanist-<new version>-linux-x64.tar.gz -C /opt/arcanist --strip-components=1
sudo systemctl start arcanist
```

### Run without installing

```sh
./arcanist --data-dir ./data
```

Without `--data-dir` it uses `$XDG_DATA_HOME/arcanist` (`/var/lib/arcanist` as root).
