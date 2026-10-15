[简体中文说明](https://github.com/cavwic/s2n-connect/blob/main/README.md) · [English guide](https://github.com/cavwic/s2n-connect/blob/main/README.en.md)

| 系统 / Platform | 直接下载 / Download |
| --- | --- |
| Windows | [常见电脑 / Most PCs (x64)](https://github.com/cavwic/s2n-connect/releases/download/{{TAG}}/S2N-{{TAG}}-win-x64.zip) · [ARM64](https://github.com/cavwic/s2n-connect/releases/download/{{TAG}}/S2N-{{TAG}}-win-arm64.zip) |
| macOS | [Apple 芯片 / Apple silicon](https://github.com/cavwic/s2n-connect/releases/download/{{TAG}}/S2N-{{TAG}}-osx-arm64.tar.gz) · [Intel](https://github.com/cavwic/s2n-connect/releases/download/{{TAG}}/S2N-{{TAG}}-osx-x64.tar.gz) |
| Linux | [x64](https://github.com/cavwic/s2n-connect/releases/download/{{TAG}}/S2N-{{TAG}}-linux-x64.tar.gz) · [ARM64](https://github.com/cavwic/s2n-connect/releases/download/{{TAG}}/S2N-{{TAG}}-linux-arm64.tar.gz) |
| 安卓 / Android 8.0+ | [下载 APK / Download APK (ARM64 / x86_64)](https://github.com/cavwic/s2n-connect/releases/download/{{TAG}}/S2N-{{TAG}}-android-universal.apk) |

## 这是什么

S2N Connect 是一个给自有 VPS 增加 VPN 功能的工具，支持 Windows、macOS、Linux 和 Android。

## 能做什么

桌面端一键部署并连接 VPS，支持规则分流和全局模式；安卓端导入配置即可连接。

## 怎么用

自备 Ubuntu / Debian VPS 和 root SSH 私钥，放行 TCP 443；部署前备份已有配置。

1. **电脑：** 从上表下载对应系统和架构的包，完整解压并运行。填写 VPS / SSH 信息，点击“测试 VPS”核对指纹，再点“一键部署”→“连接”，允许管理员授权。
2. **安卓（8.0+，ARM64 / x86_64）：** 从上表下载并安装 APK。在电脑上“导出手机配置”，手机上“选择配置文件”→“连接 VPN”，允许 VPN 授权。
3. 点击“停止”或“停止 VPN”断开。配置文件含连接凭据，请勿公开分享。

[许可与第三方说明](https://github.com/cavwic/s2n-connect/blob/main/THIRD_PARTY_NOTICES.md) · [签名说明](https://github.com/cavwic/s2n-connect/blob/main/SIGNING.md)
