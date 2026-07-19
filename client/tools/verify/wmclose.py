#!/usr/bin/env python3
# CCP greenfield verification — X11 graceful close via WM_DELETE_WINDOW (SP-010).
# Contract: client/docs/release-publish-gates.md §6. Same libX11 ctypes mechanism as the
# proven xgetimage.py (SP-008). Three modes:
#   wmclose.py "<title needle>" --check     find the window AND assert its XID advertises
#                                           WM_DELETE_WINDOW via XGetWMProtocols (the WSLg
#                                           RAIL reparent trap: never send blind)
#   wmclose.py "<title needle>" --negative  send a deliberately MALFORMED ClientMessage
#                                           (wrong data atom) — negative control; the app
#                                           must ignore it and stay alive
#   wmclose.py "<title needle>"             send the real WM_DELETE_WINDOW ClientMessage
#                                           (type 33, message_type=WM_PROTOCOLS, format 32,
#                                           data.l[0]=WM_DELETE_WINDOW) + XFlush
# Exit 0 = the requested action completed. Delivery/exit-code proof is the CALLER's job
# (wait on the real PID — XSendEvent returns queuing success, never delivery).
import ctypes
import sys

X11 = ctypes.cdll.LoadLibrary("libX11.so.6")
ClientMessage = 33
NoEventMask = 0


class XClientMessageEvent(ctypes.Structure):
    _fields_ = [
        ("type", ctypes.c_int),
        ("serial", ctypes.c_ulong),
        ("send_event", ctypes.c_int),
        ("display", ctypes.c_void_p),
        ("window", ctypes.c_ulong),
        ("message_type", ctypes.c_ulong),
        ("format", ctypes.c_int),
        ("l", ctypes.c_long * 5),
        ("_pad", ctypes.c_char * 96),  # XEvent union is 192 bytes
    ]


def find_window(display, root, needle):
    """Depth-first search for the first window whose name contains needle."""
    X11.XFetchName.restype = ctypes.c_int
    name = ctypes.c_char_p()
    if X11.XFetchName(display, root, ctypes.byref(name)) and name.value:
        if needle in name.value.decode("utf-8", "replace"):
            X11.XFree(name)
            return root
    if name.value:
        X11.XFree(name)
    parent = ctypes.c_ulong()
    root_ret = ctypes.c_ulong()
    children = ctypes.POINTER(ctypes.c_ulong)()
    nchildren = ctypes.c_uint()
    if not X11.XQueryTree(display, root, ctypes.byref(root_ret), ctypes.byref(parent),
                          ctypes.byref(children), ctypes.byref(nchildren)):
        return None
    try:
        for i in range(nchildren.value):
            found = find_window(display, children[i], needle)
            if found:
                return found
    finally:
        if children:
            X11.XFree(children)
    return None


def advertises_delete(display, xid, wm_delete):
    """XGetWMProtocols: does this XID advertise WM_DELETE_WINDOW?"""
    protocols = ctypes.POINTER(ctypes.c_ulong)()
    count = ctypes.c_int()
    X11.XGetWMProtocols.restype = ctypes.c_int
    if not X11.XGetWMProtocols(display, xid, ctypes.byref(protocols), ctypes.byref(count)):
        return False
    try:
        return any(protocols[i] == wm_delete for i in range(count.value))
    finally:
        if protocols:
            X11.XFree(protocols)


def send_client_message(display, xid, wm_protocols, data_atom):
    ev = XClientMessageEvent()
    ev.type = ClientMessage
    ev.window = xid
    ev.message_type = wm_protocols
    ev.format = 32
    ev.l[0] = data_atom
    ev.l[1] = 0  # CurrentTime — not validated for the delete path
    X11.XSendEvent(display, xid, 0, NoEventMask, ctypes.byref(ev))
    X11.XFlush(display)


def main():
    needle = sys.argv[1]
    mode = sys.argv[2] if len(sys.argv) > 2 else "--close"
    display = X11.XOpenDisplay(None)
    if not display:
        print("FAIL: cannot open X display", file=sys.stderr)
        return 2
    root = X11.XDefaultRootWindow(display)
    wm_protocols = X11.XInternAtom(display, b"WM_PROTOCOLS", 0)
    wm_delete = X11.XInternAtom(display, b"WM_DELETE_WINDOW", 0)

    xid = find_window(display, root, needle)
    if not xid:
        print(f"FAIL: no window named like '{needle}'", file=sys.stderr)
        return 2
    if not advertises_delete(display, xid, wm_delete):
        print(f"FAIL: window {xid} does not advertise WM_DELETE_WINDOW "
              "(frame/reparent trap — refusing to send blind)", file=sys.stderr)
        return 2

    if mode == "--check":
        print(f"OK: window {xid} advertises WM_DELETE_WINDOW")
        return 0
    if mode == "--negative":
        # Malformed control: correct envelope, WRONG data atom — must be ignored.
        bogus = X11.XInternAtom(display, b"_CCP_BOGUS_CLOSE", 0)
        send_client_message(display, xid, wm_protocols, bogus)
        print(f"OK: negative-control ClientMessage sent to {xid}")
        return 0

    send_client_message(display, xid, wm_protocols, wm_delete)
    print(f"OK: WM_DELETE_WINDOW sent to {xid}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
