#!/usr/bin/env bash
# CCP greenfield verification harness — tier 2 WSLg (Linux/X11) capture.
# Usage: ./capture-wslg.sh <surface> <state> [--click] [scale-factor]
#   dashboard      unselected                 (whole window; --click drives the System route)
#   rail-door      unselected|selected
#   rack-row       unselected|selected
#   rack-row-dot   off|armed
#   studio-dial    live
#   audio-dial     live
#
# THE ELEMENT ROUTE, ADDED 2026-08-25, AND IT IS WHY THIS SCRIPT STOPPED AT THREE CHECKS.
# Everything above `rack-row` gets its rectangle from the app's own layout probe or is the whole
# window. Every OTHER named check in checks.json begins "find this element", which capture.ps1
# does through UIA — and Linux has no UIA, so 42 of the 45 named checks were unreachable here for
# a reason that had nothing to do with presentation, input or focus, all three of which this
# script already proved. The route is AT-SPI (atspi.py, which carries the full argument); this
# script's job is to stand the bus up before the app starts and to drive the states.
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

# ValidateSet's job, done the way capture.ps1 does it: the PAIR is checked, not the two tokens
# separately, because an unpaired combination is a caller mistake and not a surface that quietly
# photographs the wrong thing.
case "$SURFACE/$STATE" in
  dashboard/unselected|rail-door/unselected|rail-door/selected) ;;
  rack-row/unselected|rack-row/selected) ;;
  rack-row-dot/off|rack-row-dot/armed) ;;
  studio-dial/live|audio-dial/live) ;;
  *)
    echo "FAIL: '$SURFACE $STATE' is not a surface/state this harness drives. It offers:"
    echo "      dashboard unselected | rail-door unselected|selected"
    echo "      rack-row unselected|selected | rack-row-dot off|armed"
    echo "      studio-dial live | audio-dial live"
    echo "      The states this list does NOT offer are gated in client/docs/linux-evidence.md,"
    echo "      each with the named mechanism that blocks it."
    exit 1 ;;
esac

# Which door carries the requested state, and how it gets there. Only the two probe-derived
# surfaces use a door at all; every element-route surface below finds its own rectangle.
DOOR=studio
if [ "$SURFACE" = rail-door ] || [ "$SURFACE" = dashboard ]; then
  if [ "$DRIVE" = click ]; then
    DOOR=companion
  else
    case "$STATE" in
      selected)   DOOR=studio ;;     # the default route: :checked when the shell opens
      unselected) DOOR=companion ;;  # never selected until something clicks it
    esac
  fi
fi

# Which route the surface's rectangle comes from — the whole subject of this file's header.
case "$SURFACE" in
  dashboard|rail-door) ROUTE=probe ;;
  *)                   ROUTE=atspi ;;
esac

echo "backend: X11 via XWayland on DISPLAY=${DISPLAY:-unset} (WSLg nested Weston, no desktop environment)"
echo "surface: $SURFACE  state: $STATE  drive: $DRIVE  door: $DOOR  rect route: $ROUTE"

# Deterministic start: remove the demonstrator settings file (demo store only). Nothing is
# SEEDED into it any more — the old `lit` drive wrote statusTickerEnabled, and the card that
# setting lit no longer exists.
#
# session_preset.json JOINS IT for the same reason capture.ps1 added it: the rack rows' module
# dials do not live in settings.json, and a `rack-row-dot off` run leaves FlashEnabled=false
# behind for the next run's `armed` capture to read as a cold start. Measured on Windows first
# (capture.ps1's deterministic-start set) and it is filesystem-agnostic.
mkdir -p "$(dirname "$SETTINGS")"
rm -f "$SETTINGS" "$(dirname "$SETTINGS")/session_preset.json"

if [ -n "$SCALE" ]; then
  export AVALONIA_GLOBAL_SCALE_FACTOR="$SCALE"
fi
export CCP_X11_SOFTWARE=1
# xinput.py imports find_window from xgetimage.py rather than keeping a third copy of the window
# search, and an import writes __pycache__ into a tracked directory that nothing ignores. Leaving
# untracked bytecode behind after every capture is how a harness starts showing up in git status
# and then in someone's commit.
export PYTHONDONTWRITEBYTECODE=1

