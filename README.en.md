[简体中文](README.md) · [**English**](README.en.md)

![S2N Connect: add VPN functionality to your VPS](docs/brand/s2n-banner.png)

# S2N Connect

**A dedicated connection, under your control.**

Service closures, peak-hour congestion, traffic limits, and privacy concerns can make shared proxy services difficult to rely on. Build a dedicated VPN on your own VPS, choose your server, bandwidth, and traffic plan, and reduce reliance on third-party proxy operators.

| Platform | Direct download |
| --- | --- |
| Windows | [Most PCs (x64)](https://github.com/cavwic/s2n-connect/releases/download/v0.3.0-beta.1/S2N-v0.3.0-beta.1-win-x64.zip) · [ARM64](https://github.com/cavwic/s2n-connect/releases/download/v0.3.0-beta.1/S2N-v0.3.0-beta.1-win-arm64.zip) |
| macOS | [Apple silicon (M series)](https://github.com/cavwic/s2n-connect/releases/download/v0.3.0-beta.1/S2N-v0.3.0-beta.1-osx-arm64.tar.gz) · [Intel](https://github.com/cavwic/s2n-connect/releases/download/v0.3.0-beta.1/S2N-v0.3.0-beta.1-osx-x64.tar.gz) |
| Linux | [x64](https://github.com/cavwic/s2n-connect/releases/download/v0.3.0-beta.1/S2N-v0.3.0-beta.1-linux-x64.tar.gz) · [ARM64](https://github.com/cavwic/s2n-connect/releases/download/v0.3.0-beta.1/S2N-v0.3.0-beta.1-linux-arm64.tar.gz) |
| Android 8.0+ | [Download APK (ARM64 / x86_64)](https://github.com/cavwic/s2n-connect/releases/download/v0.3.0-beta.1/S2N-v0.3.0-beta.1-android-universal.apk) |
| iOS | [Hiddify (third-party client)](https://apps.apple.com/us/app/hiddify-proxy-vpn/id6596777532) · [Connection guide](docs/ios.md#ios-connection-guide) |

## Overview

S2N Connect deploys a VPN service on your own VPS, provides desktop and Android clients, and supports connections through third-party iOS clients.

## Key features

- Deploy the VPS VPN service in one step.
- Use rule-based routing or global mode.
- Add mobile connections.

## Usage

**Prepare:** An Ubuntu / Debian VPS, a root SSH private key, and TCP 443 allowed through the server and cloud firewalls. Back up existing service configurations before deployment.

**Desktop:**

1. Download your system and architecture's package from the table above, extract it fully, and run `S2N.exe` (Windows) or `S2N` (macOS / Linux).
2. Enter the VPS address, SSH port, and root user, then select your private key. Click **Test VPS** and verify the server fingerprint.
3. Click **One-click deploy**, choose a local installation directory, and wait for completion.
4. Set **Rules** or global mode as needed. Click **Connect** and grant administrator access; click **Stop** to disconnect.

**Android (8.0+, ARM64 / x86_64):**

1. Click **Export mobile profile** in the desktop app and transfer the file to your phone.
2. Download and install the APK from the table above, then tap **Choose profile file** to import it.
3. Tap **Connect VPN** and grant VPN permission. Use **Verify proxy access** to check connectivity and **Stop VPN** to disconnect.

**iOS:**

Install [Hiddify](https://apps.apple.com/us/app/hiddify-proxy-vpn/id6596777532) or another VLESS + REALITY client. Follow the [iOS connection guide](docs/ios.md#ios-connection-guide) to import your connection link, allow VPN configuration, and connect.

Profile files contain connection credentials. Do not share them publicly.

[Report an issue](https://github.com/cavwic/s2n-connect/issues) · [Licenses and third-party notices](THIRD_PARTY_NOTICES.md) · [Signing notes](SIGNING.md)

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
