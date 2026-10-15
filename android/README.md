# S2N Connect for Android

[简体中文](#简体中文) · [English](#english)

## 简体中文

原生 Kotlin 客户端，应用包名 `com.cavwic.s2n`，调试包使用
`com.cavwic.s2n.debug`。支持 Android 8.0+，一个 universal APK 包含
`arm64-v8a` 和 `x86_64` 原生核心。

Android 端连接已经部署的 VPS，不执行 SSH 或 VPS 部署。在桌面端部署并导出手机配置后，
可导入 JSON 文件、粘贴 JSON／`vless://` 链接，或填写连接参数。导出的配置包含连接凭据，
请私密传输并保管。界面默认中文，可切换英文；主题默认跟随系统，也可选择浅色或深色。
当前协议为 TCP / VLESS + REALITY（`xtls-rprx-vision`），不支持其他 VLESS 传输方式。

### 固定构建环境

| 项目 | 版本 |
| --- | --- |
| JDK | 17 |
| Gradle wrapper | 9.6.0 |
| Android Gradle Plugin | 9.4.0，内置 Kotlin |
| Android SDK / 最低 API | 36 / 26 |
| Android NDK | 28.0.13004108 |
| Go | 1.26.8 |
| SagerNet gomobile | v0.1.13 |
| sing-box | v1.14.3 |
| 核心提交 | `7054cac5124465ffdb145870d7c01885566711b1` |

先安装对应 Android SDK 和 NDK、接受 SDK 许可，并设置 `JAVA_HOME`、`ANDROID_HOME`。
从仓库根目录执行：

```bash
bash android/scripts/build-libbox.sh --print-versions
bash android/scripts/build-libbox.sh
cd android
bash ./gradlew --no-daemon :app:lintDebug :app:testDebugUnitTest :app:assembleDebug
```

核心脚本从固定的官方源码生成 `app/libs/libbox.aar`；AAR 不提交仓库。
调试 APK 位于 `app/build/outputs/apk/debug/app-debug.apk`。Android 模拟器或设备就绪后，
可运行 `bash ./gradlew :app:connectedDebugAndroidTest`。

`VpnSmokeTests` 包含三个范围：检查原生核心版本并解析生产配置；检查配置导入、语言及主题界面；
通过系统对话框授权后，使用仅直连的合成配置建立真实 TUN，确认 VPN 网络出现，再停止服务。
它不连接真实 VPS，不验证代理联网。测试是否通过以对应 CI 报告为准。

### 分发与源码许可

Beta 分发使用非调试 APK，并用同一持久私钥签名；后续更新需要兼容的签名证书和递增的
`versionCode`。请私密备份密钥，勿提交到 Git。CI 签名所需的四个 secrets 和本地环境变量见
[发布指南](../docs/development/GITHUB_PUBLISHING.md#android-release-signing)。调试 APK 不使用发布密钥。

Android 应用与捆绑 libbox 的组合按 **GPL-3.0-or-later** 分发，完整文本见
[LICENSE](LICENSE)，APK 内的开源许可页保留版权和源码入口。对应应用源码是本仓库相应发布标签的
`android/` 目录；核心对应源码为
[固定 sing-box 提交](https://github.com/SagerNet/sing-box/tree/7054cac5124465ffdb145870d7c01885566711b1)，
构建入口为 [build-libbox.sh](scripts/build-libbox.sh)。桌面端仍使用根目录 MIT 许可。
详见[第三方说明](../docs/THIRD_PARTY_NOTICES.md)。

## English

This is the native Kotlin client, with application ID `com.cavwic.s2n` and debug
ID `com.cavwic.s2n.debug`. It supports Android 8.0+; one universal APK includes
`arm64-v8a` and `x86_64` native cores.
The supported protocol is TCP / VLESS + REALITY with `xtls-rprx-vision`.

Android connects to an existing VPS and does not perform SSH or server deployment.
Deploy the server with the desktop app, then import its exported mobile JSON
profile, paste JSON or a `vless://` link, or enter connection parameters. Profiles
contain credentials and must be transferred privately. The UI defaults to Chinese
with an English switch. The theme follows the system by default; light and dark
modes are also available.

### Pinned source build

Use JDK 17, Gradle wrapper 9.6.0, AGP 9.4.0 with built-in Kotlin, SDK 36,
minimum API 26, NDK 28.0.13004108, Go 1.26.8, and SagerNet gomobile v0.1.13.
The core is official sing-box v1.14.3 commit
`7054cac5124465ffdb145870d7c01885566711b1`.

Install and accept the SDK/NDK licenses, set `JAVA_HOME` and `ANDROID_HOME`, and
run the commands in the Chinese section from the repository root. The script
builds `android/app/libs/libbox.aar`; generated AARs are not committed. The debug
APK is `android/app/build/outputs/apk/debug/app-debug.apk`.

With an emulator or device running, execute
`bash ./gradlew :app:connectedDebugAndroidTest` inside `android/`.
`VpnSmokeTests` checks the core version and production-config parsing, profile
import/language/theme screens, and real VPN authorization followed by TUN
start/stop with a direct-only synthetic fixture. It does not connect to a VPS or
validate proxy networking. Consult the CI report for actual test results.

### Signing and corresponding source

Distribution APKs are non-debuggable and signed with one persistent project key.
Updates require a compatible signing certificate and an increasing `versionCode`.
Back up the private key outside Git. See the [signing instructions](../docs/development/GITHUB_PUBLISHING.md#android-release-signing)
for the four CI secrets and local signing environment variables. Debug builds do
not receive the release key.

The Android app/libbox combination is **GPL-3.0-or-later**; see [LICENSE](LICENSE).
The app's open-source screen retains notices and source links. Corresponding app
source is `android/` at the matching repository release tag. Corresponding core
source is the [pinned sing-box commit](https://github.com/SagerNet/sing-box/tree/7054cac5124465ffdb145870d7c01885566711b1),
built with [build-libbox.sh](scripts/build-libbox.sh). Desktop code retains the
root MIT license. See [third-party notices](../docs/THIRD_PARTY_NOTICES.md).
