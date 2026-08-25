#!/usr/bin/env bash
# CCP greenfield publish — Linux/WSL (release-publish-gates.md §1).
# Self-contained single-file per RID. Artifact naming DERIVES from the one version
# authority (client/Directory.Build.props) via `dotnet msbuild -getProperty:Version` —
# never a hardcoded or reparsed version string. Run from a native ext4 dir (never
# /mnt/e — a repeatedly proven failure). Usage:
#   ./publish.sh [linux-x64]
set -euo pipefail

RID="${1:-linux-x64}"
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
CSPROJ="$ROOT/src/CcpClient.Desktop/CcpClient.Desktop.csproj"

VERSION="$(dotnet msbuild "$CSPROJ" -nologo -getProperty:Version | tr -d '[:space:]')"
[ -n "$VERSION" ] || { echo "FAIL: Version property empty — the one authority (client/Directory.Build.props) is broken"; exit 1; }

NAME="CcpClient.Desktop-$VERSION-$RID"
OUT="$ROOT/artifacts/publish/$NAME"
# Always publish to a CLEAN output dir: an incremental `dotnet publish` into an existing
# single-file output dir silently DROPS the native sidecars (libSkiaSharp/libHarfBuzzSharp)
# and the app then dies with a native-load failure at startup.
rm -rf "$OUT"
dotnet publish "$CSPROJ" -c Release -r "$RID" --self-contained true -p:PublishSingleFile=true -o "$OUT" --nologo

# REDISTRIBUTION OBLIGATIONS, checked against the real output directory rather than trusted.
# Both properties are carried by project-file wiring, and a build property that silently stops
# doing anything is the failure mode this whole area has: it leaves a green build and an artifact
# that is out of compliance. So the script that produces the artifact is where they are asserted.
#   THIRD-PARTY-NOTICES.md — Apache-2.0 §4 / LGPL-2.1 §1: the notices must accompany the
#   distribution; a file that stays in the repository discharges nothing.
#   LibVLCSharp.dll as a SIDECAR — LGPL-2.1 §6: single-file would fuse the LGPL assembly into the
#   apphost, and a user cannot substitute their own build of a fused assembly.
for REQUIRED in THIRD-PARTY-NOTICES.md LibVLCSharp.dll; do
  [ -f "$OUT/$REQUIRED" ] || {
    echo "FAIL: $REQUIRED is missing from $OUT — the artifact does not carry what its licences oblige (see client/THIRD-PARTY-NOTICES.md §4, §6)"
    exit 1
  }
done

echo "PUBLISHED $NAME"
echo "ARTIFACT $OUT"
