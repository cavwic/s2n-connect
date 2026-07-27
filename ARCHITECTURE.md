# S2N Connect architecture

## Client

- .NET 8 + Avalonia desktop UI.
- OS/architecture detection selects the official sing-box release asset.
- Download archive and official checksum are stored only in a temporary directory.
- The archive is deleted after checksum verification, extraction and binary validation.
- sing-box installation directory is chosen by the user and persisted only after validation.
- SSH private-key path is chosen by the user and persisted only after a successful host-key-verified SSH session.
- SSH private-key content and passphrases are never persisted.

## Server

- Initial target: Ubuntu/Debian with root SSH.
- Xray VLESS + REALITY + Vision on TCP 443.
- Existing `/root/s2n-profile.json` and valid Xray configuration are reused.

## Routing

- Process rules route all traffic from selected executables through the VPS.
- Domain suffix rules apply to any process.
- RFC1918/LAN routes remain direct.
- Public DNS uses DoH over the VPS; local single-label/LAN names use the local resolver.

## Privilege model

- Windows: UAC `runas`.
- macOS: administrator prompt via `osascript`.
- Linux: `pkexec`.

The macOS and Linux privilege paths are alpha and require integration testing on physical/virtual machines before a stable release.


## Connection-state state machine (0.2.1-alpha.1)

The UI treats process state and network health as separate signals:

1. If the configured sing-box executable is not running, state is `Disconnected`; no Google request is made.
2. If sing-box is running, S2N probes `https://www.google.com/generate_204` through the managed local proxy at `127.0.0.1:2080`.
3. A successful response sets the UI to `Connected` and triggers a best-effort exit-IP lookup.
4. A failed probe sets the display to `Disconnected / sing-box running`, but the Stop action remains enabled because process state is authoritative for lifecycle controls.
5. Refresh operations are serialized so a slow probe cannot overlap the next timer tick.
