# Arcanist on Linux

Release tarballs (`arcanist-<version>-linux-x64.tar.gz`, `…-linux-arm64.tar.gz`) are self-contained: no .NET install
is needed, only the ICU library most distributions already have (`libicu`).

## Install as a systemd service

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
- **Port and settings:** `/var/lib/arcanist/arcanist.json` (written by the setup wizard), or environment variables
  in `systemctl edit arcanist`, e.g. `Environment=ASPNETCORE_URLS=http://*:9000`.

## Update

Stop the service, replace `/opt/arcanist` with the new tarball's contents and start it again. The data directory is
untouched and the database is migrated on start.

```sh
sudo systemctl stop arcanist
sudo rm -rf /opt/arcanist && sudo mkdir /opt/arcanist
sudo tar -xzf arcanist-<new version>-linux-x64.tar.gz -C /opt/arcanist --strip-components=1
sudo systemctl start arcanist
```

## Run without installing

```sh
./arcanist --data-dir ./data
```

Without `--data-dir` it uses `$XDG_DATA_HOME/arcanist` (`/var/lib/arcanist` as root).
