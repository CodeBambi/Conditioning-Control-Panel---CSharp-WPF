using System;
using System.Runtime.InteropServices;
using Avalonia;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>
/// Where the pointer is, whether a mouse button is down and whether Escape is held, desktop-wide,
/// in PHYSICAL pixels. The non-consuming read that stands in for WPF's low-level mouse/keyboard
/// hooks (<c>GlobalMouseHook</c> / <c>GlobalKeyboardHook</c>): <c>XQueryPointer</c> and
/// <c>XQueryKeymap</c> only look, so a click still lands on whatever it was aimed at, exactly as the
/// WPF hook returned false and let it through. A grab would swallow it, which is why this is not one.
///
/// <para>Only X11/XWayland surfaces report: under Wayland the pointer over a native Wayland window is
/// invisible to X clients, so click-away there only sees clicks on X windows. Null when there is no
/// X display at all (headless, Windows, macOS). One owned connection, opened lazily, UI thread only.</para>
/// </summary>
internal static class X11Pointer
{
    private const string LibX11 = "libX11.so.6";
    private const uint Button1Mask = 1 << 8, Button3Mask = 1 << 10;
    private const long XK_Escape = 0xff1b;

    [DllImport(LibX11)] private static extern IntPtr XOpenDisplay(IntPtr name);
    [DllImport(LibX11)] private static extern IntPtr XDefaultRootWindow(IntPtr display);
    [DllImport(LibX11)] private static extern bool XQueryPointer(IntPtr display, IntPtr window, out IntPtr root,
        out IntPtr child, out int rootX, out int rootY, out int winX, out int winY, out uint mask);
    [DllImport(LibX11)] private static extern int XQueryKeymap(IntPtr display, ref byte keys);
    [DllImport(LibX11)] private static extern byte XKeysymToKeycode(IntPtr display, IntPtr keysym);

    private static bool _tried;
    private static IntPtr _display, _root;
    private static byte _esc;

    public static (PixelPoint At, bool Pressed, bool Escape)? Read()
    {
        try
        {
            if (!_tried)
            {
                _tried = true;
                if (!OperatingSystem.IsLinux()) return null;
                _display = XOpenDisplay(IntPtr.Zero);
                if (_display == IntPtr.Zero) return null;
                _root = XDefaultRootWindow(_display);
                _esc = XKeysymToKeycode(_display, (IntPtr)XK_Escape);
            }
            if (_display == IntPtr.Zero) return null;
            if (!XQueryPointer(_display, _root, out _, out _, out int x, out int y, out _, out _, out uint mask))
                return null;
            // Privacy: the keymap is every key held right now. It lives only in this stack buffer,
            // only the Escape bit leaves it, and it is wiped before anything else runs.
            Span<byte> keys = stackalloc byte[32];
            XQueryKeymap(_display, ref MemoryMarshal.GetReference(keys));
            bool esc = _esc != 0 && (keys[_esc >> 3] & (1 << (_esc & 7))) != 0;
            keys.Clear();
            return (new PixelPoint(x, y), (mask & (Button1Mask | Button3Mask)) != 0, esc);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[X11Pointer] read failed, no desktop-wide pointer on this session");
            _display = IntPtr.Zero;
            return null;
        }
    }
}
