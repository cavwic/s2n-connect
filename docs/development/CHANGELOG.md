# Changelog

## 0.4.0-beta.2

### Fixed

- Clarify server SSH management ports, local HTTP proxy ports, and VPN traffic on UDP 443 in both languages.
- Report SSH connection failures with the selected route and port, distinguish network and authentication failures, and validate custom management ports.
- Document self-contained Windows startup and connecting to an existing server by importing a profile without SSH.

## 0.4.0-beta.1

### Added

- Hysteria 2 over UDP 443 for desktop deployment, existing connection imports, Android profiles, and portable iOS client configurations. Existing VLESS + REALITY profiles remain compatible.
- Fingerprint-verified SSH deployment with an optional local HTTP proxy, a checksum-pinned Hysteria server binary, and explicit certificate trust without disabling TLS validation.
- Isolated real QUIC tests for valid connections, rejected certificates and credentials, plus real Windows TUN creation and cancellation checks.

### Fixed

- Allocate a fresh Windows TUN identity before the first launch to avoid stale adapter name/GUID collisions.
- Verify core ownership by process identity and configuration, preserve unrelated VPNs, and wait for actual adapter/listener readiness.
- Verify UDP connections using real traffic instead of a TCP port probe; preserve existing server services and credentials during deployment.
- Resume an incomplete release draft without replacing published assets.

## 0.3.0-beta.2

### Changed

- Group technical documentation under `docs/`, community policies under `.github/`, and build helpers under `scripts/` to shorten the repository file list.
- Preserve the documentation directory structure in desktop packages, including the banner, iOS guide, notices, and full license texts.
- Resolve the repository location from the build scripts so they work when invoked from another directory.
- Reduce CI emulator load and recover only the identified Pixel Launcher system dialog while retaining VPN authorization and TUN lifecycle checks.

## 0.3.0-beta.1

### Added

- Native Android 8.0+ client with a universal ARM64/x86_64 APK, profile import, and Android VPN authorization. Android connects to an existing VPS; SSH deployment remains a desktop feature.
- Desktop export of mobile connection profiles. Exported files contain connection credentials and must be transferred privately.
- Pinned-source libbox builds, Android lint and unit checks, and emulator UI/authorization smoke tests without a real VPS connection.
- Persistent release-key signing for distribution APKs, with SHA-256 files and provenance attestations where supported.

### Changed

- Release publication waits for all six desktop packages, the Android APK, and Android smoke tests; the complete release has 15 assets including checksums.
- Public repositories run initial CodeQL analysis for both C# and Java/Kotlin.
- The empty promotion slots remain in README source comments and are hidden from rendered project pages.
- Android combined with libbox is distributed under GPL-3.0-or-later; desktop licensing remains MIT.

## 0.2.2-alpha.2

### Added

- Chinese and English project guides with a language selector at the top of each README.
- A new application icon and README title banner expressing the product's purpose: adding VPN functionality to a personal VPS.
- Empty, clearly separated spaces for future VPS recommendations and personal promotion in both languages.

### Fixed

- Official sing-box releases without a separate checksum file can use the package's official GitHub SHA-256 digest. Missing or invalid verification data and hash mismatches still refuse installation; a published checksum file takes priority without a silent fallback on failure.

## 0.2.2-alpha.1

### Added

- Chinese UI on Chinese systems and English UI on all other systems.
- System, light, and dark theme modes with immediate switching and persisted settings.
- Global routing mode for all public Internet traffic while keeping LAN/private networks and the VPS address direct.
- Cross-platform start-at-sign-in registration with an approximately 60-second delay.
- Auto-connect, unlocked only after one verified successful connection, with network, DNS, VPS-port, and sing-box readiness checks.

### Changed

- Main-window spacing and card padding are more compact; the default window is sized to show the full workflow on common desktop displays while retaining scrolling on smaller screens.
- GitHub Actions use .NET 10 SDK and Node 24-based checkout/setup actions while the application remains a self-contained `net8.0` target.
- Added public parameterless window constructors for Avalonia runtime XAML loading.

### Fixed

- Fixed compilation against current SharpCompress and Avalonia APIs, including the ambiguous cryptographic exception and asynchronous UI dispatch.
- Upgraded SSH.NET to 2026.0.0 to resolve the reported high-severity dependency advisories.
- Windows can discover and stop its elevated sing-box process. Stop failures keep the application open and prevent automatic reconnection until the process is stopped.
- Empty process or domain rule lists no longer produce unconditional proxy routes.
- Health checks explicitly use the managed proxy, and connection buttons refresh before external probes complete.
- Exit explicitly hides and disposes the tray icon to prevent a stale Windows notification-area icon.
- Release publication waits for all six platform packages, verifies every checksum, and labels alpha tags as prereleases. CodeQL and provenance availability are reported explicitly for private repositories.

## 0.2.1-alpha.1

### Changed

- Connection-state evaluation now checks whether the configured sing-box process is running before any external network probe.
- When sing-box is not running, the UI immediately shows `未连接` and does not contact Google.
- When sing-box is running, Google connectivity is tested through `127.0.0.1:2080` with a 10-second request timeout instead of the previous 1-second timeout.
- A failed or slow Google probe no longer disables the Stop button. Connect and Stop buttons are controlled only by the actual sing-box process state.
- When sing-box is running but the Google test fails, the UI shows `未连接（sing-box 已启动，外网检测失败）`, while Stop remains available.
- Overlapping five-second status refreshes are suppressed to prevent multiple slow probes from accumulating.
- Exit-IP lookup is best-effort and no longer changes a confirmed connected state to disconnected.

## 0.2.0-alpha

- Initial cross-platform alpha source package.
