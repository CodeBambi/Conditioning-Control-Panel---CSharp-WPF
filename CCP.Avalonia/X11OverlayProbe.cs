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

            var exit = 1;
            overlay.Opened += (_, _) =>
            {
                // Opened fires before the compositor has necessarily mapped the window, and an
                // unmapped window has no meaningful input shape to read back.
                DispatcherTimer.RunOnce(() =>
                {
                    try { exit = Score(overlay); }
                    catch (Exception e) { Console.Error.WriteLine("probe threw: " + e); exit = 1; }
                    finally { lifetime.Shutdown(); }
                }, TimeSpan.FromMilliseconds(1200));
            };

            overlay.Show();
            lifetime.Start(Array.Empty<string>());
            return exit;
        }

        private static int Score(Window overlay)
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

                var pass = controlOk && clickThrough && restored && stillDrawn;
                Console.WriteLine(pass
                    ? "\nPASS - the input region empties and refills on a real Avalonia window"
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
        /// <para>KWin reparents managed windows into a frame, so the id we hold is NOT a child of
        /// root - its frame is. Walking up to the child-of-root first is what makes this compare
        /// the right two things.</para></summary>
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
