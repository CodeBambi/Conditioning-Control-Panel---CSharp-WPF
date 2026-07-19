#!/usr/bin/env bash
# CCP greenfield verification harness — tier 2 WSLg (Linux/X11) capture (SP-008).
# Usage: ./capture-wslg.sh <dashboard|dashboard-card> <unlit|lit> [scale-factor]
# Runs against a native-dir build (never /mnt/e — SP-005/007 pattern). WSLg RAIL windows
# are invisible to Windows-side capture; XGetImage reads the real X window (SP-007).
# State drive: `lit` pre-seeds the demo settings file so the restart-restore path lights
# the card (a REAL user path — SP-007 proven); WSLg has no input automation (no xdotool,
# SP-007 named gate) so right-click cannot be synthesized here. Recorded, not hidden.
set -euo pipefail

SURFACE="${1:?surface: dashboard|dashboard-card}"
STATE="${2:?state: unlit|lit}"
SCALE="${3:-}"
HERE="$(cd "$(dirname "$0")" && pwd)"
DLL="$HERE/../../src/CcpClient.Desktop/bin/Debug/net10.0/CcpClient.Desktop.dll"
SETTINGS="$HOME/.config/CcpClient/settings.json"
ART="$HERE/artifacts"
LOG="$ART/wslg-app-stderr.log"
mkdir -p "$ART"

[ -f "$DLL" ] || { echo "FAIL: app not built: $DLL"; exit 1; }

# Deterministic state via the demo settings file (demo store only).
mkdir -p "$(dirname "$SETTINGS")"
if [ "$STATE" = "lit" ]; then
  printf '{"statusTickerEnabled": true}\n' > "$SETTINGS"
else
  rm -f "$SETTINGS"
fi

if [ -n "$SCALE" ]; then
  export AVALONIA_GLOBAL_SCALE_FACTOR="$SCALE"
fi

pkill -f "CcpClient[.]Desktop" 2>/dev/null || true
sleep 1
dotnet "$DLL" 2>"$LOG" &
sleep 5

OUT="$ART/wslg-$SURFACE-$STATE.bmp"
if [ "$SURFACE" = "dashboard-card" ]; then
  # Card rect from the app's own layout probe (stderr, first layout): screen coords are
  # X root coordinates; DIP*scale = device pixels.
  PROBE="$(grep 'layout-probe:' "$LOG" | tail -1 || true)"
  [[ "$PROBE" =~ card\ ([0-9.]+)x([0-9.]+)\ DIP\ @\ scale\ ([0-9.]+)\ @\ screen\ (-?[0-9]+),(-?[0-9]+) ]] \
    || { pkill -f "CcpClient[.]Desktop" || true; echo "FAIL: layout probe unreadable: $PROBE"; exit 1; }
  CW=$(awk "BEGIN{printf \"%d\", ${BASH_REMATCH[1]} * ${BASH_REMATCH[3]}}")
  CH=$(awk "BEGIN{printf \"%d\", ${BASH_REMATCH[2]} * ${BASH_REMATCH[3]}}")
  echo "probe: $PROBE"
  python3 "$HERE/xgetimage.py" "CCP Client" "$OUT" --crop "${BASH_REMATCH[4]}" "${BASH_REMATCH[5]}" "$CW" "$CH"
else
  python3 "$HERE/xgetimage.py" "CCP Client" "$OUT"
fi

pkill -f "CcpClient[.]Desktop" || true
echo "CAPTURE PASS"
