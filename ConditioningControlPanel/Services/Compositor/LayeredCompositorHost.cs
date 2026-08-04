using System.Runtime.InteropServices;
using System.Threading;
using SkiaSharp;

namespace ConditioningControlPanel.Services.Compositor;

/// <summary>
/// #550 proper-fix host: a raw Win32 layered, click-through, topmost overlay window whose content
/// is rasterized and presented OFF the UI thread. The engine records the monitor's active layers
/// into an immutable <see cref="SKPicture"/> on the UI thread (cheap - draw-command capture only)
/// and hands it here via <see cref="PresentPicture"/>; a dedicated present thread rasterizes it into
/// a DIB-backed <see cref="SKSurface"/> and blits it with UpdateLayeredWindow(ULW_ALPHA). This keeps
/// per-pixel alpha and click-through (exactly like the WPF AllowsTransparency host) but moves the
/// fullscreen software raster + layered composite off the dispatcher - the dispatcher starvation that
/// made a continuously-active fullscreen spiral lag the whole app on some machines.
///
/// Working purely in device pixels (no WPF DIP layout) is also why mixed-DPI cover is exact here.
///
/// The window is created on and owned by the UI thread (its message pump is the app's existing one;
/// a transparent click-through tool window receives almost no messages). UpdateLayeredWindow is
/// issued from the present thread against that hwnd - it targets the window's layered content and
/// does not require the owning thread.
///
/// Resource lifetime is handled by Skia's native ref-counting, not by cross-thread coordination: an
/// SKPicture takes its own native ref on every SKImage/mask filter drawn into it, so a layer freeing
/// an image on the UI thread only drops the managed ref - the in-flight picture keeps the native
/// object alive until this host disposes the picture after presenting it. SKPicture is immutable and
/// safe to rasterize on this thread while the layers keep mutating on the UI thread.
/// </summary>
internal sealed class LayeredCompositorHost : ICompositorHost
{
    public string ScreenDeviceName { get; }
    public bool IsExcludedSurface { get; }
    public double DpiScale { get; }

    // Written by the UI thread (topology changes), read by the present thread (PresentOne).
    // Rectangle is a 4-int struct, so an unsynchronized handoff can tear mid-update and hand
    // the present thread a mismatched DIB/UpdateLayeredWindow frame. Dedicated lock: never
    // held across SetWindowPos/UpdateLayeredWindow, and independent of _slotLock ordering.
    private readonly object _boundsLock = new();
    private System.Drawing.Rectangle _screenBoundsPx;
    public System.Drawing.Rectangle ScreenBoundsPx
    {
        get { lock (_boundsLock) return _screenBoundsPx; }
        private set { lock (_boundsLock) _screenBoundsPx = value; }
    }

    public bool IsVisible { get; private set; }

    public nint WindowHandle => _hwnd;

    private readonly IntPtr _hwnd;
    private readonly Thread _presentThread;
    private readonly AutoResetEvent _signal = new(false);
    private readonly object _slotLock = new();
    private SKPicture? _pending;      // latest un-presented picture (superseded ones disposed here)
    private volatile bool _stop;

    // GDI/Skia present surface (owned by the present thread; deleted on Close after the join).
    private IntPtr _memDc = IntPtr.Zero;
    private IntPtr _dib = IntPtr.Zero;
    private IntPtr _oldBmp = IntPtr.Zero;
    private SKSurface? _surface;
    private int _surfW, _surfH;

    public LayeredCompositorHost(System.Windows.Forms.Screen screen, bool excludeFromCapture)
    {
        ScreenDeviceName = screen.DeviceName;
        ScreenBoundsPx = screen.Bounds;
        IsExcludedSurface = excludeFromCapture;
        DpiScale = CompositorHostWindow.GetDpiScaleForScreen(screen);

        _hwnd = CreateHostWindow(ScreenBoundsPx);
        if (_hwnd != IntPtr.Zero && excludeFromCapture)
            SetWindowDisplayAffinity(_hwnd, WDA_EXCLUDEFROMCAPTURE);

        _presentThread = new Thread(PresentLoop)
        {
            IsBackground = true,
            Name = $"Compositor-Present-{(excludeFromCapture ? "excl-" : "")}{ScreenDeviceName}"
        };
        _presentThread.Start();
    }

    /// <summary>Hand a freshly recorded picture (device-px, monitor-local) to the present thread.
    /// Ownership transfers here: the host disposes it after presenting (or when superseded). UI thread.</summary>
    public void PresentPicture(SKPicture picture)
    {
        if (_stop) { picture.Dispose(); return; }
        lock (_slotLock)
        {
            // A picture still sitting un-presented is now stale - drop it (latest wins). Safe: the
            // present thread moves a picture OUT of the slot before rendering, so this only ever
            // disposes one the present thread has not started.
            _pending?.Dispose();
            _pending = picture;
        }
        _signal.Set();
    }

    // Show/Hide/UpdateScreenBounds run on the UI thread and touch a WS_EX_LAYERED window that the
    // present thread is concurrently blitting into with UpdateLayeredWindow. Layered-window state
    // changes are the app's historical render-thread wedge, and none of these Win32 calls can be
    // given a timeout — so they are breadcrumbed instead: a hang report whose "last UI mark" is one
    // of these names the wedge outright (see VideoDiag.UiScope).

