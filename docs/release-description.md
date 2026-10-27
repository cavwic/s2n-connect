| 系统 / Platform | 直接下载 / Download |
| --- | --- |
| Windows | [AMD64](https://github.com/cavwic/s2n-connect/releases/download/{{TAG}}/S2N-{{TAG}}-win-x64.zip) · [ARM64](https://github.com/cavwic/s2n-connect/releases/download/{{TAG}}/S2N-{{TAG}}-win-arm64.zip) |
| macOS | [Apple 芯片 / Apple silicon](https://github.com/cavwic/s2n-connect/releases/download/{{TAG}}/S2N-{{TAG}}-osx-arm64.tar.gz) · [Intel](https://github.com/cavwic/s2n-connect/releases/download/{{TAG}}/S2N-{{TAG}}-osx-x64.tar.gz) |
| Linux | [x64](https://github.com/cavwic/s2n-connect/releases/download/{{TAG}}/S2N-{{TAG}}-linux-x64.tar.gz) · [ARM64](https://github.com/cavwic/s2n-connect/releases/download/{{TAG}}/S2N-{{TAG}}-linux-arm64.tar.gz) |
| 安卓 / Android 8.0+ | [下载APK / Download APK](https://github.com/cavwic/s2n-connect/releases/download/{{TAG}}/S2N-{{TAG}}-android-universal.apk) |
| iOS | [Hiddify（第三方 / Third-party client）](https://apps.apple.com/us/app/hiddify-proxy-vpn/id6596777532) · [连接指南 / Guide](https://github.com/cavwic/s2n-connect/blob/main/docs/ios.md) |

[简体中文说明](https://github.com/cavwic/s2n-connect#user-content-s2n-chinese) · [English guide](https://github.com/cavwic/s2n-connect#user-content-s2n-english)

**专属节点，自主管理。**

面对第三方代理服务可能出现的运营中断、高峰拥堵、流量限制与隐私顾虑，通过自有 VPS 构建专属 VPN，自主选择服务器、带宽与流量方案，减少对第三方代理运营方的信任依赖。

## 项目简介

S2N Connect 用于在自有 VPS 上部署 VPN 服务，提供桌面端与 Android 客户端，并支持通过第三方 iOS 客户端接入。

## 核心功能

- 一键部署 UDP 443 服务，兼容原 TCP 443 连接。
- 支持规则分流与全局模式。
- 支持添加手机连接。

## 使用指南

自备 Ubuntu / Debian VPS，放行 UDP 443（TCP 模式则放行 TCP 443）。首次部署需 root 私钥及实际 SSH 管理端口；22 不通时，使用服务器已配置的备用端口或已有本机 HTTP 代理。UDP 443 不代替 SSH。

1. **电脑：** 完整解压下载包并运行 `S2N.exe`（Windows 无需另装 .NET）或 `S2N`（macOS / Linux）。新节点填写 VPS / SSH 信息，“测试 SSH”核对指纹后“一键部署”→“连接”；已有节点安装本地核心后“导入连接配置”→“连接”，无需 SSH。
2. **安卓（8.0+，ARM64 / x86_64）：** 从上表下载并安装 APK。在电脑上“导出手机配置”→“Android（S2N）”，手机上“选择配置文件”→“连接 VPN”，允许 VPN 授权。
3. **iOS：** 在电脑上“导出手机配置”→“iOS（Hiddify 等）”，按 [iOS 连接指南](https://github.com/cavwic/s2n-connect/blob/main/docs/ios.md)导入，允许 VPN 授权后连接。

配置文件含连接凭据，请勿公开分享。

[许可与第三方说明](https://github.com/cavwic/s2n-connect/blob/main/docs/THIRD_PARTY_NOTICES.md) · [签名说明](https://github.com/cavwic/s2n-connect/blob/main/docs/SIGNING.md)
