using System;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>
/// The two window behaviours the Chaos overlays need that Avalonia does not expose: making a
/// visible window transparent to the mouse, and pinning one overlay directly above another.
///
/// <para><b>Why this is a shim and not a port.</b> On Windows these are
/// <c>WS_EX_TRANSPARENT</c> (39 files) and <c>SetWindowPos(HWND_TOPMOST)</c> (34 files), each
/// P/Invoked at the call site. Both have exact X11 equivalents, so the overlays keep behaving
/// as they do today rather than losing a feature — but Avalonia.X11 12.1.1 binds neither, which
/// was verified by extracting the assembly's symbols rather than assumed.</para>
///
/// <para><b>Topmost is deliberately absent from this class.</b> Avalonia already maps
/// <c>Window.Topmost</c> to <c>_NET_WM_STATE_ABOVE</c>, which is the correct X11 mechanism, so
/// the 34 <c>HWND_TOPMOST</c> sites port to that property and need nothing here. Wrapping it
/// would add an indirection that only obscures where the behaviour comes from. The one case it
/// does NOT cover — sitting above ANOTHER app's focused fullscreen window, which KWin promotes
/// above the keep-above layer — is what <see cref="SetOverrideRedirect"/> is for: an
/// override-redirect window is never managed by the WM at all, so no WM layer policy applies.</para>
///
/// <para><b>Windows goes through the same entry points.</b> A window whose platform handle is an
/// "HWND" is routed to <see cref="Win32Overlay"/> (<see cref="BackendOf"/>), so no call site
/// branches on the OS. <c>RestackAbove</c> has no Windows half yet and returns false there.</para>
///
/// <para><b>Everything in here fails silently if written carelessly</b>, which is why each guard
/// below is explicit rather than defensive habit:</para>
/// <list type="bullet">
///   <item>X extensions are negotiated PER CONNECTION. Without <c>XFixesQueryExtension</c> on
///         this class's own display, the shape requests are dropped by the server with no error
///         and the window simply stays clickable.</item>
///   <item>This class owns its display, so nothing else ever flushes it. Unflushed requests sit
///         in the buffer indefinitely and the toggle appears to do nothing.</item>
///   <item>Xlib's default error handler calls <c>exit()</c>. The overlays create and destroy
///         windows constantly, so an XID that dies between the handle read and the call here
///         would take the whole app down. The handler below logs and continues.</item>
///   <item>Xlib is not thread-safe and the display is shared, so every entry point locks.</item>
/// </list>
/// </summary>
internal enum OverlayBackend { None, X11, Win32 }

internal static class X11Overlay
{
    // Values read from the system headers, not from memory - a wrong constant here compiles and
    // then silently addresses the wrong thing.
    private const int ShapeInput = 2;        // X11/extensions/shapeconst.h
    private const int ClientMessage = 33;    // X11/X.h
    private const int Above = 0;             // X11/X.h
    private const long SubstructureMask = 1572864;  // SubstructureRedirect|SubstructureNotify
    private const long SourcePager = 2;      // netwm_def.h RequestSource::FromTool
    private const int XEventSize = 192;      // sizeof(XEvent), from the compiler
    private const int XSetWindowAttributesSize = 112;  // sizeof(XSetWindowAttributes), from the compiler
    private const nuint CWOverrideRedirect = 1 << 9;   // X11/X.h

    private const string LibX11 = "libX11.so.6";
    private const string LibXfixes = "libXfixes.so.3";

    private delegate int XErrorHandler(IntPtr display, IntPtr errorEvent);

