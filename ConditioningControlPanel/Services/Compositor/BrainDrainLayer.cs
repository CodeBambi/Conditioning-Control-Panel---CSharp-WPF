using System.Runtime.InteropServices;
using SkiaSharp;

namespace ConditioningControlPanel.Services.Compositor;

/// <summary>
/// Compositor twin of the brain-drain blur windows. Per monitor, a GDI StretchBlt shrinks the
/// screen into a persistent DIB section (1/downscale size), the small frame is blurred ONCE per
/// capture tick on a small raster surface, and Render just upscales the blurred image over the
/// monitor - the upscale is part of the blur, exactly like the legacy Image Stretch=Fill path.
/// Blur radius parity with the legacy WPF BlurEffect: the legacy path set BlurEffect.Radius on the
/// ALREADY-DOWNSCALED bitmap, so the radius SetIntensity stores is a SOURCE-space radius - the
/// gaussian applied here is simply sigma = radius/3 (WPF's radius-to-sigma ratio), and the
/// xdownscale upscale that follows widens it on screen exactly as it did for the legacy Image.
/// A second /downscale here would be double-counting (it was, and it made every blur invisible).
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
    private double _sourceRadius;               // WPF-BlurEffect-equivalent radius ON THE DOWNSCALED SOURCE
    // Alpha-mix axis (compositor path only). At low intensity the gaussian is a fraction of a
    // source pixel wide - i.e. nothing - so the *strength* has to come from how much of the blurred
    // copy we paint over the real screen. Intensity 1..40 ramps the draw alpha linearly 0.35 -> 1.0
    // (the real screen shows through underneath = a subtle haze); at 40 and above the layer is fully
    // opaque and sigma alone carries the depth (sigma at 40 is already ~5 screen px at downscale 4).
    // That makes perceived strength continuous across 1 -> 100 instead of "nothing, then blur".
    // Pulse always paints at full alpha - a pulse is meant to slam.
    private const int AlphaFullIntensity = 40;
    private const double AlphaFloor = 0.35;
    private byte _drawAlpha = 255;
    // Melt variant ("braindrain_melt"): shares this layer and ALL of OverlayService's hold/ramp
    // state - a melt band and a plain blur band never co-exist by design.
    // Melt = the same blur PLUS a slowly-flowing Perlin displacement warp ("melting glass"),
    // composed into ONE filter chain on the existing small-surface draw in CapturePass. It never
    // touches Render: warping at monitor resolution on a CPU raster surface is not affordable, and
    // the capture cadence (30fps default) is smooth enough for a drift this slow.
    private bool _melt;
    private float _meltTime;                    // seconds since Start, accumulated from Update's delta
    private float _meltAmplitude;               // displacement scale, SOURCE px (see SetIntensity)
    private SKImage? _meltNoiseTile;            // pre-rendered tileable turbulence (see EnsureNoiseTile)
    private float _meltTileCoverage;            // surface px one tile spans
    private int _meltNoiseShort = -1;           // short edge the cached tile was sized for
    private SKPaint? _meltNoisePaint;           // carrier for the per-tick drifted noise shader
    private SKPaint? _meltPaint;                // carrier for the per-tick displacement+blur chain

    // Noise field. Turbulence cells are sized off the SMALL surface's short edge so the look holds
    // across monitor sizes and performance tiers: one cell = 20% of the short edge (inside the
    // 1/8..1/4 target), with the vertical frequency lowered so cells are ~1.5x taller than wide -
    // that elongation is what reads as "dripping" rather than "boiling". 2 octaves; fixed seed so
    // the pattern is reproducible run to run.
    // Skia's RASTER Perlin shader is far too slow to evaluate per frame (2 octaves x 4 channels of
    // scalar lattice noise per pixel measured 24ms/tick at 480x270 and 98ms at 960x540 - i.e. a
    // whole core at the 30fps capture cadence). So the turbulence is rasterised ONCE into a small
    // TILEABLE image and animated per tick by an image shader in Repeat mode; that is a SIMD
    // bilinear fetch instead of noise evaluation and costs ~1.5ms at 480x270. The tile spans 12
    // cells so its repeat period is wider than the frame - no visible tiling - and it is stored at
    // a fixed 256px raster, upscaled bilinearly, which is lossless in practice because the smallest
    // feature (the 2nd octave) is still ~10 tile px across.
    private const float NoiseCellFraction = 0.20f;
    private const float NoiseCellAspect = 0.65f; // freqY = freqX * this  =>  taller cells
    private const int NoiseOctaves = 2;
    private const float NoiseSeed = 7f;
    private const int NoiseTilePx = 256;
    private const float NoiseCellsPerTile = 12f;
    // Drift, expressed at downscale 4 and divided by the actual downscale so the on-SCREEN flow
    // speed is tier-independent: 6 source px/s downward (= 24 screen px/s at x4) plus a 2.5 px
    // sinusoidal sway on a ~11s period. Melting is mostly gravity with a little wander.
    private const float MeltDriftPxPerSec = 6f;
    private const float MeltSwayPx = 2.5f;
    private const float MeltSwayRate = 0.55f;    // rad/s
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

    /// <summary>Start capturing + blurring at the given intensity (legacy 1..200 scale).
    /// <paramref name="melt"/> selects the "braindrain_melt" variant - same blur plus an animated
    /// Perlin displacement warp.</summary>
    public void Start(int intensity, bool melt = false)
    {
        _melt = melt;
        _meltTime = 0f;
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
        _melt = false;
    }

    /// <summary>Normal/ramp/restore path - the legacy source-space radius is intensity*0.4/downscale
    /// (the WPF BlurEffect radius was applied to the downscaled bitmap, so it is already
    /// source-space; CapturePass just turns it into sigma = radius/3).</summary>
    public void SetIntensity(int intensity)
    {
        _sourceRadius = intensity * 0.4 / Math.Max(1, _downscale);
        _drawAlpha = AlphaFor(intensity);
        // Melt amplitude curve (SOURCE px, quoted at downscale 4 then divided by the actual
        // downscale so the on-screen warp is tier-independent):  amp = 2 + intensity * 0.12.
        // A displacement map offsets by +/- amp/2, so on screen at x4 that is +/- 2*amp px:
        //   intensity 30  -> amp 5.6  -> +/-11 screen px  = a gentle shimmer
        //   intensity 60  -> amp 9.2  -> +/-18 screen px  = clearly liquid
        //   intensity 100 -> amp 14.0 -> +/-28 screen px  = heavy dripping distortion
        // Clamped at the legacy 200 ceiling (amp 26) so a runaway value cannot eat the frame.
        _meltAmplitude = (float)((2.0 + Math.Clamp(intensity, 0, 200) * 0.12) * 4.0 / Math.Max(1, _downscale));
    }

    /// <summary>Pulse boost - legacy PulseOverlays sets the raw radius boosted*0.4 with NO
    /// downscale divide (deliberately much heavier than the steady state), at full opacity.</summary>
    public void Pulse(int boostedIntensity)
    {
        _sourceRadius = boostedIntensity * 0.4;
        _drawAlpha = 255;
    }

    /// <summary>Draw alpha for an intensity: 0.35 at 1, linear to fully opaque at
    /// <see cref="AlphaFullIntensity"/> and above. See the field comment for why.</summary>
    private static byte AlphaFor(int intensity)
    {
        int i = Math.Clamp(intensity, 1, AlphaFullIntensity);
        double a = AlphaFloor + (1.0 - AlphaFloor) * (i - 1) / (AlphaFullIntensity - 1.0);
        return (byte)Math.Clamp(Math.Round(a * 255.0), 0, 255);
    }

    public override void OnDeactivated() => ReleaseCaptures();

    public override void Update(TimeSpan delta)
    {
        _sinceCapture += delta;
        _sinceDriftCheck += delta;
        // Wall-clock melt phase: accumulated every tick (not only on capture ticks) so the flow
        // speed is independent of the capture cadence. No DateTime.Now - the engine owns the clock.
        if (_melt) _meltTime += (float)delta.TotalSeconds;
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
        // Alpha mix: white-with-alpha modulates the image draw (RGB is ignored for DrawImage).
        // Set per frame - Render, SetIntensity and Pulse are all on the UI thread, so this can
        // never read a half-written value, and it keeps the ramp path free of paint bookkeeping.
        _drawPaint.Color = SKColors.White.WithAlpha(_drawAlpha);
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

        // Rebuild the blur filter only when the radius actually changed. _sourceRadius is ALREADY
        // source-space (see SetIntensity) - dividing by _downscale again here is what made every
        // steady-state blur invisible (max intensity landed at sigma ~0.83px, 1..6 at literally zero).
        float sigma = (float)(_sourceRadius / 3.0);
        if (Math.Abs(sigma - _lastSigma) > 0.01f)
        {
            _lastSigma = sigma;
            _blurFilter?.Dispose();
            _blurFilter = sigma > 0.05f ? SKImageFilter.CreateBlur(sigma, sigma) : null;
            _blurPaint.ImageFilter = _blurFilter;
        }

        // Melt: compose displacement + blur into ONE chain. Only the cheap wrappers around the
        // cached noise tile (the drifted image shader and the two filters that carry it) are
        // rebuilt per tick; the turbulence raster itself is built once per capture size.
        SKShader? drift = null;
        SKImageFilter? noiseFilter = null, meltFilter = null;
        float gutter = 0f;
        if (_melt && _meltAmplitude > 0.05f)
        {
            var tile = EnsureNoiseTile();
            if (tile != null)
            {
                float tierScale = 4f / Math.Max(1, _downscale);
                // Wrapped into one tile period: the pattern repeats there anyway, and it keeps the
                // float drift from growing without bound over a long session.
                float dx = MeltSwayPx * tierScale * MathF.Sin(_meltTime * MeltSwayRate);
                float dy = (MeltDriftPxPerSec * tierScale * _meltTime) % _meltTileCoverage;
                float s = _meltTileCoverage / NoiseTilePx;
                drift = SKShader.CreateImage(tile, SKShaderTileMode.Repeat, SKShaderTileMode.Repeat,
                                             SKMatrix.CreateScaleTranslation(s, s, dx, dy));
                // Bilinear: a nearest-sampled noise tile would quantise the warp into visible blocks.
                (_meltNoisePaint ??= new SKPaint { FilterQuality = SKFilterQuality.Low }).Shader = drift;
                noiseFilter = SKImageFilter.CreatePaint(_meltNoisePaint);
                if (noiseFilter != null)
                {
                    // input: _blurFilter (may be null at low intensity, where sigma is sub-pixel -
                    // the warp still applies, it just warps the unblurred capture).
                    meltFilter = SKImageFilter.CreateDisplacementMapEffect(
                        SKColorChannel.R, SKColorChannel.G, _meltAmplitude, noiseFilter, _blurFilter);
                    (_meltPaint ??= new SKPaint()).ImageFilter = meltFilter;
                    // Displacement samples outside the source bleed transparent along the frame
                    // edges. Max |offset| is amplitude/2 (the map is centred on 0.5), +2px slack
                    // for the CTM scale below feeding back into the mapped displacement scale.
                    gutter = _meltAmplitude * 0.5f + 2f;
                }
            }
        }

        try
        {
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
                    if (meltFilter != null)
                    {
                        // Scale about the centre so the gutter falls off-frame entirely.
                        canvas.Save();
                        canvas.Translate(c.W * 0.5f, c.H * 0.5f);
                        canvas.Scale((c.W + 2f * gutter) / c.W, (c.H + 2f * gutter) / c.H);
                        canvas.Translate(-c.W * 0.5f, -c.H * 0.5f);
                        canvas.DrawImage(raw, 0, 0, _meltPaint);
                        canvas.Restore();
                    }
                    else
                    {
                        canvas.DrawImage(raw, 0, 0, _blurFilter != null ? _blurPaint : null);
                    }
                    c.Blurred?.Dispose();
                    c.Blurred = c.Surface.Snapshot();
                }
            }
            finally
            {
                ReleaseDC(IntPtr.Zero, screenDc);
            }
        }
        finally
        {
            // Drop the per-tick wrappers the same tick they were made (the carrier paints and the
            // turbulence tile are the only melt state that survives to the next capture).
            if (_meltPaint != null) _meltPaint.ImageFilter = null;
            if (_meltNoisePaint != null) _meltNoisePaint.Shader = null;
            meltFilter?.Dispose();
            noiseFilter?.Dispose();
            drift?.Dispose();
        }
    }

    /// <summary>Rasterises the tileable turbulence field for the melt warp, sized off the first
    /// capture's short edge. Rebuilt only when that size changes (downscale change / monitor swap);
    /// costs ~14ms once, versus ~24ms EVERY tick if the Perlin shader were evaluated live.</summary>
    private SKImage? EnsureNoiseTile()
    {
        if (_captures.Count == 0) return null;
        int shortEdge = Math.Min(_captures[0].W, _captures[0].H);
        if (_meltNoiseTile != null && _meltNoiseShort == shortEdge) return _meltNoiseTile;

        _meltNoiseTile?.Dispose();
        _meltNoiseTile = null;
        _meltNoiseShort = shortEdge;
        // One cell = NoiseCellFraction of the short edge, NoiseCellsPerTile cells to a tile.
        _meltTileCoverage = Math.Max(8f, shortEdge * NoiseCellFraction) * NoiseCellsPerTile;
        float freq = NoiseCellsPerTile / NoiseTilePx; // cycles per TILE px - Skia snaps it to tile
        using var noise = SKShader.CreatePerlinNoiseTurbulence(
            freq, freq * NoiseCellAspect, NoiseOctaves, NoiseSeed, new SKSizeI(NoiseTilePx, NoiseTilePx));
        using var surface = SKSurface.Create(
            new SKImageInfo(NoiseTilePx, NoiseTilePx, SKColorType.Bgra8888, SKAlphaType.Premul));
        if (surface == null) return null;
        using var paint = new SKPaint { Shader = noise };
        surface.Canvas.Clear(SKColors.Transparent);
        surface.Canvas.DrawRect(0, 0, NoiseTilePx, NoiseTilePx, paint);
        _meltNoiseTile = surface.Snapshot();
        return _meltNoiseTile;
    }

    private void ReleaseMelt()
    {
        if (_meltPaint != null) _meltPaint.ImageFilter = null;
        if (_meltNoisePaint != null) _meltNoisePaint.Shader = null;
        _meltPaint?.Dispose();
        _meltPaint = null;
        _meltNoisePaint?.Dispose();
        _meltNoisePaint = null;
        _meltNoiseTile?.Dispose();
        _meltNoiseTile = null;
        _meltNoiseShort = -1;
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
        ReleaseMelt();
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
