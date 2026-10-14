# Changelog

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
