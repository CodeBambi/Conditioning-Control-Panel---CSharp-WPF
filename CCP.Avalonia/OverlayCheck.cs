using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;

namespace ConditioningControlPanel.Avalonia
{
    /// <summary>
    /// `--overlay-check`: opens one transparent click-through override-redirect overlay per screen
    /// the way every desktop overlay will, then asks the X SERVER what each window became - never
    /// what we believe we set. Exits non-zero on any mismatch. Same read-back idea as
    /// <see cref="X11OverlayProbe"/>; that one covers click-through toggling and restacking, this
    /// one covers the pre-map recipe (override-redirect + empty input shape + ARGB + geometry).
    /// The windows are fully transparent and pass all input, so it is safe on a live session.
    /// </summary>
    internal static class OverlayCheck
    {
        private const string LibX11 = "libX11.so.6";
        private const string LibXext = "libXext.so.6";
        private const int ShapeInput = 2;              // X11/extensions/shapeconst.h
        private const int IsViewable = 2;              // X11/X.h
        private const int XWindowAttributesSize = 136; // sizeof(XWindowAttributes), from the compiler
        // offsetof(XWindowAttributes, ...) on x86_64, from the compiler against the real headers
        private const int OffWidth = 8, OffHeight = 12, OffDepth = 20, OffMapState = 92, OffOverrideRedirect = 120;

        [DllImport(LibX11)] private static extern IntPtr XOpenDisplay(IntPtr name);
        [DllImport(LibX11)] private static extern int XCloseDisplay(IntPtr display);
        [DllImport(LibX11)] private static extern int XFree(IntPtr data);
        [DllImport(LibX11)] private static extern IntPtr XDefaultRootWindow(IntPtr display);
        [DllImport(LibX11)] private static extern int XGetWindowAttributes(IntPtr display, IntPtr window, IntPtr attributes);
        [DllImport(LibX11)] private static extern bool XTranslateCoordinates(IntPtr display, IntPtr src, IntPtr dest,
            int srcX, int srcY, out int destX, out int destY, out IntPtr child);
        [DllImport(LibXext)] private static extern IntPtr XShapeGetRectangles(IntPtr display, IntPtr window,
            int kind, out int count, out int ordering);

        private record struct Attrs(int Width, int Height, int Depth, int MapState, bool OverrideRedirect);

        public static int Run()
        {
            Console.WriteLine($"XDG_SESSION_TYPE={Environment.GetEnvironmentVariable("XDG_SESSION_TYPE")} " +
                              $"WAYLAND_DISPLAY={Environment.GetEnvironmentVariable("WAYLAND_DISPLAY")} " +
                              $"DISPLAY={Environment.GetEnvironmentVariable("DISPLAY")}");

            var lifetime = new ClassicDesktopStyleApplicationLifetime { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            Program.BuildAvaloniaApp().SetupWithLifetime(lifetime);

            var display = XOpenDisplay(IntPtr.Zero);
            if (display == IntPtr.Zero) { Console.Error.WriteLine("FAIL: no X display"); return 1; }

            var fails = 0;
            void Check(bool ok, string what) { Console.WriteLine($"  [{(ok ? "ok" : "FAIL")}] {what}"); if (!ok) fails++; }

            var overlays = new List<(Window Window, Screen Screen, IntPtr Xid)>();
            var screens = new Window().Screens.All;
            Console.WriteLine($"screens = {screens.Count}");
            foreach (var screen in screens)
            {
                var b = screen.Bounds;
                var w = new Window
                {
                    WindowDecorations = WindowDecorations.None,
                    TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent },
                    Background = Brushes.Transparent,
                    ShowInTaskbar = false,
                    ShowActivated = false,
                    CanResize = false,
                    WindowStartupLocation = WindowStartupLocation.Manual,
                    Position = b.Position,
                    Width = b.Width / screen.Scaling,
                    Height = b.Height / screen.Scaling,
                };

                var handle = w.TryGetPlatformHandle();
                var xid = handle?.HandleDescriptor == "XID" ? handle.Handle : IntPtr.Zero;
                Console.WriteLine($"screen {b} scaling {screen.Scaling}: handle descriptor = {handle?.HandleDescriptor ?? "(none)"}, XID before Show = 0x{xid.ToInt64():x}");
                Check(xid != IntPtr.Zero, "X11 backend: handle descriptor is XID before Show()");
                if (xid == IntPtr.Zero) continue;

                var pre = Read(display, xid);
                Check(pre is { } p0 && p0.MapState != IsViewable, $"not yet mapped before Show() (map_state={pre?.MapState.ToString() ?? "unreadable"})");
                Check(X11Overlay.SetOverrideRedirect(w), "SetOverrideRedirect returned true");
                Check(X11Overlay.SetClickThrough(w, true), "SetClickThrough(true) returned true");
                w.Show();
                overlays.Add((w, screen, xid));
            }

            // Give the server a beat to map; override-redirect maps do not wait on the WM.
            DispatcherTimer.RunOnce(() =>
            {
                try
                {
                    var root = XDefaultRootWindow(display);
                    foreach (var (_, screen, xid) in overlays)
                    {
                        var b = screen.Bounds;
                        var a = Read(display, xid) ?? default; Check(Read(display, xid) is not null, "window attributes readable");
                        XTranslateCoordinates(display, xid, root, 0, 0, out var x, out var y, out _);
                        var input = XShapeGetRectangles(display, xid, ShapeInput, out var rects, out _);
                        if (input != IntPtr.Zero) XFree(input);

                        Console.WriteLine($"overlay 0x{xid.ToInt64():x} on {b}: map_state={a.MapState} override_redirect={a.OverrideRedirect} " +
                                          $"depth={a.Depth} input rects={rects} geometry={a.Width}x{a.Height}+{x}+{y}");
                        Check(a.MapState == IsViewable, "mapped (IsViewable)");
                        Check(a.OverrideRedirect, "override_redirect = True");
                        Check(a.Depth == 32, "ARGB visual (depth 32)");
                        Check(rects == 0, "input shape empty (click-through)");
                        Check(x == b.X && y == b.Y && a.Width == b.Width && a.Height == b.Height, $"geometry matches screen {b}");
                    }
                    if (overlays.Count == 0) Check(false, "at least one overlay opened");
                }
                catch (Exception e) { Console.Error.WriteLine("overlay-check threw: " + e); fails++; }
                finally { lifetime.Shutdown(); }
            }, TimeSpan.FromMilliseconds(1200));

            lifetime.Start(Array.Empty<string>());
            XCloseDisplay(display);
            Console.WriteLine(fails == 0 ? "PASS" : $"FAIL ({fails} mismatch(es))");
            return fails == 0 ? 0 : 1;
        }

        private static Attrs? Read(IntPtr display, IntPtr xid)
        {
            var p = Marshal.AllocHGlobal(XWindowAttributesSize);
            try
            {
                if (XGetWindowAttributes(display, xid, p) == 0) return null;
                return new Attrs(Marshal.ReadInt32(p, OffWidth), Marshal.ReadInt32(p, OffHeight), Marshal.ReadInt32(p, OffDepth),
                    Marshal.ReadInt32(p, OffMapState), Marshal.ReadInt32(p, OffOverrideRedirect) != 0);
            }
            finally { Marshal.FreeHGlobal(p); }
        }
    }
}
