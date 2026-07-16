using System.Runtime.InteropServices;
using SkiaSharp;

namespace ConditioningControlPanel.Services.Compositor;

/// <summary>
/// Compositor twin of the brain-drain blur windows. Per monitor, a GDI StretchBlt shrinks the
/// screen into a persistent DIB section (1/downscale size), the small frame is blurred ONCE per
/// capture tick on a small raster surface, and Render just upscales the blurred image over the
/// monitor - the upscale is part of the blur, exactly like the legacy Image Stretch=Fill path.
/// Blur radius parity with the legacy WPF BlurEffect: an on-screen radius R renders here as a
/// source-space gaussian of sigma R/(3*downscale) applied before the xdownscale upscale.
/// Renders on the capture-EXCLUDED surface (self-capture guard); plain SRCCOPY StretchBlt also
/// skips layered windows entirely, so other overlays never feed back into the blur (legacy same).
/// OverlayService owns all intensity/ramp/pulse math and pushes values; this layer only
/// captures and draws. All methods UI thread (engine tick + OverlayService RunOnUISync).
/// </summary>
public sealed class BrainDrainLayer : BaseLayer
{
    private sealed class ScreenCapture
    {
        public System.Drawing.Rectangle Bounds; // monitor rect, virtual-desktop device px
        public int W, H;                        // downscaled capture size
        public IntPtr MemDc, HBitmap, Bits, OldObj;
        public SKSurface? Surface;              // small blur target
        public SKImage? Blurred;                // latest blurred frame (drawn by Render)
    }

    private readonly List<ScreenCapture> _captures = new();
    private readonly SKPaint _blurPaint = new();
    private readonly SKPaint _drawPaint = new() { FilterQuality = SKFilterQuality.Low }; // bilinear = WPF Image default
    private SKImageFilter? _blurFilter;
    private float _lastSigma = -1f;

    private int _downscale = 4;
    private double _screenRadius;               // WPF-BlurEffect-equivalent on-screen radius
    private TimeSpan _captureInterval = TimeSpan.FromMilliseconds(33);
    private TimeSpan _sinceCapture;

    // Topology drift is rare; checking (and allocating target rects) every capture tick at
    // 30-60Hz is pure waste. ~1s detection latency on a monitor change is imperceptible.
    private static readonly TimeSpan DriftCheckInterval = TimeSpan.FromSeconds(1);
    private TimeSpan _sinceDriftCheck;

    public BrainDrainLayer(CompositorEngine engine) : base(engine) { }

    public override int ZIndex => CompositorLayers.BrainDrain;
    public override bool ExcludeFromCapture => true;
    public override bool WorldSpacePx => true;

    /// <summary>Start capturing + blurring at the given intensity (legacy 1..200 scale).</summary>
    public void Start(int intensity)
    {
        var settings = App.Settings?.Current;
        var tier = PerformanceProfile.CurrentTier;
        _downscale = PerformanceProfile.BrainDrainDownscale(tier);
        int fps = Math.Min(settings?.BrainDrainHighRefresh == true ? 60 : 30,
                           PerformanceProfile.BrainDrainFps(tier));
        _captureInterval = TimeSpan.FromMilliseconds(1000.0 / Math.Max(1, fps));
        SetIntensity(intensity);
        RebuildCaptures(GetTargetScreens());
        _sinceCapture = _captureInterval; // capture on the very first tick
        _sinceDriftCheck = TimeSpan.Zero; // captures just rebuilt - no immediate drift check
        SetActive(true);
    }

    public void Stop()
    {
        SetActive(false);
        ReleaseCaptures();
    }

    /// <summary>Normal/ramp/restore path - legacy on-screen radius is intensity*0.4/downscale
    /// (the WPF BlurEffect radius was tuned against the downscaled source).</summary>
    public void SetIntensity(int intensity) => _screenRadius = intensity * 0.4 / Math.Max(1, _downscale);

    /// <summary>Pulse boost - legacy PulseOverlays sets the raw radius boosted*0.4 with NO
    /// downscale divide (deliberately much heavier than the steady state).</summary>
    public void Pulse(int boostedIntensity) => _screenRadius = boostedIntensity * 0.4;

    public override void OnDeactivated() => ReleaseCaptures();

    public override void Update(TimeSpan delta)
    {
        _sinceCapture += delta;
        _sinceDriftCheck += delta;
        if (_sinceCapture < _captureInterval) return;
        _sinceCapture = TimeSpan.Zero;

        try
        {
            if (_sinceDriftCheck >= DriftCheckInterval)
            {
                _sinceDriftCheck = TimeSpan.Zero;
                EnsureCapturesMatchScreens();
            }
            CapturePass();
        }
        catch (Exception ex)
        {
            App.Logger?.Debug("BrainDrainLayer capture failed: {Error}", ex.Message);
        }
    }

    public override void Render(SKCanvas canvas, SKRectI boundsPx, double dpiScale, TimeSpan elapsed)
    {
        // World-space: boundsPx is this monitor's rect; draw only its own capture.
        int cx = (boundsPx.Left + boundsPx.Right) / 2, cy = (boundsPx.Top + boundsPx.Bottom) / 2;
        foreach (var c in _captures)
        {
            if (c.Blurred == null || !c.Bounds.Contains(cx, cy)) continue;
            canvas.DrawImage(c.Blurred,
                new SKRect(c.Bounds.X, c.Bounds.Y, c.Bounds.Right, c.Bounds.Bottom), _drawPaint);
            return;
        }
    }

