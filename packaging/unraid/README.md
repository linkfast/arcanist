# Unraid template

`templates/arcanist.xml` is the [Community Applications](https://docs.unraid.net/community-applications/) template
of the official image (`ghcr.io/linkfast/arcanist`), and `ca_profile.xml` the profile CA asks for.

- **User:** the container runs as `nobody:users` (`--user 99:100`, in *Extra Parameters*), Unraid's owner of shares
  and appdata: Unraid creates a missing appdata folder with that owner, so the server can write to it at once.
- **Paths:** `/data` (appdata: database, keys, caches, logs, `setup-code.txt`; on a local disk or cache pool, never a
  network share) and `/music`. More music shares can be added as more paths.
- **Tags:** `preview` by default (the newest release, pre-releases included) and `edge` as a branch. When Arcanist has
  a stable release: `latest` by default, `preview` as a branch, and drop `<Beta>`.
- Unraid passes `TZ` (the server's time zone) and `HOST_OS=Unraid` itself; Arcanist uses the latter to say how it is
  updated (*Check for updates* on the Docker page, then *Apply update*).
- **DLNA:** *Network Type: Host*. **Server audio:** add `--device /dev/snd --group-add <the audio group's id>` to
  *Extra Parameters* (Unraid may have no sound drivers loaded). Not in the template: an empty device stops the
  container from being created.

## Install it before it is in CA

Save the template on the flash drive and pick it in *Docker → Add Container → Template*:

```bash
wget -O /boot/config/plugins/dockerMan/templates-user/my-Arcanist.xml https://raw.githubusercontent.com/linkfast/unraid-templates/main/templates/arcanist.xml
```

## Publish it in Community Applications

CA lists templates from a public GitHub repository with `ca_profile.xml` at its root, so they go in their own
repository, `github.com/linkfast/unraid-templates` (with a `LICENSE`, `README.md`, this `ca_profile.xml` and
`templates/arcanist.xml`; the template's `TemplateURL` points there). Then submit it at
[ca.unraid.net/submit](https://ca.unraid.net/submit): sign in, give the repository, *Validate and Scan*, fix what it
reports and send it for review. Unraid expects a support thread in its forums: once there is one, it goes in
`<Support>` (GitHub stays as `<Project>`) and in the profile's `<Forum>`.

Not tested on a real Unraid yet; the image itself was tested running as an arbitrary user.
