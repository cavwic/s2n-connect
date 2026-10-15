| 系统 / Platform | 直接下载 / Download |
| --- | --- |
| Windows | [常见电脑 / Most PCs (x64)](https://github.com/cavwic/s2n-connect/releases/download/{{TAG}}/S2N-{{TAG}}-win-x64.zip) · [ARM64](https://github.com/cavwic/s2n-connect/releases/download/{{TAG}}/S2N-{{TAG}}-win-arm64.zip) |
| macOS | [Apple 芯片 / Apple silicon](https://github.com/cavwic/s2n-connect/releases/download/{{TAG}}/S2N-{{TAG}}-osx-arm64.tar.gz) · [Intel](https://github.com/cavwic/s2n-connect/releases/download/{{TAG}}/S2N-{{TAG}}-osx-x64.tar.gz) |
| Linux | [x64](https://github.com/cavwic/s2n-connect/releases/download/{{TAG}}/S2N-{{TAG}}-linux-x64.tar.gz) · [ARM64](https://github.com/cavwic/s2n-connect/releases/download/{{TAG}}/S2N-{{TAG}}-linux-arm64.tar.gz) |
| 安卓 / Android 8.0+ | [下载 APK / Download APK (ARM64 / x86_64)](https://github.com/cavwic/s2n-connect/releases/download/{{TAG}}/S2N-{{TAG}}-android-universal.apk) |
| iOS | [Hiddify（第三方 / Third-party client）](https://apps.apple.com/us/app/hiddify-proxy-vpn/id6596777532) · [连接指南 / Guide](https://github.com/cavwic/s2n-connect/blob/main/docs/ios.md) |

[简体中文说明](https://github.com/cavwic/s2n-connect#user-content-s2n-chinese) · [English guide](https://github.com/cavwic/s2n-connect#user-content-s2n-english)

**专属节点，自主管理。**

面对第三方代理服务可能出现的运营中断、高峰拥堵、流量限制与隐私顾虑，通过自有 VPS 构建专属 VPN，自主选择服务器、带宽与流量方案，减少对第三方代理运营方的信任依赖。

## 项目简介

S2N Connect 用于在自有 VPS 上部署 VPN 服务，提供桌面端与 Android 客户端，并支持通过第三方 iOS 客户端接入。

## 核心功能

- 一键部署 VPS 端 VPN 服务。
- 支持规则分流与全局模式。
- 支持添加手机连接。

## 使用指南

自备 Ubuntu / Debian VPS 和 root SSH 私钥，放行 TCP 443；部署前备份已有配置。

1. **电脑：** 从上表下载对应系统和架构的包，完整解压并运行。填写 VPS / SSH 信息，点击“测试 VPS”核对指纹，再点“一键部署”→“连接”，允许管理员授权。
2. **安卓（8.0+，ARM64 / x86_64）：** 从上表下载并安装 APK。在电脑上“导出手机配置”，手机上“选择配置文件”→“连接 VPN”，允许 VPN 授权。
3. **iOS：** 使用 Hiddify 等支持 VLESS + REALITY 的客户端，按 [iOS 连接指南](https://github.com/cavwic/s2n-connect/blob/main/docs/ios.md)导入连接链接，允许 VPN 授权后连接。

配置文件含连接凭据，请勿公开分享。

[许可与第三方说明](https://github.com/cavwic/s2n-connect/blob/main/THIRD_PARTY_NOTICES.md) · [签名说明](https://github.com/cavwic/s2n-connect/blob/main/SIGNING.md)
