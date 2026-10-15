# Third-party notices

## Desktop app

The desktop app is licensed under the root [MIT License](../LICENSE). It does not
vendor a sing-box executable. When requested, it downloads an official release
from [SagerNet/sing-box](https://github.com/SagerNet/sing-box), verifies the official
SHA-256 checksum file or, when no checksum file is published, the GitHub release
asset SHA-256 digest, and installs it into the chosen directory. Missing or
mismatched verification data stops installation.

sing-box is licensed under GNU GPL version 3 or later. Xray and other downloaded
components retain their upstream licenses. The desktop app also uses Avalonia,
SSH.NET, and SharpCompress under their respective upstream licenses:

- [Avalonia](https://github.com/AvaloniaUI/Avalonia): MIT.
- [SSH.NET](https://github.com/sshnet/SSH.NET): MIT.
- [SharpCompress](https://github.com/adamhathcock/sharpcompress): MIT.
- [Xray-core](https://github.com/XTLS/Xray-core): MPL-2.0.
- [sing-box](https://github.com/SagerNet/sing-box): GPL-3.0-or-later.

## Android app and bundled core

The Android APK links and bundles libbox. The Android app/libbox combination is
distributed under **GPL-3.0-or-later**. This scope includes the Android project and
its VPN-service integration; it does not change the desktop app's MIT license.
Retain upstream copyright notices and the GPL license when redistributing it.

The core is built from the unmodified official sing-box source pinned to:

- Version: `v1.14.3`.
- Commit: `7054cac5124465ffdb145870d7c01885566711b1`.
- Corresponding core source: [SagerNet/sing-box at the pinned commit](https://github.com/SagerNet/sing-box/tree/7054cac5124465ffdb145870d7c01885566711b1).
- Build entry point: `bash android/scripts/build-libbox.sh` from the repository root.
- Generated output: `android/app/libs/libbox.aar`; this binary is generated, not committed.
- Toolchain: Go 1.26.8, SagerNet gomobile v0.1.13, JDK 17, Android NDK 28.0.13004108.
- Native targets: `android/arm64` and `android/amd64` (APK ABIs `arm64-v8a` and `x86_64`).

The build script and Android project provide the instructions and integration
source for the distributed APK. The root repository's release tag supplies the
corresponding Android application source; the pinned upstream commit supplies the
core source. Further core dependencies and their licenses are recorded in that
upstream source tree and its module manifests. Preserve these references and
provide corresponding source when redistributing compiled Android packages.

Android uses platform APIs and AndroidX testing components under their upstream
licenses. Build-only Gradle, Kotlin, Go, and Android SDK/NDK tooling retain their
own licenses; their use does not replace the app/core distribution license.
