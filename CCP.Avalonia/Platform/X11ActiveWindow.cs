using System;
using System.Runtime.InteropServices;
using System.Text;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>
/// The Linux title source for <c>WindowAwarenessService</c> (WPF: user32 GetForegroundWindow +
/// GetWindowText, App.xaml.cs ReadForegroundWindowTitle). Reads the EWMH root property
/// <c>_NET_ACTIVE_WINDOW</c>, then that window's <c>_NET_WM_NAME</c> (UTF-8), falling back to
/// <c>WM_NAME</c>.
///
/// <para><b>Only X11/XWayland windows are visible to it.</b> Under a Wayland session the compositor
/// exposes no focused-window title to clients at all, so a native Wayland window (most GTK4/Qt apps
/// there) reads as "" = Unknown and she simply says nothing about it. That is a platform limit, not a
/// bug: there is no portable Wayland API for this, by design.</para>
///
/// <para>Returns "" whenever there is no display, no active window or no title, never throws.
/// Called on the UI thread every 1.5s; one owned connection, opened lazily.</para>
/// </summary>
internal static class X11ActiveWindow
{
    private const string LibX11 = "libX11.so.6";
    private delegate int XErrorHandler(IntPtr display, IntPtr errorEvent);

    [DllImport(LibX11)] private static extern IntPtr XOpenDisplay(IntPtr name);
    [DllImport(LibX11)] private static extern IntPtr XDefaultRootWindow(IntPtr display);
    [DllImport(LibX11)] private static extern IntPtr XInternAtom(IntPtr display, string name, bool onlyIfExists);
    [DllImport(LibX11)] private static extern IntPtr XSetErrorHandler(XErrorHandler handler);
    [DllImport(LibX11)] private static extern int XFree(IntPtr data);
    [DllImport(LibX11)] private static extern int XGetWindowProperty(IntPtr display, IntPtr window, IntPtr property,
        long offset, long length, bool delete, IntPtr reqType, out IntPtr actualType, out int actualFormat,
        out ulong nItems, out ulong bytesAfter, out IntPtr prop);

    // A window can close between the two reads; the default Xlib handler would exit the process on
    // that BadWindow. Same swallow-and-carry-on stance as X11Overlay.
    private static readonly XErrorHandler IgnoreErrors = (_, _) => 0;

    private static IntPtr _display;
    private static bool _tried;
    private static IntPtr _root, _active, _netWmName, _utf8, _wmName;

    public static string ReadTitle()
    {
        try
        {
            if (!_tried)
            {
                _tried = true;
                if (!OperatingSystem.IsLinux() || string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY"))) return "";
                _display = XOpenDisplay(IntPtr.Zero);
                if (_display == IntPtr.Zero) return "";
                XSetErrorHandler(IgnoreErrors);
                _root = XDefaultRootWindow(_display);
                _active = XInternAtom(_display, "_NET_ACTIVE_WINDOW", false);
                _netWmName = XInternAtom(_display, "_NET_WM_NAME", false);
                _utf8 = XInternAtom(_display, "UTF8_STRING", false);
                _wmName = XInternAtom(_display, "WM_NAME", false);
            }
            if (_display == IntPtr.Zero) return "";

            var window = ReadWindow(_root, _active);
            if (window == IntPtr.Zero) return "";
            return ReadString(window, _netWmName, _utf8) ?? ReadString(window, _wmName, IntPtr.Zero /* AnyPropertyType */) ?? "";
        }
        catch { return ""; }   // no libX11 on this machine: nothing to observe
    }

    private static IntPtr ReadWindow(IntPtr window, IntPtr property)
    {
        if (XGetWindowProperty(_display, window, property, 0, 1, false, IntPtr.Zero, out _, out var format,
                out var n, out _, out var prop) != 0 || prop == IntPtr.Zero) return IntPtr.Zero;
        try { return format == 32 && n > 0 ? Marshal.ReadIntPtr(prop) : IntPtr.Zero; }   // format-32 items are longs
        finally { XFree(prop); }
    }

    private static string? ReadString(IntPtr window, IntPtr property, IntPtr type)
    {
        if (XGetWindowProperty(_display, window, property, 0, 1024, false, type, out _, out var format,
                out var n, out _, out var prop) != 0 || prop == IntPtr.Zero) return null;
        try
        {
            if (format != 8 || n == 0) return null;
            var bytes = new byte[(int)n];
            Marshal.Copy(prop, bytes, 0, bytes.Length);
            return Encoding.UTF8.GetString(bytes);
        }
        finally { XFree(prop); }
    }
}
