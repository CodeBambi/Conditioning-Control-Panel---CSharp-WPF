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
/// captures and draws.
///
/// THREADING (#777): the capture and the blur run on <see cref="BrainDrainCapturePump"/>'s own
/// thread, NOT here. A full-screen StretchBlt off the live desktop DC cannot be time-bounded, and
/// running it on the dispatcher every ~33ms - concurrently with the compositor present thread's
/// UpdateLayeredWindow against windows on that same desktop - is a frozen app waiting to happen
/// (#777: both screens frozen, audio still running, killed from Task Manager). What is left on the
/// UI thread is this class: Start/Stop/SetIntensity/Pulse (parameter pushes, all non-blocking) and
/// Render, which takes the newest finished frame from the pump under a 2ms bounded lock and draws
/// it. A stalled capture now costs a repeated frame, never the dispatcher.
/// </summary>
public sealed class BrainDrainLayer : BaseLayer
{
    /// <summary>The frame the UI thread currently owns for one monitor. Ownership transfers from
    /// the pump on a successful take, and nothing but the UI thread ever touches these images -
    /// so there is no disposal race with the capture thread by construction.</summary>
    private sealed class UiFrame
    {
        public System.Drawing.Rectangle Bounds;
        public SKImage? Image;
    }

    private readonly List<UiFrame> _uiFrames = new();   // UI thread only
    private BrainDrainCapturePump? _pump;

    private readonly SKPaint _drawPaint = new() { FilterQuality = SKFilterQuality.Low }; // bilinear = WPF Image default

    private int _downscale = 4;
    private double _sourceRadius;               // WPF-BlurEffect-equivalent radius ON THE DOWNSCALED SOURCE
    // Alpha-mix axis (compositor path only). At low intensity the gaussian is a fraction of a
    // source pixel wide - i.e. nothing - so the *strength* has to come from how much of the blurred
    // copy we paint over the real screen. Intensity 1..100 ramps the draw alpha linearly 0.30 -> 0.85:
    // the real screen ALWAYS ghosts through (owner call, 2026-07-31: the effect must stay subtle -
    // full-opacity blur read as "basically illegible"). Sigma rises alongside, so perceived strength
    // is continuous across 1 -> 100 while the ceiling stays a dreamy haze, not a wall.
    // Pulse still paints at full alpha - a pulse is a deliberate 1-second slam.
    private const int AlphaFullIntensity = 100;
    private const double AlphaFloor = 0.30;
    private const double AlphaCeiling = 0.85;
    private byte _drawAlpha = 255;
    // Melt variant ("braindrain_melt"): shares this layer and ALL of OverlayService's hold/ramp
    // state - a melt band and a plain blur band never co-exist by design.
    // Melt = the same blur PLUS a slowly-flowing Perlin displacement warp ("melting glass"),
    // composed into ONE filter chain on the pump's small-surface draw. It never touches Render:
    // warping at monitor resolution on a CPU raster surface is not affordable, and the capture
    // cadence (30fps default) is smooth enough for a drift this slow. The warp's phase is wall
    // clock owned by the pump thread, so the flow speed stays independent of both the engine tick
    // rate and the capture cadence (it used to be accumulated from the engine's delta here). The
    // melt FLAG itself now lives only on the pump - it is fixed for the lifetime of one run.
    private float _meltAmplitude;               // displacement scale, SOURCE px (see SetIntensity)

    private TimeSpan _captureInterval = TimeSpan.FromMilliseconds(33);

    // Topology drift is rare; re-targeting the pump every engine tick at 60Hz is pure waste.
    // ~1s detection latency on a monitor change is imperceptible.
    private static readonly TimeSpan DriftCheckInterval = TimeSpan.FromSeconds(1);
    private TimeSpan _sinceDriftCheck;
    private System.Drawing.Rectangle[] _requestedScreens = Array.Empty<System.Drawing.Rectangle>();

    // #853: honest dirt. Only two things can change this layer's pixels - a NEW frame off the pump
    // (30fps, 60 on high refresh) or an intensity/pulse push - while the engine ticks at the
    // display's refresh rate. Everything else was a fullscreen upscale of the same image.
    // UI thread only (Update/Start/SetIntensity/Pulse all run there).
    private bool _dirty = true;
    private int _lastSeenFrames = -1;

    public BrainDrainLayer(CompositorEngine engine) : base(engine) { }

    public override int ZIndex => CompositorLayers.BrainDrain;
    public override bool ExcludeFromCapture => true;
    public override bool WorldSpacePx => true;

