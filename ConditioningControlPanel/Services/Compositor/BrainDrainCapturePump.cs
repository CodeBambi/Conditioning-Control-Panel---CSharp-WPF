using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using SkiaSharp;

namespace ConditioningControlPanel.Services.Compositor;

/// <summary>
/// The single-frame hand-off between a producer thread and a consumer thread: the producer parks
/// the newest frame here (disposing any older one the consumer never got round to taking), and the
/// consumer takes ownership of whatever is waiting. LATEST WINS - a consumer that falls behind
/// skips frames rather than queueing them, which is the only sane policy for live screen content.
///
/// The invariants this exists to make testable (they are the whole #777 fix):
///   * The consumer NEVER waits on the producer. <see cref="TryTake"/> is a bounded
///     <see cref="Monitor.TryEnter"/> and returns null on a miss, at which point the caller simply
///     redraws the frame it already owns.
///   * Ownership transfers exactly once. A frame the consumer has taken is never disposed by the
///     producer - <see cref="Publish"/> and <see cref="Clear"/> only ever touch a frame still
///     sitting in the slot, and they null it under the same gate before disposing.
///   * The critical section is a two-field pointer swap. Nothing allocating, native, or
///     blocking is ever done while the gate is held.
/// </summary>
internal sealed class LatestFrameSlot<T> where T : class, IDisposable
{
    /// <summary>Internal purely so the contention test can hold the gate and prove that a blocked
    /// slot makes the consumer skip a frame rather than wait. Production code never touches it.</summary>
    internal readonly object Gate = new();
    private T? _pending;

    /// <summary>Producer. Parks <paramref name="frame"/> as the newest; any un-taken predecessor is
    /// disposed OUTSIDE the gate (it is unreachable by then, so nothing can race the dispose).</summary>
    public void Publish(T frame)
    {
        T? stale;
        lock (Gate)
        {
            stale = _pending;
            _pending = frame;
        }
        stale?.Dispose();
    }

    /// <summary>Consumer. Returns the newest un-taken frame and transfers ownership, or null if
    /// there is nothing new OR the gate was busy for <paramref name="timeoutMs"/>. Never blocks
    /// longer than that.</summary>
    public T? TryTake(int timeoutMs)
    {
        if (!Monitor.TryEnter(Gate, timeoutMs)) return null;
        try
        {
            var frame = _pending;
            _pending = null;   // taken: the producer must not dispose it now
            return frame;
        }
        finally { Monitor.Exit(Gate); }
    }

    /// <summary>Producer teardown. Drops (and disposes) an un-taken frame; a frame the consumer
    /// already took is untouched, because it is no longer in the slot.</summary>
    public void Clear()
    {
        T? pending;
        lock (Gate)
        {
            pending = _pending;
            _pending = null;
        }
        pending?.Dispose();
    }
}

