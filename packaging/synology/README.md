# Synology package

`spksrc/` holds the [spksrc](https://github.com/SynoCommunity/spksrc) definitions to submit to SynoCommunity:
`cross/arcanist` downloads the release tarballs (self-contained, x64 and arm64) and `spk/arcanist` packages them for
DSM 7.2 and later.

- Runs as the internal user `sc-arcanist`. Its data (settings, SQLite database, keys, caches, logs) is in
  `/var/packages/arcanist/var`, on the NAS's own volume, never on a share.
- The install wizard asks for the music shared folder (created if missing and made accessible to `sc-arcanist`) and
  shows the setup code that Arcanist's own setup wizard asks for.
- Port 8484, registered with the DSM firewall and as a desktop shortcut.
- Updates come from Package Center; Arcanist has no updater of its own on DSM.

## Building

In a checkout of spksrc (normally its Docker build environment):

```sh
cp -R <arcanist>/packaging/synology/spksrc/cross/arcanist cross/
cp -R <arcanist>/packaging/synology/spksrc/spk/arcanist spk/
make -C cross/arcanist digests       # once per version, after the GitHub release exists
make -C spk/arcanist arch-x64-7.2 arch-armv8-7.2
```

For a new version, set `PKG_VERS` in `cross/arcanist/Makefile` and `SPK_VERS` in `spk/arcanist/Makefile` (reset
`SPK_REV` to 1), regenerate the digests and update `CHANGELOG`.
