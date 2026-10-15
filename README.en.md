[简体中文](README.md) · [**English**](README.en.md)<br>
![S2N Connect: add VPN functionality to your VPS](docs/brand/s2n-banner.png)

# S2N Connect

**Add VPN functionality to your VPS.**

That is the meaning behind the S2N name. S2N Connect provides desktop and Android clients for your own VPS. The desktop app deploys an Xray **VLESS + REALITY** service and uses **sing-box TUN** for local routing. The Android app imports a desktop-exported profile and connects to an existing VPS. You supply the VPS.

[Download the beta](https://github.com/cavwic/s2n-connect/releases) · [Changelog](CHANGELOG.md) · [Report an issue](https://github.com/cavwic/s2n-connect/issues)

## Platforms

| Client | Architecture | Package |
| --- | --- | --- |
| Windows 10 / 11 | x64, ARM64 | `.zip` |
| macOS 13+ | Intel, Apple silicon | `.tar.gz` |
| Linux desktop, including Ubuntu | x64, ARM64 | `.tar.gz` |
| Android 8.0+ | ARM64, x86_64 | universal `.apk` |

Releases include six desktop targets and one universal Android APK. Compilation, packaging, and emulator UI tests do not establish that real VPS connections and TUN networking have been validated on every platform. See the beta limitations below.

**VPS:** This beta deploys to Ubuntu / Debian servers with systemd and requires SSH login as root.

## Desktop features

- **Deployment and installation:** Deploy local sing-box and the VPS service together or separately. S2N detects the operating system and architecture, installs into your chosen directory, checks the package's SHA-256 before extraction, validates the executable before saving its path, and removes temporary downloads.
- **SSH connections:** Configure the SSH user and port, select a private key from any local directory, and validate its format by content. Verify the server fingerprint on first use; later fingerprint changes are rejected. The key path is saved only after a successful connection, and key passphrases are never written to disk.
- **Existing installations:** Discover sing-box from saved settings, running processes, PATH, and common locations, then update the available controls.
- **Routing:** Process-name and domain-suffix rules work independently. Empty rule lists do not become unconditional proxy routes. Rule mode defaults to direct connections; global mode routes public Internet traffic through the VPS while keeping LAN, private addresses, and the VPS address direct.
- **DNS:** Resolve public domains with remote DoH through the VPS, while local names and reverse lookups use local DNS.
- **Status and controls:** Check the sing-box process before testing Google through the managed proxy. Connect and Stop controls follow actual process state. A failed exit-IP lookup does not invalidate a confirmed connection.
- **Desktop preferences:** Switch immediately between system, light, and dark themes and save the selection. Chinese systems use Chinese; other systems use English. Where system trays are supported, the tray menu provides Show, Connect, Stop, and Exit.
- **Startup and auto-connect:** Optionally start about 60 seconds after sign-in. Auto-connect requires one successful connection first and checks network, DNS, VPS-port, and local-installation readiness.

Installation prefers the official release's SHA-256 checksum file. When the release provides no such file, it uses the GitHub release asset's SHA-256 digest. Missing, invalid, or mismatched checksums stop installation; verification is never skipped.

## Before you start

- Supply an Ubuntu / Debian VPS, a root SSH private key, and a reachable SSH port. The server uses **TCP 443**; ensure it is available and allowed through both the cloud and operating-system firewalls.
- Start with a clean VPS where possible. Deployment installs Xray, writes its service configuration, and enables the service. Back up existing Xray configuration and check for conflicts with other services first.
- Your computer and VPS need access to the relevant download sources. Starting TUN requires administrator authorization; Linux uses `pkexec`, and macOS uses the system administrator prompt.
- Download the package matching your system and architecture, extract the entire archive, and run `S2N.exe` on Windows or `S2N` on macOS / Linux. Release packages include the .NET runtime.

## Quick start

1. Enter the VPS address, SSH user, and port, then select your SSH private key. For an encrypted key, enter its passphrase for this session.
2. Click **Test VPS** and verify the first-use SSH server fingerprint through your provider's console or another trusted channel.
3. Click **One-click deploy**, choose the local sing-box installation directory, and wait for completion. You can also use **Select and install** and **Deploy VPS only** separately.
4. Configure process names and domain suffixes in **Rules**, and enable global mode if needed. Click **Connect**, authorize administrator access, and wait for the connectivity check.
5. Click **Stop** to disconnect and **Exit** to end the application. Where a system tray is available, closing or minimizing the window may only hide it.

After the first successful connection, enable auto-connect and start-at-sign-in if needed. Auto-connect does not supply an encrypted SSH key's passphrase; routine connections use the deployed VPS profile.

## Android quick start

1. Deploy or configure the VPS in the desktop app, then export a mobile connection profile. The file contains connection credentials; transfer it privately to your own device.
2. Install the release's `android-universal.apk` and import the profile. Android connects to an existing VPS and does not perform SSH deployment.
3. Tap Connect and confirm Android's VPN authorization. Use Stop to disconnect. Android allows only one VPN app to control routing at a time.

The APK includes ARM64 and x86_64 native cores and is signed with the project's persistent release key. Emulator tests cover screens, VPN authorization, and real TUN start/stop using a direct-only fixture. They do not connect to a VPS or validate proxy connectivity.

## Build and validation

Development builds use the **.NET 10 SDK** and **Avalonia 12**, while the application targets `net8.0`. Release packages are self-contained, so end users do not need to install .NET separately.

```bash
dotnet restore
dotnet build -c Release
dotnet run --project tests/S2N.ConfigChecks/S2N.ConfigChecks.csproj -c Release
dotnet run --project tests/S2N.ReleaseChecks/S2N.ReleaseChecks.csproj -c Release
```

For example, publish Windows x64:

```bash
dotnet publish src/S2N.App/S2N.App.csproj \
  -c Release \
  -r win-x64 \
  --self-contained true \
  -o artifacts/win-x64
```

Configuration regression checks use isolated temporary storage to validate key routing, DNS, and settings constraints. They do not connect to a VPS or start an actual TUN interface. Other release targets are `win-arm64`, `osx-x64`, `osx-arm64`, `linux-x64`, and `linux-arm64`.

### Android build

`android/` is a native Kotlin project using JDK 17, Gradle 9.6, AGP 9.4, SDK 36, and NDK 28.0.13004108. Build libbox from pinned official sing-box source with Go 1.26.8, then build the APK:

```bash
bash android/scripts/build-libbox.sh
cd android
bash ./gradlew :app:lintDebug :app:testDebugUnitTest :app:assembleDebug
```

Distribution APKs require private signing configuration; see the [publishing guide](GITHUB_PUBLISHING.md). See [third-party notices](THIRD_PARTY_NOTICES.md) for the pinned core and license.

## Beta limitations and release integrity

- This is a beta. Real VPS connections, LAN access, and route/DNS restoration after stopping still need platform testing against the [release checklist](RELEASE_CHECKLIST.md). A successful build does not validate networking behavior.
- Windows elevation is the primary implementation. The macOS `osascript` and Linux `pkexec` paths need more testing on actual systems. Other VPS distributions are outside the current deployment scope, and IPv6 behavior needs separate validation.
- The Android APK is signed with the project release key. Desktop packages are unsigned, and macOS packages are not notarized, so operating systems may show trust warnings. SHA-256 and GitHub provenance do not replace platform signing; see the [signing notes](SIGNING.md).
- GitHub Actions publishes only after all six desktop packages, the Android APK, and Android emulator checks succeed and the packages pass SHA-256 verification. Beta tags are prereleases. CodeQL and artifact attestations depend on the repository's available GitHub features; workflow summaries record whether they ran or were skipped.
- Connectivity and IP lookups contact Google, Cloudflare, and IP information services. Generated profiles contain client credentials and must not be uploaded publicly. Application settings do not store private-key contents; see the [security policy](SECURITY.md).

## License and project documents

The desktop app is available under the [MIT License](LICENSE). The Android app combined with libbox is distributed under **GPL-3.0-or-later**; this does not change the desktop app's MIT license. sing-box, Xray, and other dependencies retain their respective upstream licenses; see the [third-party notices](THIRD_PARTY_NOTICES.md).

[Architecture](ARCHITECTURE.md) · [Contributing](CONTRIBUTING.md) · [GitHub publishing guide](GITHUB_PUBLISHING.md) · [Changelog](CHANGELOG.md)

<!-- Reserved promotion slots; hidden until content is ready.
<table width="100%">
  <tr>
    <th width="50%">VPS recommendations</th>
    <th width="50%">Personal promotion</th>
  </tr>
  <tr>
    <td><br><br><br></td>
    <td><br><br><br></td>
  </tr>
</table>
-->
