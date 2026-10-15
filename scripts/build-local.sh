#!/usr/bin/env bash
set -euo pipefail
S2N_REPOSITORY_ROOT="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
cd -- "$S2N_REPOSITORY_ROOT"
RID="${1:-linux-x64}"
dotnet restore
dotnet build -c Release
dotnet publish src/S2N.App/S2N.App.csproj \
  -c Release \
  -r "$RID" \
  --self-contained true \
  -o "artifacts/$RID"
echo "Output: artifacts/$RID"
