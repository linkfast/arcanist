# Rockstor Rock-on

`arcanist.json` is the [Rock-on](https://rockstor.com/docs/interface/docker-based-rock-ons/overview.html) that
installs the official image (`ghcr.io/linkfast/arcanist`, amd64 and arm64) from Rockstor's web UI. Rockstor 5.5.0 or
later (the container runs as the owner and group of the Data share, `"uid": -1, "gid": -1`).

- **Shares:** `/data` (database, keys, caches, logs, `setup-code.txt`; never a network share) and `/music`. More
  music shares can be added later with *Add Storage* (`volume_add_support`). Give the owner of the Data share read
  access to the music (write access to upload, organize or import) in each share's *Access control* tab.
- **Port:** 8484 by default; the Rock-on's button opens the admin panel (`/admin`).
- **Tag:** `preview` (the newest release, pre-releases included) while Arcanist has no stable release; then `latest`.
  A Rock-on cannot choose its tag at install time, so an edge build would be a second Rock-on.
- **Updating:** uninstall the Rock-on and install it again with the same shares (the image is pulled again; the data
  stays). Rockstor runs it with `--restart=unless-stopped`, so the server's own restarts (exit code 75) work.
- **DLNA** needs the host network, which a Rock-on only offers to every user of it (`["--net","host"]`): left out.
  **Server audio** would need `--device /dev/snd` and the audio group: left out too.

## Install it before it is in the registry

1. Enable the Rock-ons service (it needs a share for Rock-ons' root).
2. Copy the file to the NAS: `/opt/rockstor/rockons-metastore/arcanist.json`.
3. On the Rock-ons page press **Update**, then **Install** on Arcanist.

## Submit it to the registry

The registry is [rockstor/rockon-registry](https://github.com/rockstor/rockon-registry): open an issue, then a pull
request linked to it that adds `arcanist.json` and its line in `root.json` (`"arcanist": "arcanist.json"`, in
alphabetical order), and say in the pull request how it was tested. Check the format first with the registry's
validator (it needs a `root.json` next to the file):

```bash
docker run --rm -w /files -v "$PWD:/files" ghcr.io/rockstor/rockon-validator:main --diff arcanist.json
```

Tested: the validator accepts it, and the image runs as an arbitrary user (`--user 1000:100`, as Rockstor starts it)
with the data share owned by that user. Not tested on a real Rockstor yet.
