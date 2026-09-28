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
            if (OperatingSystem.IsWindows()) return RunWin32(lifetime);

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
                };

                var handle = w.TryGetPlatformHandle();
                var xid = handle?.HandleDescriptor == "XID" ? handle.Handle : IntPtr.Zero;
                Console.WriteLine($"screen {b} scaling {screen.Scaling}: handle descriptor = {handle?.HandleDescriptor ?? "(none)"}, XID before Show = 0x{xid.ToInt64():x}");
                Check(xid != IntPtr.Zero, "X11 backend: handle descriptor is XID before Show()");
                if (xid == IntPtr.Zero) continue;

                var pre = Read(display, xid);
                Check(pre is { } p0 && p0.MapState != IsViewable, $"not yet mapped before Show() (map_state={pre?.MapState.ToString() ?? "unreadable"})");
                Check(X11Overlay.SetOverrideRedirect(w, b), "SetOverrideRedirect returned true");
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

        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
        [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleBitmap(IntPtr dc, int w, int h);
        [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
        [DllImport("gdi32.dll")] private static extern bool BitBlt(IntPtr dst, int x, int y, int w, int h, IntPtr src, int sx, int sy, uint rop);
        [DllImport("gdi32.dll")] private static extern uint GetPixel(IntPtr dc, int x, int y);
        [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
        [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);

        /// <summary>One composed screen pixel (0x00BBGGRR). CAPTUREBLT, or layered windows are left out.</summary>
        private static uint ScreenPixel(int x, int y)
        {
            IntPtr screen = GetDC(IntPtr.Zero), mem = CreateCompatibleDC(screen), bmp = CreateCompatibleBitmap(screen, 1, 1);
            var old = SelectObject(mem, bmp);
            BitBlt(mem, 0, 0, 1, 1, screen, x, y, 0x00CC0020 /* SRCCOPY */ | 0x40000000 /* CAPTUREBLT */);
            var c = GetPixel(mem, 0, 0);
            SelectObject(mem, old); DeleteObject(bmp); DeleteDC(mem); ReleaseDC(IntPtr.Zero, screen);
            return c;
        }
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
        private record struct Rect(int Left, int Top, int Right, int Bottom);

        /// <summary>The Windows twin: same recipe through the same X11Overlay entry points, read back
        /// from user32 (ex-style bits, visibility, rect vs the screen). No desktop session (no screens,
        /// no HWND) prints SKIP and exits 0 - that is the runner, not the shim.</summary>
        private static int RunWin32(ClassicDesktopStyleApplicationLifetime lifetime)
        {
            var screens = new Window().Screens.All;
            if (screens.Count == 0) { Console.WriteLine("SKIP: no desktop session (0 screens)"); return 0; }

            var fails = 0;
            void Check(bool ok, string what) { Console.WriteLine($"  [{(ok ? "ok" : "FAIL")}] {what}"); if (!ok) fails++; }
            var overlays = new List<(Window Window, PixelRect Bounds, IntPtr Hwnd)>();
            foreach (var screen in screens)
            {
                var w = new Window
                {
                    WindowDecorations = WindowDecorations.None,
                    TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent },
                    Background = Brushes.Magenta,   // solid, so the pixel read-back proves the layered window draws
                    ShowInTaskbar = false,
                    ShowActivated = false,
                    CanResize = false,
                };
                var handle = w.TryGetPlatformHandle();
                if (X11Overlay.BackendOf(handle) != OverlayBackend.Win32) { Console.WriteLine($"SKIP: handle descriptor {handle?.HandleDescriptor ?? "(none)"}, not HWND"); return 0; }
                Check(X11Overlay.SetClickThrough(w, true), "SetClickThrough(true) returned true");
                Check(X11Overlay.SetOpacity(w, 1), "SetOpacity(1) returned true");
                Check(X11Overlay.SetOverrideRedirect(w, screen.Bounds), "SetOverrideRedirect returned true");
                w.Show();
                w.CanResize = true;   // makes Avalonia rebuild GWL_EXSTYLE; the style callback must keep our bits
                overlays.Add((w, screen.Bounds, handle!.Handle));
            }

            DispatcherTimer.RunOnce(() =>
            {
                try
                {
                    const uint want = Win32Overlay.OverlayBits | Win32Overlay.WsExTransparent | Win32Overlay.WsExNoActivate | Win32Overlay.WsExTopmost;
                    foreach (var (w, b, hwnd) in overlays)
                    {
                        var ex = Win32Overlay.GetWindowLong(hwnd, -20);
                        GetWindowRect(hwnd, out var r);
                        Console.WriteLine($"overlay 0x{hwnd.ToInt64():x} on {b}: exstyle=0x{ex:x8} visible={IsWindowVisible(hwnd)} rect={r}");
                        Check((ex & want) == want, "WS_EX_LAYERED|TRANSPARENT|NOACTIVATE|TOOLWINDOW|TOPMOST after an Avalonia style rebuild");
                        Check(IsWindowVisible(hwnd), "IsWindowVisible");
                        var px = ScreenPixel(b.X + b.Width / 2, b.Y + b.Height / 2);
                        Check(px == 0x00FF00FF, $"screen pixel at the centre is magenta (0x{px:x6}): the layered window draws at SetOpacity(1)");
                        Check(r.Left == b.X && r.Top == b.Y && r.Right - r.Left == b.Width && r.Bottom - r.Top == b.Height, $"rect matches screen {b}");
                        X11Overlay.SetClickThrough(w, false);
                        ex = Win32Overlay.GetWindowLong(hwnd, -20);
                        Check((ex & (Win32Overlay.WsExTransparent | Win32Overlay.WsExNoActivate)) == 0 && (ex & Win32Overlay.OverlayBits) == Win32Overlay.OverlayBits,
                            "SetClickThrough(false) clears TRANSPARENT|NOACTIVATE, keeps LAYERED|TOOLWINDOW");
                    }
                }
                catch (Exception e) { Console.Error.WriteLine("overlay-check threw: " + e); fails++; }
                finally { lifetime.Shutdown(); }
            }, TimeSpan.FromMilliseconds(1200));

            lifetime.Start(Array.Empty<string>());
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
