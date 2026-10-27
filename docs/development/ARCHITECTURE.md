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
- Default transport: Hysteria 2 on UDP 443, with a checksum-pinned upstream binary and an independent `s2n-hysteria.service`.
- Certificate trust is returned over fingerprint-verified SSH and embedded in client profiles; TLS verification remains enabled.
- Existing VLESS + REALITY + Vision on TCP 443 remains available. Existing valid profiles are reused; foreign services and inconsistent configurations are not overwritten.

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

1. An owned core is identified by PID, start time, executable and configuration. Readiness also requires its TUN adapter and loopback listener.
2. S2N probes `https://www.google.com/generate_204` through its allocated loopback proxy port (2080 by default; an occupied port is preserved).
3. A successful response sets the UI to `Connected` and triggers a best-effort exit-IP lookup.
4. A failed probe sets the display to `Disconnected / sing-box running`, but the Stop action remains enabled because process state is authoritative for lifecycle controls.
5. Refresh operations are serialized so a slow probe cannot overlap the next timer tick.
6. UDP readiness is proved by actual proxy traffic rather than a TCP 443 probe. Each Windows connection receives a fresh adapter identity.

Tests can set `S2N_DATA_DIRECTORY` to an absolute temporary directory to isolate the desktop UI from existing user settings. Production defaults are unchanged.
