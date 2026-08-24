#!/usr/bin/env bash
# CCP greenfield verification harness — tier 2 WSLg (Linux/X11) capture.
# Usage: ./capture-wslg.sh <dashboard|rail-door> <unselected|selected> [--click] [scale-factor]
# Runs against a native-dir build (never /mnt/e, a repeatedly proven failure). WSLg RAIL windows
# are invisible to Windows-side capture; XGetImage reads the real X window.
#
# Re-anchored: the demonstrator card this script used to drive is retired and the
# navigation shell replaced it, so the surface/state tokens follow checks.json —
# dashboard-card -> rail-door, unlit|lit -> unselected|selected.
#
# WHICH BACKEND THIS IS, because the answer is not the one the display variables suggest.
# WSLg publishes BOTH wayland-0 and X0, and this harness is an X11 harness in every run: the X
# server it talks to is XWayland (vendor string "Microsoft Corporation", release 12010000)
# hosted by WSLg's nested Weston, and there is no desktop environment behind it — not GNOME,
# not KDE. Avalonia 12.1.1 has no Wayland backend at all: the only Linux windowing assembly it
# ships is Avalonia.X11.dll, so even with WAYLAND_DISPLAY set the client is an X11 client.
# A reading taken here is therefore an X11-through-XWayland reading and must never be reported
# as a Wayland one.
#
# SOFTWARE PRESENTATION IS FORCED HERE, and only here. Every Linux capture this port ever took
# before 2026-08-24 was a single colour, because Avalonia presenting through GL leaves the
# window's contents in a GPU surface the X server does not track, so XGetImage on that drawable
# returns the window background. CCP_X11_SOFTWARE=1 (Program.cs) selects
# X11RenderingMode.Software so a capture can see the window. Measured either side of the switch
# on this machine: the same binary, the same window, reads 1 distinct colour across 836,000
# pixels without it and 3,083 with it. It is a HARNESS opt-in and must stay one — the defect is
# in what a capture can see, not in what a user sees, and forcing software presentation on every
# Linux run would buy harness convenience with a real performance regression. State the caveat
# with the evidence: what this script photographs is the software-presented frame.
#
# INPUT AUTOMATION EXISTS AFTER ALL — this header used to say it did not, and that was wrong.
# The old text read "WSLg's no-input-automation limit (no xdotool — a named gate)", and the
# missing package is real: xdotool, xwd, wmctrl and scrot are all absent from the image. But the
# X server advertises XTEST 2.2 and libXtst.so.6 is already installed, so synthetic input needs
# no new package, exactly as XGetImage needed none. `--click` drives it through xinput.py.
#
# STATE DRIVE, both ways, because they prove different things.
#   without --click  Cold start, zero gestures. The shell opens on its default door (Studio), so
#                    Studio is :checked and Companion is not, and each state is readable off a
#                    DIFFERENT door of the same style. This is the cheaper reading and it proves
#                    only presentation.
#   with --click     One real left-click through XTEST, so a rail-door pair is read off the SAME
#                    door before and after a gesture — the design capture.ps1 uses on Windows,
#                    and the only reading that proves Linux INPUT reaches the control. For
#                    `dashboard` it clicks the System door, because capture.ps1's `dashboard`
#                    photographs the System route (capture.ps1:2917-2926) and a cold-start Linux
#                    capture photographs Studio; without the drive the two legs measure different
#                    pages under one surface name and the named check is not comparable at all.
#                    Measured, all three: dashboard-background scores 0.671 on Linux/Studio and
#                    FAILS, 0.973 on Linux/System, 0.982 on Windows/System.
#                    The drive is probe-derived like the crop, so it is refused at a scale the
#                    once-logged probe cannot describe — see the staleness guard below.
set -euo pipefail

SURFACE="${1:?surface: dashboard|rail-door}"
STATE="${2:?state: unselected|selected}"
shift 2
DRIVE=none
SCALE=""
for arg in "$@"; do
  case "$arg" in
    --click) DRIVE=click ;;
    *) SCALE="$arg" ;;
  esac
done

HERE="$(cd "$(dirname "$0")" && pwd)"
DLL="$HERE/../../src/CcpClient.Desktop/bin/Debug/net10.0/CcpClient.Desktop.dll"
VERIFY_DLL="$HERE/CcpVerify/bin/Debug/net10.0/CcpVerify.dll"
SETTINGS="$HOME/.config/CcpClient/settings.json"
ART="$HERE/artifacts"
LOG="$ART/wslg-app-stderr.log"
TITLE="CCP Client"
mkdir -p "$ART"