    [DllImport(LibX11)] private static extern IntPtr XOpenDisplay(IntPtr name);
    [DllImport(LibX11)] private static extern int XFlush(IntPtr display);
    [DllImport(LibX11)] private static extern int XSync(IntPtr display, bool discard);
    [DllImport(LibX11)] private static extern int XChangeWindowAttributes(IntPtr display, IntPtr window, nuint valueMask, IntPtr attributes);
    [DllImport(LibX11)] private static extern IntPtr XSetErrorHandler(XErrorHandler handler);
    [DllImport(LibX11)] private static extern IntPtr XDefaultRootWindow(IntPtr display);
    [DllImport(LibX11)] private static extern IntPtr XInternAtom(IntPtr display, string name, bool onlyIfExists);
    [DllImport(LibX11)] private static extern int XChangeProperty(IntPtr display, IntPtr window, IntPtr property, IntPtr type, int format, int mode, IntPtr[] data, int count);
    [DllImport(LibX11)] private static extern IntPtr XGetSelectionOwner(IntPtr display, IntPtr selection);
    [DllImport(LibX11)] private static extern int XDefaultScreen(IntPtr display);
    [DllImport(LibX11)] private static extern int XSendEvent(IntPtr display, IntPtr window, bool propagate, long mask, IntPtr sendEvent);

    [DllImport(LibXfixes)] private static extern int XFixesQueryExtension(IntPtr display, out int eventBase, out int errorBase);
    [DllImport(LibXfixes)] private static extern IntPtr XFixesCreateRegion(IntPtr display, IntPtr rectangles, int count);
    [DllImport(LibXfixes)] private static extern void XFixesSetWindowShapeRegion(IntPtr display, IntPtr window, int shapeKind, int xOffset, int yOffset, IntPtr region);
    [DllImport(LibXfixes)] private static extern void XFixesDestroyRegion(IntPtr display, IntPtr region);

    private static readonly object Gate = new();

    // Held in a static field on purpose: Xlib keeps the raw function pointer, so letting the
    // delegate be collected turns the next X error into a jump into freed memory.
    private static readonly XErrorHandler ErrorHandler = OnXError;

    private static IntPtr _display;
    private static bool _initialised;
    private static bool _usable;

    /// <summary>True when this process can actually drive the calls below - on Windows (the
    /// <see cref="Win32Overlay"/> shim), or when an X11 display is open and the server offers
    /// XFixes. False on headless Linux CI and under a native Wayland backend, where every method
    /// here is a no-op.</summary>
    internal static bool IsAvailable
    {
        get { if (OperatingSystem.IsWindows()) return true; lock (Gate) { return EnsureDisplay(); } }
    }

    /// <summary>EWMH: a compositing manager owns <c>_NET_WM_CM_S{screen}</c>. Without one the window
    /// manager refuses per-pixel transparency, so a tint would paint an opaque block.</summary>
    internal static bool IsCompositing
    {
        get
        {
            if (OperatingSystem.IsWindows()) return true;   // DWM always composes
            lock (Gate)
            {
                if (!EnsureDisplay()) return false;
                var atom = XInternAtom(_display, $"_NET_WM_CM_S{XDefaultScreen(_display)}", false);
                return XGetSelectionOwner(_display, atom) != IntPtr.Zero;
            }
        }
    }

