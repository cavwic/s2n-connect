[简体中文](#ios-连接指南) · [English](#ios-connection-guide)

# iOS 连接指南

使用 [Hiddify](https://apps.apple.com/us/app/hiddify-proxy-vpn/id6596777532) 等支持 VLESS + REALITY 的客户端连接已部署的 VPS。Hiddify 要求 iOS 15.0 或以上。

1. 在桌面端完成 VPS 部署，点击“导出手机配置”。
2. 用文本编辑器打开导出的 JSON，将下方链接中的 `{字段名}` 替换为文件内的对应值，保留其他参数。

```text
vless://{uuid}@{serverIp}:{serverPort}?encryption=none&security=reality&type=tcp&flow=xtls-rprx-vision&fp=chrome&pbk={publicKey}&sid={shortId}&sni={serverName}#S2N
```

3. 将完整链接复制到手机，在 Hiddify 中选择“＋ → 从剪贴板添加”，选择该节点连接并允许系统 VPN 授权。

导出的 JSON 供 S2N Android 客户端使用，不能直接作为 Hiddify 配置导入。若服务器为 IPv6 地址，链接中需加方括号，如 `[2001:db8::1]`。配置文件和链接均含连接凭据，请勿公开分享。

[Hiddify 官方使用说明](https://hiddify.com/app/How-to-use-Hiddify-app/) · [连接链接格式](https://github.com/hiddify/hiddify-app/wiki/URL-Scheme)

# iOS connection guide

Use [Hiddify](https://apps.apple.com/us/app/hiddify-proxy-vpn/id6596777532) or another VLESS + REALITY client to connect to your deployed VPS. Hiddify requires iOS 15.0 or later.

1. Deploy the VPS from the desktop app and click **Export mobile profile**.
2. Open the exported JSON in a text editor. In the link below, replace each `{fieldName}` with its value from the file and leave the other parameters unchanged.

```text
vless://{uuid}@{serverIp}:{serverPort}?encryption=none&security=reality&type=tcp&flow=xtls-rprx-vision&fp=chrome&pbk={publicKey}&sid={shortId}&sni={serverName}#S2N
```

3. Copy the complete link to your phone. In Hiddify, select **＋ → Add from clipboard**, choose the profile, connect, and allow the system VPN configuration.

The exported JSON is for the S2N Android client and cannot be imported directly as a Hiddify configuration. Enclose IPv6 server addresses in square brackets, such as `[2001:db8::1]`. Profile files and links contain connection credentials; do not share them publicly.

[Official Hiddify guide](https://hiddify.com/app/How-to-use-Hiddify-app/) · [Connection link format](https://github.com/hiddify/hiddify-app/wiki/URL-Scheme)