# ------------------------------------------------------------------------------------------------
# THE ACCESSIBILITY BUS, and it must be reachable BEFORE the app starts or the element route does
# not exist at all.
#
# THE PRECONDITION IS THE BUS AND ONLY THE BUS, AND THAT IS MEASURED RATHER THAN READ OFF THE
# DOCUMENTATION. The obvious guess — and the one this file shipped for an hour — is that Avalonia
# waits for the DESKTOP to say accessibility is switched on, because Avalonia.X11's
# X11AtSpiAccessibility does subscribe to org.a11y.Status and does have an
# OnAccessibilityEnabledChanged. Measured on this image 2026-08-25, both directions:
#   toolkit-accessibility = false, harness sets nothing  -> the tree is published, the route works
#   no session bus at all                                -> nothing is published, the route refuses
# So the switch is NOT a gate in 12.1.1, and the harness must NOT write it. That matters beyond
# tidiness: org.a11y.Status.IsEnabled is dconf-backed, so setting it is a PERSISTENT change to the
# user's own desktop settings made as a side effect of taking a screenshot. This asks org.a11y.Bus
# for its address instead, which is a read, and which stands the bus up anyway because the service
# is D-Bus ACTIVATED.
#
# NO PACKAGE WAS INSTALLED FOR THIS, which is the same finding XTEST produced. at-spi2-core 2.60.0
# and python3-dbus are already in this image, and the first call to org.a11y.Bus starts
# /usr/libexec/at-spi-bus-launcher by itself. `dbus-launch` is likewise already here.
#
# A PRIVATE SESSION BUS, NOT THE LOGIN ONE. WSL sets DBUS_SESSION_BUS_ADDRESS to
# unix:path=/run/user/0/bus and there is no socket at that path — there is no login session bus on
# this image at all. A private bus per capture also means the a11y registry this run reads holds
# exactly this run's app and nothing else, which is what makes atspi.py's "exactly one window"
# rule meaningful.
#
# THE LAUNCHER IS CLEANED UP BY PID DIFF, never by pkill on its name: another lane's harness may
# be holding one and a name sweep would take theirs down with ours.
A11Y_BEFORE="$(pgrep -f 'at-spi(-bus-launcher|2-registryd)' 2>/dev/null | sort || true)"
eval "$(dbus-launch --sh-syntax)"
cleanup_bus() {
  local after
  after="$(pgrep -f 'at-spi(-bus-launcher|2-registryd)' 2>/dev/null | sort || true)"
  for pid in $(comm -13 <(echo "$A11Y_BEFORE") <(echo "$after") 2>/dev/null); do
    kill "$pid" 2>/dev/null || true
  done
  [ -n "${DBUS_SESSION_BUS_PID:-}" ] && kill "$DBUS_SESSION_BUS_PID" 2>/dev/null || true
}
if ! A11Y_ADDR="$(gdbus call --session --dest org.a11y.Bus --object-path /org/a11y/bus \
     --method org.a11y.Bus.GetAddress 2>/dev/null)"; then
  cleanup_bus
  echo "FAIL: org.a11y.Bus is not reachable on this session bus, so no accessibility bus exists and"
  echo "      no element on this window is addressable. at-spi2-core provides that service and it"
  echo "      is D-Bus activated, so this means either the package is missing or the session bus is."
  exit 1
fi
echo "a11y: session bus $DBUS_SESSION_BUS_ADDRESS; accessibility bus $A11Y_ADDR"

