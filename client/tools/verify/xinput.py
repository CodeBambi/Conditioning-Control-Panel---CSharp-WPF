#!/usr/bin/env python3
# CCP greenfield verification harness — tier 2 WSLg/X11 real input and focus.
#
# WHY THIS EXISTS, and it corrects a limit this harness had recorded as permanent.
# capture-wslg.sh's header said WSLg allows "no input automation (no xdotool — a named gate)",
# and every Linux state was therefore read off a cold start with zero gestures. The gate was
# real but the conclusion was wrong: xdotool is indeed absent from the image, and so are xwd,
# wmctrl and scrot — but the X server WSLg runs (XWayland) advertises the XTEST extension at
# version 2.2, and libXtst.so.6 is already installed as part of the base X client libraries.
# Measured on this machine 2026-08-24, not assumed. So synthetic input needs no new package,
# exactly as XGetImage needed none: ctypes over the shipped shared library.
#
# THIS IS THE SAME MECHANISM THE WINDOWS LEG USES, not a shortcut around it. capture.ps1 drives
# SendInput, which is Win32's synthetic-input path; XTestFakeButtonEvent is X11's. Both enter
# the server's own event stream, so the application cannot tell them from a hand on the mouse —
# which is the whole point, and the reason a state driven this way is evidence that the user
# path works rather than evidence that a method could be called.
#
# Usage:
#   xinput.py "<title needle>" --focus            assert X input focus AND _NET_ACTIVE_WINDOW
#                                                 both name the app window; print both readings
#   xinput.py "<title needle>" --click X Y        left-click at WINDOW-RELATIVE device pixels
#   xinput.py "<title needle>" --rightclick X Y   right-click (button 3) at the same coordinates
#   xinput.py "<title needle>" --scroll X Y D [N] N wheel notches (button 4 up / 5 down)
#   xinput.py "<title needle>" --where            print the window's root origin and size
#
# THE SECOND AND THIRD BUTTONS ARE NOT DECORATION. The Studio rack's quick-toggle is a RIGHT-click
# and nothing else reaches it (StudioPage.axaml.cs:449-453 -> :559-569), and every module surface
# below the rack's fold is reached by a WHEEL, one notch at a time, because a fixed notch count
# silently stops scrolling far enough the moment a page grows. Both are the same XTest mechanism
# as the left-click above: buttons 4 and 5 are X11's wheel-up and wheel-down.
#
# Exit 0 = the requested action completed (and, for --focus, the assertion held).
# Exit 2 = the window was not found, or --focus found focus elsewhere.
import ctypes
import sys

from xgetimage import find_window  # one implementation of the window search, not a third copy

X11 = ctypes.cdll.LoadLibrary("libX11.so.6")
XTST = ctypes.cdll.LoadLibrary("libXtst.so.6")

AnyPropertyType = 0
CurrentTime = 0


def root_origin(display, root, win):
    """Window's position in ROOT coordinates via XTranslateCoordinates.

    Never inferred from the app's own layout probe: on WSLg the probe's PointToScreen output is
    the offset INSIDE the X window (Avalonia places the window at the monitor origin while the X
    window really sits at the frame offset — measured 16,37 here), so a click aimed at probe
    coordinates alone lands 16x37 device pixels off target.
    """
    rx, ry = ctypes.c_int(), ctypes.c_int()
    child = ctypes.c_ulong()
    if not X11.XTranslateCoordinates(display, win, root, 0, 0,
                                     ctypes.byref(rx), ctypes.byref(ry), ctypes.byref(child)):
        sys.exit("FAIL: XTranslateCoordinates refused (window on another screen?)")
    return rx.value, ry.value


def window_size(display, win):
    class XWindowAttributes(ctypes.Structure):
        _fields_ = [("x", ctypes.c_int), ("y", ctypes.c_int),
                    ("width", ctypes.c_int), ("height", ctypes.c_int),
                    ("border_width", ctypes.c_int), ("depth", ctypes.c_int),
                    ("visual", ctypes.c_void_p), ("root", ctypes.c_ulong),
                    ("class_", ctypes.c_int), ("bit_gravity", ctypes.c_int),
                    ("win_gravity", ctypes.c_int), ("backing_store", ctypes.c_int),
                    ("backing_planes", ctypes.c_ulong), ("backing_pixel", ctypes.c_ulong),
                    ("save_under", ctypes.c_int), ("colormap", ctypes.c_ulong),
                    ("map_installed", ctypes.c_int), ("map_state", ctypes.c_int),
                    ("all_event_masks", ctypes.c_long), ("your_event_mask", ctypes.c_long),
                    ("do_not_propagate_mask", ctypes.c_long), ("override_redirect", ctypes.c_int),
                    ("screen", ctypes.c_void_p)]

    attrs = XWindowAttributes()
    X11.XGetWindowAttributes(display, win, ctypes.byref(attrs))
    return attrs.width, attrs.height


