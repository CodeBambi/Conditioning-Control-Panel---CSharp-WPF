#!/usr/bin/env bash
# Runs the X11 overlay shim's probe inside a THROWAWAY compositor.
#
# Never run overlay/input probes against a live desktop session. During the research for this
# work a probe called a Qt-internal D-Bus interface with hand-marshalled arguments and aborted
# kwin_wayland, which killed the user's mail client, file sync and a browser helper along with
# it. Nothing here is more trustworthy than that probe was.
#
# --virtual renders to an offscreen framebuffer, so this compositor has no window on the real
# desktop at all: a crash costs a process, and nothing is visible while it runs.
set -u

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
KWIN_SOCKET="ccp-probe-$$"

command -v kwin_wayland >/dev/null || { echo "kwin_wayland not installed"; exit 127; }

before="$(ls /tmp/.X11-unix 2>/dev/null | sort)"

# env -u is the safety interlock: with WAYLAND_DISPLAY or DISPLAY visible, kwin nests inside the
# live session instead of taking the virtual backend, and a crash takes the real desktop down.
env -u WAYLAND_DISPLAY -u DISPLAY \
    kwin_wayland --virtual --width 1600 --height 1000 --xwayland --socket="$KWIN_SOCKET" \
    >/tmp/ccp-kwin-probe.log 2>&1 &
KWIN_PID=$!
trap 'kill "$KWIN_PID" 2>/dev/null; wait "$KWIN_PID" 2>/dev/null' EXIT

NESTED=""
for _ in $(seq 1 40); do
  sleep 0.5
  kill -0 "$KWIN_PID" 2>/dev/null || { echo "nested kwin died:"; tail -20 /tmp/ccp-kwin-probe.log; exit 1; }
  after="$(ls /tmp/.X11-unix 2>/dev/null | sort)"
  new="$(comm -13 <(echo "$before") <(echo "$after") | head -1)"
  [ -n "$new" ] && { NESTED=":${new#X}"; break; }
done
[ -n "$NESTED" ] || { echo "nested Xwayland never appeared:"; tail -20 /tmp/ccp-kwin-probe.log; exit 1; }

echo "nested compositor pid $KWIN_PID, Xwayland display $NESTED"
echo

# With arguments, run those inside the nested compositor instead - useful for running a
# known-good C probe next to the shim's own, to tell "the shim is wrong" apart from "this
# sandbox cannot score it".
if [ "$#" -gt 0 ]; then
  DISPLAY="$NESTED" WAYLAND_DISPLAY="" "$@"
  exit $?
fi

# Only the .NET 10 runtime is installed on some dev boxes while everything here targets
# net8.0, so roll forward rather than pinning the machine to an extra runtime.
DISPLAY="$NESTED" WAYLAND_DISPLAY="" DOTNET_ROLL_FORWARD=LatestMajor \
  dotnet run --project "$ROOT/CCP.Avalonia/CCP.Avalonia.csproj" -c Release --no-build -- --x11-probe
