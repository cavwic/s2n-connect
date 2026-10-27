[简体中文](#user-content-s2n-chinese) · [English](#user-content-s2n-english)

![S2N Connect · VPS → VPN](docs/brand/s2n-banner.png)

# S2N Connect

<details name="s2n-language" open>
<summary>简体中文</summary>

<a name="s2n-chinese"></a>

**专属节点，自主管理。**

面对第三方代理服务可能出现的运营中断、高峰拥堵、流量限制与隐私顾虑，通过自有 VPS 构建专属 VPN，自主选择服务器、带宽与流量方案，减少对第三方代理运营方的信任依赖。

| 系统 | 直接下载 |
| --- | --- |
| Windows | [常见电脑（x64）](https://github.com/cavwic/s2n-connect/releases/download/v0.4.0-beta.1/S2N-v0.4.0-beta.1-win-x64.zip) · [ARM64](https://github.com/cavwic/s2n-connect/releases/download/v0.4.0-beta.1/S2N-v0.4.0-beta.1-win-arm64.zip) |
| macOS | [Apple 芯片（M 系列）](https://github.com/cavwic/s2n-connect/releases/download/v0.4.0-beta.1/S2N-v0.4.0-beta.1-osx-arm64.tar.gz) · [Intel 芯片](https://github.com/cavwic/s2n-connect/releases/download/v0.4.0-beta.1/S2N-v0.4.0-beta.1-osx-x64.tar.gz) |
| Linux | [x64](https://github.com/cavwic/s2n-connect/releases/download/v0.4.0-beta.1/S2N-v0.4.0-beta.1-linux-x64.tar.gz) · [ARM64](https://github.com/cavwic/s2n-connect/releases/download/v0.4.0-beta.1/S2N-v0.4.0-beta.1-linux-arm64.tar.gz) |
| 安卓 8.0+ | [下载 APK（ARM64 / x86_64）](https://github.com/cavwic/s2n-connect/releases/download/v0.4.0-beta.1/S2N-v0.4.0-beta.1-android-universal.apk) |
| iOS | [Hiddify（第三方客户端）](https://apps.apple.com/us/app/hiddify-proxy-vpn/id6596777532) · [连接指南](docs/ios.md) |

## 项目简介

S2N Connect 用于在自有 VPS 上部署 VPN 服务，提供桌面端与 Android 客户端，并支持通过第三方 iOS 客户端接入。

## 核心功能

- 一键部署 UDP 443 服务，兼容原 TCP 443 连接。
- 支持规则分流与全局模式。
- 支持添加手机连接。

## 使用指南

**准备：** Ubuntu / Debian VPS、root SSH 私钥，以及可用的 SSH 端口（默认 22）。在服务器和云防火墙中放行 UDP 443；使用 TCP 模式时放行 TCP 443。

**电脑：**

1. 从上表下载对应系统和架构的包，完整解压后运行 `S2N.exe`（Windows）或 `S2N`（macOS / Linux）。
2. 填写 VPS 地址、SSH 端口和 root 用户，选择私钥；点击“测试 VPS”并核对服务器指纹。
3. 默认使用 UDP 443（Hysteria 2），点击“一键部署”，选择本地安装目录，等待完成。
4. 按需设置“规则管理”或全局模式，点击“连接”并允许管理员授权；点击“停止”断开。

升级前先在旧版停止连接。已有节点可通过“导入连接配置”复用，无需重新部署。SSH 无法直连时，可填写现有本机 HTTP 代理端口。

**安卓（Android 8.0+，ARM64 / x86_64）：**

1. 在桌面端点击“导出手机配置”，将文件传到手机。
2. 从上表下载并安装 APK，点击“选择配置文件”导入。
3. 点击“连接 VPN”并允许 VPN 授权；可用“验证代理访问”检查联网，点击“停止 VPN”断开。

**iOS：**

安装 [Hiddify](https://apps.apple.com/us/app/hiddify-proxy-vpn/id6596777532) 等客户端，按 [iOS 连接指南](docs/ios.md)导入 iOS 配置，允许 VPN 授权后连接。

配置文件含连接凭据，请勿公开分享。

[问题反馈](https://github.com/cavwic/s2n-connect/issues) · [许可与第三方说明](docs/THIRD_PARTY_NOTICES.md) · [签名说明](docs/SIGNING.md)

</details>

<details name="s2n-language">
<summary>English</summary>

<a name="s2n-english"></a>

**A dedicated connection, under your control.**

Service closures, peak-hour congestion, traffic limits, and privacy concerns can make shared proxy services difficult to rely on. Build a dedicated VPN on your own VPS, choose your server, bandwidth, and traffic plan, and reduce reliance on third-party proxy operators.

| Platform | Direct download |
| --- | --- |
| Windows | [Most PCs (x64)](https://github.com/cavwic/s2n-connect/releases/download/v0.4.0-beta.1/S2N-v0.4.0-beta.1-win-x64.zip) · [ARM64](https://github.com/cavwic/s2n-connect/releases/download/v0.4.0-beta.1/S2N-v0.4.0-beta.1-win-arm64.zip) |
| macOS | [Apple silicon (M series)](https://github.com/cavwic/s2n-connect/releases/download/v0.4.0-beta.1/S2N-v0.4.0-beta.1-osx-arm64.tar.gz) · [Intel](https://github.com/cavwic/s2n-connect/releases/download/v0.4.0-beta.1/S2N-v0.4.0-beta.1-osx-x64.tar.gz) |
| Linux | [x64](https://github.com/cavwic/s2n-connect/releases/download/v0.4.0-beta.1/S2N-v0.4.0-beta.1-linux-x64.tar.gz) · [ARM64](https://github.com/cavwic/s2n-connect/releases/download/v0.4.0-beta.1/S2N-v0.4.0-beta.1-linux-arm64.tar.gz) |
| Android 8.0+ | [Download APK (ARM64 / x86_64)](https://github.com/cavwic/s2n-connect/releases/download/v0.4.0-beta.1/S2N-v0.4.0-beta.1-android-universal.apk) |
| iOS | [Hiddify (third-party client)](https://apps.apple.com/us/app/hiddify-proxy-vpn/id6596777532) · [Connection guide](docs/ios.md#ios-connection-guide) |

## Overview

S2N Connect deploys a VPN service on your own VPS, provides desktop and Android clients, and supports connections through third-party iOS clients.

## Key features

- Deploy UDP 443 with Hysteria 2, or keep using TCP 443 with VLESS + REALITY.
- Use rule-based routing or global mode.
- Add mobile connections.

## Usage

**Prepare:** An Ubuntu / Debian VPS, a root SSH private key, and working SSH access (port 22 by default). Allow UDP 443 through the server and cloud firewalls, or TCP 443 if you select the TCP protocol.

**Desktop:**

1. Download your system and architecture's package from the table above, extract it fully, and run `S2N.exe` (Windows) or `S2N` (macOS / Linux).
2. Enter the VPS address, SSH port, and root user, then select your private key. Click **Test VPS** and verify the server fingerprint.
3. Leave UDP 443 (Hysteria 2) selected, click **One-click deploy**, choose a local installation directory, and wait for completion.
4. Set **Rules** or global mode as needed. Click **Connect** and grant administrator access; click **Stop** to disconnect.

Stop the previous version before upgrading. Use **Import profile** to reuse an existing server without redeploying it. If direct SSH is unavailable, enter the port of an existing local HTTP proxy.

**Android (8.0+, ARM64 / x86_64):**

1. Click **Export mobile profile** in the desktop app and transfer the file to your phone.
2. Download and install the APK from the table above, then tap **Choose profile file** to import it.
3. Tap **Connect VPN** and grant VPN permission. Use **Verify proxy access** to check connectivity and **Stop VPN** to disconnect.

**iOS:**

Install [Hiddify](https://apps.apple.com/us/app/hiddify-proxy-vpn/id6596777532) or another compatible client. Follow the [iOS connection guide](docs/ios.md#ios-connection-guide) to import the iOS configuration, allow VPN access, and connect.

Profile files contain connection credentials. Do not share them publicly.

[Report an issue](https://github.com/cavwic/s2n-connect/issues) · [Licenses and third-party notices](docs/THIRD_PARTY_NOTICES.md) · [Signing notes](docs/SIGNING.md)

</details>

<!-- Reserved promotion slots; hidden until content is ready.
<table width="100%">
  <tr>
    <th width="50%">VPS 推荐</th>
    <th width="50%">个人推广</th>
  </tr>
  <tr>
    <td><br><br><br></td>
    <td><br><br><br></td>
  </tr>
</table>
-->
