#!/usr/bin/env bash
# SPDX-License-Identifier: GPL-3.0-or-later
# Build the official sing-box Go API; do not download unverified third-party AARs.
set -euo pipefail

CORE_TAG="v1.14.3"
CORE_COMMIT="7054cac5124465ffdb145870d7c01885566711b1"
GO_VERSION="go1.26.8"
GOMOBILE_VERSION="v0.1.13"
NDK_VERSION="28.0.13004108"
ANDROID_API="26"
TARGETS="android/arm64,android/amd64"
# VLESS/REALITY is part of the base registry. gVisor provides the TUN stack,
# uTLS supplies the REALITY fingerprint, and QUIC supports configured transports.
# The two compatibility tags and -checklinkname=0 match upstream linker policy.
# Naive/Cronet, Tailscale, WireGuard, USB/IP, OpenVPN and OpenConnect are excluded.
BUILD_TAGS="with_gvisor,with_utls,with_quic,badlinkname,tfogo_checklinkname0"

if [[ "${1:-}" == "--print-versions" ]]; then
  printf '%s\n' "CORE_TAG=$CORE_TAG" "CORE_COMMIT=$CORE_COMMIT" \
    "GO_VERSION=$GO_VERSION" "GOMOBILE_VERSION=$GOMOBILE_VERSION" \
    "NDK_VERSION=$NDK_VERSION" "ANDROID_API=$ANDROID_API" \
    "TARGETS=$TARGETS" "BUILD_TAGS=$BUILD_TAGS"
  exit 0
fi
if [[ $# -ne 0 ]]; then
  printf 'Usage: bash android/scripts/build-libbox.sh [--print-versions]\n' >&2
  exit 2
fi

ANDROID_PROJECT="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
BUILD_ROOT="$ANDROID_PROJECT/.core-build"
SOURCE_ROOT="$BUILD_ROOT/source"
OUTPUT="$ANDROID_PROJECT/app/libs/libbox.aar"

export GOTOOLCHAIN=local
if [[ "$(go env GOVERSION)" != "$GO_VERSION" ]]; then
  printf 'Expected %s. Install the pinned Go toolchain before building.\n' "$GO_VERSION" >&2
  exit 1
fi
JAVA_COMMAND="${JAVA_HOME:+$JAVA_HOME/bin/}java"
if ! "$JAVA_COMMAND" -version 2>&1 | grep -Eq '(openjdk|java) version "17[.]'; then
  printf 'OpenJDK 17 is required.\n' >&2
  exit 1
fi
export ANDROID_HOME="${ANDROID_HOME:-${ANDROID_SDK_ROOT:-}}"
if [[ -z "$ANDROID_HOME" || ! -f "$ANDROID_HOME/licenses/android-sdk-license" ]]; then
  printf 'An Android SDK with accepted licenses is required.\n' >&2
  exit 1
fi
export ANDROID_NDK_HOME="$ANDROID_HOME/ndk/$NDK_VERSION"
export NDK="$ANDROID_NDK_HOME"
if [[ ! -f "$ANDROID_NDK_HOME/source.properties" ]] || \
   ! grep -Eq "Pkg.Revision[[:space:]]*=[[:space:]]*$NDK_VERSION$" "$ANDROID_NDK_HOME/source.properties"; then
  printf 'Install the pinned NDK with sdkmanager "ndk;%s".\n' "$NDK_VERSION" >&2
  exit 1
fi

mkdir -p "$BUILD_ROOT" "$(dirname -- "$OUTPUT")"
if [[ ! -d "$SOURCE_ROOT/.git" ]]; then
  git clone --depth 1 --branch "$CORE_TAG" https://github.com/SagerNet/sing-box.git "$SOURCE_ROOT"
fi
if [[ "$(git -C "$SOURCE_ROOT" rev-parse HEAD)" != "$CORE_COMMIT" ]]; then
  printf 'The sing-box checkout does not match the pinned source commit.\n' >&2
  exit 1
fi
if [[ -n "$(git -C "$SOURCE_ROOT" status --porcelain --untracked-files=no)" ]]; then
  printf 'Refusing to build modified sing-box sources.\n' >&2
  exit 1
fi

cd "$SOURCE_ROOT"
# Let bind resolve only the selected build's dependencies. A blanket download
# would also fetch the deliberately excluded Cronet/Tailscale platform bundles.
go install "github.com/sagernet/gomobile/cmd/gomobile@$GOMOBILE_VERSION"
go install "github.com/sagernet/gomobile/cmd/gobind@$GOMOBILE_VERSION"
export PATH="$(go env GOPATH)/bin:$PATH"
gomobile bind -v \
  -target="$TARGETS" \
  -androidapi="$ANDROID_API" \
  -javapkg=io.nekohasekai \
  -libname=box \
  -tags="$BUILD_TAGS" \
  -trimpath \
  -buildvcs=false \
  -ldflags="-X github.com/sagernet/sing-box/constant.Version=${CORE_TAG#v} -X runtime.godebugDefault=multipathtcp=0,tlssha1=1 -checklinkname=0 -s -w -buildid=" \
  -o "$OUTPUT" \
  ./experimental/libbox

# Verify that both promised ABIs and the Java binding are present before caching.
unzip -l "$OUTPUT" | grep -F 'jni/arm64-v8a/libbox.so' >/dev/null
unzip -l "$OUTPUT" | grep -F 'jni/x86_64/libbox.so' >/dev/null
unzip -l "$OUTPUT" | grep -F 'classes.jar' >/dev/null
sha256sum "$OUTPUT" > "$OUTPUT.sha256"
printf 'Built official sing-box %s (%s) for %s\n' "$CORE_TAG" "$CORE_COMMIT" "$TARGETS"