    public void Show()
    {
        if (_hwnd == IntPtr.Zero) return;
        using var _ = VideoDiag.UiScope($"LayeredCompositorHost.Show({ScreenDeviceName})");
        var b = ScreenBoundsPx;
        SetWindowPos(_hwnd, HWND_TOPMOST, b.X, b.Y, b.Width, b.Height, SWP_NOACTIVATE);
        ShowWindow(_hwnd, SW_SHOWNA);
        IsVisible = true;
    }

    public void Hide()
    {
        if (_hwnd == IntPtr.Zero) return;
        using var _ = VideoDiag.UiScope($"LayeredCompositorHost.Hide({ScreenDeviceName})");
        ShowWindow(_hwnd, SW_HIDE);
        IsVisible = false;
    }

    public void UpdateScreenBounds(System.Windows.Forms.Screen screen)
    {
        using var _ = VideoDiag.UiScope($"LayeredCompositorHost.UpdateScreenBounds({ScreenDeviceName})");
        var b = screen.Bounds;
        ScreenBoundsPx = b;
        if (_hwnd != IntPtr.Zero && IsVisible)
            SetWindowPos(_hwnd, HWND_TOPMOST, b.X, b.Y, b.Width, b.Height, SWP_NOACTIVATE);
        // The present thread re-sizes its DIB to the new bounds on the next present.
    }

    public void Close()
    {
        using var _ = VideoDiag.UiScope($"LayeredCompositorHost.Close({ScreenDeviceName})");
        _stop = true;
        _signal.Set();
        bool exited = false;
        try { exited = _presentThread.Join(2000); } catch { }
        lock (_slotLock) { _pending?.Dispose(); _pending = null; }

        if (!exited)
        {
            // Present thread is still running (e.g. blocked in UpdateLayeredWindow). Freeing its DC /
            // DIB / window now would be a use-after-free; leak them instead (teardown-only path).
            App.Logger?.Warning("LayeredCompositorHost: present thread did not exit; leaking surface for {Screen}", ScreenDeviceName);
            return;
        }

        // Present thread has quiesced: safe to free its surface + GDI objects and the window here.
        DestroySurface();
        if (_hwnd != IntPtr.Zero) { try { DestroyWindow(_hwnd); } catch { } }
        _signal.Dispose();
    }

    // ---- present thread ----

    private void PresentLoop()
    {
        while (!_stop)
        {
            _signal.WaitOne();
            if (_stop) break;

            SKPicture? pic;
            lock (_slotLock)
            {
                pic = _pending;
                _pending = null;   // taken - PresentPicture must not dispose it now
            }
            if (pic == null) continue;

            try { PresentOne(pic); }
            catch (Exception ex) { App.Logger?.Error(ex, "LayeredCompositorHost: present failed"); }
            finally { pic.Dispose(); }
        }
    }

    private void PresentOne(SKPicture pic)
    {
        var bounds = ScreenBoundsPx;
        int w = Math.Max(1, bounds.Width), h = Math.Max(1, bounds.Height);
        if (!EnsureSurface(w, h) || _surface == null) return;

        var canvas = _surface.Canvas;
        canvas.Clear(SKColors.Transparent);
        canvas.DrawPicture(pic);
        _surface.Flush();

        var ptDst = new POINT { x = bounds.X, y = bounds.Y };
        var size = new SIZE { cx = w, cy = h };
        var ptSrc = new POINT { x = 0, y = 0 };
        var blend = new BLENDFUNCTION
        {
            BlendOp = AC_SRC_OVER,
            BlendFlags = 0,
            SourceConstantAlpha = 255,
            AlphaFormat = AC_SRC_ALPHA
        };
        UpdateLayeredWindow(_hwnd, IntPtr.Zero, ref ptDst, ref size, _memDc, ref ptSrc, 0, ref blend, ULW_ALPHA);
    }

