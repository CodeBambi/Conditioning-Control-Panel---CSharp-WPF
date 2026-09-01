using System;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;

namespace ConditioningControlPanel.Avalonia
{
    /// <summary>
    /// Proves <see cref="X11Overlay.SetClickThrough"/> works on a REAL Avalonia window, by reading
    /// back from the X server what the window's input shape actually became.
    ///
    /// <para><b>Why a probe and not a unit test.</b> Every way that shim can fail - a missed
    /// extension negotiation, an unflushed request, a wrong constant, bad struct marshalling -
    /// produces a call that returns successfully and changes nothing. Only the server's own answer
    /// separates "worked" from "silently did nothing", and no mock can supply that.</para>
    ///
    /// <para><b>Why it reads the input region instead of clicking.</b> The obvious test is to put
    /// the pointer over the overlay and ask which window owns it - which is what the C probes
    /// <c>ct2</c>/<c>ct3</c> did, passing 3/3 against KWin on a real session, so the MECHANISM is
    /// already measured. That test cannot run here: <c>XWarpPointer</c> is silently ignored under
    /// XWayland (Wayland owns pointer position), and inside a virtual compositor an internal
    /// XWayland window sits above every client, so no client ever owns the pointer. Verified by
    /// running the known-good <c>ct3</c> binary in this same sandbox, where it fails identically -
    /// so that is the environment's limit, not the shim's. What is left to prove here is this
    /// codebase's MARSHALLING of the mechanism, and <c>XFixesCreateRegionFromWindow</c> +
    /// <c>XFixesFetchRegion</c> answers exactly that, straight from the server.</para>
    ///
    /// <para><b>Run it in a NESTED compositor, never a live session</b> - see
    /// <c>scripts/x11-overlay-probe.sh</c>. During the research a probe called a Qt-internal D-Bus
    /// interface with hand-marshalled arguments and aborted KWin, killing the user's mail client,
    /// file sync and a browser helper with it.</para>
    /// </summary>
    internal static class X11OverlayProbe
    {
        private const string LibX11 = "libX11.so.6";
        private const string LibXext = "libXext.so.6";
        private const int ShapeBounding = 0;   // X11/extensions/shapeconst.h
        private const int ShapeInput = 2;      // X11/extensions/shapeconst.h

        [StructLayout(LayoutKind.Sequential)]
        private struct XRectangle { public short X, Y; public ushort Width, Height; }

        [DllImport(LibX11)] private static extern IntPtr XOpenDisplay(IntPtr name);
        [DllImport(LibX11)] private static extern int XCloseDisplay(IntPtr display);
        [DllImport(LibX11)] private static extern int XSync(IntPtr display, bool discard);
        [DllImport(LibX11)] private static extern int XFree(IntPtr data);
        [DllImport(LibX11)] private static extern int XGetGeometry(IntPtr display, IntPtr drawable,
            out IntPtr root, out int x, out int y, out uint width, out uint height, out uint borderWidth, out uint depth);

        // NOT XFixesCreateRegionFromWindow: that call only defines WindowRegionBounding(0) and
        // WindowRegionClip(1), so asking it for the INPUT shape fails with BadValue. The Shape
        // extension is where input-shape read-back lives. Verified by getting the BadValue.
        [DllImport(LibX11)] private static extern IntPtr XDefaultRootWindow(IntPtr display);
        [DllImport(LibX11)] private static extern int XQueryTree(IntPtr display, IntPtr window,
            out IntPtr root, out IntPtr parent, out IntPtr children, out uint count);
        [StructLayout(LayoutKind.Sequential)]
        private struct XWindowChanges
        {
            public int X, Y, Width, Height, BorderWidth;
            public IntPtr Sibling;
            public int StackMode;
        }

        [DllImport(LibX11)] private static extern int XConfigureWindow(IntPtr display, IntPtr window, uint mask, ref XWindowChanges changes);
        [DllImport(LibXext)] private static extern IntPtr XShapeGetRectangles(IntPtr display, IntPtr window,
            int kind, out int count, out int ordering);

        public static int Run()
        {
            var lifetime = new ClassicDesktopStyleApplicationLifetime
            {
                ShutdownMode = ShutdownMode.OnExplicitShutdown
            };
            Program.BuildAvaloniaApp().SetupWithLifetime(lifetime);

            var overlay = new Window
            {
                Title = "probe-overlay",
                Width = 400,
                Height = 400,
                Background = Brushes.DarkRed,
                Topmost = true,
                ShowInTaskbar = false
            };

            // A second window, so restacking has something to be ordered against.
            var other = new Window
            {
                Title = "probe-other",
                Width = 300,
                Height = 300,
                Background = Brushes.DarkGreen,
                Topmost = true,
                ShowInTaskbar = false
            };

            var exit = 1;
            overlay.Opened += (_, _) =>
            {
                // Opened fires before the compositor has necessarily mapped the window, and an
                // unmapped window has no meaningful input shape to read back.
                DispatcherTimer.RunOnce(() =>
                {
                    try { exit = Score(overlay, other); }
                    catch (Exception e) { Console.Error.WriteLine("probe threw: " + e); exit = 1; }
                    finally { lifetime.Shutdown(); }
                }, TimeSpan.FromMilliseconds(1200));
            };

            other.Show();
            overlay.Show();
            lifetime.Start(Array.Empty<string>());
            return exit;
        }

