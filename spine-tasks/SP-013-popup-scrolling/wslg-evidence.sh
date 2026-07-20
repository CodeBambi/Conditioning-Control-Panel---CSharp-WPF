#!/usr/bin/env bash
# SP-013 WSLg/X11 evidence: popup render + owner-monitor working-area capping + geometry
# as SESSION FACTS (no input automation — SP-008 named limit; the popup opens itself via
# --popup-demo). Two runs: default scale and AVALONIA_GLOBAL_SCALE_FACTOR=1.5 (mixed-scale
# evidence — the env override is X11-only per SP-007 record :53). Wayland stays §5.1
# untouched (WSLg is XWayland). Run inside WSL against the native-dir copy (never /mnt/e).
set -uo pipefail

CLIENT="$HOME/ccp-sp013/client"
DLL="$CLIENT/src/CcpClient.Desktop/bin/Debug/net10.0/CcpClient.Desktop.dll"
ART="$CLIENT/tools/verify/artifacts"
SETTINGS="$HOME/.config/CcpClient/settings.json"
mkdir -p "$ART"
[ -f "$DLL" ] || { echo "FAIL: app not built: $DLL"; exit 1; }

run_capture() {
  local tag="$1" scale="${2:-}"
  rm -f "$SETTINGS"
  pkill -f "CcpClient[.]Desktop" 2>/dev/null; sleep 1
  local log="$ART/wslg-popup-stderr-$tag.log"
  if [ -n "$scale" ]; then
    AVALONIA_GLOBAL_SCALE_FACTOR="$scale" dotnet "$DLL" --popup-demo 2>"$log" &
  else
    dotnet "$DLL" --popup-demo 2>"$log" &
  fi
  sleep 7
  local out="$ART/wslg-popup-tall-$tag.bmp"
  python3 "$CLIENT/tools/verify/xgetimage.py" "settings (demonstrator)" "$out"
  echo "--- stderr probes ($tag) ---"
  grep -E "popup-probe:|scroll-probe:" "$log" | tail -6
  pkill -f "CcpClient[.]Desktop" 2>/dev/null
  sleep 1
}

run_capture default ""
run_capture scale1p5 "1.5"
echo "WSLG EVIDENCE DONE"
