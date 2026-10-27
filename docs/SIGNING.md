# Signing and platform trust

Publishing on GitHub does not eliminate operating-system trust warnings.

- Windows production builds should use Authenticode signing.
- macOS production builds should use Developer ID signing and notarization.
- GitHub Actions artifact attestations and SHA-256 files establish provenance
  but do not replace platform signing.
- Linux initially ships as `.tar.gz`; native package signing can be added later.

Android Beta APKs use a persistent project release certificate. The private key
is stored outside the source tree and supplied to Actions as encrypted repository
secrets; no debug signing fallback is permitted for a release. Android verifies
this signature on install and requires the same certificate for future updates.
The app is independently distributed on GitHub, not through Google Play.

Expected signing certificate SHA-256:
`d874cb92373a93157e4c6cb492b6a0329d866cba6a83220cbff5ecf121519e4b`

Verify with Android SDK Build Tools:

```sh
apksigner verify --verbose --print-certs S2N-v0.4.0-beta.1-android-universal.apk
```

Compare the certificate digest above and the APK hash in `SHA256SUMS.txt`. The
certificate digest is also recorded in [android/SIGNING_CERTIFICATE.sha256](https://github.com/cavwic/s2n-connect/blob/main/android/SIGNING_CERTIFICATE.sha256).
