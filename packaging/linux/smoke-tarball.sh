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
test -f "$app/io.github.CodeBambi.ConditioningControlPanel.desktop"
test -f "$app/io.github.CodeBambi.ConditioningControlPanel.png"
# Sandboxed profile: never the user's real settings or keyring.
export CCP_USERDATA_DIR=$tmp/userdata XDG_CONFIG_HOME=$tmp/config
CCP_SMOKE_BUNDLED_VOSK=1 timeout 300 "$app/CCP.Avalonia" --smoke
