[简体中文](README.md) · [**English**](README.en.md)

![S2N Connect: add VPN functionality to your VPS](docs/brand/s2n-banner.png)

# S2N Connect · Beta

**v0.3.0-beta.1** · [Report an issue](https://github.com/cavwic/s2n-connect/issues)

| Platform | Direct download |
| --- | --- |
| Windows | [Most PCs (x64)](https://github.com/cavwic/s2n-connect/releases/download/v0.3.0-beta.1/S2N-v0.3.0-beta.1-win-x64.zip) · [ARM64](https://github.com/cavwic/s2n-connect/releases/download/v0.3.0-beta.1/S2N-v0.3.0-beta.1-win-arm64.zip) |
| macOS | [Apple silicon (M series)](https://github.com/cavwic/s2n-connect/releases/download/v0.3.0-beta.1/S2N-v0.3.0-beta.1-osx-arm64.tar.gz) · [Intel](https://github.com/cavwic/s2n-connect/releases/download/v0.3.0-beta.1/S2N-v0.3.0-beta.1-osx-x64.tar.gz) |
| Linux | [x64](https://github.com/cavwic/s2n-connect/releases/download/v0.3.0-beta.1/S2N-v0.3.0-beta.1-linux-x64.tar.gz) · [ARM64](https://github.com/cavwic/s2n-connect/releases/download/v0.3.0-beta.1/S2N-v0.3.0-beta.1-linux-arm64.tar.gz) |
| Android 8.0+ | [Download APK (ARM64 / x86_64)](https://github.com/cavwic/s2n-connect/releases/download/v0.3.0-beta.1/S2N-v0.3.0-beta.1-android-universal.apk) |

## What is it?

A tool that adds VPN functionality to your own VPS, with clients for Windows, macOS, Linux, and Android. You supply the VPS.

## What can it do?

- Deploy the VPS service and connect from the desktop app.
- Route traffic by rules or use global mode.
- Export a mobile profile and import it on Android to connect.

## How to use it

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

Profile files contain connection credentials. Do not share them publicly.

[Licenses and third-party notices](THIRD_PARTY_NOTICES.md) · [Signing notes](SIGNING.md)

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
