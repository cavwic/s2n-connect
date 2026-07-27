# GitHub publishing guide

Recommended repository name: `s2n-connect`.

## First push

```bash
git init
git add .
git commit -m "Initial S2N Connect alpha"
git branch -M main
git remote add origin <your repository URL>
git push -u origin main
```

The `build` workflow compiles all configured runtime identifiers on every push
and pull request. Do not create a public release until all matrix jobs pass.

## Release

1. Update release notes and version.
2. Confirm CodeQL and all build jobs pass.
3. Create and push a tag, for example `v0.2.0-alpha.1`.
4. The release workflow publishes self-contained archives, SHA-256 files and
   artifact attestations.

## Signing

The included release workflow is unsigned by default. Before describing a
release as production-ready, configure Windows Authenticode signing and macOS
Developer ID signing/notarization. See `SIGNING.md`.
