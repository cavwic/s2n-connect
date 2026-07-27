# S2N

S2N Connect is a cross-platform desktop manager for a personal VPS-based
VLESS + REALITY connection with sing-box process and domain routing.

> Naming note: `s2n` is also associated with AWS's `s2n-tls` project.
> The recommended repository slug is `s2n-connect`; the window title may remain
> `S2N`.

## Supported client platforms

- Windows 10/11: x64 and ARM64
- macOS 13+: Intel and Apple silicon
- Linux desktop: x64 and ARM64
- Ubuntu is a supported Linux distribution

## Supported VPS targets

S2N Connect 0.2.1 alpha initially deploys to Ubuntu or Debian VPS instances with root SSH
access. Other server distributions require separate validation.

## Required behavior implemented in this alpha

- User chooses the sing-box installation directory; the path is persisted only after checksum and binary validation.
- S2N detects operating system and CPU architecture.
- The latest official sing-box release and checksum file are downloaded from
  GitHub.
- SHA-256 is checked before extraction.
- The temporary archive and extraction directory are deleted after install.
- The chosen sing-box path is persisted only after binary validation.
- The user selects an SSH private key from any local directory; SSH user and port are configurable, and encrypted-key passphrases are used only in memory.
- The key is validated by content rather than only by `.pem` extension.
- The SSH key path is persisted only after a successful connection.
- Existing sing-box executables are discovered from saved settings, running processes, PATH, and common installation locations; button state is updated.
- Connection status is process-first: when sing-box is not running, S2N immediately shows disconnected and skips the Google probe. When sing-box is running, S2N tests Google through the managed proxy with a resilient timeout before showing connected.
- Connect/Stop button availability is derived from the actual sing-box process state, not from a potentially slow external connectivity probe.
- Program-name and domain-suffix rules are supported.
- Private/LAN traffic remains direct.
- Public DNS uses remote DoH through the VPS; local names use local DNS.
- Minimize/close uses a system tray menu where supported.

## Build

```bash
dotnet restore
dotnet build -c Release
```

Publish:

```bash
dotnet publish src/S2N.App/S2N.App.csproj \
  -c Release \
  -r win-x64 \
  --self-contained true \
  -o artifacts/win-x64
```

## Alpha limitations

- The repository includes CI builds for every target. The generation environment could not restore NuGet packages, so the first GitHub Actions run is the authoritative compile gate.
- Windows elevation is the primary implementation.
- macOS `osascript` and Linux `pkexec` privilege paths require testing on actual
  systems before a stable release.
- The VPS deployment target is Ubuntu/Debian only in this alpha.

## Release integrity

GitHub Actions builds self-contained platform archives, creates SHA-256 files,
and generates GitHub artifact attestations. These controls improve
verifiability but do not replace Authenticode or Apple notarization.

## Recommended GitHub identity

Use **S2N Connect** as the product/repository identity (`s2n-connect`) while keeping **S2N** as the short UI name. AWS already uses the `s2n` name prominently for `s2n-tls`, so the longer identity reduces confusion.