    /// <summary>(Re)allocate the DIB-backed surface when the monitor size changes. Present thread only.</summary>
    private bool EnsureSurface(int w, int h)
    {
        if (_surface != null && _surfW == w && _surfH == h) return true;
        DestroySurface();

        _memDc = CreateCompatibleDC(IntPtr.Zero);
        if (_memDc == IntPtr.Zero) return false;

        var bmi = new BITMAPINFO
        {
            biSize = Marshal.SizeOf<BITMAPINFOHEADER>(),
            biWidth = w,
            biHeight = -h,           // top-down: row 0 is the top, matching Skia's default
            biPlanes = 1,
            biBitCount = 32,
            biCompression = BI_RGB
        };
        _dib = CreateDIBSection(_memDc, ref bmi, DIB_RGB_COLORS, out var bits, IntPtr.Zero, 0);
        if (_dib == IntPtr.Zero || bits == IntPtr.Zero) { DestroySurface(); return false; }
        _oldBmp = SelectObject(_memDc, _dib);

        var info = new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Premul);
        _surface = SKSurface.Create(info, bits, w * 4);
        if (_surface == null) { DestroySurface(); return false; }
        _surfW = w; _surfH = h;
        return true;
    }

    private void DestroySurface()
    {
        _surface?.Dispose();
        _surface = null;
        if (_memDc != IntPtr.Zero)
        {
            if (_oldBmp != IntPtr.Zero) { SelectObject(_memDc, _oldBmp); _oldBmp = IntPtr.Zero; }
            if (_dib != IntPtr.Zero) { DeleteObject(_dib); _dib = IntPtr.Zero; }
            DeleteDC(_memDc);
            _memDc = IntPtr.Zero;
        }
        else if (_dib != IntPtr.Zero) { DeleteObject(_dib); _dib = IntPtr.Zero; }
        _surfW = _surfH = 0;
    }

    // ---- window creation ----

    private const string ClassName = "CCPLayeredCompositorHost";
    private static readonly WndProcDelegate s_wndProc = StaticWndProc; // kept alive for the class
    private static bool s_classRegistered;
    private static readonly object s_classLock = new();

    private static IntPtr CreateHostWindow(System.Drawing.Rectangle bounds)
    {
        try
        {
            EnsureClassRegistered();
            IntPtr hInstance = GetModuleHandle(null);
            int exStyle = WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW
                        | WS_EX_NOACTIVATE | WS_EX_TOPMOST;
            var hwnd = CreateWindowEx(exStyle, ClassName, string.Empty, WS_POPUP,
                bounds.X, bounds.Y, bounds.Width, bounds.Height,
                IntPtr.Zero, IntPtr.Zero, hInstance, IntPtr.Zero);
            if (hwnd == IntPtr.Zero)
                App.Logger?.Warning("LayeredCompositorHost: CreateWindowEx failed ({Err})", Marshal.GetLastWin32Error());
            return hwnd;
        }
        catch (Exception ex)
        {
            App.Logger?.Error(ex, "LayeredCompositorHost: window creation threw");
            return IntPtr.Zero;
        }
    }

    private static void EnsureClassRegistered()
    {
        lock (s_classLock)
        {
            if (s_classRegistered) return;
            var wc = new WNDCLASSEX
            {
                cbSize = Marshal.SizeOf<WNDCLASSEX>(),
                style = 0,
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(s_wndProc),
                hInstance = GetModuleHandle(null),
                lpszClassName = ClassName
            };
            if (RegisterClassEx(ref wc) == 0)
            {
                int err = Marshal.GetLastWin32Error();
                if (err != ERROR_CLASS_ALREADY_EXISTS)
                    App.Logger?.Warning("LayeredCompositorHost: RegisterClassEx failed ({Err})", err);
            }
            s_classRegistered = true;
        }
    }

    private static IntPtr StaticWndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
        => DefWindowProc(hWnd, msg, wParam, lParam);

    // ---- Win32 ----

    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_LAYERED = 0x00080000;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_TOPMOST = 0x00000008;
    private const uint WS_POPUP = 0x80000000;
    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private const uint SWP_NOACTIVATE = 0x0010;
    private const int SW_HIDE = 0;
    private const int SW_SHOWNA = 8;
    private const uint WDA_EXCLUDEFROMCAPTURE = 0x0011;
    private const int ERROR_CLASS_ALREADY_EXISTS = 1410;
    private const uint BI_RGB = 0;
    private const uint DIB_RGB_COLORS = 0;
    private const byte AC_SRC_OVER = 0;
    private const byte AC_SRC_ALPHA = 1;
    private const uint ULW_ALPHA = 0x00000002;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int x, y; }
    [StructLayout(LayoutKind.Sequential)]
    private struct SIZE { public int cx, cy; }
    [StructLayout(LayoutKind.Sequential)]
    private struct BLENDFUNCTION
    {
        public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public int biSize;
        public int biWidth, biHeight;
        public short biPlanes, biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter, biYPelsPerMeter;
        public uint biClrUsed, biClrImportant;
    }
    // Header-only BITMAPINFO (no palette needed for BI_RGB 32bpp).
    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFO
    {
        public int biSize;
        public int biWidth, biHeight;
        public short biPlanes, biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter, biYPelsPerMeter;
        public uint biClrUsed, biClrImportant;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSEX
    {
        public int cbSize;
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra, cbWndExtra;
        public IntPtr hInstance, hIcon, hCursor, hbrBackground;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpszMenuName;
        [MarshalAs(UnmanagedType.LPWStr)] public string lpszClassName;
        public IntPtr hIconSm;
    }

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern ushort RegisterClassEx(ref WNDCLASSEX wc);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowEx(int exStyle, string className, string windowName,
        uint style, int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr hInstance, IntPtr param);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hwnd, int cmd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    private static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint affinity);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref POINT pptDst, ref SIZE psize,
        IntPtr hdcSrc, ref POINT pptSrc, int crKey, ref BLENDFUNCTION pblend, uint dwFlags);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? name);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFO bmi, uint usage,
        out IntPtr bits, IntPtr section, uint offset);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr obj);
}