    /// <summary>Start capturing + blurring at the given intensity (legacy 1..200 scale).
    /// <paramref name="melt"/> selects the "braindrain_melt" variant - same blur plus an animated
    /// Perlin displacement warp.</summary>
    public void Start(int intensity, bool melt = false)
    {
        StopPump();   // defensive: a re-Start must never strand a live capture thread

        var settings = App.Settings?.Current;
        var tier = PerformanceProfile.CurrentTier;
        _downscale = PerformanceProfile.BrainDrainDownscale(tier);
        int fps = Math.Min(settings?.BrainDrainHighRefresh == true ? 60 : 30,
                           PerformanceProfile.BrainDrainFps(tier));
        _captureInterval = TimeSpan.FromMilliseconds(1000.0 / Math.Max(1, fps));
        SetIntensity(intensity);

        _requestedScreens = GetTargetScreens();
        _sinceDriftCheck = TimeSpan.Zero;   // just targeted - no immediate drift check
        _pump = new BrainDrainCapturePump(_downscale, _captureInterval, melt,
                                          _requestedScreens, Sigma, _meltAmplitude);
        _lastSeenFrames = -1;   // fresh pump: its counter restarts at 0, so never match a stale one
        _dirty = true;
        SetActive(true);
    }

    public void Stop()
    {
        SetActive(false);
        ReleaseCaptures();
    }

    // Blur strength per intensity point, SOURCE px per unit. The legacy constant was 0.4 (radius
    // parity with the pre-compositor BlurEffect); retuned to 0.14 on the 2026-07-31 subtle pass -
    // max intensity is now sigma ~4.7 screen px at downscale 4 (a dreamy haze the screen structure
    // survives) instead of ~13 (illegible). The legacy windowed path in OverlayService reads this
    // same constant - keep them identical.
    internal const double RadiusScale = 0.14;

    /// <summary>Gaussian sigma handed to the pump. WPF's BlurEffect radius-to-sigma ratio is 3, and
    /// <see cref="_sourceRadius"/> is ALREADY source-space (see <see cref="SetIntensity"/>) - a
    /// second divide by the downscale here is what once made every steady-state blur invisible
    /// (max intensity landed at sigma ~0.83px, 1..6 at literally zero).</summary>
    private float Sigma => (float)(_sourceRadius / 3.0);

    /// <summary>Normal/ramp/restore path - a SOURCE-space radius (the pre-compositor BlurEffect
    /// radius was applied to the downscaled bitmap; the pump turns it into sigma = radius/3).
    /// Strength curve retuned subtle 2026-07-31, see <see cref="RadiusScale"/>.</summary>
    public void SetIntensity(int intensity)
    {
        _sourceRadius = intensity * RadiusScale / Math.Max(1, _downscale);
        _drawAlpha = AlphaFor(intensity);
        // Melt amplitude curve (SOURCE px, quoted at downscale 4 then divided by the actual
        // downscale so the on-screen warp is tier-independent):  amp = 1 + intensity * 0.045.
        // A displacement map offsets by +/- amp/2, so on screen at x4 that is +/- 2*amp px:
        //   intensity 30  -> amp 2.35 -> +/-4.7 screen px  = a whisper of shimmer
        //   intensity 60  -> amp 3.7  -> +/-7.4 screen px  = gently liquid
        //   intensity 100 -> amp 5.5  -> +/-11 screen px   = clearly melting, still readable
        // (Subtle retune 2026-07-31; the original 2 + i*0.12 read as "basically illegible".)
        // Clamped at the legacy 200 ceiling (amp 10) so a runaway value cannot eat the frame.
        _meltAmplitude = (float)((1.0 + Math.Clamp(intensity, 0, 200) * 0.045) * 4.0 / Math.Max(1, _downscale));
        _dirty = true;      // draw alpha moved: repaint even if the pump has no new frame yet
        _pump?.SetBlur(Sigma, _meltAmplitude);
    }

    /// <summary>Pulse boost - a deliberate 1-second slam: full draw alpha and the boosted radius
    /// WITHOUT the downscale divide was the legacy behavior, but post subtle-retune that would be
    /// a white-out (sigma ~9 SOURCE px). The pulse now keeps the divide and simply rides the
    /// boosted (2x, capped 200) intensity: ~2x the steady sigma at full alpha - still a clear kick
    /// against an 0.85-alpha steady state.</summary>
    public void Pulse(int boostedIntensity)
    {
        _sourceRadius = boostedIntensity * RadiusScale / Math.Max(1, _downscale);
        _drawAlpha = 255;
        _dirty = true;      // the slam must land on this frame, not on the pump's next one
        _pump?.SetBlur(Sigma, _meltAmplitude);
    }

    /// <summary>Draw alpha for an intensity: <see cref="AlphaFloor"/> at 1, linear to
    /// <see cref="AlphaCeiling"/> at <see cref="AlphaFullIntensity"/> and above - never fully
    /// opaque. See the field comment for why.</summary>
    private static byte AlphaFor(int intensity)
    {
        int i = Math.Clamp(intensity, 1, AlphaFullIntensity);
        double a = AlphaFloor + (AlphaCeiling - AlphaFloor) * (i - 1) / (AlphaFullIntensity - 1.0);
        return (byte)Math.Clamp(Math.Round(a * 255.0), 0, 255);
    }