[ -f "$DLL" ] || { echo "FAIL: app not built: $DLL"; exit 1; }
[ -f "$VERIFY_DLL" ] || { echo "FAIL: the capture-vacuity gate is not built: $VERIFY_DLL"; exit 1; }

case "$SURFACE" in
  dashboard|rail-door) ;;
  *) echo "FAIL: surface must be dashboard|rail-door (got '$SURFACE')"; exit 1 ;;
esac
case "$STATE" in
  unselected|selected) ;;
  *) echo "FAIL: state must be unselected|selected (got '$STATE')"; exit 1 ;;
esac

# Which door carries the requested state, and how it gets there.
#   cold start: two different doors, no gesture. --click: one door, one gesture.
if [ "$DRIVE" = click ]; then
  DOOR=companion
else
  case "$STATE" in
    selected)   DOOR=studio ;;     # the default route: :checked when the shell opens
    unselected) DOOR=companion ;;  # never selected until something clicks it
  esac
fi

echo "backend: X11 via XWayland on DISPLAY=${DISPLAY:-unset} (WSLg nested Weston, no desktop environment)"
echo "surface: $SURFACE  state: $STATE  drive: $DRIVE  door: $DOOR"

# Deterministic start: remove the demonstrator settings file (demo store only). Nothing is
# SEEDED into it any more — the old `lit` drive wrote statusTickerEnabled, and the card that
# setting lit no longer exists.
mkdir -p "$(dirname "$SETTINGS")"
rm -f "$SETTINGS"

if [ -n "$SCALE" ]; then
  export AVALONIA_GLOBAL_SCALE_FACTOR="$SCALE"
fi
export CCP_X11_SOFTWARE=1

pkill -f "CcpClient[.]Desktop" 2>/dev/null || true
sleep 1
: > "$LOG"
dotnet "$DLL" >/dev/null 2>"$LOG" </dev/null &
APP_PID=$!
trap 'kill "$APP_PID" 2>/dev/null || true' EXIT

alive_or_die() {
  if ! kill -0 "$APP_PID" 2>/dev/null; then
    echo "FAIL: $1; stderr tail:"; tail -20 "$LOG"; exit 1
  fi
}

# Poll to a DEADLINE, never a fixed sleep. This was `sleep 5` and it is the same rot the
# Windows leg already had removed: startup cost grows, the sleep expires early, and the
# harness reports a broken app when the app is healthy. The shell logs its rail layout probe
# on first layout, so that line IS the window existing — waiting for it is both correct and
# strictly faster on a warm run.
DEADLINE=$((SECONDS + 40))
PROBE=""
while [ "$SECONDS" -lt "$DEADLINE" ]; do
  alive_or_die "app exited during startup before it laid out a window"
  PROBE="$(grep -a 'layout-probe:' "$LOG" | tail -1 || true)"
  [ -n "$PROBE" ] && break
  sleep 0.25
done
[ -n "$PROBE" ] || { echo "FAIL: no layout probe within 40s; stderr tail:"; tail -20 "$LOG"; exit 1; }

