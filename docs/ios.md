[简体中文](#ios-连接指南) · [English](#ios-connection-guide)

# iOS 连接指南

使用 [Hiddify](https://apps.apple.com/us/app/hiddify-proxy-vpn/id6596777532) 等第三方客户端连接 VPS；Hiddify 要求 iOS 15 或以上。

1. 在桌面端完成部署或导入已有连接，点击“导出 iOS 配置”。
2. 将导出的 JSON 文件安全传到手机，复制文件的**全部内容**，在 Hiddify 中选择“＋ → 从剪贴板添加”。
3. 选择新增的 S2N 节点，连接并允许系统 VPN 授权，再检查实际联网情况。

此文件为完整 sing-box 配置，支持 Hysteria 2（UDP）和原有 VLESS + REALITY（TCP），并保留自定义证书验证；默认代理公网流量，局域网直连。客户端应支持 sing-box 1.12 或更新的配置格式；若 Hiddify 提示格式不兼容，可更新客户端，或使用 [sing-box 官方 Apple 客户端](https://sing-box.sagernet.org/clients/apple/) 的本地配置导入。

“导出手机配置”生成的是 **S2N Android 专用格式**，请勿将其作为 Hiddify 配置导入。不要开启“跳过证书验证”。配置含连接凭据，请勿公开上传或分享。

兼容性依据：[Hiddify 配置格式](https://github.com/hiddify/hiddify-app#-main-features)、[剪贴板本地配置导入实现](https://github.com/hiddify/hiddify-app/blob/main/lib/features/profile/notifier/profile_notifier.dart#L64-L81)。已验证导出配置的原生核心检查；尚未进行 iOS 真机验证。

# iOS connection guide

Use [Hiddify](https://apps.apple.com/us/app/hiddify-proxy-vpn/id6596777532) or another compatible client to connect to your VPS. Hiddify requires iOS 15 or later.

1. Deploy your VPS or import an existing connection in the desktop app, then select **Export iOS profile**.
2. Transfer the JSON file securely to your phone. Copy its **entire contents** and choose **＋ → Add from clipboard** in Hiddify.
3. Select the new S2N server, connect, approve the system VPN configuration, and verify internet access.

The export is a complete sing-box configuration for Hysteria 2 (UDP) or legacy VLESS + REALITY (TCP). It preserves custom certificate trust, proxies public traffic, and keeps LAN traffic direct. Use a client supporting sing-box 1.12 or newer configuration syntax. If Hiddify rejects the format, update it or import the file as a local profile in the [official sing-box Apple client](https://sing-box.sagernet.org/clients/apple/).

**Export mobile profile** produces an **S2N Android-specific format**, which is not a Hiddify configuration. Keep certificate verification enabled. These files contain connection credentials; do not upload them publicly or share them.

Compatibility references: [Hiddify formats](https://github.com/hiddify/hiddify-app#-main-features), [clipboard import implementation](https://github.com/hiddify/hiddify-app/blob/main/lib/features/profile/notifier/profile_notifier.dart#L64-L81). The export passes native core configuration checks; it has not been tested on a physical iOS device.
