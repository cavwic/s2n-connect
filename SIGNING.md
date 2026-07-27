# Signing and platform trust

Publishing on GitHub does not eliminate operating-system trust warnings.

- Windows production builds should use Authenticode signing.
- macOS production builds should use Developer ID signing and notarization.
- GitHub Actions artifact attestations and SHA-256 files establish provenance
  but do not replace platform signing.
- Linux initially ships as `.tar.gz`; native package signing can be added later.