pkill -f "CcpClient[.]Desktop" 2>/dev/null || true
sleep 1
: > "$LOG"
dotnet "$DLL" >/dev/null 2>"$LOG" </dev/null &
APP_PID=$!
trap 'kill "$APP_PID" 2>/dev/null || true; cleanup_bus' EXIT

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
#
# THE ELEMENT ROUTE IS SUBJECT TO THE SAME GUARD, and its reason is different from the probe
# route's. It takes its rectangles from AT-SPI rather than from the probe, so a stale probe cannot
# misplace a crop — but it takes the SCALE from the probe, and the scale is what converts the
# manifest's DIP constants (the 8-DIP rack dot, the 12-DIP minimum track height) into pixels. At a
# scale of 1 those constants would name an 8-pixel dot in a 14-pixel cell.
if [ -n "$SCALE" ] && [ "$SCALE" != "$PROBE_SCALE" ] \
   && { [ "$SURFACE" = "rail-door" ] || [ "$DRIVE" = click ] || [ "$ROUTE" = atspi ]; }; then
  echo "FAIL: asked for scale $SCALE but the app never published a layout probe at that scale"
  echo "      within the deadline — its last one reports scale $PROBE_SCALE, from which DIP*scale"
  echo "      computes a rect of the wrong SIZE, so any crop, click or DIP constant taken from it"
  echo "      measures the wrong rectangle. 'dashboard' with no --click needs no probe rect and is"
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

# ------------------------------------------------------------------------------------------------
# THE ELEMENT ROUTE'S OWN HELPERS — the four things capture.ps1 does with UIA, done with AT-SPI.
#   Get-Element / Get-Rect  -> el rect "<accessible name>"       (sets X Y W H ROLE SELECTED ...)
#   Get-Selected            -> the SELECTED / CHECKED bits those set
#   Click-Rect              -> click_el / rclick_el, XTest through xinput.py
#   Assert-Inside           -> assert_inside
#
# THE ONE SUBSTANTIVE DIFFERENCE IS THE SELECTOR, and it is stated here rather than buried:
# capture.ps1 addresses controls by AutomationId and AT-SPI does not carry AutomationId at all, so
# every selector below is the control's AutomationProperties.Name. Where a control has an
# AutomationId and no Name it is NOT addressable from Linux, and that is a per-surface gate.
# ------------------------------------------------------------------------------------------------
el() {  # el <verb> <selector...>  -> X Y W H SX SY ROLE SELECTED CHECKED SHOWING VISIBLE SENSITIVE ENABLED NAME
  local out
  if ! out="$(python3 "$HERE/atspi.py" "$TITLE" "$@" 2>&1)"; then
    echo "$out" >&2
    return 1
  fi
  eval "$out"
}

click_el()  { python3 "$HERE/xinput.py" "$TITLE" --click      $((X + W / 2)) $((Y + H / 2)); }
rclick_el() { python3 "$HERE/xinput.py" "$TITLE" --rightclick $((X + W / 2)) $((Y + H / 2)); }

# POLL A STATE BIT TO A DEADLINE, never sleep after a gesture. A slow-but-healthy app and a gesture
# that reached nothing are the same picture at any fixed delay, and the difference is exactly what
# a state drive exists to establish.
await_bit() {  # await_bit <selector> <VARNAME> <0|1> <what>
  local deadline=$((SECONDS + 20))
  while [ "$SECONDS" -lt "$deadline" ]; do
    alive_or_die "app exited while waiting for $4"
    el rect "$1" >/dev/null 2>&1 || true
    if [ "${!2}" = "$3" ]; then return 0; fi
    sleep 0.25
  done
  echo "FAIL: $4 — '$1' never reached $2=$3 within 20s of the gesture"
  exit 1
}

# A rect is only capturable where it is really painted. AT-SPI reports UNCLIPPED bounds for content
# scrolled out of a viewport, exactly as UIA does (measured during the Windows rack work), so a
# plausible rect is not a visible one.
assert_inside() {  # assert_inside "<what>" ix iy iw ih "<outer>" ox oy ow oh
  local what="$1" ix="$2" iy="$3" iw="$4" ih="$5" outer="$6" ox="$7" oy="$8" ow="$9" oh="${10}"
  if [ "$ix" -lt "$ox" ] || [ "$iy" -lt "$oy" ] \
     || [ $((ix + iw)) -gt $((ox + ow)) ] || [ $((iy + ih)) -gt $((oy + oh)) ]; then
    echo "FAIL: $what ($ix,$iy ${iw}x${ih}) is not fully inside $outer ($ox,$oy ${ow}x${oh}) —"
    echo "      AT-SPI reports unclipped bounds, so this rect is reported but not painted."
    exit 1
  fi
}