        private static int Score(Window overlay, Window other)
        {
            if (!X11Overlay.IsAvailable)
            {
                Console.Error.WriteLine("FAIL: X11Overlay reports unavailable - no display, or the server has no XFixes");
                return 1;
            }

            var xid = Xid(overlay);
            Console.WriteLine($"overlay XID = 0x{xid.ToInt64():x}");
            if (xid == IntPtr.Zero) { Console.Error.WriteLine("FAIL: no XID - not an X11 backend"); return 1; }

            var display = XOpenDisplay(IntPtr.Zero);
            if (display == IntPtr.Zero) { Console.Error.WriteLine("FAIL: no display"); return 1; }

            try
            {
                var root = XDefaultRootWindow(display);
                XGetGeometry(display, xid, out _, out _, out _, out var w, out var h, out _, out _);
                Console.WriteLine($"overlay geometry = {w}x{h}");

                // [1] control. A window with no input shape set reports one rectangle covering
                // itself. If that is not what comes back, nothing below means anything.
                var before = InputRects(display, xid);
                var controlOk = before.Length == 1 && before[0].Width == w && before[0].Height == h;
                Console.WriteLine($"[1] no input shape set        -> {Describe(before)}   {(controlOk ? "(control OK)" : "(CONTROL FAILED)")}");
                if (!controlOk)
                {
                    Console.Error.WriteLine("FAIL: control step - the server did not report the default full-window input shape");
                    return 1;
                }

                // [2] the actual claim: an empty input region, which is what makes every event
                // pass through to whatever is underneath.
                if (!X11Overlay.SetClickThrough(overlay, true))
                {
                    Console.Error.WriteLine("FAIL: SetClickThrough(true) returned false");
                    return 1;
                }
                XSync(display, false);
                var during = InputRects(display, xid);
                var clickThrough = during.Length == 0;
                Console.WriteLine($"[2] SetClickThrough(true)     -> {Describe(during)}   {(clickThrough ? "<-- CLICK-THROUGH (empty input region)" : "<-- FAILED, window still takes input")}");

                // [3] reversibility. The Chaos overlays toggle this at runtime - draft and result
                // surfaces go interactive again - so a one-way door is a regression even if [2]
                // passed.
                if (!X11Overlay.SetClickThrough(overlay, false))
                {
                    Console.Error.WriteLine("FAIL: SetClickThrough(false) returned false");
                    return 1;
                }
                XSync(display, false);
                var after = InputRects(display, xid);
                var restored = after.Length == 1 && after[0].Width == w && after[0].Height == h;
                Console.WriteLine($"[3] SetClickThrough(false)    -> {Describe(after)}   {(restored ? "<-- interactive again" : "<-- FAILED, toggle is one-way")}");

                // The visible shape must be untouched throughout. Click-through that also stopped
                // the window DRAWING would pass every check above and be useless - the overlays
                // have to stay visible while the mouse goes through them.
                var bounding = Rects(display, xid, ShapeBounding);
                var stillDrawn = bounding.Length == 1 && bounding[0].Width == w && bounding[0].Height == h;
                Console.WriteLine($"[4] bounding shape unchanged  -> {Describe(bounding)}   {(stillDrawn ? "<-- still drawn" : "<-- FAILED, the visible shape moved too")}");

                // [5] RestackAbove. Deliberately structured as TWO FLIPS with no seeding step:
                // whatever order the windows happen to start in, the call has to reverse it and
                // then reverse it back. A no-op cannot pass that, which a "put A above B and
                // check A is above B" test would if they already started that way.
                //
                // The settle sleeps are load-bearing. The message goes to KWin, a separate
                // process; XSync waits for the SERVER only, so reading the order immediately
                // after the call reads the OLD order and the check fails on working code.
                var otherXid = Xid(other);
                var restackOk = false;
                if (otherXid == IntPtr.Zero)
                {
                    Console.WriteLine("[5] RestackAbove              -> skipped, second window has no XID");
                }
                else
                {
                    var (a0, b0) = (IndexOf(display, root, xid), IndexOf(display, root, otherXid));
                    Console.WriteLine($"[5a] initial order            -> ours={a0} other={b0}   {(a0 > b0 ? "ours above" : "other above")}");

                    // Flip 1: whichever is underneath goes on top.
                    var (lower, upper) = a0 > b0 ? (other, overlay) : (overlay, other);
                    X11Overlay.RestackAbove(lower, upper);
                    System.Threading.Thread.Sleep(800);
                    var (a1, b1) = (IndexOf(display, root, xid), IndexOf(display, root, otherXid));
                    var flipped = (a1 > b1) != (a0 > b0);
                    Console.WriteLine($"[5b] restacked the lower one  -> ours={a1} other={b1}   {(flipped ? "<-- order REVERSED" : "<-- FAILED, nothing moved")}");

                    // Flip 2: and back, because the overlays reorder repeatedly at runtime.
                    X11Overlay.RestackAbove(upper, lower);
                    System.Threading.Thread.Sleep(800);
                    var (a2, b2) = (IndexOf(display, root, xid), IndexOf(display, root, otherXid));
                    var flippedBack = (a2 > b2) == (a0 > b0);
                    Console.WriteLine($"[5c] restacked the other one  -> ours={a2} other={b2}   {(flippedBack ? "<-- order RESTORED" : "<-- FAILED, one-way")}");

                    restackOk = a0 >= 0 && b0 >= 0 && flipped && flippedBack;
                }

                // [6] Informational, not a gate: does the DIRECT call work on these windows too?
                // The shim uses the EWMH message because that is the WM-sanctioned route, but the
                // comment justifying that choice should rest on a measurement rather than on
                // repeating what the research assumed.
                if (otherXid != IntPtr.Zero)
                {
                    var (c0, d0) = (IndexOf(display, root, xid), IndexOf(display, root, otherXid));
                    var (lo, hi) = c0 > d0 ? (otherXid, xid) : (xid, otherXid);
                    var changes = new XWindowChanges { Sibling = hi, StackMode = 0 /* Above */ };
                    XConfigureWindow(display, lo, (1u << 5) | (1u << 6) /* CWSibling|CWStackMode */, ref changes);
                    XSync(display, false);
                    System.Threading.Thread.Sleep(800);
                    var (c1, d1) = (IndexOf(display, root, xid), IndexOf(display, root, otherXid));
                    var direct = (c1 > d1) != (c0 > d0);
                    Console.WriteLine($"[6] XConfigureWindow direct   -> {c0},{d0} then {c1},{d1}   {(direct ? "(also works here)" : "(no effect - EWMH is required)")}");
                }

                var pass = controlOk && clickThrough && restored && stillDrawn && restackOk;
                Console.WriteLine(pass
                    ? "\nPASS - click-through toggles and the overlay order flips, on real Avalonia windows"
                    : "\nFAIL - the round trip did not complete");
                return pass ? 0 : 1;
            }
            finally { XCloseDisplay(display); }
        }

