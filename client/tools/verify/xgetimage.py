#!/usr/bin/env python3
# CCP greenfield verification harness — tier 2 WSLg/X11 capture (SP-008).
# Finds the app's X window by name and captures it via XGetImage (libX11 ctypes) to a BMP.
# Why: WSLg RAIL windows are invisible to Windows-side GDI capture (SP-007 surprise #3);
# no extra packages (no xdotool/scrot) exist in the WSL2 image. Usage:
#   python3 xgetimage.py "<window title substring>" <out.bmp>
import ctypes
import sys

X11 = ctypes.cdll.LoadLibrary("libX11.so.6")
ZPixmap = 2
AllPlanes = ctypes.c_ulong(~0).value


class XImage(ctypes.Structure):
    _fields_ = [
        ("width", ctypes.c_int), ("height", ctypes.c_int),
        ("xoffset", ctypes.c_int), ("format", ctypes.c_int),
        ("data", ctypes.c_void_p),
        ("byte_order", ctypes.c_int), ("bitmap_unit", ctypes.c_int),
        ("bitmap_bit_order", ctypes.c_int), ("bitmap_pad", ctypes.c_int),
        ("depth", ctypes.c_int), ("bytes_per_line", ctypes.c_int),
        ("bits_per_pixel", ctypes.c_int),
        ("red_mask", ctypes.c_ulong), ("green_mask", ctypes.c_ulong), ("blue_mask", ctypes.c_ulong),
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


def main():
    needle, out_path = sys.argv[1], sys.argv[2]
    crop = None
    if len(sys.argv) >= 7 and sys.argv[3] == "--crop":
        # Window-relative pixel coordinates from the app's layout probe: --crop X Y W H.
        # Observed on WSLg (SP-008): the probe's PointToScreen output equals the card's
        # offset within the X window (window opens at the Avalonia monitor origin, the X
        # window is the client area) — a window-relative crop lands exactly on the card
        # (966/1464 border pixels, the SP-007 count). X root coordinates are a DIFFERENT
        # space (WSLg tiles monitors under one root); never mix them.
        crop = tuple(int(v) for v in sys.argv[3 + 1:3 + 5])
    display = X11.XOpenDisplay(None)
    if not display:
        sys.exit("FAIL: XOpenDisplay returned null (no X11 session)")
    root = X11.XDefaultRootWindow(display)
    win = find_window(display, root, needle)
    if not win:
        sys.exit(f"FAIL: no X window named like '{needle}'")

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
    width, height = attrs.width, attrs.height

    if crop is not None:
        cx, cy, cw, ch = crop
        if cx < 0 or cy < 0 or cx + cw > width or cy + ch > height:
            sys.exit(f"FAIL: window-relative crop {crop} outside window size {width}x{height}")
        ox, oy, width, height = cx, cy, cw, ch
    else:
        ox, oy = 0, 0

    X11.XGetImage.restype = ctypes.POINTER(XImage)
    image = X11.XGetImage(display, win, ox, oy, width, height, AllPlanes, ZPixmap)
    if not image:
        sys.exit("FAIL: XGetImage returned null")
    img = image.contents
    raw = ctypes.string_at(img.data, img.bytes_per_line * img.height)
    X11.XDestroyImage(image)
    X11.XCloseDisplay(display)

    def shift_of(mask):
        s = 0
        while mask and not (mask & 1):
            mask >>= 1
            s += 1
        return s

    rs, gs, bs = shift_of(img.red_mask), shift_of(img.green_mask), shift_of(img.blue_mask)
    bpp = img.bits_per_pixel // 8

    # BMP (24bpp, bottom-up, rows padded to 4 bytes).
    row_size = (width * 3 + 3) & ~3
    pixel_size = row_size * height
    header = b"BM" + (54 + pixel_size).to_bytes(4, "little") + b"\0" * 4 + (54).to_bytes(4, "little")
    info = (40).to_bytes(4, "little") + width.to_bytes(4, "little", signed=True)
    info += height.to_bytes(4, "little", signed=True) + (1).to_bytes(2, "little")
    info += (24).to_bytes(2, "little") + b"\0" * 4 + pixel_size.to_bytes(4, "little") + b"\0" * 16

    rows = bytearray(pixel_size)
    for y in range(height):
        src_row = y * img.bytes_per_line
        dst_row = (height - 1 - y) * row_size
        for x in range(width):
            px = int.from_bytes(raw[src_row + x * bpp: src_row + x * bpp + 4], "little")
            rows[dst_row + x * 3] = (px & img.blue_mask) >> bs
            rows[dst_row + x * 3 + 1] = (px & img.green_mask) >> gs
            rows[dst_row + x * 3 + 2] = (px & img.red_mask) >> rs

    with open(out_path, "wb") as f:
        f.write(header + info + rows)
    print(f"CAPTURE: {out_path} ({width}x{height})")


if __name__ == "__main__":
    main()
