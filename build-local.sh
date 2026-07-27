#!/usr/bin/env bash
set -euo pipefail
RID="${1:-linux-x64}"
dotnet restore
dotnet build -c Release
dotnet publish src/S2N.App/S2N.App.csproj \
  -c Release \
  -r "$RID" \
  --self-contained true \
  -o "artifacts/$RID"
echo "Output: artifacts/$RID"