/// <summary>
/// The desktop grab + blur that feeds <see cref="BrainDrainLayer"/>, on a thread of its own (#777).
///
/// WHY THIS EXISTS: the capture used to run in the layer's Update(), i.e. ON THE UI THREAD, ~30
/// times a second: <c>GetDC(NULL)</c> then a full-screen <c>StretchBlt</c> off the LIVE DESKTOP,
/// while the compositor's present thread was issuing <c>UpdateLayeredWindow</c> against windows on
/// that same desktop. A blt against the desktop DC cannot be time-bounded - it serialises behind
/// DWM, the GPU, and every other window's redraw - so a single stalled blt takes the dispatcher
/// with it and the app is frozen with the audio still playing (#777: "both screens froze, audio
/// still running, had to kill it from Task Manager"). Moving the call here means the worst a stall
/// can now do is freeze THIS thread: the UI keeps running and the overlay simply holds its last
/// frame until the blt returns.
///
/// THREADING CONTRACT
///   * Everything native (screen DC, per-monitor memory DC + DIB section) and everything Skia that
///     is written to (the small blur surface, the melt noise tile, the filter chain) is created,
///     used and destroyed ON THIS THREAD. The UI thread never touches a GDI handle here, which is
///     also what makes the GDI accounting in the [RES] diagnostics honest.
///   * Output is handed over one immutable <see cref="SKImage"/> at a time through a per-monitor
///     slot: this thread parks the newest frame in the slot (disposing any older un-taken one), the
///     UI thread TAKES it under a short bounded <see cref="Monitor.TryEnter"/> and from then on owns
///     it. On a lock miss (which effectively cannot happen - the critical section is a pointer swap)
///     the UI thread just redraws the frame it already owns. NEITHER SIDE EVER WAITS ON THE OTHER.
///   * Parameters flow the other way as plain volatile writes (sigma, melt amplitude, target
///     screens); this thread reads them at the top of each tick. Nothing is ever pushed with a
///     lock the UI thread could contend on.
///
/// TEARDOWN: <see cref="Shutdown"/> is fire-and-forget on purpose - the UI thread must NEVER join
/// this thread, because joining a thread that is wedged in a desktop blt reproduces exactly the
/// hang the class exists to prevent. The thread frees its own resources as its last act. If it is
/// wedged forever the handles leak, which is the same trade LayeredCompositorHost.Close already
/// makes (leak beats use-after-free, and beats a frozen app).
///
/// SELF-CAPTURE: unchanged from the UI-thread version and load-bearing. The blt is a plain SRCCOPY
/// with NO <c>CAPTUREBLT</c>, so the desktop DC excludes layered windows outright - the brain-drain
/// host (and every other compositor overlay) cannot feed back into the blur. The host window is
/// additionally WDA_EXCLUDEFROMCAPTURE'd by the engine because the layer reports
/// <c>ExcludeFromCapture</c>. Running on another thread changes neither: a GDI desktop DC is a
/// process-wide object and the process is manifest-declared PerMonitorV2, so this thread sees the
/// same virtual-desktop pixel geometry the UI thread did.
/// </summary>
internal sealed class BrainDrainCapturePump
{
    /// <summary>How long the UI thread will wait for the hand-off lock before giving up and
    /// redrawing the frame it already has. The critical section is a two-field pointer swap, so
    /// this budget exists only so that "never block the UI thread" is enforced, not hoped for.</summary>
    private const int SwapTimeoutMs = 2;

    /// <summary>A capture tick slower than this is the #777 shape happening (just harmlessly now).
    /// Logged to the video-diag trace - the one place a future hang report can prove it.</summary>
    private const int StallWarnMs = 400;
    private static readonly TimeSpan StallLogCooldown = TimeSpan.FromSeconds(5);

    /// <summary>One monitor's capture: its GDI staging surface (this thread) plus the hand-off
    /// slot (shared). Bounds/W/H are written before the slot array is published and never mutated,
    /// so the UI thread can read them without a lock.</summary>
    private sealed class Slot
    {
        public readonly LatestFrameSlot<SKImage> Frame = new();   // the pump -> UI hand-off
        public System.Drawing.Rectangle Bounds;   // monitor rect, virtual-desktop device px
        public int W, H;                          // downscaled capture size
        public IntPtr MemDc, HBitmap, Bits, OldObj;
        public SKSurface? Surface;                // small blur target (pump thread only)
    }

    private readonly int _downscale;
    private readonly TimeSpan _interval;
    private readonly bool _melt;

    private readonly Thread _thread;
    private readonly AutoResetEvent _wake = new(false);
    private volatile bool _stop;

    // UI -> pump (volatile: read once at the top of each tick).
    private volatile System.Drawing.Rectangle[] _requestedScreens;
    private volatile float _sigma;
    private volatile float _meltAmplitude;

    // pump -> UI.
    private volatile Slot[] _slots = Array.Empty<Slot>();
    private int _framesPublished;

    /// <summary>Monotonic count of frames this pump has published, across all monitors. The layer's
    /// dirty gate reads it to tell "a new frame exists" from "the engine ticked again" (#853): the
    /// capture runs at 30fps (60 on high-refresh) while the engine ticks at the display's refresh
    /// rate, so without it the excluded surface re-rastered a fullscreen upscale of an IDENTICAL
    /// frame 2-4x more often than the pump could produce one. Any thread.</summary>
    public int FramesPublished => Volatile.Read(ref _framesPublished);

    // Pump-thread-only state (the blur/melt chain, lifted verbatim from the old CapturePass).
    private readonly SKPaint _blurPaint = new();
    private SKImageFilter? _blurFilter;
    private float _lastSigma = -1f;
    private SKImage? _meltNoiseTile;
    private float _meltTileCoverage;
    private int _meltNoiseShort = -1;
    private SKPaint? _meltNoisePaint;
    private SKPaint? _meltPaint;
    private int _errorsLogged;
    private DateTime _lastStallLogUtc = DateTime.MinValue;
    private DateTime _nextSlotRetryUtc = DateTime.MinValue;
    private static readonly TimeSpan SlotRetryBackoff = TimeSpan.FromSeconds(1);