    public override void OnDeactivated() => ReleaseCaptures();

    public override bool Dirty => _dirty;
    public override void ClearDirty() => _dirty = false;

    /// <summary>
    /// UI thread, once per engine tick. All this does now is re-target the pump when the display
    /// topology drifts (plus the cheap dirty gate below) - the capture itself is the pump thread's
    /// job and the melt phase is its wall clock. Cheap enough that it can never be what a hang
    /// report is pointing at.
    /// </summary>
    public override void Update(TimeSpan delta)
    {
        // #853: a repeat of the frame already on screen is not a repaint. Compare the pump's
        // publish counter rather than signalling from the capture thread - a stalled pump then
        // simply holds its last frame (the documented behaviour) instead of going stuck-clean,
        // and it re-dirties by itself the moment capture resumes.
        int published = _pump?.FramesPublished ?? _lastSeenFrames;
        if (published != _lastSeenFrames) { _lastSeenFrames = published; _dirty = true; }

        _sinceDriftCheck += delta;
        if (_sinceDriftCheck < DriftCheckInterval) return;
        _sinceDriftCheck = TimeSpan.Zero;

        try
        {
            var pump = _pump;
            if (pump == null) return;
            var screens = GetTargetScreens();
            if (screens.Length == 0) return;   // display transition - keep capturing what we have
            if (SameScreens(screens, _requestedScreens)) return;

            _requestedScreens = screens;
            pump.SetScreens(screens);
            // The frames we hold describe monitors that may no longer exist (or have moved); drop
            // them rather than keep stretching a stale grab over a new rectangle.
            ReleaseUiFrames();
        }
        catch (Exception ex)
        {
            App.Logger?.Debug("BrainDrainLayer drift check failed: {Error}", ex.Message);
        }
    }

    public override void Render(SKCanvas canvas, SKRectI boundsPx, double dpiScale, TimeSpan elapsed)
    {
        // BREADCRUMB: the ONLY brain-drain work still on the UI thread. The desktop StretchBlt this
        // mark used to name moved to the capture thread in #777, so the mark moved with it - if a
        // hang report's "last UI mark" is THIS, the wedge is the hand-off or the Skia draw, and NOT
        // the screen grab. Two volatile writes; safe on a per-frame path.
        using var _ = VideoDiag.UiScope("BrainDrainLayer.Render(take frame + draw)");

        var pump = _pump;
        if (pump == null) return;

        // World-space: boundsPx is this monitor's rect; draw only its own capture.
        int cx = (boundsPx.Left + boundsPx.Right) / 2, cy = (boundsPx.Top + boundsPx.Bottom) / 2;
        if (!pump.TryTakeFrame(cx, cy, out var fresh, out var captureBounds)) return;

        var frame = GetOrAddFrame(captureBounds);
        if (fresh != null)
        {
            frame.Image?.Dispose();   // UI-thread-owned: the pump provably is not looking at it
            frame.Image = fresh;
        }
        if (frame.Image == null) return;   // first tick(s): the pump has not produced one yet

        // Alpha mix: white-with-alpha modulates the image draw (RGB is ignored for DrawImage).
        // Set per frame - Render, SetIntensity and Pulse are all on the UI thread, so this can
        // never read a half-written value, and it keeps the ramp path free of paint bookkeeping.
        _drawPaint.Color = SKColors.White.WithAlpha(_drawAlpha);
        canvas.DrawImage(frame.Image,
            new SKRect(captureBounds.X, captureBounds.Y, captureBounds.Right, captureBounds.Bottom),
            _drawPaint);
    }

    private UiFrame GetOrAddFrame(System.Drawing.Rectangle bounds)
    {
        foreach (var f in _uiFrames)
            if (f.Bounds == bounds) return f;
        var added = new UiFrame { Bounds = bounds };
        _uiFrames.Add(added);
        return added;
    }

    private static bool SameScreens(System.Drawing.Rectangle[] a, System.Drawing.Rectangle[] b)
    {
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++)
            if (a[i] != b[i]) return false;
        return true;
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

    /// <summary>Tear the capture down. UI thread, and deliberately NON-BLOCKING: the pump thread
    /// frees its own GDI handles and Skia surfaces as its last act, because joining a thread that
    /// may be inside a stalled desktop blt would re-create the very freeze #777 is about.</summary>
    private void ReleaseCaptures()
    {
        StopPump();
        ReleaseUiFrames();
        _requestedScreens = Array.Empty<System.Drawing.Rectangle>();
    }

    private void StopPump()
    {
        var pump = _pump;
        _pump = null;
        pump?.Shutdown();
    }

    private void ReleaseUiFrames()
    {
        foreach (var f in _uiFrames)
        {
            try { f.Image?.Dispose(); } catch { }
            f.Image = null;
        }
        _uiFrames.Clear();
    }
}
