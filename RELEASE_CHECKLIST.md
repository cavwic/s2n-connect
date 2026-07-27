# Release checklist

- [ ] Build passes on win-x64, win-arm64, osx-x64, osx-arm64, linux-x64 and linux-arm64.
- [ ] Test sing-box install into a path containing spaces and non-ASCII characters.
- [ ] Test encrypted and unencrypted OpenSSH private keys.
- [ ] Test first-use SSH host fingerprint prompt and mismatch rejection.
- [ ] Test Ubuntu 24.04 and Debian VPS deployment.
- [ ] Test Windows UAC, macOS administrator prompt and Linux polkit paths.
- [ ] Confirm LAN/NAS traffic remains direct.
- [ ] Confirm process rules and domain rules operate independently.
- [ ] Confirm Stop and Exit remove the TUN route and restore DNS.
- [ ] Sign Windows artifacts.
- [ ] Sign and notarize macOS artifacts.
- [ ] Publish SHA-256 and artifact attestations.
- [ ] Review third-party notices and licenses.
