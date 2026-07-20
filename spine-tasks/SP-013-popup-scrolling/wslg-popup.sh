#!/usr/bin/env bash
# SP-013 Step 4 — popup demonstrator WSLg/X11 gate.
# Session facts only (SP-008 named limit: NO input automation on WSLg):
#   1. contract testCommand green on WSL2 (native-dir build, never /mnt/e)
#   2. popup renders (XGetImage captures, scale 1.0 and 1.5)
#   3. owner-monitor working-area capping + geometry observed (xprop -name / tree + app probes)
# _NET_CLIENT_LIST absence handled per port-lessons 2026-07-20: xprop -name resolves the
# window fresh per query (window ids went stale in an earlier id-based draft — the popup
# resizes/moves during its first seconds and early-captured ids die).
set -u -o pipefail
WT=/mnt/e/Code/Conditioning-Control-Panel/.worktrees/spine-20260720T022627/lane-1
DST=~/ccp-sp013
ART="$WT/spine-tasks/SP-013-popup-scrolling/evidence"
POPUP_GREP="Demo: Status Ticker"
POPUP_TITLE="Demo: Status Ticker — settings (demonstrator)"
DASH_TITLE="CCP Client"

echo "=== SP-013 popup demonstrator (WSLg/X11) ==="
echo "session facts: WAYLAND_DISPLAY=${WAYLAND_DISPLAY:-unset} DISPLAY=${DISPLAY:-unset} XDG_SESSION_TYPE=${XDG_SESSION_TYPE:-unset} kernel=$(uname -r)"

mkdir -p "$DST" "$ART"
rsync -a --delete --exclude 'bin/' --exclude 'obj/' --exclude 'spikes/' "$WT/client/" "$DST/client/"

cd "$DST/client"
echo "--- contract: dotnet build CcpClient.sln ---"
dotnet build CcpClient.sln -c Debug --nologo 2>&1 | tail -2 || exit 1
echo "--- contract: CcpClient.Tests ---"
dotnet test tests/CcpClient.Tests/CcpClient.Tests.csproj -c Debug --nologo 2>&1 | tail -3 || exit 1
echo "--- contract: CcpClient.HeadlessTests ---"
dotnet test tests/CcpClient.HeadlessTests/CcpClient.HeadlessTests.csproj -c Debug --nologo 2>&1 | tail -3 || exit 1

DLL="$DST/client/src/CcpClient.Desktop/bin/Debug/net10.0/CcpClient.Desktop.dll"
[ -f "$DLL" ] || { echo "FAIL: dll missing"; exit 1; }

echo "--- X11 session enumeration facts ---"
ROOT_W=$(xwininfo -root | awk '/Width:/ {print $2}')
ROOT_H=$(xwininfo -root | awk '/Height:/ {print $2}')
echo "[root] ${ROOT_W}x${ROOT_H}"
if xprop -root _NET_CLIENT_LIST >/tmp/sp013-ncl.txt 2>&1 && grep -q "0x" /tmp/sp013-ncl.txt; then
  echo "[taskbar] _NET_CLIENT_LIST present: $(cat /tmp/sp013-ncl.txt)"
else
  echo "[taskbar] _NET_CLIENT_LIST ABSENT on this X root (port-lessons 2026-07-20) — name-resolved queries instead"
fi
xprop -root _NET_WORKAREA 2>&1 | head -1 | sed 's/^/[workarea] /'

# Fresh tree line for the popup; absolute geometry is the trailing +X+Y field.
popup_geo() {
  local LINE
  LINE=$(xwininfo -root -tree 2>/dev/null | grep "$POPUP_GREP" | head -1) || return 1
  [ -n "$LINE" ] || return 1
  echo "$LINE" | grep -oE '[0-9]+x[0-9]+\+-?[0-9]+\+-?[0-9]+ +\+-?[0-9]+\+-?[0-9]+$'
}

