[简体中文说明](https://github.com/cavwic/s2n-connect/blob/main/README.md) · [English guide](https://github.com/cavwic/s2n-connect/blob/main/README.en.md)

## 这是什么

S2N Connect 是一个给自有 VPS 增加 VPN 功能的工具，支持 Windows、macOS、Linux 和 Android。

## 能做什么

桌面端一键部署并连接 VPS，支持规则分流和全局模式；安卓端导入配置即可连接。

## 怎么用

自备 Ubuntu / Debian VPS 和 root SSH 私钥，放行 TCP 443；部署前备份已有配置。

1. **电脑：** 下载下方对应系统和架构的包，完整解压并运行。填写 VPS / SSH 信息，点击“测试 VPS”核对指纹，再点“一键部署”→“连接”，允许管理员授权。
2. **安卓（8.0+，ARM64 / x86_64）：** 安装下方 `android-universal.apk`。在电脑上“导出手机配置”，手机上“选择配置文件”→“连接 VPN”，允许 VPN 授权。
3. 点击“停止”或“停止 VPN”断开。配置文件含连接凭据，请勿公开分享。

[许可与第三方说明](https://github.com/cavwic/s2n-connect/blob/main/THIRD_PARTY_NOTICES.md) · [签名说明](https://github.com/cavwic/s2n-connect/blob/main/SIGNING.md)
