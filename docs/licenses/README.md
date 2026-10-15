# Desktop dependency licenses

This directory contains complete upstream license texts and notices for the
desktop application's packaged dependencies. `manifest.json` records the
dependency versions, immutable source revisions, source URLs, and SHA-256 hashes
of the notice files. The baseline dependency inventory comes from the actual
`artifacts/win-x64/S2N.deps.json` publish output and local NuGet package metadata.

Copy this whole directory into `docs/licenses/` in every desktop archive, together
with the repository's root [LICENSE](../../LICENSE) and [README](../../README.md),
and the complete `docs/` directory, including the [English guide](../README.en.md)
and [third-party notices](../THIRD_PARTY_NOTICES.md). License texts remain unmodified. Runtime and native
component notices apply to the platform assets included in that archive.

The desktop application does not bundle sing-box or Xray. Their downloaded
components retain their own upstream licenses. Android's bundled libbox and
GPL licensing are documented separately in the [third-party notices](../THIRD_PARTY_NOTICES.md)
and Android project; this desktop inventory does not replace those obligations.

When changing dependency versions, review the new package metadata and refresh
the corresponding source revisions, license files, and manifest hashes.

Run `python docs/licenses/verify.py` from the repository root to verify all
checked-in notice hashes and dependency-to-notice mappings without networking.
The directory's Git attributes retain LF line endings so hashes are stable on
Windows, macOS, and Linux.
