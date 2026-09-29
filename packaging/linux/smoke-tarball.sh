#!/usr/bin/env bash
# Extract a built tarball to a temp dir and run --smoke from it, requiring the bundled Vosk model to load.
#   bash packaging/linux/smoke-tarball.sh <ConditioningControlPanel-*-linux-x64.tar.gz>
set -euo pipefail
tmp=$(mktemp -d)
trap 'rm -rf "$tmp"' EXIT
tar -xzf "$1" -C "$tmp"
app=$tmp/ConditioningControlPanel
test -x "$app/CCP.Avalonia"
test -f "$app/libvosk.so"
id=io.github.CodeBambi.ConditioningControlPanel
test -f "$app/LICENSE"
test -f "$app/share/applications/$id.desktop"
test -f "$app/share/icons/hicolor/256x256/apps/$id.png"
test -f "$app/share/metainfo/$id.metainfo.xml"
# Sandboxed profile: never the user's real settings or keyring.
export CCP_USERDATA_DIR=$tmp/userdata XDG_CONFIG_HOME=$tmp/config
CCP_SMOKE_BUNDLED_VOSK=1 timeout 300 "$app/CCP.Avalonia" --smoke
