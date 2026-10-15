[**简体中文**](README.md) · [English](README.en.md)<br>

![S2N Connect：VPS上 增加 VPN 功能](docs/brand/s2n-banner.png)

# S2N Connect

**VPS 上增加 VPN 功能。**

S2N Connect 是管理自有 VPS 连接的跨平台桌面工具：在 VPS 上部署 Xray 的 **VLESS + REALITY** 服务，在本机使用 **sing-box TUN** 接管流量，并按程序、域名或全局模式分流。你需要自备 VPS；这里提供的是部署与连接管理软件。

[下载 Alpha 版本](https://github.com/cavwic/s2n-connect/releases) · [更新记录](CHANGELOG.md) · [报告问题](https://github.com/cavwic/s2n-connect/issues)

## 平台

| 客户端 | 架构 | 发布包 |
| --- | --- | --- |
| Windows 10 / 11 | x64、ARM64 | `.zip` |
| macOS 13+ | Intel、Apple silicon | `.tar.gz` |
| Linux 桌面（包括 Ubuntu） | x64、ARM64 | `.tar.gz` |

以上对应六个发布构建目标。跨平台编译、打包通过不代表各系统的提权和 TUN 联网已经完成实机验证，具体限制见下文。

**VPS：** 当前 Alpha 的部署目标为带 systemd 的 Ubuntu / Debian，需通过 root 用户进行 SSH 连接。

## 主要功能

- **部署与安装：** 可一键部署本机 sing-box 和 VPS 服务，也可分别操作。自动识别系统、架构；安装目录由你选择，下载包通过 SHA-256 校验后解压，验证可执行文件后保存路径，并清理临时下载文件。
- **SSH 连接：** 自定义 SSH 用户和端口，从任意本地目录选择私钥并按内容验证格式；首次连接需核对服务器指纹，后续指纹变化会拒绝连接。私钥路径仅在连接成功后保存，私钥口令不落盘。
- **发现已有安装：** 从保存设置、运行进程、PATH 和常用位置查找 sing-box，并更新操作按钮状态。
- **按需分流：** 程序名与域名后缀规则独立生效；清空规则不会变成无条件代理。规则模式默认直连，全局模式将公网流量交给 VPS；局域网、私有地址和 VPS 地址保持直连。
- **DNS：** 公网域名通过 VPS 使用远端 DoH，本地名称和反向查询使用本地 DNS。
- **状态与控制：** 先判断 sing-box 进程，再经受管理的代理检测 Google 连通性；连接与停止按钮按实际进程状态更新。出口 IP 查询失败不会否定已确认的连接。
- **桌面偏好：** 系统、浅色和深色主题可即时切换并保存；中文系统使用中文，其余系统使用英文；支持系统托盘菜单的环境可从托盘显示、连接、停止和退出。
- **启动与连接：** 可在登录后延迟约 60 秒启动；自动连接需先成功连接一次，并检查网络、DNS、VPS 端口与本地安装是否就绪。

安装校验优先使用官方发布的 SHA-256 校验和文件；发布未提供该文件时，使用 GitHub 发布资产的 SHA-256 digest。校验值缺失、无效或不匹配都会停止安装，不会跳过验证。

## 使用前准备

- 自备 Ubuntu / Debian VPS、root SSH 私钥，以及可用的 SSH 端口；服务端使用 **TCP 443**，请确保该端口可用，并在云防火墙和系统防火墙中放行。
- 建议先在干净 VPS 上使用。部署会安装 Xray、写入服务配置并启用服务；已有 Xray 配置或其他服务时，请先备份并检查冲突。
- 本机与 VPS 需要能访问相关下载源。本机启动 TUN 时需要管理员授权；Linux 使用 `pkexec`，macOS 使用系统管理员提示。
- 下载与你的系统和架构匹配的发布包，完整解压后运行 `S2N.exe`（Windows）或 `S2N`（macOS / Linux）。发布包自带 .NET 运行时。

## 快速使用

1. 填写 VPS 地址、SSH 用户和端口，选择 SSH 私钥；加密私钥需填写本次使用的口令。
2. 点击“测试 VPS”，通过供应商控制台或其他可信渠道核对首次显示的 SSH 服务器指纹。
3. 点击“一键部署”，选择本地 sing-box 安装目录并等待完成。也可使用“选择并安装”和“仅部署 VPS”分别操作。
4. 在“规则管理”中设置程序名和域名后缀，按需启用全局模式；点击“连接”并完成管理员授权，等待连通性检测。
5. 点击“停止”断开连接，使用“退出”结束程序。支持托盘的环境中，关闭窗口或最小化可能只会隐藏主窗口。

首次成功连接后，可再按需开启自动连接和登录启动。自动连接不会代填加密 SSH 私钥口令；日常连接使用已部署的 VPS 配置。

## 许可与项目文档

S2N Connect 使用 [MIT 许可证](LICENSE)。sing-box、Xray 及其他依赖遵循各自上游许可证，参见[第三方说明](THIRD_PARTY_NOTICES.md)。

[架构](ARCHITECTURE.md) · [参与贡献](CONTRIBUTING.md) · [GitHub 发布指南](GITHUB_PUBLISHING.md) · [更新记录](CHANGELOG.md)

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