    // Noise field constants - see BrainDrainLayer's field comments for the reasoning; they live
    // here because this is where the turbulence is rasterised.
    private const float NoiseCellFraction = 0.20f;
    private const float NoiseCellAspect = 0.65f;
    private const int NoiseOctaves = 2;
    private const float NoiseSeed = 7f;
    private const int NoiseTilePx = 256;
    private const float NoiseCellsPerTile = 12f;
    private const float MeltDriftPxPerSec = 6f;
    private const float MeltSwayPx = 2.5f;
    private const float MeltSwayRate = 0.55f;   // rad/s

    public BrainDrainCapturePump(int downscale, TimeSpan interval, bool melt,
                                 System.Drawing.Rectangle[] screens, float sigma, float meltAmplitude)
    {
        _downscale = Math.Max(1, downscale);
        _interval = interval > TimeSpan.Zero ? interval : TimeSpan.FromMilliseconds(33);
        _melt = melt;
        _requestedScreens = screens ?? Array.Empty<System.Drawing.Rectangle>();
        _sigma = sigma;
        _meltAmplitude = meltAmplitude;

        _thread = new Thread(Loop)
        {
            IsBackground = true,           // a wedged blt must never hold up process exit
            Name = "BrainDrain-Capture",
            Priority = ThreadPriority.BelowNormal,
        };
        _thread.Start();
    }

    /// <summary>Push the blur strength (and melt warp amplitude). UI thread; never blocks.</summary>
    public void SetBlur(float sigma, float meltAmplitude)
    {
        _sigma = sigma;
        _meltAmplitude = meltAmplitude;
    }

    /// <summary>Push the monitors to capture. The pump rebuilds its staging bitmaps on its own
    /// thread at the next tick. UI thread; never blocks.</summary>
    public void SetScreens(System.Drawing.Rectangle[] screens)
    {
        _requestedScreens = screens ?? Array.Empty<System.Drawing.Rectangle>();
        // try/catch: the pump thread disposes the event on its way out, and a Set() that races
        // a shutdown is a no-op, not an error the caller should ever see.
        try { _wake.Set(); } catch { }
    }

    /// <summary>
    /// UI thread. Reports whether the monitor containing (<paramref name="cx"/>,<paramref name="cy"/>)
    /// is covered, and hands over the newest completed frame for it IF one is waiting - ownership
    /// transfers to the caller, which must dispose it once it has a newer one. <paramref name="fresh"/>
    /// is null when no new frame has landed since the last call (or on a lock miss), and the caller
    /// is expected to redraw the frame it already holds. Never blocks for more than
    /// <see cref="SwapTimeoutMs"/>.
    /// </summary>
    public bool TryTakeFrame(int cx, int cy, out SKImage? fresh, out System.Drawing.Rectangle bounds)
    {
        fresh = null;
        bounds = default;
        var slots = _slots;
        foreach (var slot in slots)
        {
            if (!slot.Bounds.Contains(cx, cy)) continue;
            bounds = slot.Bounds;
            fresh = slot.Frame.TryTake(SwapTimeoutMs);
            return true;
        }
        return false;
    }

    /// <summary>Stop capturing. Returns immediately - the pump thread frees its own GDI/Skia
    /// resources on the way out. NEVER joined from the UI thread (see the class remarks).</summary>
    public void Shutdown()
    {
        _stop = true;
        try { _wake.Set(); } catch { }
    }

    // ------------------------------------------------------------------ pump thread from here on

    private void Loop()
    {
        var phase = Stopwatch.StartNew();   // melt drift is wall-clock, owned by this thread
        var tick = new Stopwatch();
        try
        {
            while (!_stop)
            {
                tick.Restart();
                try
                {
                    SyncSlots();
                    if (!_stop) CaptureOnce((float)phase.Elapsed.TotalSeconds);
                }
                catch (Exception ex)
                {
                    if (++_errorsLogged <= 5)
                        App.Logger?.Debug("BrainDrain capture pump failed: {Error}", ex.Message);
                }

                var remaining = _interval - tick.Elapsed;
                _wake.WaitOne(remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero);
            }
        }
        catch (Exception ex)
        {
            App.Logger?.Error(ex, "BrainDrain capture pump died");
        }
        finally
        {
            ReleaseAll();
            try { _wake.Dispose(); } catch { }
        }
    }

