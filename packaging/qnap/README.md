# QNAP package

`Arcanist_<version>_<x86_64|arm_64>.qpkg`, for QTS and QuTS hero 5.0 and later, built with QNAP's
[QDK](https://github.com/qnap-dev/QDK). Install it in **App Center → Install Manually**: it is not signed by QNAP,
so App Center asks to confirm it (or allow unsigned apps in App Center's settings).

- **The C library:** QTS and QuTS hero still have glibc 2.21, and Arcanist (.NET 10) needs 2.27. Each package
  brings Debian 12's (2.36) with its loader and the libraries the server loads (libstdc++, ICU 72, OpenSSL 3,
  ALSA), plus time zones and CA certificates (about 40 MB). `bin/arcanist-run` starts the server through that loader
  (`app/ld-linux-*`, next to the server: under an explicit loader `/proc/self/exe` is the loader, and .NET finds the
  application in its folder) with `--library-path`, which, unlike `LD_LIBRARY_PATH`, is not passed on to the
  programs it starts.
- **ffmpeg:** Arcanist downloads it as everywhere (setup wizard or Settings), BtbN's static build, which needs a newer
  C library too: `bin/ffmpeg` (Arcanist's ffmpeg path, `Arcanist__Ffmpeg__Path`) runs it through the same loader, or a
  fully static ffmpeg in `/opt/ffmpeg` (the community's ffmpeg package) as it is. Other programs Arcanist downloads
  for the same C library (Ultimate Cloud's `ultimate-audio`, the YouTube extension's tools) would not run.
- **Data:** in `<volume>/.arcanist` (for example `/share/CACHEDEV1_DATA/.arcanist`), outside the package's folder:
  App Center deletes that folder when it removes the package, never the data. `package.conf` there has the
  port (`PORT=8484`); `package.log` is the package's output (cut to 2 MB past 10 MB).
- **User:** App Center runs it as root (QTS has no service users): it reads every shared folder (`/share/<name>`).
- **Supervision:** `bin/arcanist-run` starts the server again when it asks to (exit code 75: a restart from the
  panel, a factory reset, a restore; more than five times in a minute and it stops) and after a crash (waiting
  longer each time, five at most). `Arcanist.sh` is the service script App Center, boot and shutdown call.
- **Its page:** App Center's *Open* shows `ui/index.cgi` on QTS's own web server, only to QTS administrators (their
  session is checked with QTS's `authLogin.cgi`): state, version, port, the setup code until setup is done, the end
  of the log, and the button that opens Arcanist (`http://<NAS>:8484/`). The setup code also goes to QTS's system log
  when it is installed.
- **Version:** QDK takes at most 10 characters, so pre-releases are shortened: `0.1.0-alpha.6` is `0.1.0a6`.

## Build

`pwsh packaging/qnap/build.ps1 [-Arch x64|arm64|all]` builds everything with Docker. From the Linux builds
(`artifacts/linux/arcanist-<version>-linux-<arch>` or its `.tar.gz`):

```bash
docker build -t arcanist-qnap-builder -f packaging/qnap/builder.Dockerfile packaging/qnap
docker run --rm -v "$PWD:/src" -w /src arcanist-qnap-builder sh packaging/qnap/qpkg.sh <version>
```

The release attaches both packages. Signing them takes a certificate from QNAP (its developer program); a
repository for App Center (*Settings → App Repository*, an XML list) would give updates from App Center.

Tested: the package built from 0.1.0-alpha.6, extracted and run with busybox's `sh` in CentOS 7 (glibc 2.17, older
than QTS's): the server starts, its setup wizard answers, it downloads ffmpeg over HTTPS and runs it through the
wrapper, and the supervisor restarts it after a crash. Not tested on a real QNAP yet (App Center, `authLogin.cgi`,
QTS's web server).
