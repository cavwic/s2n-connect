# Changelog

## 0.2.1-alpha.1

### Changed

- Connection-state evaluation now checks whether the configured sing-box process is running before any external network probe.
- When sing-box is not running, the UI immediately shows `未连接` and does not contact Google.
- When sing-box is running, Google connectivity is tested through `127.0.0.1:2080` with a 10-second request timeout instead of the previous 1-second timeout.
- A failed or slow Google probe no longer disables the Stop button. Connect and Stop buttons are controlled only by the actual sing-box process state.
- When sing-box is running but the Google test fails, the UI shows `未连接（sing-box 已启动，外网检测失败）`, while Stop remains available.
- Overlapping five-second status refreshes are suppressed to prevent multiple slow probes from accumulating.
- Exit-IP lookup is best-effort and no longer changes a confirmed connected state to disconnected.

## 0.2.0-alpha

- Initial cross-platform alpha source package.
