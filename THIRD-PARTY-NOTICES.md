# Third-party notices

Arcanist includes or depends on the following third-party components. Each is used under the license shown; full license texts are distributed with each package.

## Server (`src/Arcanist.Core`)

| Component | License |
|---|---|
| .NET / ASP.NET Core (including `Microsoft.Extensions.Hosting.WindowsServices`, `.Systemd` and `Microsoft.AspNetCore.Authentication.JwtBearer`) | MIT |
| Microsoft.IdentityModel (JSON Web Tokens) | MIT |
| Serilog (`Serilog.AspNetCore` and its sinks) | Apache-2.0 |
| Entity Framework Core, Microsoft.Data.Sqlite | MIT |
| SQLitePCLRaw | Apache-2.0 |
| SQLite | Public domain |
| Npgsql, Npgsql.EntityFrameworkCore.PostgreSQL | PostgreSQL License |
| GraphQL.NET (`GraphQL`, `GraphQL.SystemTextJson`, `GraphQL.Server.Transports.AspNetCore`) | MIT |
| Microsoft.AspNetCore.OpenApi | MIT |
| Scalar API reference (`Scalar.AspNetCore`, served at `/rest/docs`) | MIT |
| ATL.NET (`z440.atl.core`) | MIT |
| SkiaSharp (including `SkiaSharp.NativeAssets.Linux.NoDependencies`) | MIT |
| SharpCompress (XZ decompression of downloaded ffmpeg builds) | MIT |
| Skia | BSD-3-Clause |

