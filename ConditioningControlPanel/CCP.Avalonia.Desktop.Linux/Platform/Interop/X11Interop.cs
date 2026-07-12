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

    /// <summary>
    /// Flushes the output buffer and waits until all requests have been processed by the
    /// server. Used as the error-collection point of the scoped Xlib error trap
    /// (linux-overlay-contract.md §3.1): reset trap → issue requests → XSync → read trap.
    /// </summary>
    [DllImport(LibX11)]
    public static extern int XSync(IntPtr display, bool discard);

    /// <summary>
    /// Installs a process-global Xlib error handler and returns the previous handler's
    /// function pointer (IntPtr.Zero when the Xlib DEFAULT handler was installed).
    /// The default handler TERMINATES THE PROCESS on any X error (e.g. BadWindow from a
    /// shape call racing window destruction on monitor hot-unplug) — see XlibErrorTrap.
    /// </summary>
    [DllImport(LibX11)]
    public static extern IntPtr XSetErrorHandler(XErrorHandlerDelegate? handler);

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

    /// <summary>
    /// Negotiates and returns the XFixes protocol version. Input-shape regions
    /// (XFixesCreateRegion / XFixesSetWindowShapeRegion) require protocol version >= 2
    /// (linux-overlay-contract.md §3.1 / §7.1 row 4 — fixesproto: regions are v2 additions),
    /// so the probe must gate on major >= 2, not just extension presence.
    /// </summary>
    [DllImport(LibXfixes)]
    public static extern int XFixesQueryVersion(IntPtr display, out int majorVersion, out int minorVersion);

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

    /// <summary>
    /// Xlib error handler signature: int (*)(Display*, XErrorEvent*).
    /// </summary>
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate int XErrorHandlerDelegate(IntPtr display, ref XErrorEvent errorEvent);

    /// <summary>
    /// Matches Xlib's XErrorEvent (Xlib.h). LP64 layout — the Linux head is x64-only.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct XErrorEvent
    {
        public int Type;
        public IntPtr Display;
        public UIntPtr ResourceId;   // XID of the failed resource
        public UIntPtr Serial;       // serial number of the failed request
        public byte ErrorCode;       // e.g. BadWindow = 3, BadMatch = 8
        public byte RequestCode;     // major opcode
        public byte MinorCode;       // minor opcode
    }

    /// <summary>
    /// Matches Xlib's XClientMessageEvent. Size is pinned to 192 bytes because XSendEvent
    /// takes an XEvent* and Xlib copies sizeof(XEvent) — the full event UNION, defined as
    /// "long pad[24]" = 192 bytes on LP64. Without the explicit size Xlib reads past the
    /// end of this 96-byte struct (stack garbage; latent memory-safety bug).
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Size = 192)]
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