# WHEEL A ROW INTO ITS VIEWPORT, ONE NOTCH AT A TIME, TESTING AFTER EACH — capture.ps1's
# Scroll-RowIntoView and its rule. Never a fixed notch count: a rack that grew another row would
# otherwise stop scrolling far enough while still reporting a plausible rect, and AT-SPI would go
# on reporting unclipped bounds for it exactly as UIA reports IsOffscreen=False.
scroll_into_view() {  # scroll_into_view <selector>   -> leaves X Y W H on the row, sets NOTCHES
  local sel="$1" rx ry rw rh
  NOTCHES=0
  while :; do
    alive_or_die "app exited while wheeling '$sel' into view"
    el rect "$sel" || { echo "FAIL: '$sel' is not in the accessible tree"; exit 1; }
    rx=$X ry=$Y rw=$W rh=$H
    el scroll "$sel" || { echo "FAIL: '$sel' has no viewport"; exit 1; }
    if [ "$rx" -ge "$X" ] && [ "$ry" -ge "$Y" ] \
       && [ $((rx + rw)) -le $((X + W)) ] && [ $((ry + rh)) -le $((Y + H)) ]; then
      X=$rx Y=$ry W=$rw H=$rh
      return 0
    fi
    if [ "$NOTCHES" -ge 24 ]; then
      echo "FAIL: '$sel' never came fully inside its viewport after $NOTCHES wheel notches:"
      echo "      row $rx,$ry ${rw}x${rh} vs viewport $X,$Y ${W}x${H}"
      exit 1
    fi
    python3 "$HERE/xinput.py" "$TITLE" --scroll $((X + W / 2)) $((Y + H / 2)) down 1 >/dev/null
    NOTCHES=$((NOTCHES + 1))
  done
}

# The session lock banner, matched on the INVARIANT half of its sentence: the session's own name
# opens it, so a prefix would pin this harness to one session's title
# (Views/Pages/StudioPage.axaml:501).
BANNER='~is running this. Its features and intensity are locked until the session ends.'

WHERE="$(python3 "$HERE/xinput.py" "$TITLE" --where)"
[[ "$WHERE" =~ size\ ([0-9]+)x([0-9]+) ]] || { echo "FAIL: could not read the window size: $WHERE"; exit 1; }
WIN_W=${BASH_REMATCH[1]}; WIN_H=${BASH_REMATCH[2]}

OUT="$ART/wslg-$SURFACE-$STATE.bmp"
# DELETE THE PREVIOUS RUN'S IMAGE BEFORE DOING ANYTHING ELSE. Found the hard way 2026-08-25: a run
# that refused at its state gate left the PREVIOUS run's BMP on disk under this exact name, and the
# named-check tool then scored that stale file and printed ALL CHECKS PASSED for a capture that was
# never taken. A gate that fails must leave nothing behind for a later step to mistake for evidence.
rm -f "$OUT"
capture_surface() {
  if [ "$SURFACE" = "rail-door" ]; then
    door_rect "$DOOR"
    python3 "$HERE/xgetimage.py" "$TITLE" "$1" --crop "$DOOR_X" "$DOOR_Y" "$DOOR_W" "$DOOR_H"
  else
    python3 "$HERE/xgetimage.py" "$TITLE" "$1"
  fi
}

