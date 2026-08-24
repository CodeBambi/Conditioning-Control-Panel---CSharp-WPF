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
#                    snapshotted probe cannot describe — see the scale guard below.
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

# ------------------------------------------------------------------------------------------------
# THE MACHINE-WIDE REAL-DESKTOP LEASE, and until 2026-08-25 this script took none at all.
#
# This script raises a window on the interactive desktop, reads that desktop back with XGetImage
# and (with --click) drives REAL input into it through XTEST. That is the same machine-wide
# singleton every CcpClient.Tests real-desktop fact contends for, and CcpClient.Tests serialises
# itself against its own peers through this exact file. A harness run that takes no lease is
# invisible to the whole mechanism. What that costs was measured on the Windows side of the same
# harness and is recorded in HarnessLeaseGuardTests.cs: 10 of 10 filtered floor runs red with a
# stand-in harness driving the desktop beside them, 0 of 10 with the same harness leased.
#
# THE CONTRACT IS RealDesktopLease'S. The exclusion is an exclusive lock on
# "$(temp)/ccp-real-desktop.lease"; the identity is "pid=<n>", raw, in the '.holder' sidecar beside
# it (RealDesktopCollection.cs:206,224). ${TMPDIR:-/tmp} is Path.GetTempPath()'s own rule, so a
# Linux floor run and this script name the same file.
#
# WHY flock(1) AND NOT A LOCK FILE. .NET maps FileShare.None to flock(LOCK_EX) on Unix, so
# flock(1) is the SAME primitive and not a parallel scheme. All four directions were measured on
# this machine's WSL2 kernel rather than assumed: flock(1) holding refuses .NET's FileShare.None;
# .NET's FileShare.None holding refuses flock(1); two flock(1) holders refuse each other; and a
# holder killed with SIGKILL leaves the lock FREE, because the kernel drops it when the fd closes.
# That last one is why this is a lock and not a lock FILE: with-slot.mjs's existence-based scheme
# needs a reaper for exactly the killed-harness case and this needs none.
#
# THE FD IS HELD BY THE SHELL, not by a subprocess, so it lives exactly as long as this script and
# is released by the kernel however the script ends - normally, on `set -e`, or on SIGKILL. There is
# deliberately no trap: a trap would be a second mechanism that a SIGKILL defeats anyway.
#
# NAMED BLIND SPOT, and it is a real one: WSL's /tmp and Windows' %TEMP% are different filesystems,
# so this lease excludes a LINUX floor run and does NOT exclude a Windows one. A WSLg RAIL window
# is composited onto the Windows desktop, so a capture here can still disturb a Windows-side
# real-desktop fact. Closing that needs a lease on a filesystem both sides share, and DrvFs over
# /mnt/c is the one filesystem RealDesktopLease already names as unable to carry an advisory flock.
# ------------------------------------------------------------------------------------------------
LEASE="${TMPDIR:-/tmp}/ccp-real-desktop.lease"
exec 9>"$LEASE"
if ! flock -w 300 -x 9; then
  HOLDER=$(head -c 64 "$LEASE.holder" 2>/dev/null || true)
  case "$HOLDER" in
    pid=*) WHO="the lease file names process ${HOLDER#pid=} as the holder" ;;
    *)     WHO="the lease file names no readable holder, so WHO has the desktop is unknown" ;;
  esac
  echo "FAIL: could not take the real-desktop lease at $LEASE within 300s. This process is $$; $WHO."
  echo "A contended desktop is not a flake and must NOT be captured around: the desktop is a"
  echo "singleton and this capture would photograph another run's windows."
  exit 1
fi
printf 'pid=%s' "$$" > "$LEASE.holder" 2>/dev/null || true
echo "real-desktop lease held by pid=$$ ($LEASE)"

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
# xinput.py imports find_window from xgetimage.py rather than keeping a third copy of the window
# search, and an import writes __pycache__ into a tracked directory that nothing ignores. Leaving
# untracked bytecode behind after every capture is how a harness starts showing up in git status
# and then in someone's commit.
export PYTHONDONTWRITEBYTECODE=1

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
#
# WAIT FOR THE REQUESTED SCALE, not merely for a line. The probe now re-logs whenever its values
# change, and on X11 the first layout runs BEFORE the scale factor lands — so at
# AVALONIA_GLOBAL_SCALE_FACTOR=1.75 the app publishes `scale 1` first and `scale 1.75` a moment
# later. Breaking on the first line would snapshot the scale-1 one, from which DIP*scale computes
# a 175x44 rect where the door is 306x77; the guard below would then refuse a run that was about
# to be perfectly good. The deadline still bounds it, and an app that never reaches the requested
# scale still ends up refused rather than measured.
probe_scale_of() { sed -E 's/.*@ scale ([0-9.]+) @.*/\1/' <<<"$1"; }
DEADLINE=$((SECONDS + 40))
PROBE=""
while [ "$SECONDS" -lt "$DEADLINE" ]; do
  alive_or_die "app exited during startup before it laid out a window"
  PROBE="$(grep -a 'layout-probe:' "$LOG" | tail -1 || true)"
  [ -n "$PROBE" ] && { [ -z "$SCALE" ] || [ "$SCALE" = "$(probe_scale_of "$PROBE")" ]; } && break
  sleep 0.25
