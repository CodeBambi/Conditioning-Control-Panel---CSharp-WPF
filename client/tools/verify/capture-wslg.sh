#!/usr/bin/env bash
# CCP greenfield verification harness — tier 2 WSLg (Linux/X11) capture.
# Usage: ./capture-wslg.sh <dashboard|rail-door> <unselected|selected> [scale-factor]
# Runs against a native-dir build (never /mnt/e, a repeatedly proven failure). WSLg RAIL windows
# are invisible to Windows-side capture; XGetImage reads the real X window.
#
# Re-anchored: the demonstrator card this script used to drive is retired and the
# navigation shell replaced it, so the surface/state tokens follow checks.json —
# dashboard-card -> rail-door, unlit|lit -> unselected|selected.
#
# State drive: NONE, and it needs none. On cold start the shell opens on its default door
# (Studio), so the Studio door is already :checked and the Companion door is not. Each state
# is therefore capturable with ZERO input, which is exactly what WSLg's no-input-automation
# limit (no xdotool — a named gate) allows. The two states are read off two
# DIFFERENT doors of the same style rather than one door before and after a click; the
# Windows leg (capture.ps1) drives the click and is where that half of the evidence lives.
# Recorded, not hidden.
set -euo pipefail

SURFACE="${1:?surface: dashboard|rail-door}"
STATE="${2:?state: unselected|selected}"
SCALE="${3:-}"
HERE="$(cd "$(dirname "$0")" && pwd)"
DLL="$HERE/../../src/CcpClient.Desktop/bin/Debug/net10.0/CcpClient.Desktop.dll"
SETTINGS="$HOME/.config/CcpClient/settings.json"
ART="$HERE/artifacts"
LOG="$ART/wslg-app-stderr.log"
mkdir -p "$ART"

[ -f "$DLL" ] || { echo "FAIL: app not built: $DLL"; exit 1; }

case "$SURFACE" in
  dashboard|rail-door) ;;
  *) echo "FAIL: surface must be dashboard|rail-door (got '$SURFACE')"; exit 1 ;;
esac

# Which door carries the requested state on a cold start, with no input at all.
case "$STATE" in
  selected)   DOOR=studio ;;     # the default route: :checked when the shell opens
  unselected) DOOR=companion ;;  # never selected until something clicks it
  *) echo "FAIL: state must be unselected|selected (got '$STATE')"; exit 1 ;;
esac

# Deterministic start: remove the demonstrator settings file (demo store only). Nothing is
# SEEDED into it any more — the old `lit` drive wrote statusTickerEnabled, and the card that
# setting lit no longer exists.
mkdir -p "$(dirname "$SETTINGS")"
rm -f "$SETTINGS"

if [ -n "$SCALE" ]; then
  export AVALONIA_GLOBAL_SCALE_FACTOR="$SCALE"
fi

pkill -f "CcpClient[.]Desktop" 2>/dev/null || true
sleep 1
: > "$LOG"
dotnet "$DLL" 2>"$LOG" &
APP_PID=$!

# Poll to a DEADLINE, never a fixed sleep. This was `sleep 5` and it is the same rot the
# Windows leg already had removed: startup cost grows, the sleep expires early, and the
# harness reports a broken app when the app is healthy. The shell logs its rail layout probe
# on first layout, so that line IS the window existing — waiting for it is both correct and
# strictly faster on a warm run.
DEADLINE=$((SECONDS + 40))
PROBE=""
while [ "$SECONDS" -lt "$DEADLINE" ]; do
  if ! kill -0 "$APP_PID" 2>/dev/null; then
    echo "FAIL: app exited during startup before it laid out a window; stderr tail:"; tail -20 "$LOG"; exit 1
  fi
  PROBE="$(grep 'layout-probe:' "$LOG" | tail -1 || true)"
  [ -n "$PROBE" ] && break
  sleep 0.25
done
[ -n "$PROBE" ] || { pkill -f "CcpClient[.]Desktop" || true; echo "FAIL: no layout probe within 40s; stderr tail:"; tail -20 "$LOG"; exit 1; }

OUT="$ART/wslg-$SURFACE-$STATE.bmp"
if [ "$SURFACE" = "rail-door" ]; then
  # Door rect from the app's own layout probe (stderr, first layout): screen coords are X
  # root coordinates; DIP*scale = device pixels. One log line carries every door, so the
  # requested door's id is part of the pattern.
  PROBE_RE="door $DOOR ([0-9.]+)x([0-9.]+) DIP @ scale ([0-9.]+) @ screen (-?[0-9]+),(-?[0-9]+)"
  [[ "$PROBE" =~ $PROBE_RE ]] \
    || { pkill -f "CcpClient[.]Desktop" || true; echo "FAIL: layout probe for door '$DOOR' unreadable: $PROBE"; exit 1; }
  CW=$(awk "BEGIN{printf \"%d\", ${BASH_REMATCH[1]} * ${BASH_REMATCH[3]}}")
  CH=$(awk "BEGIN{printf \"%d\", ${BASH_REMATCH[2]} * ${BASH_REMATCH[3]}}")
  echo "probe: door $DOOR (${STATE}) ${BASH_REMATCH[1]}x${BASH_REMATCH[2]} DIP @ scale ${BASH_REMATCH[3]} @ screen ${BASH_REMATCH[4]},${BASH_REMATCH[5]}"
  python3 "$HERE/xgetimage.py" "CCP Client" "$OUT" --crop "${BASH_REMATCH[4]}" "${BASH_REMATCH[5]}" "$CW" "$CH"
else
  python3 "$HERE/xgetimage.py" "CCP Client" "$OUT"
fi

pkill -f "CcpClient[.]Desktop" || true
echo "CAPTURE: $OUT"
echo "CAPTURE PASS"