if [ "$ROUTE" = atspi ] && { [ "$SURFACE" = studio-dial ] || [ "$SURFACE" = audio-dial ]; }; then
  # ==============================================================================================
  # THE TWO DIALS, AND THE CLAIM IS ABOUT WHAT A SESSION MAY AND MAY NOT TAKE FROM THE USER.
  # `studio-dial live` is the Lock Card's Repeats slider with nothing running — the control the
  # session feature lock OWNS, photographed in the state it is the user's. `audio-dial live` is the
  # audio row's master volume, which upstream names in its own never-lock list
  # (MainWindow/MainWindow.SessionFeatureLock.cs:39-42) and which a session must never take.
  #
  # ONLY THE `live` HALF OF EACH PAIR IS DRIVEN HERE. The other half needs a real scripted session
  # running, and that is gated on Linux for a reason this file records rather than works around —
  # see the AMBIGUOUS NAME gate in client/docs/linux-evidence.md.
  #
  # WHAT AT-SPI GIVES INSTEAD OF UIA'S IsEnabled: two bits, SENSITIVE and ENABLED. Avalonia's
  # accessible sets both from IsEffectivelyEnabled, so both are asserted rather than one — a route
  # that silently reported only one of them would pass a locked dial.
  # ==============================================================================================
  # THE ROLE QUALIFIERS ARE NOT DECORATION. A dial's caption TextBlock carries the same words as
  # the dial's own AutomationProperties.Name, so 'Master volume' names a LABEL and a SLIDER
  # (Views/Pages/StudioPage.axaml:1849-1855) and an unqualified selector is genuinely ambiguous —
  # atspi.py refuses it rather than photographing the caption.
  if [ "$SURFACE" = studio-dial ]; then
    ROW='Lock Card rack row'; DIAL='Lock Card repeats'; DIAL_ROLE='@slider'
    MATE='Lock Card strict mode'; MATE_ROLE='@check-box'
  else
    ROW='Audio rack row';     DIAL='Master volume';     DIAL_ROLE='@slider'
    MATE='Test audio';        MATE_ROLE='@push-button'
  fi

  scroll_into_view "$ROW"
  ROW_X=$X ROW_Y=$Y ROW_W=$W ROW_H=$H ROW_NOTCHES=$NOTCHES
  assert_inside "rack row '$ROW'" "$ROW_X" "$ROW_Y" "$ROW_W" "$ROW_H" \
                'the shell window' 0 0 "$WIN_W" "$WIN_H"
  click_el
  await_bit "$ROW" SELECTED 1 "the left-click did not open the $ROW"
  echo "state drive: one XTest left-click on the $ROW -> SELECTED=1 ($ROW_NOTCHES wheel notch(es))"

  # THE GATE, READ BEFORE ANY PIXEL. A dial that is live because the lock let it be and a dial that
  # is live because nothing ever locked it photograph identically, so the pixels are only evidence
  # once these have held.
  el rect "$DIAL" "$DIAL_ROLE" || { echo "FAIL: '$DIAL' is not in the accessible tree"; exit 1; }
  DIAL_X=$X DIAL_Y=$Y DIAL_W=$W DIAL_H=$H
  if [ "$SENSITIVE" != 1 ] || [ "$ENABLED" != 1 ]; then
    echo "FAIL: '$DIAL' is disabled with no session running (SENSITIVE=$SENSITIVE ENABLED=$ENABLED);"
    echo "      the live capture would be a lie"
    exit 1
  fi
  el rect "$MATE" "$MATE_ROLE" || { echo "FAIL: '$MATE' is not in the accessible tree"; exit 1; }
  if [ "$SENSITIVE" != 1 ] || [ "$ENABLED" != 1 ]; then
    echo "FAIL: '$MATE' is disabled with no session running (SENSITIVE=$SENSITIVE ENABLED=$ENABLED)"
    exit 1
  fi
  if python3 "$HERE/atspi.py" "$TITLE" rect "$BANNER" >/dev/null 2>&1; then
    echo "FAIL: a session lock banner is on screen with nothing running:"
    python3 "$HERE/atspi.py" "$TITLE" text "$BANNER"
    exit 1
  fi
  echo "live gate: '$DIAL' and '$MATE' both SENSITIVE=1 ENABLED=1, and no session lock banner"

  if [ "$SURFACE" = audio-dial ]; then
    # THE VALUE, so a later `running` capture can be shown to be the same picture rather than a
    # different thumb position. 32% is upstream's own fresh-install master volume and this run
    # cleared the settings file, so anything else means a document leaked in from a previous run.
    # The value is read off the panel's OWN rendered sentence (AudioDialsNotices) rather than off
    # the bare '32%' cell, because that cell carries an AutomationId and no Name — which is the
    # AT-SPI limit this whole route works within.
    python3 "$HERE/atspi.py" "$TITLE" text '~Master is 32%.' >/dev/null 2>&1 || {
      echo "FAIL: the audio panel does not report the fresh-install master volume of 32%;"
      echo "      a leaked audio.json would move the thumb and the two captures would differ for"
      echo "      a reason that has nothing to do with the lock."
      exit 1
    }
    # AND THE DEVICE, which is the other half of this row's contract and a genuinely different
    # claim on Linux: opening the panel lists endpoints and opens NOTHING, on an audio stack
    # (PulseAudio over WSLg's RDP sink) that shares no code with the Windows one.
    DEVICE_LINE="$(python3 "$HERE/atspi.py" "$TITLE" text '^Nothing has been asked of the operating system yet.' 2>/dev/null || true)"
    [ -n "$DEVICE_LINE" ] || {
      echo "FAIL: a device was brought up merely by opening the audio panel, or the panel's device"
      echo "      line is not on screen at all."
      exit 1
    }
    echo "device gate: '$DEVICE_LINE'"
  fi

  # THE BAND checks.json SAMPLES, PROVED AGAINST THE MEASURED CONTROL: y 0.40..0.60 is the slider's
  # own centre line where Fluent draws the track, and a control too short for that band to BE the
  # track would sample its background instead.
  MIN_H=$(awk "BEGIN{printf \"%d\", 12 * $PROBE_SCALE + 0.5}")
  if [ "$DIAL_H" -lt "$MIN_H" ]; then
    echo "FAIL: '$DIAL' is only $DIAL_H px tall at scale $PROBE_SCALE; its centre band would not be the track"
    exit 1
  fi
  assert_inside "'$DIAL'" "$DIAL_X" "$DIAL_Y" "$DIAL_W" "$DIAL_H" 'the shell window' 0 0 "$WIN_W" "$WIN_H"
  echo "dial rect $DIAL_X,$DIAL_Y ${DIAL_W}x${DIAL_H} @ scale $PROBE_SCALE; track band y 0.40..0.60, filled band x 0.02..0.10"

  CAP_X=$DIAL_X CAP_Y=$DIAL_Y CAP_W=$DIAL_W CAP_H=$DIAL_H
  python3 "$HERE/xgetimage.py" "$TITLE" "$OUT" --crop "$CAP_X" "$CAP_Y" "$CAP_W" "$CAP_H"