ffmpeg is not shipped. When an administrator asks for it, the server downloads a static GPL build from
[BtbN/FFmpeg-Builds](https://github.com/BtbN/FFmpeg-Builds), whose releases link to the corresponding source code.

GraphiQL (MIT), React and ReactDOM (MIT) are loaded by the browser from jsDelivr at `/graphql/ui`; they are not bundled.

## Admin panel (`src/Arcanist.Web`, production dependencies)

| Component | License |
|---|---|
| Angular (`@angular/*`, `@angular/cdk`) | MIT |
| spartan-ng (`@spartan-ng/brain`; helm components copied into `libs/ui`) | MIT |
| Tailwind CSS (`tailwindcss`, `@tailwindcss/postcss`) | MIT |
| PostCSS | MIT |
| class-variance-authority | Apache-2.0 |
| clsx | MIT |
| tailwind-merge | MIT |
| RxJS | Apache-2.0 |
| Apollo Client, apollo-angular | MIT |
| GraphQL.js (`graphql`) | MIT |
| graphql-ws | MIT |
| ng-icons (`@ng-icons/core`) | MIT |
| Lucide icons (`@ng-icons/lucide`) | ISC |
| tslib | 0BSD |
| qrcode-generator (QR codes of P2P invitations) | MIT |

## Native audio library (`native/arcanist-audio`, shipped with the server)

| Component | License |
|---|---|
| Symphonia (`symphonia` and its `symphonia-*` format and codec crates) | MPL-2.0 |
| ebur128 | MIT |
| rusty-chromaprint (a Rust port of Chromaprint), rubato, realfft | MIT |
| RustFFT, num-complex, num-traits, num-integer, primal-check, strength_reduce, transpose | MIT OR Apache-2.0 |
| serde, serde_json, itoa, zmij, smallvec, bitflags, log, lazy_static, regex-lite, extended, dasp_frame, dasp_sample | MIT OR Apache-2.0 / MIT |
| memchr | Unlicense OR MIT |
| bytemuck | Zlib OR Apache-2.0 OR MIT |

Symphonia's source code is available at https://github.com/pdeljanov/Symphonia, as the MPL-2.0 requires.

## Native player (`native/arcanist-player`, shipped with the server for the server audio extension)

| Component | License |
|---|---|
| Symphonia (`symphonia` and its `symphonia-*` format and codec crates) | MPL-2.0 |
| cpal, and on each system its backend crates (`windows`, `alsa`/`alsa-sys`, `coreaudio-rs`) | Apache-2.0 / MIT OR Apache-2.0 / MIT |
| rubato, realfft | MIT |
| RustFFT, num-complex, num-traits, num-integer, primal-check, strength_reduce, transpose, dasp_sample, serde, serde_json | MIT OR Apache-2.0 |
| alsa-lib (`libasound.so.2` and ALSA's configuration, only in the Docker image and the Synology package, which builds it with SynoCommunity's `cross/alsa-lib`; elsewhere the system's) | LGPL-2.1-or-later |

alsa-lib is linked dynamically and can be replaced; its source code is available at https://github.com/alsa-project/alsa-lib, as the LGPL requires.

## Native P2P library (`native/arcanist-p2p`, shipped with the server and in the mobile bindings)

About 300 crates in all; `cargo metadata --manifest-path native/arcanist-p2p/Cargo.toml` lists each one with its
license. All of them are permissive or MPL-2.0:

| Component | License |
|---|---|
| iroh, iroh-base, iroh-relay, iroh-dns, iroh-tickets, noq (QUIC) and the other n0 crates | MIT OR Apache-2.0 |
| tokio, rustls, hyper, the RustCrypto crates and most other dependencies | MIT / Apache-2.0 / MIT OR Apache-2.0 |
| ring | Apache-2.0 AND ISC |
| uniffi and its crates (mobile bindings only), attohttpc (port mapping) | MPL-2.0 |
| webpki-roots (Mozilla's root certificates) | CDLA-Permissive-2.0 |
| ICU4X crates (`icu_*`, `zerovec`, `tinystr`…) | Unicode-3.0 |
| curve25519-dalek, ed25519-dalek, subtle, sha1_smol | BSD-3-Clause |
| rustls-webpki, untrusted | ISC |
| spez | BSD-2-Clause |
| foldhash | Zlib |
| blake3, constant_time_eq, ryu, tinyvec, num_enum | permissive alternatives including Apache-2.0 |

The source code of uniffi (https://github.com/mozilla/uniffi-rs) and attohttpc (https://github.com/sbstp/attohttpc) is available there, as the MPL-2.0 requires.

## Players that come with the server (`wwwroot/players`)

Webamp for Arcanist ([linkfast/arcanist-webamp](https://github.com/linkfast/arcanist-webamp), AGPL-3.0-or-later) is
downloaded from its releases when the server is published. Its folder carries its own `LICENSE.txt` and a
`THIRD-PARTY-NOTICES.txt` with the full license of everything in its bundle; the main ones:

| Component | License |
|---|---|
| Webamp, ani-cursor, winamp-eqf | MIT |
| React, ReactDOM, Redux, React Redux, reselect | MIT |
| Butterchurn and its presets (Milkdrop visualizer) | MIT |
| music-metadata (tags of local files dropped on the player) | MIT |
| JSZip (skins) | MIT or GPL-3.0-or-later, used under MIT |
| lucide-react | ISC |
| class-variance-authority | Apache-2.0 |
| clsx, tailwind-merge, Tailwind CSS (build only) | MIT |

## QNAP package (`packaging/qnap`)

QTS's C library is too old for .NET, so the `.qpkg` brings Debian 12's runtime libraries, unchanged (in `app/lib`,
their Debian `copyright` files in `app/licenses`):

| Component | License |
|---|---|
| GNU C Library (glibc: `ld-linux`, `libc`, `libm`, `libmvec`, `libpthread`, `libdl`, `librt`, `libresolv`, `libnss_*`) | LGPL-2.1-or-later (with GPL parts not shipped) |
| libstdc++, libgcc_s (GCC runtime) | GPL-3.0-or-later WITH GCC-exception-3.1 |
| ICU 72 (`libicuuc`, `libicui18n`, `libicudata`) | Unicode-3.0 (ICU license) |
| OpenSSL 3 (`libssl`, `libcrypto`) | Apache-2.0 |
| alsa-lib (`libasound.so.2` and ALSA's configuration) | LGPL-2.1-or-later |
| tzdata (time zones) | public domain |
| ca-certificates (Mozilla's CA bundle) | MPL-2.0 |

They are loaded dynamically and can be replaced; their source code is Debian 12's (https://sources.debian.org:
glibc, gcc-12, icu, openssl, alsa-lib), as the LGPL requires. The build tool, QNAP's QDK, is not distributed.

## OpenMediaVault plugin (`packaging/openmediavault`)

Written for Arcanist (AGPL-3.0-or-later); it uses OpenMediaVault's own APIs and includes no third-party code.

## Windows app (`src/Arcanist.Tray`)

| Component | License |
|---|---|
| Avalonia (`Avalonia`, `.Desktop`, `.Themes.Fluent` and their platform packages) | MIT |
| ANGLE (`Avalonia.Angle.Windows.Natives`) | BSD-3-Clause |
| SkiaSharp, HarfBuzzSharp | MIT |
| Skia | BSD-3-Clause |
| HarfBuzz | MIT ("Old MIT") |
| MicroCom.Runtime | MIT |
| Tmds.DBus.Protocol | MIT |
| Velopack (installer and updates) | MIT |
| System.ServiceProcess.ServiceController, System.Diagnostics.EventLog | MIT |

## Build tools (not distributed)

| Component | License |
|---|---|
| Velopack CLI (`vpk`, local dotnet tool) | MIT |
| Entity Framework Core tools (`dotnet-ef`) | MIT |
