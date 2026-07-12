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
    /// the root window before XGetImage/XShmGetImage (contract §3.5: monitor layouts change
    /// between the ScreenInfo snapshot and capture). Returns 0 (failure) on a trapped error.
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

    // --- X11 frame-source helpers (root visual/depth + display-string locality) ---

    /// <summary>Default screen number for the display (the root window's screen).</summary>
    [DllImport(LibX11)]
    public static extern int XDefaultScreen(IntPtr display);

    /// <summary>
    /// Default visual of a screen — the visual of the root window. Needed by
    /// <see cref="XShmCreateImage"/>: the root-window capture image must match the root's
    /// visual so the pixel layout is B,G,R,X on a little-endian TrueColor server
    /// (contract §3.3), matching the RawFrame BGRA contract directly.
    /// </summary>
    [DllImport(LibX11)]
    public static extern IntPtr XDefaultVisual(IntPtr display, int screen);

    /// <summary>Default depth of a screen — the depth of the root window (typically 24 or 32).</summary>
    [DllImport(LibX11)]
    public static extern int XDefaultDepth(IntPtr display, int screen);

    /// <summary>
    /// Returns the display connection string (e.g. <c>":0"</c> local, <c>"host:0"</c> remote).
    /// Used by the MIT-SHM backend's locality guard (contract §3.4: MIT-SHM only works when
    /// client and server share a machine; the attach round-trip is the authoritative probe,
    /// this is a cheap pre-filter for the obvious remote/SSH-forwarded case).
    /// </summary>
    [DllImport(LibX11)]
    public static extern IntPtr XDisplayString(IntPtr display);

    // --- MIT-SHM shared-memory capture (libXext.so.6, linux-framesource-contract.md §3.4/§3.6) ---
    // NOTE: every XShm* symbol lives in libXext, NOT libX11 (contract §3.4 correction #1).

    private const string LibXext = "libXext.so.6";

    /// <summary>
    /// Queries whether the server supports the MIT-SHM extension. Presence does NOT guarantee
    /// usability — a remote display (SSH forwarding) may report the extension present yet fail
    /// the attach (contract §3.4); the attach round-trip is the authoritative probe.
    /// </summary>
    [DllImport(LibXext)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool XShmQueryExtension(IntPtr display);

    /// <summary>
    /// Allocates an XImage whose pixel storage is a (caller-provided) SysV shared-memory
    /// segment. <paramref name="data"/> is IntPtr.Zero here — the caller sets image-&gt;data AND
    /// shminfo-&gt;shmaddr to the shmat() address AFTER shmget/shmat (contract §3.4 lifecycle).
    /// The <paramref name="shminfo"/> pointer is stored in the image (image-&gt;obdata) and must
    /// remain valid for the image's lifetime, so the backend keeps it in STABLE UNMANAGED
    /// memory (Marshal.AllocHGlobal), never a movable managed field (a GC compaction would
    /// dangle the pointer that XShmGetImage dereferences).
    /// </summary>
    [DllImport(LibXext)]
    public static extern IntPtr XShmCreateImage(
        IntPtr display,
        IntPtr visual,
        uint depth,
        int format,
        IntPtr data,
        IntPtr shminfo,
        uint width,
        uint height);

    /// <summary>
    /// Tells the server to attach the shared-memory segment. Raises <c>BadAccess</c>
    /// ASYNCHRONOUSLY (delivered on the next XSync) when the server refuses (remote display /
    /// SHM policy) — must run inside a scoped Xlib error trap (contract §3.2/§3.4); on failure
    /// the backend tears the segment down and falls back to the XGetImage basic path silently.
    /// </summary>
    [DllImport(LibXext)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool XShmAttach(IntPtr display, IntPtr shminfo);

    /// <summary>
    /// Captures <paramref name="drawable"/> (the root window) into the shared-memory-backed
    /// <paramref name="image"/> at root coords (<paramref name="x"/>,<paramref name="y"/>).
    /// Returns false (and/or raises <c>BadMatch</c> on XSync) when the rect is not fully inside
    /// the drawable — must run inside a scoped Xlib error trap (contract §3.2/§3.4).
    /// </summary>
    [DllImport(LibXext)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool XShmGetImage(
        IntPtr display,
        IntPtr drawable,
        IntPtr image,
        int x,
        int y,
        ulong planeMask);

    /// <summary>
    /// Detaches the server from the shared-memory segment. Called on dispose / size change
    /// BEFORE shmdt (contract §3.4 teardown order: XShmDetach → shmdt → XDestroyImage).
    /// </summary>
    [DllImport(LibXext)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool XShmDetach(IntPtr display, IntPtr shminfo);

    /// <summary>
    /// Native <c>XShmSegmentInfo</c> (X11/extensions/shm.h). LP64 layout: shmid@0, shmaddr@8,
    /// readOnly@16, size 24. Passed to XShmCreateImage/XShmAttach as a POINTER into stable
    /// unmanaged memory (the image holds this pointer in image-&gt;obdata across calls — a
    /// movable managed copy would dangle after a GC). <c>Bool</c> is <c>int</c> on X11, so the
    /// 4-byte <see cref="ReadOnly"/> maps 1:1 to the native field.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct XShmSegmentInfo
    {
        /// <summary>SysV shared-memory segment id returned by shmget.</summary>
        public int Shmid;
        /// <summary>Mapped address returned by shmat (also stored in image-&gt;data).</summary>
        public IntPtr Shmaddr;
        /// <summary>False — capture segments are read-write from the client.</summary>
        public bool ReadOnly;
    }

    // --- libc SysV shared memory (linux-framesource-contract.md §3.6) ---

    private const string LibC = "libc";

    /// <summary>shmget <c>key_t</c>: <c>IPC_PRIVATE</c> (0) — create a new private segment.</summary>
    public const int IPC_PRIVATE = 0;

    /// <summary>shmget flag: create the segment if it does not exist.</summary>
    public const int IPC_CREAT = 0b001_000_000_000; // octal 01000 == 512

    /// <summary>shmctl command: mark the segment for removal (refcount-gated deletion).</summary>
    public const int IPC_RMID = 0;

    /// <summary>
    /// shmget mode: OWNER READ/WRITE ONLY (octal 0600 == 384). The draft's 0777 left screen
    /// pixels in a world-readable-writable segment — corrected to 0600
    /// (contract §3.4 correction #3).
    /// </summary>
    public const int ShmMode0600 = 0b110_000_000; // octal 0600 == 384

    /// <summary>
    /// Allocates a SysV shared-memory segment of <paramref name="size"/> bytes. Returns the
    /// shmid (&gt;= 0) or -1 on failure. Use <see cref="IPC_PRIVATE"/> +
    /// <see cref="IPC_CREAT"/> | <see cref="ShmMode0600"/> (contract §3.4).
    /// </summary>
    [DllImport(LibC, SetLastError = true)]
    public static extern int shmget(int key, UIntPtr size, int shmflg);

    /// <summary>
    /// Maps the segment at an arbitrary address. Returns the address, or <c>(IntPtr)(-1)</c>
    /// on failure. Pass <see cref="IntPtr.Zero"/> for <paramref name="shmaddr"/> (let the
    /// kernel choose).
    /// </summary>
    [DllImport(LibC, SetLastError = true)]
    public static extern IntPtr shmat(int shmid, IntPtr shmaddr, int shmflg);

    /// <summary>Unmaps the segment from the calling process's address space.</summary>
    [DllImport(LibC, SetLastError = true)]
    public static extern int shmdt(IntPtr shmaddr);

    /// <summary>
    /// Control operation. <see cref="IPC_RMID"/> marks the segment for deletion — call
    /// IMMEDIATELY after the server attach is confirmed (contract §3.4 correction #4) so the
    /// kernel reclaims the segment even if the process dies; the segment lives until BOTH
    /// server and client detach (refcount-gated), so the post-attach mark never frees a
    /// still-attached segment.
    /// </summary>
    [DllImport(LibC, SetLastError = true)]
    public static extern int shmctl(int shmid, int cmd, IntPtr buf);

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
