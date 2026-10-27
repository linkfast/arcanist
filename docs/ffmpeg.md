# ffmpeg in Arcanist

Arcanist uses [ffmpeg](https://ffmpeg.org) to convert audio for players (Opus, MP3, AAC, FLAC), to decode the formats
its own decoder does not read, and to make music videos. It is optional: without it, players get the original
files.

## Which ffmpeg it uses

In this order:

1. `Arcanist:Ffmpeg:Path` (in `arcanist.json`, or the `Arcanist__Ffmpeg__Path` environment variable).
2. The path set in the setup wizard or in the panel (**Transcoding**).
3. The one the panel downloaded (`<data folder>/tools/ffmpeg`): a GPL build from
   [BtbN/FFmpeg-Builds](https://github.com/BtbN/FFmpeg-Builds), checked against its SHA-256.
4. Synology's ffmpeg packages, then the `PATH`.

The **Transcoding** page shows the ffmpeg in use and the formats it can convert to. The API gives them in
`serverInfo.transcodingFormats`.

## HE-AAC

HE-AAC (AAC with spectral band replication) keeps music listenable at 48–96 kbit/s, which helps on mobile data.
Arcanist offers it as the `HE_AAC` format (24 to 96 kbit/s, 64 by default) **only when its ffmpeg can encode it**:

- **`libfdk_aac`**, the Fraunhofer encoder. Its license is not compatible with the GPL, so no public ffmpeg build
  includes it. That covers the one Arcanist downloads, distribution packages and Docker images. You have to build it
  yourself, as described below.
- **`aac_at`**, Apple's AudioToolbox encoder, which macOS builds of ffmpeg have (Homebrew's does).

ffmpeg's own `aac` encoder makes AAC-LC only. When a client asks for `HE_AAC` and the ffmpeg in use cannot encode it,
Arcanist sends AAC-LC instead. This happens, for example, with a default quality chosen while another ffmpeg was
installed.

To check an ffmpeg:

```sh
ffmpeg -hide_banner -encoders | grep -E "libfdk_aac|aac_at"
```

On Windows, use `findstr` instead of `grep`. After switching ffmpeg, the **Transcoding** page lists HE-AAC among the
formats. You can then choose it as the default quality of the Ultimate apps (it suits **Mobile apps**).

> **License.** An ffmpeg built with `--enable-nonfree` (libfdk_aac) may be used, but **not redistributed**: do not
> share the binary, put it in an image you publish, or ship it with anything. Arcanist never downloads or bundles one.

### Building it with BtbN's scripts (Linux, Windows; recommended)

The scripts that make the builds Arcanist downloads can also make a **nonfree** variant, with libfdk_aac. They run in
Docker, on Linux or in WSL 2 on Windows. The result is a static build, so it needs nothing else on the server. See the
[repository's README](https://github.com/BtbN/FFmpeg-Builds) for the current targets and variants.

```sh
git clone https://github.com/BtbN/FFmpeg-Builds.git
cd FFmpeg-Builds
./makeimage.sh linux64 nonfree      # or win64, linuxarm64
./build.sh linux64 nonfree          # leaves an archive in artifacts/
```

Extract the archive somewhere Arcanist can read, then point Arcanist at its `bin/ffmpeg` (`bin\ffmpeg.exe` on Windows):

- **In the panel:** **Transcoding → ffmpeg path**, with the full path. This needs an administrator and asks for the
  password again.
- **By configuration:** `"Arcanist": { "Ffmpeg": { "Path": "/opt/ffmpeg-nonfree/bin/ffmpeg" } }` in `arcanist.json`,
  or `Arcanist__Ffmpeg__Path=/opt/ffmpeg-nonfree/bin/ffmpeg`.

ffprobe is looked for next to ffmpeg, and the build includes it.

### Building it from source (Debian, Ubuntu)

The `libfdk-aac-dev` package is in Debian's `non-free` component and Ubuntu's `multiverse`:

```sh
sudo apt install build-essential nasm pkg-config libfdk-aac-dev libmp3lame-dev libopus-dev libx264-dev libx265-dev
curl -LO https://ffmpeg.org/releases/ffmpeg-7.1.tar.xz && tar xf ffmpeg-7.1.tar.xz && cd ffmpeg-7.1
./configure --prefix=/opt/ffmpeg-nonfree --enable-gpl --enable-nonfree --enable-libfdk-aac \
    --enable-libmp3lame --enable-libopus --enable-libx264 --enable-libx265
make -j"$(nproc)" && sudo make install
```

Then set the path to `/opt/ffmpeg-nonfree/bin/ffmpeg`, as above. libmp3lame and libopus keep MP3 and Opus available,
and libx264 and libx265 are for music videos.

### Where you run Arcanist

- **Windows** (tray or service): build `win64`, extract it, for example to `C:\Tools\ffmpeg-nonfree`, and set the path
  in the panel. In service mode, the service account (`NT SERVICE\Arcanist`) must be able to read that folder.
  `C:\Tools` usually lets it; `%UserProfile%` does not.
- **Linux, `.deb`, systemd:** put the build in `/opt` and set the path. The unit's `DynamicUser` can read `/opt`, but
  not home folders.
- **Docker, TrueNAS, Unraid, Rockstor:** extract the `linux64` (or `linuxarm64`) build on the host and mount it into
  the container read-only. Then set the path to the mounted file:

  ```yaml
  services:
    arcanist:
      volumes:
        - /opt/ffmpeg-nonfree:/opt/ffmpeg:ro
      environment:
        Arcanist__Ffmpeg__Path: /opt/ffmpeg/bin/ffmpeg
  ```

- **Synology:** copy the `linux64` (or `linuxarm64`) build to a shared folder that `sc-arcanist` can read. Set the path
  in the panel, or in the package window's test mode as `Arcanist:Ffmpeg:Path`.
- **QNAP:** QTS's C library is too old for BtbN's builds, so the package runs them through its own loader
  (`bin/ffmpeg`, which runs the ffmpeg in `<volume>/.arcanist/tools/ffmpeg`). Build `linux64` (or `linuxarm64`) and
  copy its `bin/ffmpeg` and `bin/ffprobe` over the files in that folder. Downloading ffmpeg again from the panel
  replaces them.
- **OpenMediaVault:** as on Linux.
- **macOS:** Homebrew's ffmpeg already has `aac_at` (`brew install ffmpeg`). Set the path to
  `/opt/homebrew/bin/ffmpeg` (or `/usr/local/bin/ffmpeg` on Intel Macs) if Arcanist does not find it.

To go back to Arcanist's own download, empty the path in the panel (**Automatic**).
