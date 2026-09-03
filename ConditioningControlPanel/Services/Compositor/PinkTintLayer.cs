using SkiaSharp;

namespace ConditioningControlPanel.Services.Compositor;

/// <summary>
/// Compositor twin of the pink-filter windows: a fullscreen solid tint whose opacity rides in
/// the color's alpha channel, exactly like the legacy Border/SolidColorBrush (OverlayService
/// owns all opacity math - ramps, pulses, holds - and just pushes the final value here).
/// </summary>
public class PinkTintLayer : BaseLayer
{
    private byte _r = 255, _g = 105, _b = 180;
    private double _opacity;
    private bool _dirty = true;   // #550: a steady tint is static - only repaint when it changes
    private readonly SKPaint _paint = new();

    /// <summary>How far down the tint sinks at the bottom of a breath, as a fraction of the
    /// chosen opacity. Shared with the legacy-window route (OverlayService.ApplyPinkBreathe)
    /// so the two render paths breathe to the same depth instead of drifting apart.</summary>
    public const double BreatheFloor = 0.35;

    // Breathe (suggestions thread 1537106473534885938). The clock is advanced in Update from
    // the engine's own delta rather than read off the Render elapsed, so Dirty can answer
    // "did the visible alpha actually change this frame?" BEFORE the surface is rastered.
    private bool _breathe;
    private double _breatheSeconds = 6;
    private double _breatheClock;
    private byte _lastAlpha;

    public PinkTintLayer(CompositorEngine engine) : base(engine) { }

    public override int ZIndex => CompositorLayers.PinkTint;

    // Fullscreen fill: honor the pink filter's per-effect monitor target (suggestion #639), which
    // itself falls back to DualMonitorEnabled when set to -1. The compositor hosts every screen,
    // so a non-targeted monitor just records an empty (transparent) frame for this layer.
    public override bool ShouldRenderOnScreen(System.Drawing.Rectangle screenBoundsPx)
        => App.ShouldRenderTargetOnScreen(
               App.Settings?.Current?.PinkFilterTargetMonitor ?? App.MonitorTargetFollowGlobal,
               screenBoundsPx);

    // A breathing tint is only dirty on the frames where the quantised alpha actually moves, so
    // a slow 12s breath still costs a fraction of the refresh rate instead of re-rastering the
    // shared surface every frame (#853).
    public override bool Dirty => _dirty || (_breathe && CurrentAlpha() != _lastAlpha);
    public override void ClearDirty() => _dirty = false;

    /// <summary>Show the tint (or retarget a visible one). Opacity 0..1. UI thread.</summary>
    public void Show(byte r, byte g, byte b, double opacity)
    {
        Set(r, g, b, opacity);
        _dirty = true;            // guarantee a paint on (re)show even if the values were unchanged
        SetActive(true);
    }

    /// <summary>Update color + opacity without changing visibility. UI thread.</summary>
    public void Set(byte r, byte g, byte b, double opacity)
    {
        var o = Math.Clamp(opacity, 0.0, 1.0);
        if (r == _r && g == _g && b == _b && o == _opacity) return;  // no visible change
        _r = r; _g = g; _b = b; _opacity = o;
        _dirty = true;
    }

    /// <summary>Turn the slow in-and-out pulse on or off. Seconds is one FULL cycle. UI thread.</summary>
    public void SetBreathe(bool on, double seconds)
    {
        seconds = Math.Clamp(seconds, 4.0, 12.0);
        if (on == _breathe && Math.Abs(seconds - _breatheSeconds) < 0.001) return;
        _breathe = on;
        _breatheSeconds = seconds;
        _breatheClock = 0;        // every (re)start begins at the trough and swells in
        _dirty = true;
    }

    public void Hide() => SetActive(false);

    public override void Update(TimeSpan delta)
    {
        if (!_breathe) return;
        _breatheClock += delta.TotalSeconds;
        if (_breatheClock >= _breatheSeconds) _breatheClock %= _breatheSeconds;
    }

    /// <summary>The alpha this layer would paint right now, breathe included.</summary>
    private byte CurrentAlpha()
    {
        var o = _opacity;
        if (_breathe && _breatheSeconds > 0)
        {
            // Raised cosine: 0 at the trough, 1 at the peak, one full pass per period. Same
            // shape the WPF route gets from AutoReverse + SineEase(InOut).
            var wave = (1 - Math.Cos(_breatheClock / _breatheSeconds * 2 * Math.PI)) * 0.5;
            o *= BreatheFloor + (1 - BreatheFloor) * wave;
        }
        return (byte)Math.Clamp(o * 255, 0, 255);
    }

    public override void Render(SKCanvas canvas, SKRectI boundsPx, double dpiScale, TimeSpan elapsed)
    {
        if (_opacity <= 0) return;
        _lastAlpha = CurrentAlpha();
        _paint.Color = new SKColor(_r, _g, _b, _lastAlpha);
        canvas.DrawRect(boundsPx, _paint);
    }
}