        /// <summary>The window's CURRENT input shape as the server holds it - not as we believe we
        /// set it. This is the whole point: it is a read-back, so a request that was dropped,
        /// never flushed, or aimed at the wrong shape kind shows up as the wrong answer here.</summary>
        private static XRectangle[] InputRects(IntPtr display, IntPtr window) => Rects(display, window, ShapeInput);

        private static XRectangle[] Rects(IntPtr display, IntPtr window, int kind)
        {
            var ptr = XShapeGetRectangles(display, window, kind, out var count, out _);
            if (ptr == IntPtr.Zero || count <= 0) return Array.Empty<XRectangle>();
            try
            {
                var rects = new XRectangle[count];
                for (var i = 0; i < count; i++)
                    rects[i] = Marshal.PtrToStructure<XRectangle>(ptr + i * Marshal.SizeOf<XRectangle>());
                return rects;
            }
            finally { XFree(ptr); }
        }

        /// <summary>Position of a window in the root's stacking order, bottom first, or -1.
        ///
        /// <para>Walks up to the child-of-root first. KWin does not reparent an undecorated
        /// XWayland window - measured, the frame and the window are the same id - but it does
        /// reparent a decorated one, and this has to compare the right two things either way.</para></summary>
        private static int IndexOf(IntPtr display, IntPtr root, IntPtr window)
        {
            var frame = window;
            for (var hop = 0; hop < 8; hop++)
            {
                if (XQueryTree(display, frame, out _, out var parent, out var kids, out _) == 0) return -1;
                if (kids != IntPtr.Zero) XFree(kids);
                if (parent == root) break;
                if (parent == IntPtr.Zero) return -1;
                frame = parent;
            }

            if (XQueryTree(display, root, out _, out _, out var children, out var count) == 0) return -1;
            if (children == IntPtr.Zero) return -1;
            try
            {
                for (var i = 0; i < count; i++)
                    if (Marshal.ReadIntPtr(children, i * IntPtr.Size) == frame) return i;
                return -1;
            }
            finally { XFree(children); }
        }

        private static string Describe(XRectangle[] rects) =>
            rects.Length == 0
                ? "input region: EMPTY (0 rectangles)"
                : $"input region: {rects.Length} rect(s), first {rects[0].Width}x{rects[0].Height} at {rects[0].X},{rects[0].Y}";

        private static IntPtr Xid(TopLevel window)
        {
            var handle = window.TryGetPlatformHandle();
            return handle is not null && handle.HandleDescriptor == "XID" ? handle.Handle : IntPtr.Zero;
        }
    }
}
