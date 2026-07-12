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

    /// <summary>
    /// Reads a window property (linux-foreground-title-contract.md §3.2-3.4: EWMH
    /// <c>_NET_ACTIVE_WINDOW</c> and <c>_NET_WM_NAME</c>/<c>WM_NAME</c>). Returns <see cref="Success"/>
    /// on completion; X errors (e.g. BadWindow from a window destroyed mid-read) are delivered
    /// ASYNCHRONOUSLY to the Xlib error handler, NOT via this return value — the caller must run
    /// inside a scoped <see cref="XlibErrorTrap"/> and consult <see cref="XlibErrorTrap.GetLastErrorCode"/>
    /// after <see cref="XSync"/>. long_offset/long_length are in 32-BIT UNITS (not bytes).
    /// </summary>
    [DllImport(LibX11)]
    public static extern int XGetWindowProperty(
        IntPtr display,
        IntPtr window,
        IntPtr property,
        long long_offset,
        long long_length,
        bool delete,
        IntPtr req_type,
        out IntPtr actual_type,
        out int actual_format,
        out ulong nitems,
        out ulong bytes_after,
        out IntPtr prop);

    /// <summary>
    /// Frees data returned by <see cref="XGetWindowProperty"/> (the caller owns the buffer).
    /// </summary>
    [DllImport(LibX11)]
    public static extern int XFree(IntPtr data);

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

    // --- X11 Frame-Source (XGetImage screen capture, linux-framesource-contract.md §3.3/§3.6) ---

    /// <summary>
    /// Captures a rectangular region of <paramref name="drawable"/> (the root window) into
    /// a newly-allocated XImage. Returns IntPtr.Zero on failure. Raises BadMatch
    /// asynchronously (delivered on the next XSync) when the rect is not fully inside the
    /// drawable — must run inside a scoped Xlib error trap (contract §3.2).
    /// </summary>
    /// <param name="planeMask">AllPlanes (~0) for every plane.</param>
    /// <param name="format">ZPixmap (2) for a contiguous-pixel layout matching BGRA.</param>
    [DllImport(LibX11)]
    public static extern IntPtr XGetImage(
        IntPtr display,
        IntPtr drawable,
        int x,
        int y,
        uint width,
        uint height,
        ulong planeMask,
        int format);

    /// <summary>
    /// Frees an XImage AND its data buffer. libX11 exports this as a symbol despite it
    /// being a Xutil.h macro historically; Avalonia's own X11 backend P/Invokes it the same
    /// way (contract §3.3 / §7.1 — high confidence, anchored on Avalonia's interop).
    /// </summary>
    [DllImport(LibX11)]
    public static extern int XDestroyImage(IntPtr ximage);

    /// <summary>
    /// Returns the geometry of a drawable. Used to defensively clamp the capture rect to
    /// the root window before XGetImage (contract §3.5: monitor layouts change between the
    /// ScreenInfo snapshot and capture). Returns 0 (failure) on a trapped error.
    /// </summary>
    [DllImport(LibX11)]
    public static extern int XGetGeometry(
        IntPtr display,
        IntPtr drawable,
        out IntPtr rootReturn,
        out int xReturn,
        out int yReturn,
        out uint widthReturn,
        out uint heightReturn,
        out uint borderWidthReturn,
        out uint depthReturn);

    // --- XGetImage format / plane constants (Xlib.h) ---

    /// <summary>All bits set — capture every plane (Xlib AllPlanes).</summary>
    public const ulong AllPlanes = ~0UL;

    /// <summary>
    /// ZPixmap format: scanlines are contiguous pixels (not bit planes). On little-endian
    /// servers a standard TrueColor 32bpp ZPixmap lays out as B,G,R,X in memory, matching
    /// the RawFrame BGRA contract directly (contract §3.3).
    /// </summary>
    public const int ZPixmap = 2;

    /// <summary>Xlib byte order: least-significant byte first (every x86/ARM desktop).</summary>
    public const int LSBFirst = 0;

    /// <summary>Xlib byte order: most-significant byte first (big-endian; out of scope, §3.3).</summary>
    public const int MSBFirst = 1;

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

    // --- XGetWindowProperty constants (linux-foreground-title-contract.md §3.2-3.4) ---

    /// <summary>Xlib request-success status (the return value of XGetWindowProperty).</summary>
    public const int Success = 0;

    /// <summary>
    /// Predefined atom XA_WINDOW (33) — the property type of <c>_NET_ACTIVE_WINDOW</c>.
    /// </summary>
    public static readonly IntPtr XA_WINDOW = (IntPtr)33;

    /// <summary>
    /// <c>AnyPropertyType</c> sentinel (Xlib's <c>XInternAtom(..., "ANY")</c> is not used; this is
    /// the literal <c>None</c> passed as req_type to accept any property type). Used for the
    /// <c>WM_NAME</c> fallback read (§3.3).
    /// </summary>
    public static readonly IntPtr AnyPropertyType = IntPtr.Zero;
}
