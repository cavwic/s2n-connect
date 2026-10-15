# GitHub publishing guide

## CI and release layout

`build.yml` runs desktop regression checks and six self-contained desktop builds.
It also calls `android.yml`, which builds the pinned libbox core, runs Android
lint/unit checks, assembles a debug APK, and runs UI smoke tests on an Android 15
(API 35) x86_64 emulator. The smoke tests exercise screens, grant VPN permission,
and start and stop a real TUN with a direct-only synthetic fixture. They do not
connect to a VPS or test outbound proxy connectivity.

`codeql.yml` analyzes C# and Java/Kotlin. A public repository is allowed to run its
initial scan even before the code-scanning API contains results. Private projects
retain an availability check and explicitly report skipped analysis. Inspect the
analysis jobs themselves; a passed capability check is not an analysis result.

## Android toolchain and core

The native project is under `android/`, with application ID `com.cavwic.s2n`,
minimum API 26 (Android 8.0), and a universal ARM64/x86_64 APK. The pinned build
uses JDK 17, Gradle 9.6.0, AGP 9.4.0 with built-in Kotlin, SDK 36, Go 1.26.8,
and NDK 28.0.13004108.

From the repository root, `bash android/scripts/build-libbox.sh` builds
`android/app/libs/libbox.aar` from official sing-box v1.14.3 commit
`7054cac5124465ffdb145870d7c01885566711b1`. The AAR is generated in CI and is not
committed. Its cache key includes the core commit, build-script hash, and Go
version; both native ABIs are checked before the core is reused.

Local debug build:

```bash
bash android/scripts/build-libbox.sh
cd android
bash ./gradlew :app:lintDebug :app:testDebugUnitTest :app:assembleDebug
```

`connectedDebugAndroidTest` requires a running emulator or device. CI uses a
normal `google_apis` image because system VPN authorization is outside the app;
ATD images remove SystemUI and are unsuitable for that check.

## Android release signing

Distribution APKs must use the same privately stored release key across updates.
Generate and back up that key once, outside the repository. Do not commit a
keystore, passwords, a private profile, or a CI-generated signing file.

Configure these repository Actions secrets before pushing a release tag:

| Secret | Value |
| --- | --- |
| `ANDROID_KEYSTORE_BASE64` | Base64 of the private PKCS12 keystore |
| `ANDROID_KEYSTORE_PASSWORD` | Keystore password |
| `ANDROID_KEY_ALIAS` | Signing-key alias |
| `ANDROID_KEY_PASSWORD` | Signing-key password |

The reusable workflow receives these secrets only from the tag-release caller.
Main/PR checks create a debug artifact without release-key access. The release
job refuses missing secrets, decodes the keystore into `RUNNER_TEMP`, passes its
path as `ANDROID_KEYSTORE_PATH`, and removes the temporary file when signing
finishes. Gradle reads that path and the three password/alias environment values.
`apksigner verify --verbose --print-certs` verifies the signed APK; certificate
fingerprints are public information, while the private signing key stays secret.

Debug APKs are CI testing artifacts. Only the non-debuggable, release-key-signed
universal APK is included in the GitHub release. Desktop archives remain unsigned;
Windows Authenticode and macOS Developer ID/notarization are separate work. See
[SIGNING.md](SIGNING.md).

## Publish a beta

1. Set the same version in the desktop project, Android `versionName`, and the
   first CHANGELOG section. Increase Android `versionCode` for every distributed
   update. For this release, use `0.3.0-beta.1` and tag `v0.3.0-beta.1`.
2. Push main and wait for desktop regression checks, all six desktop builds, the
   Android build, and emulator smoke tests to pass. Review both CodeQL results.
3. Confirm the Android signing secrets and private key backup are available.
4. Create and push the matching version tag.
5. The release workflow repeats version/regression checks, builds all seven
   packages, and runs Android smoke tests. It verifies every checksum before
   creating a draft release, uploads all assets, and publishes only after the
   complete set is present. Failed uploads leave a draft that can be retried;
   published releases are never overwritten by this workflow.

The complete release has **15 assets**:

- Six desktop archives and six individual `.sha256` files.
- `S2N-v0.3.0-beta.1-android-universal.apk` and its `.sha256` file.
- `SHA256SUMS.txt` covering all seven packages.

Beta status remains in the version tag and release name. Releases use GitHub's
Latest classification so the repository sidebar links directly to the current
version; the platform's prerelease flag is false because GitHub cannot mark a
prerelease as Latest. Public repositories generate provenance attestations
for desktop archives and the APK. Private repositories require Enterprise Cloud
support and explicit `ENABLE_PRIVATE_ATTESTATIONS=true`; otherwise the workflow
summary records the skipped attestation. Checksums, provenance, Android signing,
and desktop platform signatures have separate purposes.

Before changing repository visibility, review the complete Git history and
untracked content for credentials and private configuration. The Android
app/libbox combination is GPL-3.0-or-later; retain the corresponding source,
pinned-core build instructions, and license notices described in
[THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md). Desktop licensing remains MIT.
