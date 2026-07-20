#!/usr/bin/env bash
# SP-012 Step 3 — dashboard window-behavior demonstrator (WSLg/X11, observation-only).
# Property-level procedures only (xprop/xwininfo/XGetImage/wmclose.py); input-injection
# procedures (drag resize, modality click) are not demonstrable on WSLg (no xdotool, no
# passwordless sudo — SP-007 named gate). Session facts, never backend claims.
set -u
WT=/mnt/e/Code/Conditioning-Control-Panel/.worktrees/spine-20260720T004519/lane-1
DST=~/ccp-sp012
ART="$WT/spine-tasks/SP-012-window-behavior-manifest/artifacts"

echo "=== SP-012 dashboard demonstrator (WSLg/X11) ==="
echo "session facts: WAYLAND_DISPLAY=$WAYLAND_DISPLAY DISPLAY=$DISPLAY XDG_SESSION_TYPE=${XDG_SESSION_TYPE:-unset} kernel=$(uname -r)"

mkdir -p "$DST"
rsync -a --delete --exclude 'bin/' --exclude 'obj/' --exclude 'spikes/' --exclude 'tools/' "$WT/client/" "$DST/client/"

cd "$DST/client"
echo "--- build ---"
dotnet build src/CcpClient.Desktop/CcpClient.Desktop.csproj -c Debug --nologo -v q 2>&1 | tail -2 || exit 1

DLL="$DST/client/src/CcpClient.Desktop/bin/Debug/net10.0/CcpClient.Desktop.dll"
[ -f "$DLL" ] || { echo "FAIL: dll missing"; exit 1; }

echo "--- launch ---"
dotnet "$DLL" >/tmp/sp012-app.log 2>&1 &
APP_PID=$!
echo "app pid: $APP_PID"

WID=""
for i in $(seq 1 40); do
  sleep 0.5
  WID=$(xprop -root _NET_CLIENT_LIST 2>/dev/null | tr ',' '\n' | grep -o '0x[0-9a-fA-F]*' | while read -r id; do
    name=$(xprop -id "$id" _NET_WM_NAME 2>/dev/null | head -1)
    case "$name" in *"CCP Client"*) echo "$id"; break;; esac
  done)
  [ -n "$WID" ] && break
done
if [ -z "$WID" ]; then echo "FAIL: window not found in _NET_CLIENT_LIST"; kill $APP_PID 2>/dev/null; exit 1; fi
sleep 2
echo "window id: $WID"

echo "--- properties (xprop) ---"
echo "[taskbar] _NET_CLIENT_LIST contains window: YES (found via it)"
xprop -id "$WID" _NET_WM_WINDOW_TYPE | sed 's/^/[window-type] /'
xprop -id "$WID" _NET_WM_STATE | sed 's/^/[topmost+modal] /'
xprop -id "$WID" WM_TRANSIENT_FOR | sed 's/^/[owner] /'
xprop -id "$WID" WM_NORMAL_HINTS | sed 's/^/[resize-hints] /'
ACTIVE_BEFORE=$(xprop -root _NET_ACTIVE_WINDOW)
echo "[activation] _NET_ACTIVE_WINDOW: $ACTIVE_BEFORE  ours: $WID"
echo "--- geometry (xwininfo) ---"
xwininfo -id "$WID" | grep -E "Absolute|Width|Height" | sed 's/^/[placement] /'
echo "--- capture (XGetImage) ---"
python3 "$WT/client/tools/verify/xgetimage.py" "CCP Client" "$ART/wslg-dashboard-window.bmp" && echo "[capture] wslg-dashboard-window.bmp"
echo "--- graceful close (WM_DELETE_WINDOW) ---"
python3 "$WT/client/tools/verify/wmclose.py" "CCP Client" --check && \
python3 "$WT/client/tools/verify/wmclose.py" "CCP Client" && \
  for i in $(seq 1 20); do sleep 0.5; kill -0 $APP_PID 2>/dev/null || break; done
if kill -0 $APP_PID 2>/dev/null; then echo "[close] FAIL: still alive after WM_DELETE_WINDOW"; kill $APP_PID; exit 1; fi
wait $APP_PID; RC=$?
echo "[close] WM_DELETE_WINDOW => process exit code $RC"
echo "=== WSLg demonstrator done ==="