    /// <summary>Rebuild the staging bitmaps when the UI thread has re-targeted the capture
    /// (dual-monitor toggle, display topology drift). Pump thread.</summary>
    private void SyncSlots()
    {
        var want = _requestedScreens;
        var have = _slots;
        if (want.Length == have.Length)
        {
            bool same = true;
            for (int i = 0; i < want.Length; i++)
                if (have[i].Bounds != want[i]) { same = false; break; }
            if (same) return;
        }

        // Under GDI exhaustion CreateSlot returns null and the counts stay mismatched, so without
        // a floor this would re-attempt DC/DIB creation at the capture cadence. Back off to ~1s,
        // which is what the old UI-thread drift check happened to give us for free.
        if (DateTime.UtcNow < _nextSlotRetryUtc) return;

        ReleaseSlots(have);
        _slots = Array.Empty<Slot>();
        var built = new List<Slot>(want.Length);
        foreach (var bounds in want)
        {
            var slot = CreateSlot(bounds);
            if (slot != null) built.Add(slot);
        }
        _slots = built.ToArray();   // volatile publish: Bounds/W/H are already final
        // Only a FAILED build arms the backoff, so a real topology change still re-targets on the
        // very next tick.
        _nextSlotRetryUtc = built.Count == want.Length ? DateTime.MinValue : DateTime.UtcNow + SlotRetryBackoff;
    }

    private Slot? CreateSlot(System.Drawing.Rectangle bounds)
    {
        int dw = Math.Max(2, (bounds.Width / _downscale) & ~1);
        int dh = Math.Max(2, (bounds.Height / _downscale) & ~1);

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
            if (hBitmap != IntPtr.Zero) DeleteObject(hBitmap);
            DeleteDC(memDc);
            return null;
        }

        var surface = SKSurface.Create(new SKImageInfo(dw, dh, SKColorType.Bgra8888, SKAlphaType.Premul));
        if (surface == null)
        {
            DeleteObject(hBitmap);
            DeleteDC(memDc);
            return null;
        }