    /// <summary>Makes <paramref name="window"/> transparent to the mouse while it keeps drawing,
    /// or gives it back its input. The X11 equivalent of adding and clearing
    /// <c>WS_EX_TRANSPARENT</c>, and reversible at runtime the same way.
    ///
    /// <para>An empty input region passes every event to whatever is underneath; region
    /// <c>None</c> restores the default, which is the window's whole shape. Note this is
    /// strictly more capable than the Win32 original: a NON-empty region would give partial
    /// click-through, which <c>WS_EX_TRANSPARENT</c> cannot express at all.</para></summary>
    /// <returns>False when the platform cannot do this, so callers can branch without catching.</returns>
    internal static bool SetClickThrough(TopLevel window, bool clickThrough)
    {
        if (TryGet(window, OverlayBackend.Win32, out var hwnd)) return Win32Overlay.SetClickThrough(window, hwnd, clickThrough);
        if (!TryGetXid(window, out var xid)) return false;

        lock (Gate)
        {
            if (!EnsureDisplay()) return false;

            var region = IntPtr.Zero;             // None
            if (clickThrough)
                region = XFixesCreateRegion(_display, IntPtr.Zero, 0);

            XFixesSetWindowShapeRegion(_display, xid, ShapeInput, 0, 0, region);

            if (region != IntPtr.Zero)
                XFixesDestroyRegion(_display, region);

            XFlush(_display);
            return true;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XRectangle { public short X, Y; public ushort W, H; }

    /// <summary>Partial click-through: only <paramref name="rect"/> (window-relative px) takes input,
    /// null gives the whole window back. X11 has no per-pixel alpha hit-test, so this is how the
    /// attached avatar tube's transparent margin stops swallowing the shell's clicks.</summary>
    internal static bool SetInputRect(TopLevel window, PixelRect? rect)
    {
        if (!TryGetXid(window, out var xid)) return false;
        lock (Gate)
        {
            if (!EnsureDisplay()) return false;
            var region = IntPtr.Zero;
            if (rect is PixelRect r)
            {
                var xr = new XRectangle { X = (short)r.X, Y = (short)r.Y, W = (ushort)Math.Max(0, r.Width), H = (ushort)Math.Max(0, r.Height) };
                var buf = Marshal.AllocHGlobal(Marshal.SizeOf<XRectangle>());
                try { Marshal.StructureToPtr(xr, buf, false); region = XFixesCreateRegion(_display, buf, 1); }
                finally { Marshal.FreeHGlobal(buf); }
            }
            XFixesSetWindowShapeRegion(_display, xid, ShapeInput, 0, 0, region);
            if (region != IntPtr.Zero) XFixesDestroyRegion(_display, region);
            XFlush(_display);
            return true;
        }
    }

    private static IntPtr _rectBuf;   // grown, never freed: one per process, reused every frame
    private static int _rectCap;

    /// <summary>Several input rects (window-relative px); count 0 = fully click-through. Allocation-
    /// free after the first call at a given size, so a per-frame caller (the bubble field) costs
    /// one XFixes request, not garbage.</summary>
    internal static bool SetInputRects(TopLevel window, PixelRect[] rects, int count)
    {
        if (TryGet(window, OverlayBackend.Win32, out var hwnd)) return Win32Overlay.SetInputRects(window, hwnd, rects, count);
        if (!TryGetXid(window, out var xid)) return false;
        lock (Gate)
        {
            if (!EnsureDisplay()) return false;
            if (count > _rectCap)
            {
                if (_rectBuf != IntPtr.Zero) Marshal.FreeHGlobal(_rectBuf);
                _rectCap = Math.Max(count, 64);
                _rectBuf = Marshal.AllocHGlobal(_rectCap * 8);
            }
            for (var i = 0; i < count; i++)
            {
                var r = rects[i];
                Marshal.WriteInt16(_rectBuf, i * 8, (short)r.X);
                Marshal.WriteInt16(_rectBuf, i * 8 + 2, (short)r.Y);
                Marshal.WriteInt16(_rectBuf, i * 8 + 4, (short)(ushort)Math.Max(0, r.Width));
                Marshal.WriteInt16(_rectBuf, i * 8 + 6, (short)(ushort)Math.Max(0, r.Height));
            }
            var region = XFixesCreateRegion(_display, count > 0 ? _rectBuf : IntPtr.Zero, count);
            XFixesSetWindowShapeRegion(_display, xid, ShapeInput, 0, 0, region);
            XFixesDestroyRegion(_display, region);
            XFlush(_display);
            return true;
        }
    }

    /// <summary>Takes <paramref name="window"/> out of the window manager's hands: no frame, no
    /// focus, no taskbar entry, no WM layer policy - the X11 form of a Win32 tool/topmost overlay.
    ///
    /// <para><b>Must run before <c>Show()</c>.</b> The WM decides whether to manage a window when
    /// it maps, so setting this on a mapped window changes nothing until the next map. Avalonia's
    /// X11 backend creates the XID in the <c>Window</c> constructor and maps only in <c>Show()</c>
    /// (proved by <c>--overlay-check</c>, which reads map_state before showing).</para>
    ///
    /// <para><b>XSync, not XFlush.</b> This display is a different connection from Avalonia's, and
    /// the server orders requests per connection only. A flushed-but-unprocessed request could
    /// land after Avalonia's XMapWindow; XSync returns once the server has applied it.</para>
    ///
    /// <para><b>Why this also places the window.</b> Until override-redirect is set, every
    /// ConfigureWindow on the window is redirected to the WM as a ConfigureRequest, and the WM
    /// replays it whenever it gets round to it. Avalonia's X11 <c>Position</c> setter issues one
    /// (x/y), and its DPI rescale on that move issues another carrying the 300x200-logical default
    /// size. Set <c>Position</c> first and KWin can replay that stale size AFTER Show() has
    /// configured the real one - measured on XWayland: 537x358 overlays on the non-primary
    /// screens in 14 of 25 runs, 0 of 25 with this order. So override-redirect goes on before
    /// any geometry, and geometry is set here so no caller can get the order wrong. Width/Height
    /// use the window's own scaling after the move, i.e. the exact factor Avalonia multiplies back
    /// by in Show().</para></summary>
    /// <param name="bounds">Where the overlay goes, in screen pixels (e.g. <c>Screen.Bounds</c>).
    /// Applied on every platform; only the override-redirect part is X11-only.</param>
    /// <returns>False when the platform cannot do this (no XID, no display, not 64-bit) or the server refused it.</returns>
    /// <param name="passive">A toast that takes clicks but never focus: Windows keeps WS_EX_NOACTIVATE
    /// (X11 override-redirect already never takes focus).</param>
    internal static bool SetOverrideRedirect(Window window, PixelRect bounds, bool passive = false)
    {
        var ok = SetOverrideRedirect(window, passive);
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Position = bounds.Position;
        window.Width = bounds.Width / window.DesktopScaling;
        window.Height = bounds.Height / window.DesktopScaling;
        return ok;
    }

    private static bool SetOverrideRedirect(TopLevel window, bool passive)
    {
        if (TryGet(window, OverlayBackend.Win32, out var hwnd)) return Win32Overlay.SetOverrideRedirect(window, hwnd, passive);
        // The struct offsets below are the LP64 layout (x86_64, arm64).
        if (IntPtr.Size != 8 || !TryGetXid(window, out var xid)) return false;

        lock (Gate)
        {
            if (!EnsureDisplay()) return false;

            // XSetWindowAttributes: 112 bytes, override_redirect (Bool = int) at 88 - offsetof()
            // against the real headers on x86_64. Only the field named in the mask is read.
            var attrs = Marshal.AllocHGlobal(XSetWindowAttributesSize);
            try
            {
                for (var i = 0; i < XSetWindowAttributesSize; i += IntPtr.Size) Marshal.WriteIntPtr(attrs, i, IntPtr.Zero);
                Marshal.WriteInt32(attrs, 88, 1);
                _xErrored = false;
                XChangeWindowAttributes(_display, xid, CWOverrideRedirect, attrs);
                XSync(_display, false);
                return !_xErrored; // e.g. BadWindow: the server refused the request
            }
            finally { Marshal.FreeHGlobal(attrs); }
        }
    }

    /// <summary>Whole-window alpha applied by the COMPOSITOR (<c>_NET_WM_WINDOW_OPACITY</c>), the X11
    /// form of a layered window's alpha. Unlike a visual Opacity it costs the app no re-render:
    /// measured on XWayland/GLX, re-rendering the overlays every fade frame stalled the UI
    /// dispatcher ~1.7 s per flash burst. Works before Show(), so a window can map invisible.</summary>
    internal static bool SetOpacity(TopLevel window, double alpha)
    {
        if (TryGet(window, OverlayBackend.Win32, out var hwnd)) return Win32Overlay.SetOpacity(window, hwnd, alpha);
        if (!TryGetXid(window, out var xid)) return false;
        lock (Gate)
        {
            if (!EnsureDisplay()) return false;
            // Format-32 property data is an array of C longs, i.e. pointer-sized on LP64.
            var value = (IntPtr)(long)OpacityCardinal(alpha);
            XChangeProperty(_display, xid, XInternAtom(_display, "_NET_WM_WINDOW_OPACITY", false),
                (IntPtr)6 /* XA_CARDINAL */, 32, 0 /* PropModeReplace */, new[] { value }, 1);
            XFlush(_display);
            return true;
        }
    }

    /// <summary>The _NET_WM_WINDOW_OPACITY value for <paramref name="alpha"/>, floored at 1 of 2^32-1:
    /// still invisible, but never exactly 0. KWin sends no frame callbacks to an opacity-0 XWayland
    /// window, so the render thread's vsync'd swap on it waits for XWayland's 1 s fallback timer and
    /// stalls every window's frames and the UI thread 1-2 s (measured, overlay-first-frame-perf).</summary>
    internal static uint OpacityCardinal(double alpha) => Math.Max(1u, (uint)(Math.Clamp(alpha, 0, 1) * uint.MaxValue));

    /// <summary>Stacks <paramref name="window"/> immediately above <paramref name="sibling"/> -
    /// the analogue of <c>SetWindowPos(hwnd, hwndInsertAfter, SWP_NOACTIVATE)</c>, and what holds
    /// the ~15 Chaos overlay classes in their fixed internal order.
    ///
    /// <para>This is a separate concern from being above OTHER applications:
    /// <c>Window.Topmost</c> puts the whole band in the keep-above layer, and this orders the band
    /// within it.</para>
    ///
    /// <para><b>Why a client message and not <c>XConfigureWindow</c>.</b> Not because the direct
    /// call fails - it works on these windows, measured side by side in the same probe. An earlier
    /// version of this shim concluded it did not work, and that conclusion was wrong: the test read
    /// the stacking order immediately after the call, and stacking is applied by the window
    /// manager, which is a separate process. The call was fine; the test was too fast.
    ///
    /// <para>The client message is still the right choice, for reasons that outlive this machine.
    /// Stacking is the WM's to arbitrate, so a raw <c>ConfigureWindow</c> is a request the WM may
    /// apply its own policy to, and it addresses the id we hold - which stops being a child of the
    /// root window as soon as anything reparents it into a decoration frame. KWin does not reparent
    /// an undecorated XWayland window (measured: frame id == window id), but that is one compositor
    /// on one backend, and the overlays have to hold their order on X11 proper and under other
    /// window managers too. <c>_NET_RESTACK_WINDOW</c> asks the WM rather than going around it, so
    /// it holds in every one of those cases, and KWin lists it in <c>_NET_SUPPORTED</c> and honours
    /// it in both directions with either source indication.</para>
    ///
    /// <para><b>It is asynchronous.</b> The message goes to the WM, which is a separate process:
    /// <c>XSync</c> only waits for the SERVER, so a caller that reads the stacking order straight
    /// after this call can legitimately still see the old one.</para></summary>
    internal static bool RestackAbove(TopLevel window, TopLevel sibling)
    {
        if (!TryGetXid(window, out var xid)) return false;
        if (!TryGetXid(sibling, out var siblingXid)) return false;

        lock (Gate)
        {
            if (!EnsureDisplay()) return false;

            // XEvent is a union of 24 longs and XSendEvent reads the whole thing, so the buffer
            // has to be full size even though only the client-message head is filled in. Every
            // offset below was taken from offsetof() against the real headers, not from memory -
            // a wrong one here sends a well-formed message that means something else.
            var e = Marshal.AllocHGlobal(XEventSize);
            try
            {
                for (var i = 0; i < XEventSize; i += IntPtr.Size) Marshal.WriteIntPtr(e, i, IntPtr.Zero);

                Marshal.WriteInt32(e, 0, ClientMessage);
                Marshal.WriteIntPtr(e, 24, _display);
                Marshal.WriteIntPtr(e, 32, xid);                                          // window to restack
                Marshal.WriteIntPtr(e, 40, XInternAtom(_display, "_NET_RESTACK_WINDOW", false));
                Marshal.WriteInt32(e, 48, 32);                                            // format
                Marshal.WriteIntPtr(e, 56, new IntPtr(SourcePager));                      // data.l[0] source
                Marshal.WriteIntPtr(e, 64, siblingXid);                                   // data.l[1] sibling
                Marshal.WriteIntPtr(e, 72, new IntPtr(Above));                            // data.l[2] detail

                // Addressed to the ROOT window, not to ours: the WM holds SubstructureRedirect
                // there, and that is how the request reaches it.
                XSendEvent(_display, XDefaultRootWindow(_display), false, SubstructureMask, e);
                XFlush(_display);
                return true;
            }
            finally { Marshal.FreeHGlobal(e); }
        }
    }

    /// <summary>The window's X11 id, or false when there is not one to have.
    ///
    /// <para>Gating on the descriptor rather than on the OS is what lets the call sites stay
    /// identical across heads: on Windows, under the headless backend used by the render proof,
    /// and before the window has opened, this simply returns false.</para></summary>
    private static bool TryGetXid(TopLevel window, out IntPtr xid) => TryGet(window, OverlayBackend.X11, out xid);

    private static bool TryGet(TopLevel window, OverlayBackend want, out IntPtr handle)
    {
        var h = window?.TryGetPlatformHandle();
        handle = BackendOf(h) == want ? h!.Handle : IntPtr.Zero;
        return handle != IntPtr.Zero;
    }

    /// <summary>Which backend drives a window, from its platform handle alone: "XID" is X11
    /// (this class), "HWND" is Win32 (<see cref="Win32Overlay"/>), anything else - headless,
    /// not yet opened - is neither and every entry point returns false.</summary>
    internal static OverlayBackend BackendOf(IPlatformHandle? handle)
        => handle is null || handle.Handle == IntPtr.Zero ? OverlayBackend.None : handle.HandleDescriptor switch
        {
            "XID" => OverlayBackend.X11,
            "HWND" => OverlayBackend.Win32,
            _ => OverlayBackend.None,
        };

    private static bool EnsureDisplay()
    {
        if (_initialised) return _usable;
        _initialised = true;

        try
        {
            _display = XOpenDisplay(IntPtr.Zero);
            if (_display == IntPtr.Zero)
            {
                Log.Debug("X11Overlay: no X display; click-through and restacking are no-ops");
                return _usable = false;
            }

            XSetErrorHandler(ErrorHandler);

            // Per-connection negotiation. Skipping this is the silent failure: the server drops
            // the shape requests and the overlay stays clickable with nothing logged anywhere.
            if (XFixesQueryExtension(_display, out _, out _) == 0)
            {
                Log.Warning("X11Overlay: the X server has no XFixes; overlays cannot be click-through");
                return _usable = false;
            }

            return _usable = true;
        }
        catch (DllNotFoundException e)
        {
            Log.Debug(e, "X11Overlay: libX11/libXfixes not present; overlay window control is a no-op");
            return _usable = false;
        }
    }

    // Set by OnXError; SetOverrideRedirect clears it before its request and reads it after XSync.
    private static volatile bool _xErrored;

    private static int OnXError(IntPtr display, IntPtr errorEvent)
    {
        _xErrored = true;
        // Xlib's default handler exits the process. An overlay destroyed between reading its
        // handle and the call above is normal churn, not a reason to take the app down.
        Log.Debug("X11Overlay: X error on the overlay display, ignored");
        return 0;
    }
}