def net_active_window(display, root):
    """The window manager's own answer, read off the root's _NET_ACTIVE_WINDOW property."""
    atom = X11.XInternAtom(display, b"_NET_ACTIVE_WINDOW", 1)
    if not atom:
        return None
    actual_type = ctypes.c_ulong()
    actual_format = ctypes.c_int()
    nitems = ctypes.c_ulong()
    bytes_after = ctypes.c_ulong()
    prop = ctypes.POINTER(ctypes.c_ubyte)()
    status = X11.XGetWindowProperty(display, root, atom, 0, 1, 0, AnyPropertyType,
                                    ctypes.byref(actual_type), ctypes.byref(actual_format),
                                    ctypes.byref(nitems), ctypes.byref(bytes_after),
                                    ctypes.byref(prop))
    if status != 0 or not prop or nitems.value == 0:
        return None
    try:
        return ctypes.cast(prop, ctypes.POINTER(ctypes.c_ulong))[0]
    finally:
        X11.XFree(prop)


def is_self_or_descendant(display, ancestor, candidate):
    """Focus may name a child of the toplevel; walk up rather than demanding an exact id."""
    win = candidate
    root_ret, parent = ctypes.c_ulong(), ctypes.c_ulong()
    children = ctypes.POINTER(ctypes.c_ulong)()
    nchildren = ctypes.c_uint()
    for _ in range(32):  # depth guard: an X tree is shallow, a cycle would be a server bug
        if win == ancestor:
            return True
        if win in (0, 1):  # None / PointerRoot
            return False
        if not X11.XQueryTree(display, win, ctypes.byref(root_ret), ctypes.byref(parent),
                              ctypes.byref(children), ctypes.byref(nchildren)):
            return False
        if children:
            X11.XFree(children)
        if parent.value == 0 or parent.value == root_ret.value:
            return False
        win = parent.value
    return False


def main():
    needle = sys.argv[1]
    mode = sys.argv[2] if len(sys.argv) > 2 else "--where"
    display = X11.XOpenDisplay(None)
    if not display:
        print("FAIL: XOpenDisplay returned null (no X11 session)", file=sys.stderr)
        return 2
    root = X11.XDefaultRootWindow(display)
    win = find_window(display, root, needle)
    if not win:
        print(f"FAIL: no X window named like '{needle}'", file=sys.stderr)
        return 2

    ox, oy = root_origin(display, root, win)
    width, height = window_size(display, win)

    if mode == "--where":
        print(f"window 0x{win:x} at root {ox},{oy} size {width}x{height}")
        return 0

    if mode == "--focus":
        focus = ctypes.c_ulong()
        revert = ctypes.c_int()
        X11.XGetInputFocus(display, ctypes.byref(focus), ctypes.byref(revert))
        active = net_active_window(display, root)
        focused = is_self_or_descendant(display, win, focus.value)
        active_ok = active is not None and is_self_or_descendant(display, win, active)
        print(f"focus: XGetInputFocus=0x{focus.value:x} "
              f"_NET_ACTIVE_WINDOW={'0x%x' % active if active is not None else 'absent'} "
              f"window=0x{win:x} at root {ox},{oy} size {width}x{height}")
        if not focused:
            print("FAIL: X input focus does not name the app window or a descendant", file=sys.stderr)
            return 2
        if active is None:
            print("FAIL: the window manager publishes no _NET_ACTIVE_WINDOW on this root",
                  file=sys.stderr)
            return 2
        if not active_ok:
            print("FAIL: _NET_ACTIVE_WINDOW names another window", file=sys.stderr)
            return 2
        print("FOCUS OK: X input focus and _NET_ACTIVE_WINDOW both name the app window")
        return 0

    if mode in ("--click", "--rightclick", "--scroll"):
        cx, cy = int(sys.argv[3]), int(sys.argv[4])
        if not (0 <= cx < width and 0 <= cy < height):
            print(f"FAIL: pointer target {cx},{cy} is outside the window {width}x{height}",
                  file=sys.stderr)
            return 2
        # XTestFakeMotionEvent takes ROOT coordinates; the button events follow the pointer.
        # XSync after each so the server has processed the motion before the press is queued —
        # a press delivered before the pointer arrived lands on whatever was under it.
        XTST.XTestFakeMotionEvent(display, -1, ox + cx, oy + cy, CurrentTime)
        X11.XSync(display, 0)

        if mode == "--scroll":
            direction = sys.argv[5]
            notches = int(sys.argv[6]) if len(sys.argv) > 6 else 1
            if direction not in ("up", "down"):
                print(f"FAIL: scroll direction must be up|down (got '{direction}')",
                      file=sys.stderr)
                return 2
            button = 4 if direction == "up" else 5
            for _ in range(notches):
                XTST.XTestFakeButtonEvent(display, button, 1, CurrentTime)
                X11.XSync(display, 0)
                XTST.XTestFakeButtonEvent(display, button, 0, CurrentTime)
                X11.XSync(display, 0)
            print(f"scroll: {notches}x button {button} ({direction}) at window {cx},{cy} "
                  f"= root {ox + cx},{oy + cy} (XTest, window 0x{win:x})")
            return 0

        button = 1 if mode == "--click" else 3
        XTST.XTestFakeButtonEvent(display, button, 1, CurrentTime)
        X11.XSync(display, 0)
        XTST.XTestFakeButtonEvent(display, button, 0, CurrentTime)
        X11.XSync(display, 0)
        print(f"click: button {button} at window {cx},{cy} = root {ox + cx},{oy + cy} "
              f"(XTest, window 0x{win:x})")
        return 0

    print(f"FAIL: unknown mode '{mode}'", file=sys.stderr)
    return 2


if __name__ == "__main__":
    sys.exit(main())