run_session() {
  local SCALE="$1"
  echo "--- popup session at AVALONIA_GLOBAL_SCALE_FACTOR=$SCALE ---"
  export AVALONIA_GLOBAL_SCALE_FACTOR="$SCALE"
  local LOG=/tmp/sp013-app-$SCALE.log
  pkill -f "CcpClient[.]Desktop" 2>/dev/null; sleep 1
  dotnet "$DLL" --popup-demo >"$LOG" 2>&1 &
  local APP_PID=$!
  echo "app pid: $APP_PID"

  # Wait for the popup, then for its geometry to SETTLE (it resizes/moves over the
  # first ~2s: min-size placeholder -> capped -> positioned; early ids went stale).
  local GEO="" PREV="" STABLE=0
  for i in $(seq 1 24); do
    sleep 0.5
    GEO=$(popup_geo) || continue
    if [ "$GEO" = "$PREV" ]; then STABLE=$((STABLE+1)); else STABLE=0; fi
    PREV="$GEO"
    [ "$STABLE" -ge 2 ] && break
  done
  if [ -z "$GEO" ]; then echo "FAIL: popup window never appeared"; tail -20 "$LOG"; kill $APP_PID 2>/dev/null; exit 1; fi
  echo "[geometry] settled tree geo: $GEO"
  local PW PH PX PY REST
  REST=$(echo "$GEO" | sed -E 's/ +.*$//')          # WxH+px+py (parent-relative, unused)
  PW=$(echo "$GEO" | sed -E 's/^([0-9]+)x.*/\1/')
  PH=$(echo "$GEO" | sed -E 's/^[0-9]+x([0-9]+).*/\1/')
  PX=$(echo "$GEO" | sed -E 's/.* \+(-?[0-9]+)\+-?[0-9]+$/\1/')
  PY=$(echo "$GEO" | sed -E 's/.* \+-?[0-9]+\+(-?[0-9]+)$/\1/')
  echo "[geometry] popup ${PW}x${PH} @ root ${PX},${PY} (physical px)"

  echo "[xprop popup — name-resolved]"
  xprop -name "$POPUP_TITLE" _NET_WM_WINDOW_TYPE | sed 's/^/  /'
  xprop -name "$POPUP_TITLE" _NET_WM_STATE | sed 's/^/  /'
  xprop -name "$POPUP_TITLE" WM_TRANSIENT_FOR | sed 's/^/  /'
  xprop -name "$POPUP_TITLE" WM_NORMAL_HINTS | sed 's/^/  /'
  local OWNER_ID
  OWNER_ID=$(xprop -name "$POPUP_TITLE" WM_TRANSIENT_FOR | grep -oE '0x[0-9a-fA-F]+' || true)
  if [ -n "$OWNER_ID" ]; then
    local OWNER_LINE
    OWNER_LINE=$(xwininfo -root -tree | grep "^ *$OWNER_ID " | head -1 || true)
    echo "  transient-for => $OWNER_ID (${OWNER_LINE:-unknown window})"
    case "$OWNER_LINE" in *"$DASH_TITLE"*) echo "  [owner] PASS: WM_TRANSIENT_FOR names the dashboard window";; *) echo "  [owner] CHECK: transient-for is not the dashboard";; esac
  fi

  # App-side probes (stderr): the observable evidence channel, machine-readable here.
  grep "popup-probe:" "$LOG" | tail -1 | sed 's/^/[app-probe] /'
  grep "scroll-probe:" "$LOG" | tail -1 | sed 's/^/[app-probe] /'
  grep "layout-probe:" "$LOG" | tail -1 | sed 's/^/[app-probe] /'

  # Capping arithmetic: capDIP = min(640, 0.9 * rootH/scale); TALL desired exceeds the cap,
  # so expected popup physical height = round(capDIP * scale). Tolerance +-2 px.
  local EXPECT
  EXPECT=$(awk -v h="$ROOT_H" -v s="$SCALE" 'BEGIN{c=h/s*0.9; if (c>640) c=640; printf "%d", c*s+0.5}')
  echo "[capping] root ${ROOT_H} / scale ${SCALE} => expected capped popup height ~${EXPECT}px; measured ${PH}px"
  if [ "$PH" -le $((EXPECT+2)) ] && [ "$PH" -ge $((EXPECT-2)) ]; then
    echo "[capping] PASS: popup height == min(640 DIP, 0.9*workarea) of the owner monitor"
  else
    echo "[capping] FAIL: measured height outside expectation"
    kill $APP_PID 2>/dev/null; exit 1
  fi
  # Containment: popup fully inside the (single) owner working area.
  if [ "$PX" -ge 0 ] && [ "$PY" -ge 0 ] && [ $((PX+PW)) -le "$ROOT_W" ] && [ $((PY+PH)) -le "$ROOT_H" ]; then
    echo "[containment] PASS: popup inside owner working area ${ROOT_W}x${ROOT_H}"
  else
    echo "[containment] FAIL: popup escapes working area"
    kill $APP_PID 2>/dev/null; exit 1
  fi

  python3 "$WT/client/tools/verify/xgetimage.py" "$POPUP_GREP" "$ART/wslg-popup-scale$SCALE.bmp" \
    && echo "[capture] wslg-popup-scale$SCALE.bmp"

  # Graceful close: popup first (app must SURVIVE — ShutdownMode.OnMainWindowClose),
  # then the dashboard (app exits 0).
  python3 "$WT/client/tools/verify/wmclose.py" "$POPUP_GREP" --check | sed 's/^/[close] /'
  python3 "$WT/client/tools/verify/wmclose.py" "$POPUP_GREP" | sed 's/^/[close] /'
  sleep 1
  if kill -0 $APP_PID 2>/dev/null; then
    echo "[close] PASS: app survived popup WM_DELETE_WINDOW (popup is not the main window)"
  else
    echo "[close] FAIL: app died when the popup closed"; exit 1
  fi
  python3 "$WT/client/tools/verify/wmclose.py" "$DASH_TITLE" >/dev/null 2>&1
  for i in $(seq 1 20); do sleep 0.5; kill -0 $APP_PID 2>/dev/null || break; done
  if kill -0 $APP_PID 2>/dev/null; then echo "[close] FAIL: app alive after dashboard close"; kill $APP_PID; exit 1; fi
  wait $APP_PID; local RC=$?
  echo "[close] dashboard WM_DELETE_WINDOW => app exit code $RC"
  [ "$RC" -eq 0 ] || exit 1
}

run_session "1"
run_session "1.5"
unset AVALONIA_GLOBAL_SCALE_FACTOR || true
echo "=== SP-013 WSLg gate done ==="
