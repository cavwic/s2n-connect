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
2. Confirm all six build jobs pass. Review the CodeQL availability result: a skipped scan is not a successful security scan. Private repositories require an appropriate GitHub Code Security plan to run CodeQL.
3. Create and push a tag, for example `v0.2.0-alpha.1`.
4. The release workflow waits for all six self-contained archives, verifies their
   SHA-256 files, uploads the complete asset set to a draft, and then publishes it.
   Alpha tags are marked as prereleases. Artifact attestations run on public
   repositories; for a private repository with Enterprise support, enable the
   `ENABLE_PRIVATE_ATTESTATIONS` repository variable. Otherwise the summary
   explicitly reports that provenance attestations were not generated.

## Signing

The included release workflow is unsigned by default. Before describing a
release as production-ready, configure Windows Authenticode signing and macOS
Developer ID signing/notarization. See `SIGNING.md`.