elif [ "$ROUTE" = atspi ]; then
  # ==============================================================================================
  # THE RACK. The shell opens on Studio (ShellRoutes.Default), so the rack is already in front of
  # us and no navigation is needed; navigating anywhere else would unmount the page and take its
  # accessible subtree with it — the AT-SPI tree holds only mounted, visible controls, exactly as
  # the UIA tree does.
  #
  # The captured row is Flash Images: first row of the first group, so it is above the scroll fold
  # at every window size this shell has, and it is the row whose module can be armed without
  # anything appearing on the screen ("Armed. Nothing is scheduled until the session starts." —
  # Views/Pages/StudioPage.axaml.cs:3447).
  # ==============================================================================================
  ROW='Flash Images rack row'

  el rect "$ROW" || { echo "FAIL: the Flash Images rack row is not in the accessible tree"; exit 1; }
  ROW_X=$X ROW_Y=$Y ROW_W=$W ROW_H=$H ROW_SEL=$SELECTED
  el scroll "$ROW" || { echo "FAIL: the rack row has no viewport"; exit 1; }
  VP_X=$X VP_Y=$Y VP_W=$W VP_H=$H
  echo "rack: viewport $VP_X,$VP_Y ${VP_W}x${VP_H}; row '$ROW' $ROW_X,$ROW_Y ${ROW_W}x${ROW_H} @ scale $PROBE_SCALE (AT-SPI, no probe)"

  assert_inside "rack row '$ROW'" "$ROW_X" "$ROW_Y" "$ROW_W" "$ROW_H" \
                'the rack viewport' "$VP_X" "$VP_Y" "$VP_W" "$VP_H"
  assert_inside "rack row '$ROW'" "$ROW_X" "$ROW_Y" "$ROW_W" "$ROW_H" \
                'the shell window' 0 0 "$WIN_W" "$WIN_H"

  if [ "$SURFACE" = rack-row ] && [ "$STATE" = unselected ]; then
    # The captured row is genuinely NOT the open one — read, not assumed.
    [ "$ROW_SEL" = 0 ] || { echo "FAIL: '$ROW' is already selected on a cold start; the unselected capture would be a lie"; exit 1; }
    echo "state: '$ROW' SELECTED=0 on a cold start (AT-SPI state set)"
  else
    # Every other rack state starts by OPENING the row, through real input, because the dot states
    # need the module panel: FlashLiveState lives inside FlashModulePanel, whose IsVisible is gated
    # on this row being checked (Views/Pages/StudioPage.axaml.cs:540). A right-click alone sets
    # Handled and deliberately does NOT select (:556-565), so the state read below would be
    # unreachable without this left-click first.
    X=$ROW_X Y=$ROW_Y W=$ROW_W H=$ROW_H
    click_el
    await_bit "$ROW" SELECTED 1 'the left-click did not open the Flash Images rack row'
    ROW_X=$X ROW_Y=$Y ROW_W=$W ROW_H=$H
    echo "state drive: one XTest left-click on the Flash Images rack row -> SELECTED=1"
  fi

  if [ "$SURFACE" = rack-row-dot ]; then
    # DRIVE THE STATE, NEVER ASSUME IT. SessionPresetDocument.FlashEnabled defaults to TRUE, so a
    # cold start is already ARMED and 'off' is the state that needs the toggle — but a persisted
    # preset would invert that, which is why the deterministic-start set above deletes
    # session_preset.json and why the state is READ, toggled only if it disagrees, and read again.
    if [ "$STATE" = armed ]; then WANT='Armed.'; else WANT='Switched off.'; fi
    read_live() {
      # FlashLiveState carries an AutomationId and no AutomationProperties.Name, so AT-SPI names it
      # by its TEXT (Avalonia falls back to the control's text). The three heads are disjoint and
      # DescribeState writes exactly one of them (Views/Pages/StudioPage.axaml.cs:3444-3448), so a
      # prefix probe of each is a read of the control rather than an assumption about it.
      LIVE=''
      for head in 'Running: the next flash is on the clock.' 'Armed.' 'Switched off.'; do
        if python3 "$HERE/atspi.py" "$TITLE" text "^$head" >/dev/null 2>&1; then
          LIVE="$(python3 "$HERE/atspi.py" "$TITLE" text "^$head")"
          return 0
        fi
      done
      return 1
    }
    read_live || { echo "FAIL: the Flash module's live-state line is not on screen at all"; exit 1; }
    case "$LIVE" in
      "$WANT"*) : ;;
      *)
        echo "state drive: right-click quick-toggle on the Flash Images row (it read '$LIVE')"
        X=$ROW_X Y=$ROW_Y W=$ROW_W H=$ROW_H
        rclick_el
        DEADLINE=$((SECONDS + 20))
        while [ "$SECONDS" -lt "$DEADLINE" ]; do
          alive_or_die 'app exited while waiting for the quick-toggle'
          read_live || true
          case "$LIVE" in "$WANT"*) break ;; esac
          sleep 0.25
        done ;;
    esac
    case "$LIVE" in
      "$WANT"*) echo "state drive confirmed: the module's live line reads '$LIVE'" ;;
      *) echo "FAIL: the module did not reach '$STATE': it reads '$LIVE' (expected it to start '$WANT')"; exit 1 ;;
    esac

    # THE DOT CELL, DERIVED EXACTLY AS THE WINDOWS LEG DERIVES IT AND THEN CROSS-CHECKED AGAINST
    # THE DOT'S OWN BOUNDS — which is a check the Windows leg cannot make.
    # A rack row's Grid is ColumnDefinitions="*,Auto": the caption fills the star column and the
    # 8-DIP dot is the auto column, so the dot cell begins at the caption's right edge. The
    # Windows cross-check is the Visuals row, the one row upstream gives no dot
    # (Views/Pages/StudioPage.axaml:167), whose caption therefore spans the WHOLE grid.
    # AT-SPI additionally gives the Ellipse ITS OWN rectangle, so the derivation is checked against
    # the thing it derives rather than only against another row's arithmetic.
    el in "$ROW" '@label' || { echo "FAIL: the rack row has no single caption"; exit 1; }
    LBL_X=$X LBL_W=$W
    el in 'Visuals rack row' '@label' || { echo "FAIL: the dotless Visuals row has no single caption"; exit 1; }
    GRID_W=$W
    DOT_PX=$(awk "BEGIN{printf \"%d\", 8 * $PROBE_SCALE + 0.5}")
    if [ $(( (LBL_W + DOT_PX) - GRID_W )) -gt 1 ] || [ $(( GRID_W - (LBL_W + DOT_PX) )) -gt 1 ]; then
      echo "FAIL: the rack row grid does not close: caption $LBL_W px + 8 DIP dot $DOT_PX px at"
      echo "      scale $PROBE_SCALE is $((LBL_W + DOT_PX)) px, but the Visuals row's dotless caption spans"
      echo "      $GRID_W px. The row grid has changed and this derivation no longer names the dot."
      exit 1
    fi
    DERIVED_X=$((LBL_X + LBL_W))
    DERIVED_Y=$((ROW_Y + (ROW_H - DOT_PX) / 2))

    # THE CAPTURE RECT IS THE DOT'S OWN BOUNDS, and the row-centred arithmetic above is now its
    # CORROBORATION rather than its source. AT-SPI publishes the Ellipse as an element of its own,
    # which UIA does not, so the direct reading exists here and is strictly better evidence than
    # arithmetic over its container.
    #
    # AND THE TWO DISAGREE AT FRACTIONAL SCALE, WHICH IS A FINDING RATHER THAN A TOLERANCE.
    # Measured on this machine: at scale 1 the derived cell and the dot's own bounds are identical;
    # at scale 1.75 the derivation puts the cell at y=301 and the dot really sits at y=303, because
    # (rowH - dot)/2 is 24.5 DIP-derived pixels and the row's content presenter rounds elsewhere.
    # The Windows leg derives exactly this way and has no second reading to notice it with, so its
    # 14x14 cell is 2 px above the dot it names — recorded in client/docs/linux-evidence.md.
    # The corroboration therefore holds X and SIZE exactly (both are scale-invariant) and holds Y
    # to less than half a dot, which is the statement "this still names THIS row's dot and not a
    # neighbour's".
    el in "$ROW" 'Ellipse' || { echo "FAIL: the rack row publishes no dot of its own"; exit 1; }
    CAP_X=$X CAP_Y=$Y CAP_W=$W CAP_H=$H
    DELTA_Y=$((DERIVED_Y - CAP_Y)); [ "$DELTA_Y" -lt 0 ] && DELTA_Y=$((-DELTA_Y))
    if [ "$DERIVED_X" -ne "$CAP_X" ] || [ "$CAP_W" -ne "$DOT_PX" ] || [ "$CAP_H" -ne "$DOT_PX" ] \
       || [ $((DELTA_Y * 2)) -ge "$DOT_PX" ]; then
      echo "FAIL: the derived dot cell ${DERIVED_X},${DERIVED_Y} ${DOT_PX}x${DOT_PX} does not name the"
      echo "      dot whose own AT-SPI bounds are $CAP_X,$CAP_Y ${CAP_W}x${CAP_H}. The row grid has"
      echo "      changed and this derivation no longer names the dot."
      exit 1
    fi
    echo "dot cell: $CAP_X,$CAP_Y ${CAP_W}x${CAP_H} (the dot's own AT-SPI bounds) — caption $LBL_W px"
    echo "          + dot $DOT_PX px == Visuals dotless caption $GRID_W px; the row-centred derivation"
    echo "          puts it at ${DERIVED_X},${DERIVED_Y}, ${DELTA_Y} px away in y at scale $PROBE_SCALE"
  else
    CAP_X=$ROW_X CAP_Y=$ROW_Y CAP_W=$ROW_W CAP_H=$ROW_H
  fi

  python3 "$HERE/xgetimage.py" "$TITLE" "$OUT" --crop "$CAP_X" "$CAP_Y" "$CAP_W" "$CAP_H"

elif [ "$DRIVE" = click ]; then
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