        return new Slot
        {
            Bounds = bounds,
            W = dw,
            H = dh,
            MemDc = memDc,
            HBitmap = hBitmap,
            Bits = bits,
            OldObj = SelectObject(memDc, hBitmap),
            Surface = surface,
        };
    }

    /// <summary>One capture tick: shrink every target monitor off the desktop DC, blur (and melt)
    /// the small frames, publish them. Pump thread. Identical pixel work to the old UI-thread
    /// CapturePass - only the thread it runs on changed.</summary>
    private void CaptureOnce(float phaseSeconds)
    {
        var slots = _slots;
        if (slots.Length == 0) return;

        // Rebuild the blur filter only when the radius actually changed. The sigma pushed in is
        // ALREADY source-space (see BrainDrainLayer.SetIntensity); dividing by the downscale again
        // here is what once made every steady-state blur invisible.
        float sigma = _sigma;
        if (MathF.Abs(sigma - _lastSigma) > 0.01f)
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
        float amplitude = _meltAmplitude;
        float gutter = 0f;
        if (_melt && amplitude > 0.05f)
        {
            var tile = EnsureNoiseTile(slots);
            if (tile != null)
            {
                float tierScale = 4f / _downscale;
                // Wrapped into one tile period: the pattern repeats there anyway, and it keeps the
                // float drift from growing without bound over a long session.
                float dx = MeltSwayPx * tierScale * MathF.Sin(phaseSeconds * MeltSwayRate);
                float dy = (MeltDriftPxPerSec * tierScale * phaseSeconds) % _meltTileCoverage;
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
                        SKColorChannel.R, SKColorChannel.G, amplitude, noiseFilter, _blurFilter);
                    (_meltPaint ??= new SKPaint()).ImageFilter = meltFilter;
                    // Displacement samples outside the source bleed transparent along the frame
                    // edges. Max |offset| is amplitude/2 (the map is centred on 0.5), +2px slack
                    // for the CTM scale below feeding back into the mapped displacement scale.
                    gutter = amplitude * 0.5f + 2f;
                }
            }
        }

        try
        {
            // THE call this whole class exists to get off the UI thread. Plain SRCCOPY, no
            // CAPTUREBLT: layered windows (every compositor overlay, including our own host) are
            // excluded from a desktop DC blt, which is the self-capture guard.
            var screenDc = GetDC(IntPtr.Zero);
            if (screenDc == IntPtr.Zero) return;
            long startTs = Stopwatch.GetTimestamp();
            try
            {
                foreach (var slot in slots)
                {
                    if (_stop) return;
                    if (slot.Surface == null) continue;
                    SetStretchBltMode(slot.MemDc, HALFTONE);
                    if (!StretchBlt(slot.MemDc, 0, 0, slot.W, slot.H,
                                    screenDc, slot.Bounds.X, slot.Bounds.Y,
                                    slot.Bounds.Width, slot.Bounds.Height, SRCCOPY))
                        continue;
                    GdiFlush();

                    var info = new SKImageInfo(slot.W, slot.H, SKColorType.Bgra8888, SKAlphaType.Opaque);
                    using var raw = SKImage.FromPixels(info, slot.Bits, slot.W * 4); // zero-copy wrap; consumed below
                    if (raw == null) continue;

                    var canvas = slot.Surface.Canvas;
                    canvas.Clear(SKColors.Transparent);
                    if (meltFilter != null)
                    {
                        // Scale about the centre so the gutter falls off-frame entirely.
                        canvas.Save();
                        canvas.Translate(slot.W * 0.5f, slot.H * 0.5f);
                        canvas.Scale((slot.W + 2f * gutter) / slot.W, (slot.H + 2f * gutter) / slot.H);
                        canvas.Translate(-slot.W * 0.5f, -slot.H * 0.5f);
                        canvas.DrawImage(raw, 0, 0, _meltPaint);
                        canvas.Restore();
                    }
                    else
                    {
                        canvas.DrawImage(raw, 0, 0, _blurFilter != null ? _blurPaint : null);
                    }
                    slot.Frame.Publish(slot.Surface.Snapshot());
                    Interlocked.Increment(ref _framesPublished);
                }
            }
            finally
            {
                ReleaseDC(IntPtr.Zero, screenDc);
                ReportStall((Stopwatch.GetTimestamp() - startTs) * 1000.0 / Stopwatch.Frequency);
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

    /// <summary>A tick that took this long on the UI thread WAS the #777 freeze. Here it is a
    /// dropped frame, and a line the next hang report can be read against.</summary>
    private void ReportStall(double ms)
    {
        if (ms < StallWarnMs) return;
        var now = DateTime.UtcNow;
        if (now - _lastStallLogUtc < StallLogCooldown) return;
        _lastStallLogUtc = now;
        VideoDiag.Log("BDRAIN", $"desktop capture blt took {ms:F0}ms on the capture thread " +
                                "(would have wedged the UI thread before #777)");
    }

    /// <summary>Rasterises the tileable turbulence field for the melt warp, sized off the first
    /// capture's short edge. Rebuilt only when that size changes (downscale change / monitor swap);
    /// costs ~14ms once, versus ~24ms EVERY tick if the Perlin shader were evaluated live.</summary>
    private SKImage? EnsureNoiseTile(Slot[] slots)
    {
        if (slots.Length == 0) return null;
        int shortEdge = Math.Min(slots[0].W, slots[0].H);
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

    /// <summary>Free every GDI + Skia object one slot array owns. Pump thread only. The un-taken
    /// frame is cleared UNDER the slot's gate so a concurrent TryTakeFrame either got it (and owns
    /// it) or sees null - never a disposed image.</summary>
    private static void ReleaseSlots(Slot[] slots)
    {
        foreach (var slot in slots)
        {
            try
            {
                slot.Frame.Clear();

                slot.Surface?.Dispose();
                slot.Surface = null;
                if (slot.MemDc != IntPtr.Zero)
                {
                    if (slot.OldObj != IntPtr.Zero) SelectObject(slot.MemDc, slot.OldObj);
                    DeleteDC(slot.MemDc);
                    slot.MemDc = IntPtr.Zero;
                }
                if (slot.HBitmap != IntPtr.Zero)
                {
                    DeleteObject(slot.HBitmap);
                    slot.HBitmap = IntPtr.Zero;
                }
            }
            catch { /* GDI cleanup best-effort */ }
        }
    }

    private void ReleaseAll()
    {
        try
        {
            ReleaseSlots(_slots);
            _slots = Array.Empty<Slot>();
        }
        catch { }
        try
        {
            _blurPaint.ImageFilter = null;
            _blurFilter?.Dispose();
            _blurFilter = null;
            _blurPaint.Dispose();
            if (_meltPaint != null) { _meltPaint.ImageFilter = null; _meltPaint.Dispose(); _meltPaint = null; }
            if (_meltNoisePaint != null) { _meltNoisePaint.Shader = null; _meltNoisePaint.Dispose(); _meltNoisePaint = null; }
            _meltNoiseTile?.Dispose();
            _meltNoiseTile = null;
            _meltNoiseShort = -1;
        }
        catch { }
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
