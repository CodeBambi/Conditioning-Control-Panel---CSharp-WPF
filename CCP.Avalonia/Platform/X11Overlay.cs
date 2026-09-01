using System;
using System.Runtime.InteropServices;
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
/// above the keep-above layer — needs an override-redirect or KDE window-type flip applied
/// before the window maps, so it belongs in its own change rather than bolted on here.</para>
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

    private const string LibX11 = "libX11.so.6";
    private const string LibXfixes = "libXfixes.so.3";

    private delegate int XErrorHandler(IntPtr display, IntPtr errorEvent);

    [DllImport(LibX11)] private static extern IntPtr XOpenDisplay(IntPtr name);
    [DllImport(LibX11)] private static extern int XFlush(IntPtr display);
    [DllImport(LibX11)] private static extern IntPtr XSetErrorHandler(XErrorHandler handler);
    [DllImport(LibX11)] private static extern IntPtr XDefaultRootWindow(IntPtr display);
    [DllImport(LibX11)] private static extern IntPtr XInternAtom(IntPtr display, string name, bool onlyIfExists);
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

    /// <summary>True when this process can actually drive the calls below - an X11 display is
    /// open and the server offers XFixes. False on Windows, on headless CI and under a native
    /// Wayland backend, where every method here is a no-op.</summary>
    internal static bool IsAvailable
    {
        get { lock (Gate) { return EnsureDisplay(); } }
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
    private static bool TryGetXid(TopLevel window, out IntPtr xid)
    {
        xid = IntPtr.Zero;
        var handle = window?.TryGetPlatformHandle();
        if (handle is null || !string.Equals(handle.HandleDescriptor, "XID", StringComparison.Ordinal))
            return false;
        if (handle.Handle == IntPtr.Zero) return false;

        xid = handle.Handle;
        return true;
    }

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

    private static int OnXError(IntPtr display, IntPtr errorEvent)
    {
        // Xlib's default handler exits the process. An overlay destroyed between reading its
        // handle and the call above is normal churn, not a reason to take the app down.
        Log.Debug("X11Overlay: X error on the overlay display, ignored");
        return 0;
    }
}
