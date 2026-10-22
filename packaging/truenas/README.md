# TrueNAS SCALE app

For TrueNAS SCALE 24.10 (Electric Eel) or later, whose apps are Docker Compose. Older SCALE (k3s and Helm charts) and
TrueNAS CORE are not supported.

- `arcanist/` is the app for TrueNAS's community catalog, as it goes in
  [truenas/apps](https://github.com/truenas/apps) under `ix-dev/community/arcanist/`: the official image pinned by digest,
  running as the `apps` user (568), with a data storage (`/data`), a music storage (`/music`, read-only optional), more
  storages, the port (`Urls`, 31117 by default, unique in the catalog), the time zone and the sound card for server
  audio. It sets `ARCANIST_INSTALL_KIND=TRUE_NAS`, so the panel says how it is updated.
- `install-via-yaml.yaml` installs it today, before it is in the catalog: *Apps → Discover Apps → ⋮ → Install via YAML*.

## Install via YAML

1. Create the datasets first, or Docker creates the folders owned by root: one for the data (preset *Apps*, owner
   `apps`, on a local pool, never an SMB/NFS share) and, if the music is not in one yet, one for the music. Give the
   `apps` group read access to the music in *Edit ACL* (*Modify* to let Arcanist take uploads, organize and import),
   applied recursively, and access to traverse the datasets above it.
2. Paste `install-via-yaml.yaml` with your paths and time zone, name the app `arcanist` and save.
3. Open `http://<nas>:8484/` and follow the setup wizard; the setup code is in the app's logs (*Apps → Installed →
   arcanist → Workloads → logs*) and in `setup-code.txt` in the data dataset.

To update, pull the image again (the app's *Update* when the tag is `preview`) or change the tag.

## Submit it to the catalog

In a clone of `truenas/apps` (Linux or WSL, with Docker): copy `arcanist/` to `ix-dev/community/arcanist/`, then

```bash
devbox run copy-lib                 # copies the library into templates/library and fills lib_version_hash
./.github/scripts/port_validation.py
./.github/scripts/ci.py --app arcanist --train community --test-file basic-values.yaml
./.github/scripts/generate_metadata.py --app arcanist --train community
```

and open a pull request with the template for new apps (an issue first; the template asks to say that it was written
with an LLM). The icon and screenshot URLs in `app.yaml` are placeholders: the reviewers upload the images (attach them
to the pull request) and give the real ones. The image is pinned as `<version>@sha256:<digest>`
(`docker buildx imagetools inspect ghcr.io/linkfast/arcanist:<version>`) and their Renovate bot updates it; Arcanist's
pre-release versions need a versioning rule for `ghcr.io/linkfast/arcanist` in their `renovate-config.js`, to agree with
the reviewers.

Tested: the library's renderer (`ghcr.io/truenas/apps_validation`) renders both test values, and the rendered app
deploys (permissions container, then Arcanist healthy as 568 with the music read-only). Not tested on a real TrueNAS yet.