done
[ -n "$PROBE" ] || { echo "FAIL: no layout probe within 40s; stderr tail:"; tail -20 "$LOG"; exit 1; }

# THE STDERR PROBE USED TO BE STALE ON LINUX AND THIS GUARD IS WHAT IS LEFT OF THAT. Keep it: it
# is now a live assertion that the log describes the window we are about to photograph, and it
# costs one `sed`.
#
# WHAT WAS WRONG, and it is FIXED IN THE PRODUCT rather than tolerated here.
# MainWindow.axaml.cs recomputed the on-screen probe on every LayoutUpdated but called
# LogDiagnostic exactly once, on the FIRST one. On Windows the first layout already carries the
# final values. On Linux/X11 it does not: the first layout runs before the X11 scale factor and the
# window placement have landed, so the once-logged copy froze pre-scale, pre-placement numbers
# while the on-screen copy went on being right. The probe now logs whenever the values it describes
# change, so the LAST line on stderr is the line on the screen. Measured 2026-08-24 at
# AVALONIA_GLOBAL_SCALE_FACTOR=1.75: stderr's last line and the rendered footer both read
# "174.9x44.0 DIP @ scale 1.75 @ screen 21,79" for the studio door, against a 1925x1330 X window.
#
# WHAT IT COST WHEN THIS WENT UNGUARDED, all three measured rather than imagined: `rail-door
# selected 1.75` cropped 175x44 at 12,45, photographed the wrong part of the window, passed the
# vacuity gate on 25 colours and scored 0/525; `rail-door unselected 1.75` scored 0.926 and PASSED
# off pixels that were not a door border at all; and a `--click` aimed at the System door's stale
# DIP coordinates landed on the PLAY door two rows up, so the capture was of the wrong page and
# still scored 0.982 on dashboard-background. The same three, re-measured after the fix:
# rail-door-selected-border 884/918 = 0.963 and 0.000 on the other state's capture,
# rail-door-unselected-border 892/918 = 0.972 and 0.004 on the other's, and the `--click` reached
# the System door and the shell's own footer read `route: system`.
#
# WHY THE GUARD STAYS ANYWAY. The probe is read ONCE into $PROBE, as early as the app will publish
# one, and an app still on its way to the requested scale would hand this script a line whose
# scale factor is 1 — from which DIP*scale computes a 175x44 rect where the door is 306x77. That
# is a WRONG-SIZE crop, and this refuses instead of taking it. Only the whole-window `dashboard`
# capture with no drive needs no probe rect, so only that one stays available at other scales.
PROBE_SCALE="$(probe_scale_of "$PROBE")"
if [ -n "$SCALE" ] && [ "$SCALE" != "$PROBE_SCALE" ] \
   && { [ "$SURFACE" = "rail-door" ] || [ "$DRIVE" = click ]; }; then
  echo "FAIL: asked for scale $SCALE but the app never published a layout probe at that scale"
  echo "      within the deadline — its last one reports scale $PROBE_SCALE, from which DIP*scale"
  echo "      computes a rect of the wrong SIZE, so any crop or click taken from it measures the"
  echo "      wrong rectangle. 'dashboard' with no --click needs no probe rect and is the only"
  echo "      capture available at this scale."
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

# Door rect from the app's own layout probe: DIP*scale = device pixels. One log line carries every
# door, so the requested door's id is part of the pattern.
#
# TAKE `@ window`, NEVER `@ screen`, AND THAT IS A CONTRACT RATHER THAN A PREFERENCE. Both of this
# script's consumers want WINDOW-relative device pixels — xgetimage.py's --crop takes them
# directly, and xinput.py's --click adds the window's own root origin itself — and on X11 the
# meaning of `@ screen` MOVES during startup. Measured in one WSLg run at scale 1.75, three
# successive readings of the studio door: `scale 1 @ screen 12,45`, then
# `scale 1.75 @ screen 21,79` (Avalonia still believes the window is at 0,0), then
# `scale 1.75 @ screen 37,116` (the WM's placement landed — root 16,37 plus 21,79). Every earlier
# version of this script read `@ screen` and happened to catch the middle reading, which is the
# same kind of luck the once-logged probe was living on. `@ window` is the app's own subtraction
# against its client-area origin (MainWindow.axaml.cs ProbeLine) and reads 21,79 in all three.
door_rect() {
  local id="$1"
  local re="door $id ([0-9.]+)x([0-9.]+) DIP @ scale ([0-9.]+) @ screen (-?[0-9]+),(-?[0-9]+) @ window (-?[0-9]+),(-?[0-9]+)"
  [[ "$PROBE" =~ $re ]] || { echo "FAIL: layout probe for door '$id' unreadable: $PROBE" >&2; exit 1; }
  DOOR_W=$(awk "BEGIN{printf \"%d\", ${BASH_REMATCH[1]} * ${BASH_REMATCH[3]}}")
  DOOR_H=$(awk "BEGIN{printf \"%d\", ${BASH_REMATCH[2]} * ${BASH_REMATCH[3]}}")
  DOOR_X=${BASH_REMATCH[6]}
  DOOR_Y=${BASH_REMATCH[7]}
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