# THE STDERR PROBE IS STALE ON LINUX UNDER A NON-UNIT SCALE FACTOR, and a rect derived from it
# then aims at the wrong place while still producing a plausible, non-vacuous image.
#
# WHAT IS ACTUALLY WRONG, from the source rather than from the symptom. MainWindow.axaml.cs:229-237
# recomputes the on-screen probe TextBlock on every LayoutUpdated but calls LogDiagnostic exactly
# once, behind `_layoutProbeLogged`, on the FIRST one. On Windows the first layout already carries
# the final values. On Linux/X11 it does not: the first layout runs before the X11 scale factor and
# the window placement have landed, so the once-logged copy freezes pre-scale, pre-placement
# numbers while the on-screen copy goes on to be right.
#
# Measured 2026-08-24, both of Avalonia's X11 scale knobs, and the two copies read differently in
# the SAME run:
#   AVALONIA_GLOBAL_SCALE_FACTOR=1.75 -> X window 1925x1330; the selected door's real device rect
#     measured 306x77 at 21,79 (exactly 1.75x); the ON-SCREEN probe read "174.9x44.0 DIP @ scale
#     1.75 @ screen 37,116" — correct, and 37,116 is true root (window root 16,37 plus 21,79) —
#     while STDERR still read "175.0x44.0 DIP @ scale 1 @ screen 12,45".
#   AVALONIA_GLOBAL_SCALE_FACTOR=2 and AVALONIA_SCREEN_SCALE_FACTORS=XWAYLAND0=2 -> X window
#     2200x1520, real device rect 350x88 at 24,89; stderr unchanged at "scale 1 @ screen 12,45".
# AT SCALE 1 THE SCALE AGREES BUT THE ORIGIN IS STILL STALE, and that is the coincidence this
# whole harness rests on. Measured in the same run: stderr said "@ screen 12,45" while the app's
# on-screen probe, recomputed after the window was placed at root 16,37, said "@ screen 28,82" —
# true root. The stale 12,45 is exactly the door's offset INSIDE the X window, which is precisely
# what xgetimage.py's --crop wants, so every scale-1 crop here lands correctly by accident rather
# than by contract. It is not silent: xgetimage.py bounds-checks the crop against the window and
# the named checks would fail loudly on a wrong rectangle. Recorded so the next reader is not
# surprised when a fix to the probe's logging moves these coordinates.
#
# WHAT IT COST WHEN THIS WENT UNGUARDED, all three measured rather than imagined: `rail-door
# selected 1.75` cropped 175x44 at 12,45, photographed the wrong part of the window, passed the
# vacuity gate on 25 colours and scored 0/525; `rail-door unselected 1.75` scored 0.926 and PASSED
# off pixels that were not a door border at all; and a `--click` aimed at the System door's stale
# DIP coordinates landed on the PLAY door two rows up, so the capture was of the wrong page and
# still scored 0.982 on dashboard-background.
#
# Refuse rather than measure the wrong rectangle. Only the whole-window `dashboard` capture with
# no drive needs no probe rect, so only that one stays available at other scales.
PROBE_SCALE="$(sed -E 's/.*@ scale ([0-9.]+) @.*/\1/' <<<"$PROBE")"
if [ -n "$SCALE" ] && [ "$SCALE" != "$PROBE_SCALE" ] \
   && { [ "$SURFACE" = "rail-door" ] || [ "$DRIVE" = click ]; }; then
  echo "FAIL: asked for scale $SCALE but the app's once-logged layout probe reports scale"
  echo "      $PROBE_SCALE, so its coordinates are stale and any crop or click taken from them"
  echo "      lands in the wrong place. 'dashboard' with no --click needs no probe rect and is"
  echo "      the only capture available at this scale."
  exit 1
fi

# FIRST PRESENT IS A SECOND EVENT AND THE PROBE IS NOT IT. Measured 2026-08-24: with the window
# laid out and the probe already on stderr, a whole-window XGetImage still read 1 colour, and
# the same window read 227 colours 0.8s later. The probe fires on first LAYOUT; the pixels
# arrive on first PRESENT. Every earlier version of this script captured straight off the probe
# and was passing on luck. There is no cross-client happens-before edge to wait on here — the
# Windows leg has DwmFlush and X11 offers no equivalent for another client's presentation — so
# this polls a DEADLINE on the precondition "the window has presented at least once", using the
# same CcpVerify --vacuity rule the capture gate below uses. The predicate is a precondition,
# never the evidence: the named checks are evaluated once, afterwards, on their own capture.
FIRST="$ART/wslg-first-present.bmp"
DEADLINE=$((SECONDS + 30))
PRESENTED=no
while [ "$SECONDS" -lt "$DEADLINE" ]; do
  alive_or_die "app exited while waiting for its first present"
  python3 "$HERE/xgetimage.py" "$TITLE" "$FIRST" >/dev/null 2>&1 || true
  if [ -f "$FIRST" ] && dotnet "$VERIFY_DLL" --vacuity "$FIRST" >/dev/null 2>&1; then
    PRESENTED=yes
    break
  fi
  sleep 0.25
done
[ "$PRESENTED" = yes ] || { echo "FAIL: the window never presented a non-vacuous frame within 30s"; exit 1; }
echo "first present: reached (whole-window capture is non-vacuous)"

# FOCUS, asserted on every run because it costs one round trip and it is a fact nothing else
# here measures: X input focus AND the window manager's own _NET_ACTIVE_WINDOW must both name
# this window. A click driven below would otherwise be aimed at a window that is not taking
# input, and a capture would photograph a plausible-looking unfocused shell.
python3 "$HERE/xinput.py" "$TITLE" --focus