    private static System.Drawing.Rectangle[] GetTargetScreens()
    {
        try
        {
            var screens = App.GetAllScreensCached(); // Screen.PrimaryScreen re-enumerates per call
            if (App.Settings?.Current?.DualMonitorEnabled == true)
                return screens.Select(s => s.Bounds).ToArray();
            foreach (var s in screens)
                if (s.Primary) return new[] { s.Bounds };
            return Array.Empty<System.Drawing.Rectangle>();
        }
        catch { return Array.Empty<System.Drawing.Rectangle>(); }
    }

    /// <summary>Throttled (~1s) drift check so display topology / dual-monitor changes
    /// mid-run re-target the captures without a restart.</summary>
    private void EnsureCapturesMatchScreens()
    {
        var screens = GetTargetScreens();
        if (screens.Length == 0) return; // display transition - keep last frames
        if (screens.Length == _captures.Count)
        {
            bool same = true;
            for (int i = 0; i < screens.Length; i++)
                if (_captures[i].Bounds != screens[i]) { same = false; break; }
            if (same) return;
        }
        RebuildCaptures(screens);
    }

    private void RebuildCaptures(System.Drawing.Rectangle[] screens)
    {
        ReleaseCaptures();
        foreach (var bounds in screens)
        {
            var c = CreateCapture(bounds);
            if (c != null) _captures.Add(c);
        }
    }

    private ScreenCapture? CreateCapture(System.Drawing.Rectangle bounds)
    {
        int divisor = Math.Max(1, _downscale);
        int dw = Math.Max(2, (bounds.Width / divisor) & ~1);
        int dh = Math.Max(2, (bounds.Height / divisor) & ~1);

        var memDc = CreateCompatibleDC(IntPtr.Zero);
        if (memDc == IntPtr.Zero) return null;

        var bmi = new BITMAPINFOHEADER
        {
            biSize = Marshal.SizeOf<BITMAPINFOHEADER>(),
            biWidth = dw,
            biHeight = -dh, // top-down so the pixel pointer reads row 0 first
            biPlanes = 1,
            biBitCount = 32,
            biCompression = 0, // BI_RGB
        };
        var hBitmap = CreateDIBSection(memDc, ref bmi, 0 /*DIB_RGB_COLORS*/, out var bits, IntPtr.Zero, 0);
        if (hBitmap == IntPtr.Zero || bits == IntPtr.Zero)
        {
            DeleteDC(memDc);
            return null;
        }

        return new ScreenCapture
        {
            Bounds = bounds,
            W = dw,
            H = dh,
            MemDc = memDc,
            HBitmap = hBitmap,
            Bits = bits,
            OldObj = SelectObject(memDc, hBitmap),
            Surface = SKSurface.Create(new SKImageInfo(dw, dh, SKColorType.Bgra8888, SKAlphaType.Premul)),
        };
    }

    private void CapturePass()
    {
        if (_captures.Count == 0) return;

        // Rebuild the blur filter only when the radius actually changed.
        float sigma = (float)(_screenRadius / (3.0 * Math.Max(1, _downscale)));
        if (Math.Abs(sigma - _lastSigma) > 0.01f)
        {
            _lastSigma = sigma;
            _blurFilter?.Dispose();
            _blurFilter = sigma > 0.05f ? SKImageFilter.CreateBlur(sigma, sigma) : null;
            _blurPaint.ImageFilter = _blurFilter;
        }

        var screenDc = GetDC(IntPtr.Zero);
        if (screenDc == IntPtr.Zero) return;
        try
        {
            foreach (var c in _captures)
            {
                if (c.Surface == null) continue;
                SetStretchBltMode(c.MemDc, HALFTONE);
                if (!StretchBlt(c.MemDc, 0, 0, c.W, c.H,
                                screenDc, c.Bounds.X, c.Bounds.Y, c.Bounds.Width, c.Bounds.Height, SRCCOPY))
                    continue;
                GdiFlush();

                var info = new SKImageInfo(c.W, c.H, SKColorType.Bgra8888, SKAlphaType.Opaque);
                using var raw = SKImage.FromPixels(info, c.Bits, c.W * 4); // zero-copy wrap; consumed synchronously below
                if (raw == null) continue;

                var canvas = c.Surface.Canvas;
                canvas.Clear(SKColors.Transparent);
                canvas.DrawImage(raw, 0, 0, _blurFilter != null ? _blurPaint : null);
                c.Blurred?.Dispose();
                c.Blurred = c.Surface.Snapshot();
            }
        }
        finally
        {
            ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    private void ReleaseCaptures()
    {
        foreach (var c in _captures)
        {
            try
            {
                c.Blurred?.Dispose();
                c.Surface?.Dispose();
                if (c.MemDc != IntPtr.Zero)
                {
                    if (c.OldObj != IntPtr.Zero) SelectObject(c.MemDc, c.OldObj);
                    DeleteDC(c.MemDc);
                }
                if (c.HBitmap != IntPtr.Zero) DeleteObject(c.HBitmap);
            }
            catch { /* GDI cleanup best-effort */ }
        }
        _captures.Clear();
    }

    #region Win32

    private const int SRCCOPY = 0x00CC0020;
    private const int HALFTONE = 4;

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public int biSize;
        public int biWidth;
        public int biHeight;
        public short biPlanes;
        public short biBitCount;
        public int biCompression;
        public int biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public int biClrUsed;
        public int biClrImportant;
    }

    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr hdc);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr hdc);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool GdiFlush();
    [DllImport("gdi32.dll")] private static extern int SetStretchBltMode(IntPtr hdc, int mode);
    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFOHEADER pbmi, uint usage,
        out IntPtr ppvBits, IntPtr hSection, uint offset);
    [DllImport("gdi32.dll")]
    private static extern bool StretchBlt(IntPtr hdcDest, int xDest, int yDest, int wDest, int hDest,
        IntPtr hdcSrc, int xSrc, int ySrc, int wSrc, int hSrc, int rop);

    #endregion
}
