#!/usr/bin/env bash
set -euo pipefail
S2N_REPOSITORY_ROOT="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
cd -- "$S2N_REPOSITORY_ROOT"
RID="${1:-linux-x64}"
case "$RID" in
  win-x64|win-arm64|linux-x64|linux-arm64|osx-x64|osx-arm64) ;;
  *) echo "Unsupported runtime identifier: $RID" >&2; exit 1 ;;
esac
S2N_OUTPUT="$S2N_REPOSITORY_ROOT/artifacts/$RID"
if [[ -L "$S2N_REPOSITORY_ROOT/artifacts" || -L "$S2N_OUTPUT" ]]; then
  echo "Publish output cannot use symbolic links." >&2; exit 1
fi
if [[ -e "$S2N_OUTPUT" ]] && { [[ ! -d "$S2N_OUTPUT" ]] || [[ -n "$(find "$S2N_OUTPUT" -mindepth 1 -maxdepth 1 -print -quit)" ]]; }; then
  echo "Use an empty artifacts/$RID directory; publishing over old files retains unused DLLs." >&2
  exit 1
fi
dotnet restore
dotnet build -c Release
dotnet publish src/S2N.App/S2N.App.csproj \
  -c Release \
  -r "$RID" \
  --self-contained true \
  -o "$S2N_OUTPUT"
for name in S2N.dll S2N.deps.json S2N.runtimeconfig.json; do
  [[ -f "$S2N_OUTPUT/$name" ]] || { echo "Missing published file: $name" >&2; exit 1; }
done
# This directory was empty before publish. Remove only generated debugger files.
find "$S2N_OUTPUT" -type f \( -iname '*.pdb' -o -iname '*.dbg' -o -iname '*.debug' -o -iname '*.mdb' \) -delete
echo "Output: artifacts/$RID"
