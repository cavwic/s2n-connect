[**简体中文**](README.md) · [English](README.en.md)

![S2N Connect：VPS 上增加 VPN 功能](docs/brand/s2n-banner.png)

# S2N Connect · Beta

**v0.3.0-beta.1** · [问题反馈](https://github.com/cavwic/s2n-connect/issues)

| 系统 | 直接下载 |
| --- | --- |
| Windows | [常见电脑（x64）](https://github.com/cavwic/s2n-connect/releases/download/v0.3.0-beta.1/S2N-v0.3.0-beta.1-win-x64.zip) · [ARM64](https://github.com/cavwic/s2n-connect/releases/download/v0.3.0-beta.1/S2N-v0.3.0-beta.1-win-arm64.zip) |
| macOS | [Apple 芯片（M 系列）](https://github.com/cavwic/s2n-connect/releases/download/v0.3.0-beta.1/S2N-v0.3.0-beta.1-osx-arm64.tar.gz) · [Intel 芯片](https://github.com/cavwic/s2n-connect/releases/download/v0.3.0-beta.1/S2N-v0.3.0-beta.1-osx-x64.tar.gz) |
| Linux | [x64](https://github.com/cavwic/s2n-connect/releases/download/v0.3.0-beta.1/S2N-v0.3.0-beta.1-linux-x64.tar.gz) · [ARM64](https://github.com/cavwic/s2n-connect/releases/download/v0.3.0-beta.1/S2N-v0.3.0-beta.1-linux-arm64.tar.gz) |
| 安卓 8.0+ | [下载 APK（ARM64 / x86_64）](https://github.com/cavwic/s2n-connect/releases/download/v0.3.0-beta.1/S2N-v0.3.0-beta.1-android-universal.apk) |

## 这是什么

一个给自有 VPS 增加 VPN 功能的工具，提供 Windows、macOS、Linux 和 Android 客户端。需要自备 VPS。

## 能做什么

- 在桌面端一键部署 VPS 服务并连接。
- 按规则分流，或使用全局模式。
- 导出手机配置，在 Android 上导入连接。

## 怎么用

**准备：** Ubuntu / Debian VPS、root SSH 私钥，并在服务器和云防火墙中放行 TCP 443。已有服务请先备份配置。

**电脑：**

1. 从上表下载对应系统和架构的包，完整解压后运行 `S2N.exe`（Windows）或 `S2N`（macOS / Linux）。
2. 填写 VPS 地址、SSH 端口和 root 用户，选择私钥；点击“测试 VPS”并核对服务器指纹。
3. 点击“一键部署”，选择本地安装目录，等待完成。
4. 按需设置“规则管理”或全局模式，点击“连接”并允许管理员授权；点击“停止”断开。

**安卓（Android 8.0+，ARM64 / x86_64）：**

1. 在桌面端点击“导出手机配置”，将文件传到手机。
2. 从上表下载并安装 APK，点击“选择配置文件”导入。
3. 点击“连接 VPN”并允许 VPN 授权；可用“验证代理访问”检查联网，点击“停止 VPN”断开。

配置文件含连接凭据，请勿公开分享。

[许可与第三方说明](THIRD_PARTY_NOTICES.md) · [签名说明](SIGNING.md)

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
