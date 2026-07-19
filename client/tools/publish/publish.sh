#!/usr/bin/env bash
# CCP greenfield publish — Linux/WSL (release-publish-gates.md §1).
# Self-contained single-file per RID. Artifact naming DERIVES from the one version
# authority (client/Directory.Build.props) via `dotnet msbuild -getProperty:Version` —
# never a hardcoded or reparsed version string. Run from a native ext4 dir (never
# /mnt/e — SP-005/007/008/009 pattern). Usage:
#   ./publish.sh [linux-x64]
set -euo pipefail

RID="${1:-linux-x64}"
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
CSPROJ="$ROOT/src/CcpClient.Desktop/CcpClient.Desktop.csproj"

VERSION="$(dotnet msbuild "$CSPROJ" -nologo -getProperty:Version | tr -d '[:space:]')"
[ -n "$VERSION" ] || { echo "FAIL: Version property empty — the one authority (client/Directory.Build.props) is broken"; exit 1; }

NAME="CcpClient.Desktop-$VERSION-$RID"
OUT="$ROOT/artifacts/publish/$NAME"
dotnet publish "$CSPROJ" -c Release -r "$RID" --self-contained true -p:PublishSingleFile=true -o "$OUT" --nologo

echo "PUBLISHED $NAME"
echo "ARTIFACT $OUT"
