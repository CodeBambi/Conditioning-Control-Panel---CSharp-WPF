#!/usr/bin/env bash
# Runs `--panic-check` against a ROOTFUL Xwayland inside a throwaway kwin --virtual (see
# x11-overlay-probe.sh for why input probes never touch the live desktop).
#
# Rootful and with LIBEI_SOCKET unset on purpose: a KWin-launched Xwayland forwards XTest through
# libei to the compositor, which hands the key to its focused surface and never back into the X
# server, so a synthetic press proves nothing there (measured: KWin 6.7 / Xwayland 24.1, no raw
# event, no core event). Without EI, XTest enters the X server's own input path, which is the path
# a physical key on a focused X11 window takes.
set -u
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
SOCK="ccp-panic-$$"
LOG="$(mktemp)"
command -v kwin_wayland >/dev/null && command -v Xwayland >/dev/null || { echo "kwin_wayland/Xwayland not installed"; exit 127; }

env -u WAYLAND_DISPLAY -u DISPLAY kwin_wayland --virtual --width 1600 --height 1000 --socket="$SOCK" >"$LOG" 2>&1 &
KWIN=$!
trap 'kill $XW $KWIN 2>/dev/null; wait 2>/dev/null; rm -f "$LOG"' EXIT
XW=""
for _ in $(seq 1 40); do [ -S "${XDG_RUNTIME_DIR:-/run/user/$(id -u)}/$SOCK" ] && break; sleep 0.25; done
for N in $(seq 20 60); do [ -e "/tmp/.X11-unix/X$N" ] || break; done
env -u LIBEI_SOCKET -u DISPLAY WAYLAND_DISPLAY="$SOCK" Xwayland ":$N" -geometry 1600x1000 >>"$LOG" 2>&1 &
XW=$!
for _ in $(seq 1 40); do [ -e "/tmp/.X11-unix/X$N" ] && break; sleep 0.25; done
echo "throwaway kwin $KWIN, rootful Xwayland :$N"

DISPLAY=":$N" WAYLAND_DISPLAY="" \
  dotnet run --project "$ROOT/CCP.Avalonia/CCP.Avalonia.csproj" -c Release --no-build -- --panic-check "$@"