# Door rect from the app's own layout probe (stderr, first layout): DIP*scale = device pixels,
# and the offsets are WINDOW-relative on WSLg (xgetimage.py records why). One log line carries
# every door, so the requested door's id is part of the pattern.
door_rect() {
  local id="$1"
  local re="door $id ([0-9.]+)x([0-9.]+) DIP @ scale ([0-9.]+) @ screen (-?[0-9]+),(-?[0-9]+)"
  [[ "$PROBE" =~ $re ]] || { echo "FAIL: layout probe for door '$id' unreadable: $PROBE" >&2; exit 1; }
  DOOR_W=$(awk "BEGIN{printf \"%d\", ${BASH_REMATCH[1]} * ${BASH_REMATCH[3]}}")
  DOOR_H=$(awk "BEGIN{printf \"%d\", ${BASH_REMATCH[2]} * ${BASH_REMATCH[3]}}")
  DOOR_X=${BASH_REMATCH[4]}
  DOOR_Y=${BASH_REMATCH[5]}
  DOOR_DIP="${BASH_REMATCH[1]}x${BASH_REMATCH[2]} DIP @ scale ${BASH_REMATCH[3]}"
}

OUT="$ART/wslg-$SURFACE-$STATE.bmp"
capture_surface() {
  if [ "$SURFACE" = "rail-door" ]; then
    door_rect "$DOOR"
    python3 "$HERE/xgetimage.py" "$TITLE" "$1" --crop "$DOOR_X" "$DOOR_Y" "$DOOR_W" "$DOOR_H"
  else
    python3 "$HERE/xgetimage.py" "$TITLE" "$1"
  fi
}

if [ "$DRIVE" = click ]; then
  # Which door the gesture lands on: the surface's own door for rail-door, and the System door
  # for `dashboard`, because that is the route capture.ps1 photographs.
  if [ "$SURFACE" = "rail-door" ]; then
    TARGET="$DOOR"
  else
    TARGET=system
  fi
  door_rect "$TARGET"
  echo "probe: door $TARGET $DOOR_DIP @ screen $DOOR_X,$DOOR_Y"

  # `unselected` on a --click run is the PRE-click reading of the same door, so the pair is one
  # door either side of one gesture. `selected` clicks first.
  BEFORE="$ART/wslg-$SURFACE-preclick.bmp"
  capture_surface "$BEFORE" >/dev/null

  if [ "$STATE" = selected ] || [ "$SURFACE" = dashboard ]; then
    python3 "$HERE/xinput.py" "$TITLE" --click \
      $((DOOR_X + DOOR_W / 2)) $((DOOR_Y + DOOR_H / 2))

    # WAIT ON "THE PIXELS MOVED", NOT ON "THE PIXELS ARE RIGHT". Polling until the named check
    # passes would make the wait and the evidence the same statement; polling until the capture
    # DIFFERS from the pre-click bytes is independent of what the new colour ought to be, so a
    # click that reached nothing expires the deadline and this refuses instead of photographing
    # an unchanged window and calling it a state.
    DEADLINE=$((SECONDS + 20))
    MOVED=no
    while [ "$SECONDS" -lt "$DEADLINE" ]; do
      alive_or_die "app exited while waiting for the click to repaint"
      capture_surface "$OUT" >/dev/null
      if ! cmp -s "$BEFORE" "$OUT"; then MOVED=yes; break; fi
      sleep 0.25
    done
    [ "$MOVED" = yes ] || { echo "FAIL: no repaint within 20s of the click — the gesture reached nothing"; exit 1; }
    echo "drive: one XTest left-click on the $TARGET door; the surface repainted"
  else
    cp "$BEFORE" "$OUT"
    echo "drive: none for this state — this is the pre-click reading of the $DOOR door"
  fi
else
  if [ "$SURFACE" = "rail-door" ]; then
    door_rect "$DOOR"
    echo "probe: door $DOOR (${STATE}) $DOOR_DIP @ screen $DOOR_X,$DOOR_Y"
  fi
  capture_surface "$OUT"
fi

pkill -f "CcpClient[.]Desktop" || true

# NON-VACUITY GATE, and this is the defect this script was found with: it probed the door's real
# geometry, wrote a correctly-sized BMP and printed CAPTURE PASS over 7,700 pixels of a single
# colour, (0,0,0). An image with no second colour cannot be evidence of anything drawn, so the
# capture step REFUSES it here rather than leaving the tier-3 checks to report it as a wrong
# border colour. The rule lives in CcpVerify (--vacuity) so the Windows leg and this one share
# one implementation and one message.
#
# CALLED DIRECTLY, NEVER THROUGH A PIPE. `cmd | tail` makes $? the tail's, and that is how a
# non-zero verdict gets read as success — measured on this repository's own tool: the built
# assembly direct gives $?=2 and the SAME assembly piped to `tail` gives $?=0. (`dotnet run`
# was blamed for that; on SDK 10.0.400 it propagates 2 correctly, the pipe does not.) `set -e`
# then makes a refusal end the run before CAPTURE PASS can be printed.
dotnet "$VERIFY_DLL" --vacuity "$OUT"

echo "CAPTURE: $OUT"
echo "CAPTURE PASS"
