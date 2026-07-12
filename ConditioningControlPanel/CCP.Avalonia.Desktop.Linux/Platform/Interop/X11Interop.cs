using System;
using System.Runtime.InteropServices;

namespace ConditioningControlPanel.Avalonia.Desktop.Linux.Platform.Interop;

/// <summary>
/// P/Invoke bindings for Xlib and XFixes extension.
/// These compile on any platform but resolve at runtime only on Linux with X11.
/// </summary>
internal static class X11Interop
{
    private const string LibX11 = "libX11.so.6";
    private const string LibXfixes = "libXfixes.so.3";

    // --- Xlib Core ---

    [DllImport(LibX11)]
    public static extern IntPtr XOpenDisplay(string? displayName);

    [DllImport(LibX11)]
    public static extern int XCloseDisplay(IntPtr display);

    [DllImport(LibX11)]
    public static extern IntPtr XDefaultRootWindow(IntPtr display);

    [DllImport(LibX11)]
    public static extern int XFlush(IntPtr display);

    [DllImport(LibX11)]
    public static extern IntPtr XInternAtom(IntPtr display, string atomName, bool onlyIfExists);

    [DllImport(LibX11)]
    public static extern int XChangeProperty(
        IntPtr display,
        IntPtr window,
        IntPtr property,
        IntPtr type,
        int format,
        PropertyMode mode,
        IntPtr[] data,
        int nelements);

    [DllImport(LibX11)]
    public static extern int XChangeProperty(
        IntPtr display,
        IntPtr window,
        IntPtr property,
        IntPtr type,
        int format,
        PropertyMode mode,
        ref int data,
        int nelements);

    [DllImport(LibX11)]
    public static extern int XSendEvent(
        IntPtr display,
        IntPtr window,
        bool propagate,
        long eventMask,
        ref XClientMessageEvent eventSend);

    // --- XFixes Extension (input shape for click-through) ---

    [DllImport(LibXfixes)]
    public static extern int XFixesQueryExtension(IntPtr display, out int eventBase, out int errorBase);

    [DllImport(LibXfixes)]
    public static extern IntPtr XFixesCreateRegion(IntPtr display, XRectangle[] rectangles, int nrectangles);

    [DllImport(LibXfixes)]
    public static extern IntPtr XFixesCreateRegion(IntPtr display, IntPtr rectangles, int nrectangles);

    [DllImport(LibXfixes)]
    public static extern void XFixesDestroyRegion(IntPtr display, IntPtr region);

    [DllImport(LibXfixes)]
    public static extern void XFixesSetWindowShapeRegion(
        IntPtr display,
        IntPtr window,
        ShapeKind shapeKind,
        int xOff,
        int yOff,
        IntPtr region);

    // --- Enums and Structs ---

    public enum PropertyMode
    {
        Replace = 0,
        Prepend = 1,
        Append = 2
    }

    /// <summary>
    /// Shape kinds for XFixesSetWindowShapeRegion.
    /// </summary>
    public enum ShapeKind
    {
        /// <summary>Bounding shape (visual outline).</summary>
        Bounding = 0,
        /// <summary>Clip shape (drawing clip).</summary>
        Clip = 1,
        /// <summary>Input shape (determines where input is accepted).</summary>
        Input = 2
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct XRectangle
    {
        public short X;
        public short Y;
        public ushort Width;
        public ushort Height;

        public XRectangle(int x, int y, int width, int height)
        {
            X = (short)x;
            Y = (short)y;
            Width = (ushort)width;
            Height = (ushort)height;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct XClientMessageEvent
    {
        public int Type;           // Always ClientMessage (33)
        public ulong Serial;
        public bool SendEvent;
        public IntPtr Display;
        public IntPtr Window;
        public IntPtr MessageType;
        public int Format;
        public ClientMessageData Data;
    }

    /// <summary>
    /// Client message data union. Use L0-L4 for 32-bit format messages.
    /// </summary>
    [StructLayout(LayoutKind.Explicit, Size = 40)]
    public struct ClientMessageData
    {
        // Long array (5 elements, 8 bytes each = 40 bytes)
        [FieldOffset(0)] public long L0;
        [FieldOffset(8)] public long L1;
        [FieldOffset(16)] public long L2;
        [FieldOffset(24)] public long L3;
        [FieldOffset(32)] public long L4;
    }

    // --- Event Masks ---

    public const long SubstructureRedirectMask = 1 << 20;
    public const long SubstructureNotifyMask = 1 << 19;

    // --- Event Types ---

    public const int ClientMessage = 33;

    // --- Atom Names ---

    public const string NET_WM_STATE = "_NET_WM_STATE";
    public const string NET_WM_STATE_ABOVE = "_NET_WM_STATE_ABOVE";
    public const string NET_WM_STATE_SKIP_TASKBAR = "_NET_WM_STATE_SKIP_TASKBAR";
    public const string NET_WM_STATE_SKIP_PAGER = "_NET_WM_STATE_SKIP_PAGER";
    public const string ATOM = "ATOM";

    // --- _NET_WM_STATE actions ---

    public const long NET_WM_STATE_REMOVE = 0;
    public const long NET_WM_STATE_ADD = 1;
    public const long NET_WM_STATE_TOGGLE = 2;
}
